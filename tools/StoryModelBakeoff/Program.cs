// StoryModelBakeoff — Armenian story model bake-off.
//
// F1.1 shipped a dry-run-only planner. F1.2 adds the first live
// provider path: Claude (Anthropic Messages API) over plain
// HttpClient + System.Text.Json. The live path is gated behind
// explicit cost flags and refuses every non-Claude provider for
// now; OpenAI / Gemini live execution is deferred to F1.3+.
//
// Hard contract for this tool (across slices):
//  - No backend dependency. Does not load AppDbContext, ChatService,
//    ModerationService, or any Domain entity.
//  - No PackageReference, no ProjectReference. Only the BCL.
//  - Reads bakeoff-prompts.json + system-prompt.txt next to the
//    binary (CopyToOutputDirectory in csproj). Reads
//    backend/src/ArmenianAiToy.Api/appsettings.json from the repo
//    tree to compute a drift SHA-256 against the bake-off's frozen
//    copy of the production prompt.
//  - Live runs write to tools/StoryModelBakeoff/results/<UTCts>/
//    (gitignored). Dry-run never creates the directory.
//
// Usage:
//   dotnet run --project tools/StoryModelBakeoff
//   dotnet run --project tools/StoryModelBakeoff -- --provider claude
//   dotnet run --project tools/StoryModelBakeoff -- --max-prompts 3
//   dotnet run --project tools/StoryModelBakeoff -- --help
//
//   # F1.2 live (Claude only). Triple opt-in plus a scope flag.
//   dotnet run --project tools/StoryModelBakeoff -- \
//     --run --provider claude --i-understand-live-cost --max-prompts 1
//   dotnet run --project tools/StoryModelBakeoff -- \
//     --run --provider claude --i-understand-live-cost --allow-full-set

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ArmenianAiToy.Tools.StoryModelBakeoff;

internal static class Program
{
    private static readonly string[] AllProviderNames =
        { "openai", "claude", "gemini", "local" };

    private const int ClaudeMaxTokens = 1024;
    private const int RequestTimeoutSeconds = 60;
    private const string AnthropicEndpoint =
        "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        // ---- argument parsing ----
        var live = args.Contains("--run");
        var providerArg = ParseStringArg(args, "--provider") ?? "all";
        var maxPromptsArg = ParseIntArg(args, "--max-prompts");
        var costFlag = args.Contains("--i-understand-live-cost");
        var allowFullSet = args.Contains("--allow-full-set");

        if (!IsValidProviderArg(providerArg))
        {
            Console.Error.WriteLine(
                $"Unknown --provider value: '{providerArg}'. " +
                "Expected one of: openai|claude|gemini|local|all.");
            return 1;
        }

        if (maxPromptsArg is { } v && v < 1)
        {
            Console.Error.WriteLine(
                $"--max-prompts must be >= 1, got {v}.");
            return 1;
        }

        if (maxPromptsArg.HasValue && allowFullSet)
        {
            Console.Error.WriteLine(
                "--max-prompts and --allow-full-set are mutually exclusive. " +
                "Pick one.");
            return 1;
        }

        // ---- locate scaffold files (next to the binary) ----
        var systemPromptPath = Path.Combine(
            AppContext.BaseDirectory, "system-prompt.txt");
        var promptsPath = Path.Combine(
            AppContext.BaseDirectory, "bakeoff-prompts.json");

        if (!File.Exists(systemPromptPath))
        {
            Console.Error.WriteLine(
                $"system-prompt.txt not found next to binary: {systemPromptPath}");
            return 1;
        }
        if (!File.Exists(promptsPath))
        {
            Console.Error.WriteLine(
                $"bakeoff-prompts.json not found next to binary: {promptsPath}");
            return 1;
        }

        // ---- compute bake-off system-prompt text + SHA-256 ----
        // The text WITHOUT the leading "# Source: ..." header line is
        // what we send to providers AND what we hash for the drift
        // check, so the comment header is structurally invisible to
        // the model.
        var bakeoffPromptText = NormalizeNewlines(
            StripCommentHeader(File.ReadAllText(systemPromptPath)));
        var bakeoffPromptSha = Sha256Hex(bakeoffPromptText);

        // ---- compute production system-prompt SHA-256 (best-effort) ----
        var (productionPromptSha, productionPath) =
            TryReadProductionPromptSha();

