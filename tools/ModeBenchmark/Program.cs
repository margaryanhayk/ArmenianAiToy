using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

// D2.1 — End-to-end mode-routing scaffold.
//
// CLI: first positional arg is baseUrl. No --write-baseline support in
// this slice — the committed baseline ships with Placeholder=true and
// the first real capture is a separate D2.1.bench follow-up slice.
//
// Per-scenario contract:
//   1. Register a fresh device via POST /api/devices/register.
//   2. POST /api/chat with { "message": <scenario.message> }.
//   3. For "hard" scenarios (expectedMode != null):
//        pass iff HTTP 200 AND resp.Mode == expectedMode.
//   4. For "soft" scenarios (expectedMode == null):
//        pass iff HTTP 200; observed mode is recorded but not asserted.
//        Soft scenarios do NOT count against ModeMatches.
//
// D1-F2 contract is mirrored: SHA-256 of scenarios.json is included in
// summary.json; baseline-side hash mismatch flips the verdict to
// "unavailable" with promptsChanged=true.
var positional = args.Where(a => !a.StartsWith("--")).ToArray();
var baseUrl = positional.Length > 0 ? positional[0] : "http://localhost:5000";
var scenariosPath = Path.Combine(AppContext.BaseDirectory, "scenarios.json");
var baselinePath = Path.Combine(AppContext.BaseDirectory, "baseline.json");
var resultsDir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(resultsDir);

// Pin scenario-set identity (D1-F2). Hash is the SHA-256 of the raw
// scenarios.json bytes; count comes from the deserialized list below.
var scenariosBytes = await File.ReadAllBytesAsync(scenariosPath);
var scenariosSha256 = Convert.ToHexString(
    System.Security.Cryptography.SHA256.HashData(scenariosBytes)).ToLowerInvariant();

var jsonOpts = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

// Load scenarios early so the self-check runs before any network call.
var scenarios = JsonSerializer.Deserialize<List<Scenario>>(
    await File.ReadAllTextAsync(scenariosPath), jsonOpts)
    ?? throw new Exception("Failed to load scenarios.json");

// --- Inline self-check (runs BEFORE any HttpClient construction) ---
// Every scenario must have a non-empty id and message; ids must be
// unique; expectedMode (when non-null) must be in the allowed set.
{
    string[] allowedModes = { "story", "game", "riddle", "curiosity", "calm" };
    var ids = new HashSet<string>(StringComparer.Ordinal);
    foreach (var s in scenarios)
    {
        if (string.IsNullOrWhiteSpace(s.Id))
        {
            Console.WriteLine("[selfcheck] FAILED: scenario has empty id");
            return 2;
        }
        if (!ids.Add(s.Id))
        {
            Console.WriteLine($"[selfcheck] FAILED: duplicate scenario id '{s.Id}'");
            return 2;
        }
        if (string.IsNullOrWhiteSpace(s.Message))
        {
            Console.WriteLine($"[selfcheck] FAILED: scenario {s.Id} has empty message");
            return 2;
        }
        if (s.ExpectedMode is not null
            && Array.IndexOf(allowedModes, s.ExpectedMode) < 0)
        {
            Console.WriteLine(
                $"[selfcheck] FAILED: scenario {s.Id} expectedMode '{s.ExpectedMode}' not in allowed set");
            return 2;
        }
    }
}

Console.WriteLine($"ModeBenchmark target: {baseUrl}");
Console.WriteLine($"Loaded {scenarios.Count} scenarios\n");

int hardCount = scenarios.Count(s => s.ExpectedMode is not null);
int softCount = scenarios.Count(s => s.ExpectedMode is null);
int scenariosOk = 0;
int modeMatches = 0;
var failures = new List<string>();
var results = new List<ScenarioResult>();

Console.WriteLine("ID    | Kind | Expected   | Observed   | Status");
Console.WriteLine("------|------|------------|------------|-------");

