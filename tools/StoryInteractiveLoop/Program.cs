// StoryInteractiveLoop — multi-turn Armenian Story-mode loop runner.
//
// What it does:
//   1) Registers a fresh device against the running backend.
//   2) Sends an Armenian seed prompt ("Պատմիր հեքիաթ ...") through
//      /api/chat.
//   3) Parses ChoiceA/ChoiceB from the response.
//   4) Picks one (alternating across sessions / or forced A|B).
//   5) Sends the chosen choice text back as the next child input,
//      carrying storySessionId + selectedChoice forward.
//   6) Repeats up to --max-turns, stopping early on:
//        - SafetyFlag != Clean (moderation fallback)
//        - missing choices (story ended)
//        - HTTP error
//   7) Evaluates every turn deterministically (see Evaluators.cs).
//   8) Writes a per-session MD + JSON evidence file under
//      tools/StoryInteractiveLoop/evidence/.
//   9) Updates evidence/LATEST_SUMMARY.md with the roll-up.
//
// COST GATE: real OpenAI chat completions cost real money. By default
// the runner caps the run at 6 total chat calls (e.g. 1 session × 3
// turns + the start). Larger runs require --allow-larger-run.
//
// Usage:
//   dotnet run --project tools/StoryInteractiveLoop                          # smoke (1×3)
//   dotnet run --project tools/StoryInteractiveLoop -- --max-sessions 2 --max-turns 3
//   dotnet run --project tools/StoryInteractiveLoop -- --max-sessions 10 --max-turns 6 --allow-larger-run

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArmenianAiToy.Tools.StoryInteractiveLoop;

internal static class Program
{
    private const int DefaultMaxSessions = 1;
    private const int DefaultMaxTurns = 3;
    private const int CostGateBudget = 6; // chat-call ceiling before --allow-larger-run

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        var baseUrl = ArgString(args, "--base-url") ?? "http://localhost:5000";
        var maxSessions = ArgInt(args, "--max-sessions") ?? DefaultMaxSessions;
        var maxTurns = ArgInt(args, "--max-turns") ?? DefaultMaxTurns;
        var strategy = (ArgString(args, "--strategy") ?? "alternating").ToLowerInvariant();
        var seedIdsCsv = ArgString(args, "--seed-id");
        var outputDirRel = ArgString(args, "--output") ?? "evidence";
        var allowLarger = args.Contains("--allow-larger-run");

        if (maxSessions < 1 || maxTurns < 1)
        {
            Console.Error.WriteLine("--max-sessions and --max-turns must be >= 1.");
            return 1;
        }
        if (strategy is not ("alternating" or "a" or "b"))
        {
            Console.Error.WriteLine("--strategy must be alternating|a|b.");
            return 1;
        }

        // Cost gate: chat-call ceiling = sessions × (1 start + maxTurns).
        var maxCalls = maxSessions * (1 + maxTurns);
        if (maxCalls > CostGateBudget && !allowLarger)
        {
            Console.Error.WriteLine(
                $"Refusing: planned worst-case is {maxCalls} live chat calls ({maxSessions}×{maxTurns + 1}), " +
                $"over the {CostGateBudget}-call cost gate. Re-run with --allow-larger-run to override.");
            return 1;
        }

        // ---- load seeds ----
        var seedsPath = Path.Combine(AppContext.BaseDirectory, "seed-prompts.json");
        SeedSet seedSet;
        try
        {
            var bytes = await File.ReadAllBytesAsync(seedsPath);
            seedSet = JsonSerializer.Deserialize<SeedSet>(bytes, JsonOpts)
                ?? throw new InvalidOperationException("seed-prompts.json deserialized to null");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load seeds from {seedsPath}: {ex.Message}");
            return 1;
        }

        var seeds = seedSet.Seeds.ToList();
        if (!string.IsNullOrWhiteSpace(seedIdsCsv))
        {
            var keep = seedIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            seeds = seeds.Where(s => keep.Contains(s.Id)).ToList();
            if (seeds.Count == 0)
            {
                Console.Error.WriteLine($"No seeds matched --seed-id={seedIdsCsv}.");
                return 1;
            }
        }