        // ---- load + validate prompts ----
        List<Scenario> scenarios;
        try
        {
            scenarios = LoadScenarios(promptsPath);
            ValidateScenarios(scenarios);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"bakeoff-prompts.json invalid: {ex.Message}");
            return 1;
        }

        // ---- max-prompts vs scenario count ----
        if (maxPromptsArg is { } cap && cap > scenarios.Count)
        {
            Console.Error.WriteLine(
                $"--max-prompts {cap} exceeds scenario count {scenarios.Count}.");
            return 1;
        }
        if (maxPromptsArg is { } cap2 && cap2 < scenarios.Count)
            scenarios = scenarios.Take(cap2).ToList();

        // ---- resolve providers ----
        var resolved = ResolveProviders(providerArg);

        // ---- dry-run path (unchanged from F1.1) ----
        if (!live)
        {
            PrintPlan(
                resolved,
                scenarios,
                bakeoffPromptPath: systemPromptPath,
                bakeoffPromptSha: bakeoffPromptSha,
                productionPath: productionPath,
                productionPromptSha: productionPromptSha);
            return 0;
        }

        // ---- live-path guard chain ----
        if (!string.Equals(providerArg, "claude", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "F1.2 ships Claude live execution only. " +
                "Pass --provider claude. " +
                "OpenAI / Gemini / all live execution is deferred to a " +
                "later F1 slice.");
            return 2;
        }

        if (!costFlag)
        {
            Console.Error.WriteLine(
                "Live runs require --i-understand-live-cost. This is a " +
                "belt-and-braces opt-in to acknowledge that real API spend " +
                "happens.");
            return 1;
        }

        if (!maxPromptsArg.HasValue && !allowFullSet)
        {
            Console.Error.WriteLine(
                "Live runs require an explicit scope: pass --max-prompts N " +
                "for a small smoke, or --allow-full-set for the full " +
                $"{scenarios.Count}-scenario run.");
            return 1;
        }

        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine(
                "ANTHROPIC_API_KEY is not set. Live Claude execution " +
                "requires the env var to be present and non-empty.");
            return 1;
        }

        var claudeConfig = resolved.Single(p => p.Name == "claude");

        // ---- live execution ----
        return await RunLiveClaudeAsync(
            apiKey: apiKey,
            claude: claudeConfig,
            scenarios: scenarios,
            bakeoffPromptText: bakeoffPromptText,
            bakeoffPromptSha: bakeoffPromptSha,
            productionPromptSha: productionPromptSha);
    }

    // ---------- argument parsing ----------

    private static string? ParseStringArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }

    private static int? ParseIntArg(string[] args, string name)
    {
        var s = ParseStringArg(args, name);
        if (s is null) return null;
        return int.TryParse(s, out var v) ? v : -1;
    }

    private static bool IsValidProviderArg(string arg)
    {
        if (string.Equals(arg, "all", StringComparison.OrdinalIgnoreCase))
            return true;
        return AllProviderNames.Contains(arg, StringComparer.OrdinalIgnoreCase);
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "StoryModelBakeoff — Armenian story model bake-off.\n");
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  dotnet run --project tools/StoryModelBakeoff [-- <args>]");
        Console.WriteLine();
        Console.WriteLine("Args:");
        Console.WriteLine(
            "  --provider <name>            openai | claude | gemini | local | all  (default: all)");
        Console.WriteLine(
            "  --max-prompts N              Cap scenario count (live and dry-run).");
        Console.WriteLine(
            "  --run                        Execute live calls. F1.2 ships Claude only.");
        Console.WriteLine(
            "  --i-understand-live-cost     Required for any --run.");
        Console.WriteLine(
            "  --allow-full-set             Run all scenarios; XOR with --max-prompts.");
        Console.WriteLine(
            "  --help / -h                  Print this help.");
        Console.WriteLine();
        Console.WriteLine("Live invocation (Claude only in F1.2):");
        Console.WriteLine(
            "  dotnet run --project tools/StoryModelBakeoff -- \\");
        Console.WriteLine(
            "    --run --provider claude --i-understand-live-cost --max-prompts 1");
        Console.WriteLine();
        Console.WriteLine("Environment variables:");
        Console.WriteLine(
            "  OPENAI_API_KEY        / OPENAI_BAKEOFF_MODEL       (default: gpt-4o)");
        Console.WriteLine(
            "  ANTHROPIC_API_KEY     / ANTHROPIC_BAKEOFF_MODEL    (default: claude-opus-4-7)");
        Console.WriteLine(
            "  GEMINI_API_KEY        / GEMINI_BAKEOFF_MODEL       (default: gemini-2.5-pro)");
        Console.WriteLine(
            "  AAT_LOCAL_API_KEY     reserved for a future Armenian-local provider.");
    }

    // ---------- prompt-text helpers ----------

    // Strips a single leading "# ..." line (the source-of-truth comment
    // header in system-prompt.txt). Anything after the first newline is
    // returned untouched. If the first line is not a comment, the input
    // is returned as-is.
    private static string StripCommentHeader(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (!text.StartsWith("# ", StringComparison.Ordinal))
            return text;
        var nl = text.IndexOf('\n');
        if (nl < 0) return string.Empty;
        return text[(nl + 1)..];
    }

    // Normalize line endings + trim trailing whitespace so an editor-
    // added final newline (or trailing spaces in either copy) doesn't
    // produce a false drift warning. Internal whitespace is preserved.
    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
               .TrimEnd('\n', '\r', ' ', '\t');

    private static string Sha256Hex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    // ---------- production prompt drift check ----------

    // Walks up from cwd and from the binary location looking for
    // backend/src/ArmenianAiToy.Api/appsettings.json. On Windows
    // Path.GetFullPath is used to handle relative cwd execution.
    // Returns (Sha, ResolvedPath) — both null when not located.
    private static (string? Sha, string? Path) TryReadProductionPromptSha()
    {
        var rel = Path.Combine(
            "backend", "src", "ArmenianAiToy.Api", "appsettings.json");

        var roots = new List<string>
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };

        foreach (var root in roots)
        {
            var dir = new DirectoryInfo(root);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, rel);
                if (File.Exists(candidate))
                {
                    try
                    {
                        var json = File.ReadAllText(candidate);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty(
                                "SystemPrompt", out var prop)
                            && prop.ValueKind == JsonValueKind.String)
                        {
                            var prompt = NormalizeNewlines(
                                prop.GetString() ?? string.Empty);
                            return (Sha256Hex(prompt), candidate);
                        }
                    }
                    catch
                    {
                        // Quiet best-effort — drift check is advisory.
                    }
                    return (null, candidate);
                }
                dir = dir.Parent;
            }
        }
        return (null, null);
    }

    // ---------- scenario loading + validation ----------

    private sealed record TurnDoc(string? Role, string? Content);
    private sealed record ScenarioDoc(
        string? Id,
        string? Category,
        List<TurnDoc>? Turns);

    public sealed record Turn(string Role, string Content);
    public sealed record Scenario(string Id, string Category, List<Turn> Turns);

    private static List<Scenario> LoadScenarios(string path)
    {
        var json = File.ReadAllText(path);
        var docs = JsonSerializer.Deserialize<List<ScenarioDoc>>(json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidOperationException("empty file");

        var result = new List<Scenario>(docs.Count);
        foreach (var d in docs)
        {
            var turns = (d.Turns ?? new List<TurnDoc>())
                .Select(t => new Turn(t.Role ?? string.Empty, t.Content ?? string.Empty))
                .ToList();
            result.Add(new Scenario(
                d.Id ?? string.Empty,
                d.Category ?? string.Empty,
                turns));
        }
        return result;
    }

    private static void ValidateScenarios(List<Scenario> scenarios)
    {
        if (scenarios.Count == 0)
            throw new InvalidOperationException("no scenarios loaded");

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            if (string.IsNullOrWhiteSpace(s.Id))
                throw new InvalidOperationException(
                    $"scenario at index {i} has empty id");
            if (!seenIds.Add(s.Id))
                throw new InvalidOperationException(
                    $"duplicate scenario id: {s.Id}");
            if (string.IsNullOrWhiteSpace(s.Category))
                throw new InvalidOperationException(
                    $"scenario {s.Id} has empty category");
            if (s.Turns.Count == 0)
                throw new InvalidOperationException(
                    $"scenario {s.Id} has no turns");
            for (var t = 0; t < s.Turns.Count; t++)
            {
                var turn = s.Turns[t];
                if (turn.Role != "user")
                    throw new InvalidOperationException(
                        $"scenario {s.Id} turn {t} has role '{turn.Role}', " +
                        "only 'user' is allowed in the bake-off prompt set");
                if (string.IsNullOrWhiteSpace(turn.Content))
                    throw new InvalidOperationException(
                        $"scenario {s.Id} turn {t} has empty content");
            }
        }
    }

    // ---------- provider resolution ----------

    public sealed record ResolvedProvider(
        string Name,
        string Model,
        string EnvKeyName,
        bool KeyPresent,
        bool Selected);

    private static List<ResolvedProvider> ResolveProviders(string providerArg)
    {
        bool Want(string name) =>
            string.Equals(providerArg, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerArg, name, StringComparison.OrdinalIgnoreCase);

        bool HasKey(string envName) =>
            !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(envName));

        string Model(string envName, string fallback)
        {
            var v = Environment.GetEnvironmentVariable(envName);
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }

        return new List<ResolvedProvider>
        {
            new("openai",
                Model("OPENAI_BAKEOFF_MODEL", "gpt-4o"),
                "OPENAI_API_KEY",
                HasKey("OPENAI_API_KEY"),
                Want("openai")),
            new("claude",
                Model("ANTHROPIC_BAKEOFF_MODEL", "claude-opus-4-7"),
                "ANTHROPIC_API_KEY",
                HasKey("ANTHROPIC_API_KEY"),
                Want("claude")),
            new("gemini",
                Model("GEMINI_BAKEOFF_MODEL", "gemini-2.5-pro"),
                "GEMINI_API_KEY",
                HasKey("GEMINI_API_KEY"),
                Want("gemini")),
            new("local",
                Model("AAT_LOCAL_BAKEOFF_MODEL", "(reserved)"),
                "AAT_LOCAL_API_KEY",
                HasKey("AAT_LOCAL_API_KEY"),
                Want("local")),
        };
    }

    // ---------- dry-run plan output ----------

    private static void PrintPlan(
        List<ResolvedProvider> resolved,
        List<Scenario> scenarios,
        string bakeoffPromptPath,
        string bakeoffPromptSha,
        string? productionPath,
        string? productionPromptSha)
    {
        var bar = new string('=', 60);
        Console.WriteLine();
        Console.WriteLine(bar);
        Console.WriteLine("  StoryModelBakeoff — dry-run plan");
        Console.WriteLine(bar);

        // Provider matrix
        Console.WriteLine();
        Console.WriteLine("Providers:");
        foreach (var p in resolved)
        {
            string status;
            if (!p.Selected)              status = "not-selected";
            else if (p.Name == "local")   status = "reserved (no live path yet)";
            else if (!p.KeyPresent)       status = $"skipped (env {p.EnvKeyName} unset)";
            else                          status = "live-ready";
            Console.WriteLine(
                $"  - {p.Name,-7} model={p.Model,-25} status={status}");
        }

        // Scenario summary
        var totalTurns = scenarios.Sum(s => s.Turns.Count);
        Console.WriteLine();
        Console.WriteLine($"Scenarios: {scenarios.Count}");
        Console.WriteLine($"Total turns across all scenarios: {totalTurns}");
        foreach (var s in scenarios)
        {
            var preview = s.Turns[0].Content;
            if (preview.Length > 60) preview = preview[..60] + "...";
            Console.WriteLine(
                $"  {s.Id} [{s.Category}] turns={s.Turns.Count}  " +
                $"first=\"{preview}\"");
        }

        // Estimated calls
        var liveProviders = resolved
            .Where(p => p.Selected && p.KeyPresent && p.Name != "local")
            .ToList();
        Console.WriteLine();
        Console.WriteLine("Estimated calls per provider (per --run):");
        var totalCalls = 0;
        foreach (var p in liveProviders)
        {
            var perProvider = totalTurns;
            totalCalls += perProvider;
            Console.WriteLine($"  {p.Name,-7} calls = {perProvider}");
        }
        if (liveProviders.Count == 0)
        {
            Console.WriteLine(
                "  (none — no live-ready provider in the selected matrix)");
        }
        Console.WriteLine($"  TOTAL                = {totalCalls}");

        // Prompt-text identity
        Console.WriteLine();
        Console.WriteLine("Prompt identity:");
        Console.WriteLine($"  bake-off  sha256 = {bakeoffPromptSha}");
        Console.WriteLine($"            path   = {bakeoffPromptPath}");
        if (productionPromptSha is null)
        {
            Console.WriteLine("  production sha256 = (not located)");
            if (productionPath is not null)
                Console.WriteLine(
                    $"            path   = {productionPath} (read failed)");
            else
                Console.WriteLine(
                    "            path   = (appsettings.json not found from cwd or binary dir)");
        }
        else
        {
            Console.WriteLine($"  production sha256 = {productionPromptSha}");
            Console.WriteLine($"            path   = {productionPath}");
            if (string.Equals(productionPromptSha, bakeoffPromptSha,
                StringComparison.Ordinal))
            {
                Console.WriteLine("  drift   = none (hashes match)");
            }
            else
            {
                Console.WriteLine(
                    "  drift   = WARNING — bake-off prompt has drifted from " +
                    "production. Refresh tools/StoryModelBakeoff/system-prompt.txt " +
                    "and update the # Source comment line.");
            }
        }

        // Live execution hint
        Console.WriteLine();
        Console.WriteLine("Live execution:");
        Console.WriteLine(
            "  F1.2 supports Claude only. Re-run with --run --provider claude " +
            "--i-understand-live-cost and either --max-prompts N or " +
            "--allow-full-set.");
        Console.WriteLine(
            "  ANTHROPIC_API_KEY must be set in your environment.");
        Console.WriteLine(
            "  OpenAI / Gemini live execution remain deferred to a later F1 slice.");

        Console.WriteLine(bar);
    }

    // ---------- live execution (F1.2) ----------

    private sealed record TurnResult(
        string UserContent,
        string AssistantContent,
        string? StopReason,
        long LatencyMs,
        int? InputTokens,
        int? OutputTokens,
        string? ErrorKind,
        string? ErrorMessage);

    private sealed record ScenarioResult(
        Scenario Scenario,
        List<TurnResult> Turns);

    private sealed record RunResult(
        DateTime StartedUtc,
        DateTime CompletedUtc,
        DateTime? InterruptedUtc,
        ResolvedProvider Claude,
        string BakeoffPromptSha,
        string? ProductionPromptSha,
        bool DriftDetected,
        List<ScenarioResult> Scenarios);

    private static async Task<int> RunLiveClaudeAsync(
        string apiKey,
        ResolvedProvider claude,
        List<Scenario> scenarios,
        string bakeoffPromptText,
        string bakeoffPromptSha,
        string? productionPromptSha)
    {
        var totalTurns = scenarios.Sum(s => s.Turns.Count);
        var resultsRoot = Path.Combine(
            AppContext.BaseDirectory, "results");
        var runStamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var runDir = Path.Combine(resultsRoot, runStamp);

        // Print pre-execution plan (no API key, no system prompt body).
        var bar = new string('=', 60);
        Console.WriteLine();
        Console.WriteLine(bar);
        Console.WriteLine("  StoryModelBakeoff — live Claude run");
        Console.WriteLine(bar);
        Console.WriteLine($"  Provider:   claude");
        Console.WriteLine($"  Model:      {claude.Model}");
        Console.WriteLine($"  Scenarios:  {scenarios.Count}");
        Console.WriteLine($"  Turns:      {totalTurns}");
        Console.WriteLine($"  Calls:      {totalTurns}");
        Console.WriteLine($"  Output dir: {runDir}");
        Console.WriteLine($"  Prompt sha: {bakeoffPromptSha}");
        if (productionPromptSha is not null
            && !string.Equals(bakeoffPromptSha, productionPromptSha,
                StringComparison.Ordinal))
        {
            Console.WriteLine(
                "  WARNING — bake-off prompt has drifted from production. " +
                "Refresh system-prompt.txt before relying on these results.");
        }
        Console.WriteLine();
        Console.WriteLine(
            "  Ctrl-C now if this is unexpected — the run starts immediately.");
        Console.WriteLine(bar);
        Console.WriteLine();

        // ---- cancellation wiring ----
        using var cts = new CancellationTokenSource();
        var sigintHandler = new ConsoleCancelEventHandler((s, e) =>
        {
            // Suppress the default process-termination so we can flush.
            e.Cancel = true;
            // Already cancelled? second Ctrl-C → let the runtime kill us.
            if (cts.IsCancellationRequested) return;
            Console.Error.WriteLine(
                "  ^C received — finishing current call, then flushing partial " +
                "results.");
            cts.Cancel();
        });
        Console.CancelKeyPress += sigintHandler;

        var startedUtc = DateTime.UtcNow;
        var scenarioResults = new List<ScenarioResult>();
        DateTime? interruptedUtc = null;

        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds),
            };
            // Headers per the Anthropic Messages API contract. The
            // x-api-key header is the only place the key appears in
            // memory; never logged, never persisted.
            http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            http.DefaultRequestHeaders.Add(
                "anthropic-version", AnthropicVersion);

            for (var si = 0; si < scenarios.Count; si++)
            {
                if (cts.IsCancellationRequested)
                {
                    interruptedUtc = DateTime.UtcNow;
                    break;
                }

                var scenario = scenarios[si];
                var turnResults = new List<TurnResult>(scenario.Turns.Count);

                // Per-scenario rolling history. `messages` is the array
                // sent to Anthropic on every turn, growing as we
                // accumulate user/assistant pairs.
                var messages = new List<(string Role, string Content)>();
                var scenarioFailed = false;

                for (var ti = 0; ti < scenario.Turns.Count; ti++)
                {
                    if (cts.IsCancellationRequested)
                    {
                        interruptedUtc = DateTime.UtcNow;
                        // Whatever turns remain in this scenario are
                        // skipped via the loop break; outer loop will
                        // stop on the next iteration.
                        break;
                    }

                    var turn = scenario.Turns[ti];
                    var label =
                        $"[{scenario.Id} t{ti + 1}/{scenario.Turns.Count} claude]";

                    if (scenarioFailed)
                    {
                        // Earlier turn in this scenario failed; mark
                        // remaining turns and don't make any call.
                        Console.WriteLine($"{label} skipped (prior turn failed)");
                        turnResults.Add(new TurnResult(
                            UserContent: turn.Content,
                            AssistantContent: string.Empty,
                            StopReason: null,
                            LatencyMs: 0,
                            InputTokens: null,
                            OutputTokens: null,
                            ErrorKind: "skipped_due_to_prior_error",
                            ErrorMessage: null));
                        continue;
                    }

                    messages.Add(("user", turn.Content));

                    TurnResult result;
                    try
                    {
                        result = await CallClaudeOnceAsync(
                            http: http,
                            model: claude.Model,
                            systemPrompt: bakeoffPromptText,
                            messages: messages,
                            userContent: turn.Content,
                            ct: cts.Token);
                    }
                    catch (OperationCanceledException)
                        when (cts.IsCancellationRequested)
                    {
                        // User Ctrl-C — bubble up to outer cancellation
                        // handling. Don't record a turn for this; the
                        // user explicitly stopped mid-call.
                        interruptedUtc = DateTime.UtcNow;
                        break;
                    }

                    turnResults.Add(result);

                    if (result.ErrorKind is null)
                    {
                        // Success — append assistant reply to history
                        // so subsequent turns see the conversation.
                        messages.Add(("assistant", result.AssistantContent));
                        Console.WriteLine(
                            $"{label} ok {result.LatencyMs}ms " +
                            $"{result.OutputTokens?.ToString() ?? "?"}out");
                    }
                    else
                    {
                        scenarioFailed = true;
                        Console.WriteLine(
                            $"{label} FAIL {result.ErrorKind} {result.LatencyMs}ms");
                    }
                }

                scenarioResults.Add(new ScenarioResult(scenario, turnResults));

                if (cts.IsCancellationRequested && interruptedUtc is not null)
                    break;
            }
        }
        finally
        {
            Console.CancelKeyPress -= sigintHandler;
        }

        var completedUtc = DateTime.UtcNow;
        var driftDetected = productionPromptSha is not null
            && !string.Equals(productionPromptSha, bakeoffPromptSha,
                StringComparison.Ordinal);

        var runResult = new RunResult(
            StartedUtc: startedUtc,
            CompletedUtc: completedUtc,
            InterruptedUtc: interruptedUtc,
            Claude: claude,
            BakeoffPromptSha: bakeoffPromptSha,
            ProductionPromptSha: productionPromptSha,
            DriftDetected: driftDetected,
            Scenarios: scenarioResults);

        await WriteRunResultsAsync(runDir, runResult);

        Console.WriteLine();
        Console.WriteLine($"Wrote results: {runDir}");
        Console.WriteLine(
            "  results.json (machine), review.md (human), summary.json (totals)");

        return 0;
    }

    private static async Task<TurnResult> CallClaudeOnceAsync(
        HttpClient http,
        string model,
        string systemPrompt,
        List<(string Role, string Content)> messages,
        string userContent,
        CancellationToken ct)
    {
        // Build request body.
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = ClaudeMaxTokens,
            ["system"] = systemPrompt,
            ["messages"] = messages
                .Select(m => new Dictionary<string, object?>
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content,
                })
                .ToList(),
        };

        var jsonOpts = LiveJsonOptions();
        var bodyJson = JsonSerializer.Serialize(requestBody, jsonOpts);
        using var content = new StringContent(
            bodyJson, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        HttpResponseMessage? resp = null;
        try
        {
            resp = await http.PostAsync(AnthropicEndpoint, content, ct);

            if (!resp.IsSuccessStatusCode)
            {
                sw.Stop();
                var status = (int)resp.StatusCode;
                string detail;
                try
                {
                    detail = await resp.Content.ReadAsStringAsync(ct);
                }
                catch
                {
                    detail = "<failed to read response body>";
                }
                return new TurnResult(
                    UserContent: userContent,
                    AssistantContent: string.Empty,
                    StopReason: null,
                    LatencyMs: sw.ElapsedMilliseconds,
                    InputTokens: null,
                    OutputTokens: null,
                    ErrorKind: $"http_{status}",
                    ErrorMessage: Truncate(detail, 500));
            }

            string raw;
            try
            {
                raw = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new TurnResult(
                    UserContent: userContent,
                    AssistantContent: string.Empty,
                    StopReason: null,
                    LatencyMs: sw.ElapsedMilliseconds,
                    InputTokens: null,
                    OutputTokens: null,
                    ErrorKind: "network",
                    ErrorMessage: Truncate(ex.GetType().Name + ": " + ex.Message, 500));
            }
            sw.Stop();

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var text = ExtractAssistantText(root);
                string? stopReason = null;
                if (root.TryGetProperty("stop_reason", out var sr)
                    && sr.ValueKind == JsonValueKind.String)
                {
                    stopReason = sr.GetString();
                }
                int? inputTokens = null;
                int? outputTokens = null;
                if (root.TryGetProperty("usage", out var usage)
                    && usage.ValueKind == JsonValueKind.Object)
                {
                    if (usage.TryGetProperty("input_tokens", out var it)
                        && it.ValueKind == JsonValueKind.Number)
                    {
                        inputTokens = it.GetInt32();
                    }
                    if (usage.TryGetProperty("output_tokens", out var ot)
                        && ot.ValueKind == JsonValueKind.Number)
                    {
                        outputTokens = ot.GetInt32();
                    }
                }

                return new TurnResult(
                    UserContent: userContent,
                    AssistantContent: text,
                    StopReason: stopReason,
                    LatencyMs: sw.ElapsedMilliseconds,
                    InputTokens: inputTokens,
                    OutputTokens: outputTokens,
                    ErrorKind: null,
                    ErrorMessage: null);
            }
            catch (JsonException jex)
            {
                return new TurnResult(
                    UserContent: userContent,
                    AssistantContent: string.Empty,
                    StopReason: null,
                    LatencyMs: sw.ElapsedMilliseconds,
                    InputTokens: null,
                    OutputTokens: null,
                    ErrorKind: "parse",
                    ErrorMessage: Truncate(jex.Message, 500));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException tce)
        {
            // HttpClient timeout (Timeout fired without our token).
            sw.Stop();
            return new TurnResult(
                UserContent: userContent,
                AssistantContent: string.Empty,
                StopReason: null,
                LatencyMs: sw.ElapsedMilliseconds,
                InputTokens: null,
                OutputTokens: null,
                ErrorKind: "timeout",
                ErrorMessage: Truncate(tce.Message, 500));
        }
        catch (HttpRequestException hre)
        {
            sw.Stop();
            return new TurnResult(
                UserContent: userContent,
                AssistantContent: string.Empty,
                StopReason: null,
                LatencyMs: sw.ElapsedMilliseconds,
                InputTokens: null,
                OutputTokens: null,
                ErrorKind: "network",
                ErrorMessage: Truncate(hre.GetType().Name + ": " + hre.Message, 500));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TurnResult(
                UserContent: userContent,
                AssistantContent: string.Empty,
                StopReason: null,
                LatencyMs: sw.ElapsedMilliseconds,
                InputTokens: null,
                OutputTokens: null,
                ErrorKind: "network",
                ErrorMessage: Truncate(ex.GetType().Name + ": " + ex.Message, 500));
        }
        finally
        {
            resp?.Dispose();
        }
    }

    private static string ExtractAssistantText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var contentEl)
            || contentEl.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }
        var sb = new StringBuilder();
        foreach (var block in contentEl.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object) continue;
            if (!block.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String) continue;
            if (typeEl.GetString() != "text") continue;
            if (block.TryGetProperty("text", out var textEl)
                && textEl.ValueKind == JsonValueKind.String)
            {
                sb.Append(textEl.GetString());
            }
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length <= maxChars) return s;
        return s[..maxChars] + "…";
    }

    private static JsonSerializerOptions LiveJsonOptions()
        => new()
        {
            WriteIndented = true,
            // Preserve Armenian script (and other non-ASCII) literally
            // in the JSON output instead of \u-escaping it. The bake-off
            // result files are intended to be read by a human.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

    // ---------- result writers ----------

    private static async Task WriteRunResultsAsync(
        string runDir, RunResult run)
    {
        Directory.CreateDirectory(runDir);

        var resultsJson = BuildResultsJson(run);
        var summaryJson = BuildSummaryJson(run);
        var review = BuildReviewMarkdown(run);

        await WriteAtomicAsync(
            Path.Combine(runDir, "results.json"), resultsJson);
        await WriteAtomicAsync(
            Path.Combine(runDir, "summary.json"), summaryJson);
        await WriteAtomicAsync(
            Path.Combine(runDir, "review.md"), review);
    }

    private static async Task WriteAtomicAsync(string path, string content)
    {
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, content, new UTF8Encoding(false));
        // Move with overwrite — succeeds whether the destination exists
        // (re-run scenarios) or not.
        File.Move(tmp, path, overwrite: true);
    }

    private static string BuildResultsJson(RunResult run)
    {
        var jsonOpts = LiveJsonOptions();

        var scenariosOut = run.Scenarios.Select(sr => new
        {
            id = sr.Scenario.Id,
            category = sr.Scenario.Category,
            turns = sr.Scenario.Turns
                .Select(t => new { role = t.Role, content = t.Content })
                .ToList(),
            results = new Dictionary<string, object?>
            {
                ["claude"] = new
                {
                    model = run.Claude.Model,
                    turns = sr.Turns.Select(tr => new
                    {
                        userContent = tr.UserContent,
                        assistantContent = tr.AssistantContent,
                        stopReason = tr.StopReason,
                        latencyMs = tr.LatencyMs,
                        tokenUsage = new
                        {
                            input = tr.InputTokens,
                            output = tr.OutputTokens,
                        },
                        errorKind = tr.ErrorKind,
                        errorMessage = tr.ErrorMessage,
                    }).ToList(),
                },
            },
        }).ToList();

        var doc = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["runStartedUtc"] = run.StartedUtc.ToString("o"),
            ["runCompletedUtc"] = run.CompletedUtc.ToString("o"),
        };
        if (run.InterruptedUtc is not null)
        {
            doc["runInterruptedUtc"] = run.InterruptedUtc.Value.ToString("o");
        }
        doc["promptIdentity"] = new
        {
            bakeoffPromptSha256 = run.BakeoffPromptSha,
            productionPromptSha256 = run.ProductionPromptSha,
            driftDetected = run.DriftDetected,
        };
        doc["providers"] = new[]
        {
            new { name = "claude", model = run.Claude.Model },
        };
        doc["scenarios"] = scenariosOut;

        return JsonSerializer.Serialize(doc, jsonOpts);
    }

    private static string BuildSummaryJson(RunResult run)
    {
        var jsonOpts = LiveJsonOptions();

        var totalTurns = run.Scenarios.Sum(s => s.Turns.Count);
        var attempted = run.Scenarios
            .SelectMany(s => s.Turns)
            .Count(t => t.ErrorKind != "skipped_due_to_prior_error");
        var succeeded = run.Scenarios
            .SelectMany(s => s.Turns)
            .Count(t => t.ErrorKind is null);
        var failed = run.Scenarios
            .SelectMany(s => s.Turns)
            .Count(t => t.ErrorKind is not null
                     && t.ErrorKind != "skipped_due_to_prior_error");
        var totalLatencyMs = run.Scenarios
            .SelectMany(s => s.Turns)
            .Sum(t => t.LatencyMs);
        var inputTokens = run.Scenarios
            .SelectMany(s => s.Turns)
            .Sum(t => (long)(t.InputTokens ?? 0));
        var outputTokens = run.Scenarios
            .SelectMany(s => s.Turns)
            .Sum(t => (long)(t.OutputTokens ?? 0));

        var doc = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["runStartedUtc"] = run.StartedUtc.ToString("o"),
            ["runCompletedUtc"] = run.CompletedUtc.ToString("o"),
        };
        if (run.InterruptedUtc is not null)
        {
            doc["runInterruptedUtc"] = run.InterruptedUtc.Value.ToString("o");
        }
        doc["totalScenarios"] = run.Scenarios.Count;
        doc["totalTurns"] = totalTurns;
        doc["providers"] = new Dictionary<string, object?>
        {
            ["claude"] = new
            {
                model = run.Claude.Model,
                callsAttempted = attempted,
                callsSucceeded = succeeded,
                callsFailed = failed,
                totalLatencyMs = totalLatencyMs,
                tokenUsage = new
                {
                    input = inputTokens,
                    output = outputTokens,
                },
            },
        };
        return JsonSerializer.Serialize(doc, jsonOpts);
    }

    private static string BuildReviewMarkdown(RunResult run)
    {
        var sb = new StringBuilder();
        var totalTurns = run.Scenarios.Sum(s => s.Turns.Count);

        sb.AppendLine("# StoryModelBakeoff — F1.2 Claude review");
        sb.AppendLine();
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Run started (UTC) | {run.StartedUtc:o} |");
        sb.AppendLine($"| Run completed (UTC) | {run.CompletedUtc:o} |");
        if (run.InterruptedUtc is not null)
        {
            sb.AppendLine(
                $"| Run interrupted (UTC) | {run.InterruptedUtc.Value:o} |");
        }
        sb.AppendLine($"| Provider | claude |");
        sb.AppendLine($"| Model | {run.Claude.Model} |");
        sb.AppendLine($"| Scenarios | {run.Scenarios.Count} |");
        sb.AppendLine($"| Total turns | {totalTurns} |");
        sb.AppendLine($"| bake-off prompt sha256 | `{run.BakeoffPromptSha}` |");
        sb.AppendLine(
            $"| production prompt sha256 | `{run.ProductionPromptSha ?? "(not located)"}` |");
        sb.AppendLine(
            $"| Drift | {(run.DriftDetected ? "**WARNING — drifted**" : "none")} |");
        sb.AppendLine();
        sb.AppendLine(
            "OpenAI / Gemini are NOT in F1.2; review live OpenAI output via " +
            "the existing `tools/StoryBenchmark` runs.");
        sb.AppendLine();

        foreach (var sr in run.Scenarios)
        {
            sb.AppendLine($"## {sr.Scenario.Id} — {sr.Scenario.Category}");
            sb.AppendLine();
            sb.AppendLine("**Prompt turns:**");
            sb.AppendLine();
            for (var i = 0; i < sr.Scenario.Turns.Count; i++)
            {
                sb.AppendLine(
                    $"{i + 1}. «{sr.Scenario.Turns[i].Content}»");
            }
            sb.AppendLine();
            sb.AppendLine($"### claude ({run.Claude.Model})");
            sb.AppendLine();

            for (var ti = 0; ti < sr.Turns.Count; ti++)
            {
                var tr = sr.Turns[ti];
                if (tr.ErrorKind is null)
                {
                    sb.AppendLine(
                        $"**Turn {ti + 1} ({tr.LatencyMs} ms)**");
                    sb.AppendLine();
                    sb.AppendLine(QuoteMd(tr.AssistantContent));
                    sb.AppendLine();
                    var tokInOut =
                        $"{tr.InputTokens?.ToString() ?? "?"} / " +
                        $"{tr.OutputTokens?.ToString() ?? "?"}";
                    sb.AppendLine(
                        $"*stop_reason: {tr.StopReason ?? "(none)"} — " +
                        $"tokens in/out: {tokInOut}*");
                    sb.AppendLine();
                }
                else if (tr.ErrorKind == "skipped_due_to_prior_error")
                {
                    sb.AppendLine(
                        $"**Turn {ti + 1} — skipped (prior turn failed)**");
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine(
                        $"**Turn {ti + 1} — FAILED ({tr.LatencyMs} ms)**");
                    sb.AppendLine();
                    sb.AppendLine($"- errorKind: `{tr.ErrorKind}`");
                    if (!string.IsNullOrEmpty(tr.ErrorMessage))
                    {
                        sb.AppendLine($"- errorMessage:");
                        sb.AppendLine();
                        sb.AppendLine("```");
                        sb.AppendLine(tr.ErrorMessage);
                        sb.AppendLine("```");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("#### Manual scoring (claude, full scenario)");
            sb.AppendLine();
            sb.AppendLine("- Armenian naturalness — _ / 5");
            sb.AppendLine("- Eastern Armenian correctness — _ / 5");
            sb.AppendLine("- Fairy-tale feeling — _ / 5");
            sb.AppendLine("- Warmth for age 4–7 — _ / 5");
            sb.AppendLine("- Length / pacing — _ / 5");
            sb.AppendLine("- Choice quality — _ / 5");
            sb.AppendLine("- Continuation coherence — _ / 5");
            sb.AppendLine("- Safety / age appropriateness — pass / fail");
            sb.AppendLine(
                "- \"Would I let Areg say this aloud?\" — yes / no");
            sb.AppendLine("- Notes:");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string QuoteMd(string text)
    {
        if (string.IsNullOrEmpty(text)) return "> _(empty)_";
        var sb = new StringBuilder();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            sb.Append("> ").Append(lines[i]);
            if (i < lines.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }
}
