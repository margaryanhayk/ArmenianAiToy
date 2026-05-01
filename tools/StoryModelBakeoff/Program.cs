// StoryModelBakeoff — F1 slice 1 (DRY-RUN ONLY).
//
// Compares Armenian story-generation quality across model providers
// (OpenAI, Anthropic Claude, Google Gemini, reserved local slot) on a
// frozen prompt set, with the production system prompt held constant.
//
// THIS SLICE DOES NOT MAKE ANY NETWORK CALL. `--run` is recognized
// but explicitly short-circuited — live execution is F1 slice 2.
//
// Hard contract for this slice:
//  - No HttpClient.SendAsync, no DNS lookup, no listening socket.
//  - No backend dependency; this binary does not load AppDbContext,
//    ChatService, ModerationService, or any Domain entity.
//  - No PackageReference, no ProjectReference. Only the BCL.
//  - Reads bakeoff-prompts.json + system-prompt.txt next to the
//    binary (CopyToOutputDirectory in csproj). Reads
//    backend/src/ArmenianAiToy.Api/appsettings.json from the repo
//    tree to compute a drift SHA-256 against the bake-off's frozen
//    copy of the production prompt.
//
// Usage:
//   dotnet run --project tools/StoryModelBakeoff
//   dotnet run --project tools/StoryModelBakeoff -- --provider claude
//   dotnet run --project tools/StoryModelBakeoff -- --max-prompts 3
//   dotnet run --project tools/StoryModelBakeoff -- --help

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArmenianAiToy.Tools.StoryModelBakeoff;

internal static class Program
{
    private static readonly string[] AllProviderNames =
        { "openai", "claude", "gemini", "local" };

    public static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        var live = args.Contains("--run");
        var providerArg = ParseStringArg(args, "--provider") ?? "all";
        var maxPromptsArg = ParseIntArg(args, "--max-prompts");

        if (live)
        {
            Console.Error.WriteLine(
                "Live provider calls are deferred to F1 slice 2.");
            return 2;
        }

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

        // ---- compute bake-off system-prompt SHA-256 ----
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

        if (maxPromptsArg is { } cap && cap < scenarios.Count)
            scenarios = scenarios.Take(cap).ToList();

        // ---- resolve providers ----
        var resolved = ResolveProviders(providerArg);

        // ---- print plan ----
        PrintPlan(
            resolved,
            scenarios,
            bakeoffPromptPath: systemPromptPath,
            bakeoffPromptSha: bakeoffPromptSha,
            productionPath: productionPath,
            productionPromptSha: productionPromptSha);

        return 0;
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
            "StoryModelBakeoff — Armenian story model bake-off (F1 slice 1: dry-run only).\n");
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  dotnet run --project tools/StoryModelBakeoff [-- <args>]");
        Console.WriteLine();
        Console.WriteLine("Args:");
        Console.WriteLine(
            "  --provider <name>    openai | claude | gemini | local | all  (default: all)");
        Console.WriteLine(
            "  --max-prompts N      Cap scenario count for cheap iteration.");
        Console.WriteLine(
            "  --run                RECOGNIZED but slice 2 only — exits with a notice.");
        Console.WriteLine(
            "  --help / -h          Print this help.");
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

    // ---------- plan output ----------

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
        Console.WriteLine("  StoryModelBakeoff — F1 slice 1 dry-run plan");
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
        Console.WriteLine("Estimated calls (slice 2 only — nothing fires today):");
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
        Console.WriteLine(
            $"  bake-off  sha256 = {bakeoffPromptSha}");
        Console.WriteLine(
            $"            path   = {bakeoffPromptPath}");
        if (productionPromptSha is null)
        {
            Console.WriteLine(
                $"  production sha256 = (not located)");
            if (productionPath is not null)
                Console.WriteLine(
                    $"            path   = {productionPath} (read failed)");
            else
                Console.WriteLine(
                    "            path   = (appsettings.json not found from cwd or binary dir)");
        }
        else
        {
            Console.WriteLine(
                $"  production sha256 = {productionPromptSha}");
            Console.WriteLine(
                $"            path   = {productionPath}");
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

        // Slice-2 results dir (advisory — not created in slice 1)
        var resultsDirPlanned = Path.Combine(
            AppContext.BaseDirectory, "results");
        Console.WriteLine();
        Console.WriteLine("Results directory (slice 2 will populate this):");
        Console.WriteLine($"  {resultsDirPlanned}");
        Console.WriteLine(
            "  (gitignored via .gitignore -> tools/StoryModelBakeoff/results/)");

        Console.WriteLine();
        Console.WriteLine(
            "Slice 1 stops here. Live provider calls are deferred to F1 slice 2.");
        Console.WriteLine(bar);
    }
}