        // ---- resolve repo + output ----
        var repoRoot = ResolveRepoRoot(AppContext.BaseDirectory);
        var outputDir = Path.IsPathRooted(outputDirRel)
            ? outputDirRel
            : Path.GetFullPath(Path.Combine(repoRoot, "tools/StoryInteractiveLoop", outputDirRel));
        Directory.CreateDirectory(outputDir);

        var runStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var git = await GitSnapshot.CaptureAsync(repoRoot);

        Console.WriteLine($"StoryInteractiveLoop — {DateTime.UtcNow:O}");
        Console.WriteLine($"  base-url:     {baseUrl}");
        Console.WriteLine($"  max-sessions: {maxSessions}");
        Console.WriteLine($"  max-turns:    {maxTurns}");
        Console.WriteLine($"  strategy:     {strategy}");
        Console.WriteLine($"  cost-gate:    {CostGateBudget} (planned worst-case {maxCalls})");
        Console.WriteLine($"  git sha:      {git.Sha[..Math.Min(8, git.Sha.Length)]} (dirty={git.Dirty})");
        Console.WriteLine($"  branch:       {git.Branch}");
        Console.WriteLine($"  output:       {outputDir}");
        Console.WriteLine();

        // ---- HTTP client ----
        using var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };

        // ---- run sessions ----
        var sessionRecords = new List<SessionRecord>();
        for (int i = 0; i < maxSessions; i++)
        {
            var seed = seeds[i % seeds.Count];
            var startChoice = strategy switch
            {
                "a" => "A",
                "b" => "B",
                _ => (i % 2 == 0) ? "A" : "B"
            };

            Console.WriteLine($"---- Session {i + 1}/{maxSessions} — seed={seed.Id} strategy=start-with-{startChoice} ----");
            var record = await RunSessionAsync(http, seed, maxTurns, startChoice, git, runStamp, i + 1);
            sessionRecords.Add(record);

            var path = await EvidenceWriter.WritePerSessionAsync(outputDir, record);
            Console.WriteLine($"  evidence: {path}");
            Console.WriteLine();
        }

        // ---- roll-up ----
        var summaryPath = await EvidenceWriter.WriteLatestSummaryAsync(
            outputDir, sessionRecords, git, runStamp,
            new RunSettings(baseUrl, maxSessions, maxTurns, strategy, allowLarger));
        Console.WriteLine($"LATEST_SUMMARY: {summaryPath}");

        // ---- exit code: 0 if every session PASS or WARN, 1 if any FAIL ----
        var anyFail = sessionRecords.Any(s => s.Verdict.OverallVerdict == "FAIL");
        return anyFail ? 1 : 0;
    }

    // ---- one session ----
    private static async Task<SessionRecord> RunSessionAsync(
        HttpClient http, Seed seed, int maxTurns, string startChoice,
        GitSnapshot git, string runStamp, int sessionIndex)
    {
        var record = new SessionRecord
        {
            RunStamp = runStamp,
            SessionIndex = sessionIndex,
            SeedId = seed.Id,
            SeedPrompt = seed.Prompt,
            StartChoiceStrategy = startChoice,
            GitSha = git.Sha,
            Branch = git.Branch,
            Dirty = git.Dirty,
            StartedAtUtc = DateTime.UtcNow
        };

        // ---- device registration ----
        try
        {
            var regResp = await http.PostAsJsonAsync("/api/devices/register",
                new { macAddress = $"LOOP-{runStamp}-{sessionIndex:D2}" });
            regResp.EnsureSuccessStatusCode();
            var device = await regResp.Content.ReadFromJsonAsync<DeviceReg>(JsonOpts)
                ?? throw new InvalidOperationException("registration returned null");
            http.DefaultRequestHeaders.Remove("X-Device-Id");
            http.DefaultRequestHeaders.Remove("X-Api-Key");
            http.DefaultRequestHeaders.Add("X-Device-Id", device.DeviceId.ToString());
            http.DefaultRequestHeaders.Add("X-Api-Key", device.ApiKey);
            record.DeviceId = device.DeviceId;
        }
        catch (Exception ex)
        {
            record.FatalError = $"device registration failed: {ex.Message}";
            record.EndedAtUtc = DateTime.UtcNow;
            record.Verdict = new SessionVerdict { OverallVerdict = "FAIL" };
            Console.WriteLine($"  ! FATAL: {record.FatalError}");
            return record;
        }

        // ---- turn 0: send seed prompt ----
        string? nextMessage = seed.Prompt;
        string? selectedLabel = null;
        string? selectedAB = null;
        Guid? storySessionId = null;
        string? previousBody = null;
        string nextChoiceLetter = startChoice;

        for (int t = 0; t < maxTurns; t++)
        {
            var turn = new TurnRecord
            {
                TurnIndex = t,
                UserInput = nextMessage ?? "",
                SelectedChoice = t == 0 ? null : selectedAB,
                SelectedChoiceLabel = t == 0 ? null : selectedLabel,
                StorySessionIdIn = storySessionId
            };

            ChatResponseDto? resp = null;
            var sw = Stopwatch.StartNew();
            try
            {
                var body = new
                {
                    message = nextMessage,
                    storySessionId = storySessionId,
                    selectedChoice = selectedAB
                };
                var http_resp = await http.PostAsJsonAsync("/api/chat", body);
                turn.HttpStatus = (int)http_resp.StatusCode;
                http_resp.EnsureSuccessStatusCode();
                resp = await http_resp.Content.ReadFromJsonAsync<ChatResponseDto>(JsonOpts);
            }
            catch (Exception ex)
            {
                turn.ErrorMessage = ex.Message;
            }
            sw.Stop();
            turn.LatencyMs = (int)sw.ElapsedMilliseconds;

            if (resp is null || !string.IsNullOrEmpty(turn.ErrorMessage))
            {
                turn.Warnings.Add("http_error");
                record.Turns.Add(turn);
                record.StopReason = "http_error";
                break;
            }

            turn.AssistantBody = resp.Response;
            turn.ChoiceA = resp.ChoiceA;
            turn.ChoiceB = resp.ChoiceB;
            turn.ConversationId = resp.ConversationId;
            turn.StorySessionIdOut = resp.StorySessionId;
            turn.SafetyFlag = resp.SafetyFlag;
            turn.Mode = resp.Mode;
            turn.BodyLen = resp.Response?.Length ?? 0;
            turn.ArmenianRatio = Evaluators.ArmenianRatio(resp.Response);

            // Run deterministic evaluators on this turn.
            var hasChoices = !string.IsNullOrWhiteSpace(resp.ChoiceA)
                && !string.IsNullOrWhiteSpace(resp.ChoiceB);
            var warnings = Evaluators.EvaluateTurn(new TurnEvaluationInput
            {
                Body = resp.Response ?? "",
                ChoiceA = resp.ChoiceA,
                ChoiceB = resp.ChoiceB,
                HasChoices = hasChoices,
                PreviousBody = previousBody,
                SelectedChoiceLabel = selectedLabel
            });
            turn.Warnings.AddRange(warnings);

            record.Turns.Add(turn);

            // Stop conditions.
            if (resp.SafetyFlag != 0) // Clean = 0
            {
                record.StopReason = $"safety_fallback:{resp.SafetyFlag}";
                break;
            }
            if (!hasChoices)
            {
                record.StopReason = "no_choices_returned";
                break;
            }

            // Pick next choice.
            var pick = nextChoiceLetter;
            selectedAB = pick;
            selectedLabel = pick == "A" ? resp.ChoiceA : resp.ChoiceB;
            nextMessage = selectedLabel;
            storySessionId = resp.StorySessionId ?? storySessionId;
            previousBody = resp.Response;

            // Alternate strictly inside the session too, so we exercise
            // both branches across the turn sequence.
            nextChoiceLetter = nextChoiceLetter == "A" ? "B" : "A";
        }

        if (record.StopReason is null)
            record.StopReason = "max_turns_reached";

        record.EndedAtUtc = DateTime.UtcNow;
        record.Verdict = Evaluators.EvaluateSession(
            record.Turns.Select(t => (IReadOnlyList<string>)t.Warnings).ToList());

        Console.WriteLine($"  stop={record.StopReason} turns={record.Turns.Count} " +
            $"verdict={record.Verdict.OverallVerdict} " +
            $"arm={record.Verdict.ArmenianQualityScore} " +
            $"logic={record.Verdict.StoryLogicScore} " +
            $"choice={record.Verdict.ChoiceQualityScore} " +
            $"cont={record.Verdict.ContinuationCoherenceScore}");

        return record;
    }

    // ---- helpers ----

    private static void PrintUsage()
    {
        Console.WriteLine("StoryInteractiveLoop — multi-turn Armenian Story-mode loop runner.");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("  dotnet run --project tools/StoryInteractiveLoop -- [options]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("  --base-url <url>          Backend root (default http://localhost:5000)");
        Console.WriteLine($"  --max-sessions <n>        Default {DefaultMaxSessions}");
        Console.WriteLine($"  --max-turns <n>           Default {DefaultMaxTurns}");
        Console.WriteLine("  --seed-id <S01,S02,...>   Restrict to listed seeds (default: all)");
        Console.WriteLine("  --strategy alternating|a|b  Start-choice strategy across sessions");
        Console.WriteLine("  --output <dir>            Output dir (default ./evidence)");
        Console.WriteLine($"  --allow-larger-run        Required when sessions×(1+turns) > {CostGateBudget}");
        Console.WriteLine("  --help                    Print this help");
    }

    private static string? ArgString(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    private static int? ArgInt(string[] args, string name)
    {
        var s = ArgString(args, name);
        return int.TryParse(s, out var v) ? v : (int?)null;
    }

    private static string ResolveRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
            dir = dir.Parent;
        }
        // Fallback to the start dir — git probes will simply fail closed.
        return start;
    }

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

// ---- DTOs ----

internal sealed record SeedSet
{
    public List<Seed> Seeds { get; init; } = new();
}

internal sealed record Seed
{
    public string Id { get; init; } = "";
    public string Prompt { get; init; } = "";
}

internal sealed record DeviceReg
{
    public Guid DeviceId { get; init; }
    public string ApiKey { get; init; } = "";
}

internal sealed record ChatResponseDto
{
    public string? Response { get; init; }
    public Guid ConversationId { get; init; }
    public Guid MessageId { get; init; }
    public int SafetyFlag { get; init; }
    public string? ChoiceA { get; init; }
    public string? ChoiceB { get; init; }
    public Guid? StorySessionId { get; init; }
    public string? Mode { get; init; }
}

internal sealed record RunSettings(
    string BaseUrl, int MaxSessions, int MaxTurns, string Strategy, bool AllowLarger);

internal sealed class TurnRecord
{
    public int TurnIndex { get; set; }
    public string UserInput { get; set; } = "";
    public string? SelectedChoice { get; set; }
    public string? SelectedChoiceLabel { get; set; }
    public Guid? StorySessionIdIn { get; set; }
    public string? AssistantBody { get; set; }
    public string? ChoiceA { get; set; }
    public string? ChoiceB { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? StorySessionIdOut { get; set; }
    public int SafetyFlag { get; set; }
    public string? Mode { get; set; }
    public int BodyLen { get; set; }
    public double ArmenianRatio { get; set; }
    public int HttpStatus { get; set; }
    public int LatencyMs { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Warnings { get; set; } = new();
}

internal sealed class SessionRecord
{
    public string RunStamp { get; set; } = "";
    public int SessionIndex { get; set; }
    public string SeedId { get; set; } = "";
    public string SeedPrompt { get; set; } = "";
    public string StartChoiceStrategy { get; set; } = "";
    public Guid? DeviceId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public string? StopReason { get; set; }
    public string? FatalError { get; set; }
    public List<TurnRecord> Turns { get; set; } = new();
    public SessionVerdict Verdict { get; set; } = new();
    public string GitSha { get; set; } = "";
    public string Branch { get; set; } = "";
    public bool Dirty { get; set; }
}

internal sealed class GitSnapshot
{
    public string Sha { get; init; } = "";
    public string Branch { get; init; } = "";
    public bool Dirty { get; init; }

    public static async Task<GitSnapshot> CaptureAsync(string repoRoot)
    {
        return new GitSnapshot
        {
            Sha = (await GitAsync(repoRoot, "rev-parse HEAD")).Trim(),
            Branch = (await GitAsync(repoRoot, "rev-parse --abbrev-ref HEAD")).Trim(),
            Dirty = !string.IsNullOrWhiteSpace(await GitAsync(repoRoot, "status --porcelain"))
        };
    }

    private static async Task<string> GitAsync(string repoRoot, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"-C \"{repoRoot}\" {args}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return stdout;
        }
        catch
        {
            return "";
        }
    }
}