foreach (var scenario in scenarios)
{
    var sResult = new ScenarioResult
    {
        Id = scenario.Id,
        Message = scenario.Message,
        ExpectedMode = scenario.ExpectedMode,
    };

    // Fresh HttpClient + device per scenario so any per-conversation
    // state in ChatService starts clean for this assertion.
    using var http = new HttpClient
    {
        BaseAddress = new Uri(baseUrl),
        Timeout = TimeSpan.FromSeconds(60),
    };

    DeviceReg device;
    try
    {
        var regBody = new { macAddress = $"MBENCH-{scenario.Id}-{DateTime.UtcNow:HHmmssfff}" };
        var regResp = await http.PostAsJsonAsync("/api/devices/register", regBody);
        regResp.EnsureSuccessStatusCode();
        device = await regResp.Content.ReadFromJsonAsync<DeviceReg>(jsonOpts)
            ?? throw new Exception("device registration returned null");
    }
    catch (Exception ex)
    {
        sResult.Error = $"device registration failed: {ex.Message}";
        failures.Add($"{scenario.Id}: device registration failed — {ex.Message}");
        results.Add(sResult);
        Console.WriteLine($" {scenario.Id} | {(scenario.ExpectedMode is null ? "soft" : "hard")} | reg-fail");
        continue;
    }
    http.DefaultRequestHeaders.Add("X-Device-Id", device.DeviceId.ToString());
    http.DefaultRequestHeaders.Add("X-Api-Key", device.ApiKey);

    ChatResponseShape? resp = null;
    int httpStatus = 0;
    try
    {
        var body = new { message = scenario.Message };
        var httpResp = await http.PostAsJsonAsync("/api/chat", body);
        httpStatus = (int)httpResp.StatusCode;
        if (!httpResp.IsSuccessStatusCode)
        {
            sResult.HttpStatus = httpStatus;
            sResult.Error = $"HTTP {httpStatus}";
            failures.Add($"{scenario.Id}: HTTP {httpStatus}");
            results.Add(sResult);
            Console.WriteLine($" {scenario.Id} | {(scenario.ExpectedMode is null ? "soft" : "hard")} | HTTP {httpStatus}");
            continue;
        }
        resp = await httpResp.Content.ReadFromJsonAsync<ChatResponseShape>(jsonOpts);
    }
    catch (Exception ex)
    {
        sResult.HttpStatus = httpStatus;
        sResult.Error = ex.Message;
        failures.Add($"{scenario.Id}: chat request failed — {ex.Message}");
        results.Add(sResult);
        Console.WriteLine($" {scenario.Id} | {(scenario.ExpectedMode is null ? "soft" : "hard")} | error");
        continue;
    }

    sResult.HttpStatus = httpStatus;
    sResult.ObservedMode = resp?.Mode;
    var responseText = resp?.Response ?? "";
    sResult.ResponseSnippet = responseText.Length > 120
        ? responseText.Substring(0, 120) + "…"
        : responseText;

    bool isHard = scenario.ExpectedMode is not null;
    bool ok;
    if (isHard)
    {
        ok = httpStatus == 200
             && string.Equals(resp?.Mode, scenario.ExpectedMode, StringComparison.Ordinal);
        if (ok)
        {
            modeMatches++;
        }
        else
        {
            failures.Add(
                $"{scenario.Id}: expected mode='{scenario.ExpectedMode}' got '{resp?.Mode ?? "(null)"}'");
        }
    }
    else
    {
        // Soft scenarios: HTTP 200 is the only requirement. We record
        // the observed mode but never increment ModeMatches and never
        // fail on a particular mode value.
        ok = httpStatus == 200;
        if (!ok)
        {
            failures.Add($"{scenario.Id}: soft scenario non-200 ({httpStatus})");
        }
    }
    sResult.Ok = ok;
    if (ok) scenariosOk++;
    results.Add(sResult);

    var kindLabel = isHard ? "hard" : "soft";
    var expectedLabel = scenario.ExpectedMode ?? "(any)";
    var observedLabel = resp?.Mode ?? "(null)";
    var statusLabel = ok ? "PASS" : "FAIL";
    Console.WriteLine(
        $" {scenario.Id} | {kindLabel} | {expectedLabel,-10} | {observedLabel,-10} | {statusLabel}");
}

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("  MODE BENCHMARK SUMMARY");
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine($"  Total scenarios:   {scenarios.Count}  (hard {hardCount} / soft {softCount})");
Console.WriteLine($"  Scenarios passed:  {scenariosOk}/{scenarios.Count}");
Console.WriteLine($"  Mode matches:      {modeMatches}/{hardCount}");

// --- Save per-scenario results + markdown ---
var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
var resultsJson = Path.Combine(resultsDir, $"run_{timestamp}.json");
var resultsMd = Path.Combine(resultsDir, $"run_{timestamp}.md");

await File.WriteAllTextAsync(resultsJson, JsonSerializer.Serialize(results, jsonOpts));

var md = new System.Text.StringBuilder();
md.AppendLine("# ModeBenchmark Results");
md.AppendLine();
md.AppendLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
md.AppendLine($"**Target:** {baseUrl}");
md.AppendLine($"**Scenarios:** {scenarios.Count}  (hard {hardCount} / soft {softCount})");
md.AppendLine();
md.AppendLine("| ID | Kind | Message | Expected | Observed | Status |");
md.AppendLine("|----|------|---------|----------|----------|--------|");
foreach (var r in results)
{
    var kind = r.ExpectedMode is null ? "soft" : "hard";
    var expected = r.ExpectedMode ?? "(any)";
    var observed = r.ObservedMode ?? "(null)";
    var status = r.Ok ? "PASS" : "FAIL";
    md.AppendLine($"| {r.Id} | {kind} | {r.Message} | {expected} | {observed} | {status} |");
}
if (failures.Count > 0)
{
    md.AppendLine();
    md.AppendLine("## Failures");
    foreach (var f in failures) md.AppendLine($"- {f}");
}
await File.WriteAllTextAsync(resultsMd, md.ToString());

Console.WriteLine();
Console.WriteLine($"  Results JSON:      {resultsJson}");
Console.WriteLine($"  Results markdown:  {resultsMd}");

// --- Suite summary artifact (D1-F2 contract consumed by BenchmarkAll if
// ever wired in; ModeBenchmark is intentionally NOT wired today). ---
bool runSucceeded = (scenariosOk == scenarios.Count);
int currentWeakCases = scenarios.Count - scenariosOk;
bool promptsChanged = false;
int? baselineWeakCasesForSummary = null;

if (File.Exists(baselinePath))
{
    try
    {
        var b = JsonSerializer.Deserialize<ModeMetrics>(
            await File.ReadAllTextAsync(baselinePath), jsonOpts);
        if (b is not null)
        {
            // D1-F2: detect scenario-set drift before computing the verdict.
            // A null/empty PromptsSha256 on the baseline is treated as a
            // mismatch — once a baseline is recaptured under the new
            // tooling it always carries a hash; absence means the baseline
            // pre-dates this check and the verdict cannot be trusted.
            if (string.IsNullOrEmpty(b.PromptsSha256)
                || !string.Equals(b.PromptsSha256, scenariosSha256, StringComparison.Ordinal))
            {
                promptsChanged = true;
                Console.WriteLine();
                Console.WriteLine(
                    "  WARNING: Prompts hash differs from baseline — regression verdict unavailable for this run");
            }
            // Comparable WeakCases only when (a) hashes matched, (b) the
            // baseline is not a placeholder, and (c) the run itself fully
            // succeeded.
            if (runSucceeded && !promptsChanged && !b.Placeholder)
            {
                baselineWeakCasesForSummary = b.WeakCases;
            }
        }
    }
    catch
    {
        // Leave baselineWeakCasesForSummary null → verdict stays "unavailable".
    }
}

string regressionVerdict;
if (promptsChanged)
{
    regressionVerdict = "unavailable";
}
else if (baselineWeakCasesForSummary is null)
{
    regressionVerdict = "unavailable";
}
else if (currentWeakCases < baselineWeakCasesForSummary.Value)
{
    regressionVerdict = "improved";
}
else if (currentWeakCases > baselineWeakCasesForSummary.Value)
{
    regressionVerdict = "regressed";
}
else
{
    regressionVerdict = "unchanged";
}

var summaryPath = Path.Combine(resultsDir, "summary.json");
await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(new
{
    timestampUtc = DateTime.UtcNow.ToString(
        "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
    benchmarkName = "ModeBenchmark",
    baselineWeakCases = baselineWeakCasesForSummary,
    currentWeakCases,
    regressionVerdict,
    promptsCount = scenarios.Count,
    promptsSha256 = scenariosSha256,
    promptsChanged,
    totalScenarios = scenarios.Count,
    hardScenarios = hardCount,
    scenariosOk,
    modeMatches,
    softScenarios = softCount,
}, jsonOpts));

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"  FAILURES ({failures.Count}):");
    foreach (var f in failures) Console.WriteLine($"    - {f}");
}

Console.WriteLine("═══════════════════════════════════════");

return runSucceeded ? 0 : 1;

// --- DTOs ---

record Scenario
{
    public string Id { get; init; } = "";
    public string Message { get; init; } = "";
    public string? ExpectedMode { get; init; }
}

record ChatResponseShape
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

record DeviceReg
{
    public Guid DeviceId { get; init; }
    public string ApiKey { get; init; } = "";
}

record ModeMetrics
{
    public int TotalScenarios { get; init; }
    public int HardScenarios { get; init; }
    public int SoftScenarios { get; init; }
    public int ScenariosOk { get; init; }
    public int ModeMatches { get; init; }
    public int GateTrips { get; init; }
    public int WeakCases { get; init; }
    public bool Placeholder { get; init; }
    public int PromptsCount { get; init; }
    public string? PromptsSha256 { get; init; }
}

record ScenarioResult
{
    public string Id { get; init; } = "";
    public string Message { get; init; } = "";
    public string? ExpectedMode { get; init; }
    public string? ObservedMode { get; set; }
    public string? ResponseSnippet { get; set; }
    public int HttpStatus { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
}
