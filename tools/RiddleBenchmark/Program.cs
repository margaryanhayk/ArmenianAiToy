using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// CLI: first positional arg is baseUrl; --write-baseline saves baseline.json
// to AppContext.BaseDirectory after the run. Operator copies the generated
// file to the source tools/RiddleBenchmark/baseline.json and commits.
//
// RiddleBenchmark runs MULTI-TURN scenarios — each scenario gets a fresh
// device registration so per-conversation state (RiddleSessions) starts
// empty for every scenario. That's what the generic ModeBenchmark cannot
// do, and is why Riddle gets its own tool after the GameBenchmark split.
//
// Important: the bench cannot see the model's RIDDLE_ANSWER (the tail
// block is stripped by ChatService before the response is returned).
// That means scripted "correct guess" scenarios are not reliable — a
// blind guess will almost always produce a HINT-shape turn, not
// CELEBRATE. Scoring is therefore structural (per-turn hard contract +
// `expect` shape markers for turns where a specific shape is
// deterministic: `pose` for explicit new-riddle triggers, `reveal` for
// explicit give-up triggers).
bool writeBaseline = args.Any(a => a == "--write-baseline");
var positional = args.Where(a => !a.StartsWith("--")).ToArray();
var baseUrl = positional.Length > 0 ? positional[0] : "http://localhost:5000";
var promptsPath = Path.Combine(AppContext.BaseDirectory, "prompts.json");
var baselinePath = Path.Combine(AppContext.BaseDirectory, "baseline.json");
var resultsDir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(resultsDir);

// D1-F2: pin prompt-set identity so prompt edits cannot silently invalidate
// the regression verdict. The hash is the SHA-256 of prompts.json on disk;
// the count comes from the deserialized list later. Both land in
// summary.json and in any --write-baseline output.
var promptsBytes = await File.ReadAllBytesAsync(promptsPath);
var promptsSha256 = Convert.ToHexString(
    System.Security.Cryptography.SHA256.HashData(promptsBytes)).ToLowerInvariant();

// --- Thresholds ---
const int MaxTurnLen = 200;  // Riddle turns should stay short; same soft cap Game uses.

var armenianRegex = new Regex(@"[\u0530-\u058F]");
var latinRunRegex = new Regex(@"[A-Za-z]{4,}");
var choiceBlockRegex = new Regex(@"CHOICE_[AB]\s*:", RegexOptions.IgnoreCase);
var leakedTailRegex = new Regex(
    @"\b(?:RIDDLE_ANSWER|RIDDLE_CATEGORY|RIDDLE_DIFFICULTY|RIDDLE_TURN_KIND|GAME_TYPE|GAME_DIFFICULTY|GAME_TURN_KIND|STORY_MEMORY)\s*:",
    RegexOptions.IgnoreCase);

// Riddle-pose marker: Armenian question mark (՞, U+055E). Every NEW_RIDDLE
// and HINT turn asks a question of some kind. Checking for the mark is a
// reliable lightweight shape check.
const char ArmenianQuestionMark = '\u055E';

// Reveal marker: the v2 prompt tells the model to say «Պատասխանն էր՝ <X>։»
// or similar. We match the noun stem «պատասխան» (lowercase) against a
// lower-cased copy of the response so both Պատասխան (sentence-initial)
// and պատասխան (mid-sentence) forms match.

// Offer-next marker: «Ուզու՞մ ես ևս մեկ հանելուկ» — detect by the presence
// of «հանելուկ» (with a leading space to avoid triggering on a hint that
// uses the word in isolation) together with the Armenian question mark.
const string HanelukStem = "\u0570\u0561\u0576\u0565\u056c\u0578\u0582\u056f"; // հանելուկ

var jsonOpts = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

Console.WriteLine($"RiddleBenchmark target: {baseUrl}");

var scenarios = JsonSerializer.Deserialize<List<Scenario>>(
    await File.ReadAllTextAsync(promptsPath), jsonOpts)
    ?? throw new Exception("Failed to load prompts");
Console.WriteLine($"Loaded {scenarios.Count} scenarios\n");

var results = new List<ScenarioResult>();
var failures = new List<string>();
var weakCases = new List<string>();

int totalTurns = 0;
int turnsOk = 0;
int scenariosOk = 0;
int leakedTail = 0;
int latinRun = 0;
int missingRiddlePose = 0;
int missingRevealMarker = 0;
int missingOfferNext = 0;
int tooLong = 0;

Console.WriteLine("ID    | Turns | OkN | Hard | Label");
Console.WriteLine("------|-------|-----|------|--------------------------");

foreach (var scenario in scenarios)
{
    var sResult = new ScenarioResult { Id = scenario.Id, Label = scenario.Label };

    using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };

    DeviceReg device;
    try
    {
        var regBody = new { macAddress = $"RBENCH-{scenario.Id}-{DateTime.UtcNow:HHmmssfff}" };
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
        Console.WriteLine($"{scenario.Id,5} |     - |   - |   X  | reg-fail");
        continue;
    }
    http.DefaultRequestHeaders.Add("X-Device-Id", device.DeviceId.ToString());
    http.DefaultRequestHeaders.Add("X-Api-Key", device.ApiKey);

    int sTurnsOk = 0;
    bool sHardFail = false;

    foreach (var turn in scenario.Turns)
    {
        totalTurns++;
        var turnResult = new TurnResult { User = turn.User, Expect = turn.Expect };

        ChatResponse? resp = null;
        try
        {
            var body = new { message = turn.User };
            var httpResp = await http.PostAsJsonAsync("/api/chat", body);
            httpResp.EnsureSuccessStatusCode();
            resp = await httpResp.Content.ReadFromJsonAsync<ChatResponse>(jsonOpts);
        }
        catch (Exception ex)
        {
            turnResult.Error = ex.Message;
            sResult.Turns.Add(turnResult);
            sHardFail = true;
            failures.Add($"{scenario.Id} turn '{turn.User}': request failed — {ex.Message}");
            continue;
        }
        if (resp?.Response is null)
        {
            turnResult.Error = "null response";
            sResult.Turns.Add(turnResult);
            sHardFail = true;
            failures.Add($"{scenario.Id} turn '{turn.User}': null response");
            continue;
        }

        var text = resp.Response;
        turnResult.Response = text;
        turnResult.Mode = resp.Mode;
        turnResult.ResponseLen = text.Length;
        turnResult.HasArmenian = armenianRegex.IsMatch(text);
        turnResult.HasChoiceBlock = choiceBlockRegex.IsMatch(text);
        turnResult.HasChoiceField = !string.IsNullOrWhiteSpace(resp.ChoiceA)
                                 || !string.IsNullOrWhiteSpace(resp.ChoiceB);
        turnResult.HasLeakedTail = leakedTailRegex.IsMatch(text);
        turnResult.HasLatinRun = latinRunRegex.IsMatch(text);

        // Hard failures
        bool hard = false;
        if (!turnResult.HasArmenian)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': no Armenian");
            hard = true;
        }
        if (turnResult.HasChoiceBlock || turnResult.HasChoiceField)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': choice block leaked into riddle mode");
            hard = true;
        }
        if (turnResult.HasLeakedTail)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': leaked tail block (RIDDLE_*/GAME_*/STORY_MEMORY)");
            leakedTail++;
            hard = true;
        }
        if (turnResult.HasLatinRun)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': 4+ Latin letter run");
            latinRun++;
            hard = true;
        }
        if (resp.Mode != "riddle")
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': mode='{resp.Mode}' (expected riddle)");
            hard = true;
        }
        if (text.Length > MaxTurnLen)
        {
            tooLong++;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': length {text.Length} > {MaxTurnLen}");
        }

        // Shape checks based on `expect` (optional per-turn tag).
        // "pose"   — response must contain the Armenian question mark «՞».
        // "reveal" — response must contain the noun «Պատասխան» AND should
        //            offer the next riddle («հանելուկ» + «՞»).
        if (turn.Expect == "pose")
        {
            if (!text.Contains(ArmenianQuestionMark))
            {
                missingRiddlePose++;
                weakCases.Add($"{scenario.Id} turn '{turn.User}': expected riddle pose but no Armenian question mark found");
            }
        }
        else if (turn.Expect == "reveal")
        {
            var lower = text.ToLowerInvariant();

            if (!lower.Contains("\u057a\u0561\u057f\u0561\u057d\u056d\u0561\u0576")) // պատասխան
            {
                missingRevealMarker++;
                weakCases.Add($"{scenario.Id} turn '{turn.User}': expected reveal but no «Պատասխան» stem found");
            }

            if (!(lower.Contains(HanelukStem) && text.Contains(ArmenianQuestionMark)))
            {
                missingOfferNext++;
                weakCases.Add($"{scenario.Id} turn '{turn.User}': reveal did not offer next riddle");
            }
        }

        if (!hard) { turnsOk++; sTurnsOk++; }
        else { sHardFail = true; }

        sResult.Turns.Add(turnResult);
    }

    sResult.TurnsTotal = scenario.Turns.Count;
    sResult.TurnsOk = sTurnsOk;
    sResult.HardFail = sHardFail;
    if (!sHardFail) scenariosOk++;

    Console.WriteLine($"{scenario.Id,5} | {scenario.Turns.Count,5} | {sTurnsOk,3} | {(sHardFail ? "X" : " "),4} | {scenario.Label}");
    results.Add(sResult);
}

// --- Summary ---
Console.WriteLine();
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine("  RIDDLE BENCHMARK SUMMARY");
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine($"  Scenarios pass:         {scenariosOk}/{scenarios.Count}");
Console.WriteLine($"  Turns pass:             {turnsOk}/{totalTurns}");
Console.WriteLine();
Console.WriteLine($"  Weak cases:             {weakCases.Count}");
Console.WriteLine($"  Leaked tail:            {leakedTail}");
Console.WriteLine($"  Latin run:              {latinRun}");
Console.WriteLine($"  Missing riddle pose:    {missingRiddlePose}");
Console.WriteLine($"  Missing reveal marker:  {missingRevealMarker}");
Console.WriteLine($"  Missing offer-next:     {missingOfferNext}");
Console.WriteLine($"  Too long:               {tooLong}");

// --- Save results ---
var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
var resultsJson = Path.Combine(resultsDir, $"run_{timestamp}.json");
var resultsMd = Path.Combine(resultsDir, $"run_{timestamp}.md");

await File.WriteAllTextAsync(resultsJson, JsonSerializer.Serialize(results, jsonOpts));

var md = new System.Text.StringBuilder();
md.AppendLine("# RiddleBenchmark Results");
md.AppendLine();
md.AppendLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
md.AppendLine($"**Target:** {baseUrl}");
md.AppendLine($"**Scenarios:** {scenarios.Count}");
md.AppendLine();
md.AppendLine("| Metric | Count |");
md.AppendLine("|--------|-------|");
md.AppendLine($"| Scenarios ok | {scenariosOk} / {scenarios.Count} |");
md.AppendLine($"| Turns ok | {turnsOk} / {totalTurns} |");
md.AppendLine($"| Weak cases | {weakCases.Count} |");
md.AppendLine($"| Leaked tail | {leakedTail} |");
md.AppendLine($"| Latin run | {latinRun} |");
md.AppendLine($"| Missing riddle pose | {missingRiddlePose} |");
md.AppendLine($"| Missing reveal marker | {missingRevealMarker} |");
md.AppendLine($"| Missing offer-next | {missingOfferNext} |");
md.AppendLine($"| Too long | {tooLong} |");
md.AppendLine();
if (failures.Count > 0)
{
    md.AppendLine("## Failures");
    foreach (var f in failures) md.AppendLine($"- {f}");
    md.AppendLine();
}
if (weakCases.Count > 0)
{
    md.AppendLine("## Weak cases");
    foreach (var w in weakCases) md.AppendLine($"- {w}");
}
await File.WriteAllTextAsync(resultsMd, md.ToString());

Console.WriteLine();
Console.WriteLine($"  Results JSON:      {resultsJson}");
Console.WriteLine($"  Results markdown:  {resultsMd}");

// --- Baseline comparison ---
var current = new RiddleMetrics
{
    TotalScenarios = scenarios.Count,
    ScenariosOk = scenariosOk,
    TurnsTotal = totalTurns,
    TurnsOk = turnsOk,
    WeakCases = weakCases.Count,
    LeakedTail = leakedTail,
    LatinRun = latinRun,
    MissingRiddlePose = missingRiddlePose,
    MissingRevealMarker = missingRevealMarker,
    MissingOfferNext = missingOfferNext,
    TooLong = tooLong,
    Placeholder = false,
    PromptsCount = scenarios.Count,
    PromptsSha256 = promptsSha256,
};

bool promptsChanged = false;

if (File.Exists(baselinePath))
{
    try
    {
        var baseline = JsonSerializer.Deserialize<RiddleMetrics>(
            await File.ReadAllTextAsync(baselinePath), jsonOpts);
        if (baseline is not null && !baseline.Placeholder)
        {
            // D1-F2: detect prompt-set drift before printing deltas. A
            // null/empty PromptsSha256 on the baseline is treated as a
            // mismatch — once a baseline is recaptured under the new
            // tooling it always carries a hash; absence means the baseline
            // pre-dates this check and the verdict cannot be trusted.
            if (string.IsNullOrEmpty(baseline.PromptsSha256)
                || !string.Equals(baseline.PromptsSha256, promptsSha256, StringComparison.Ordinal))
            {
                promptsChanged = true;
                Console.WriteLine();
                Console.WriteLine("  WARNING: Prompts hash differs from baseline — regression verdict unavailable for this run");
            }

            Console.WriteLine();
            Console.WriteLine("  Delta vs baseline (negative = improvement for weak counts)");
            Console.WriteLine($"    scenarios_ok:           {Delta(baseline.ScenariosOk, current.ScenariosOk)}");
            Console.WriteLine($"    turns_ok:               {Delta(baseline.TurnsOk, current.TurnsOk)}");
            Console.WriteLine($"    weak_cases:             {Delta(baseline.WeakCases, current.WeakCases)}");
            Console.WriteLine($"    leaked_tail:            {Delta(baseline.LeakedTail, current.LeakedTail)}");
            Console.WriteLine($"    latin_run:              {Delta(baseline.LatinRun, current.LatinRun)}");
            Console.WriteLine($"    missing_riddle_pose:    {Delta(baseline.MissingRiddlePose, current.MissingRiddlePose)}");
            Console.WriteLine($"    missing_reveal_marker:  {Delta(baseline.MissingRevealMarker, current.MissingRevealMarker)}");
            Console.WriteLine($"    missing_offer_next:     {Delta(baseline.MissingOfferNext, current.MissingOfferNext)}");
            Console.WriteLine($"    too_long:               {Delta(baseline.TooLong, current.TooLong)}");
        }
        else if (baseline is not null && baseline.Placeholder)
        {
            Console.WriteLine();
            Console.WriteLine("  Baseline is a placeholder \u2014 run with --write-baseline and commit.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Baseline read failed: {ex.Message}");
    }
}
else
{
    Console.WriteLine();
    Console.WriteLine($"  No baseline at {baselinePath}. Run with --write-baseline, then copy");
    Console.WriteLine($"  the generated file to tools/RiddleBenchmark/baseline.json and commit.");
}

if (writeBaseline)
{
    await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(current, jsonOpts));
    Console.WriteLine();
    Console.WriteLine($"  Baseline written: {baselinePath}");
    Console.WriteLine($"  Copy to: tools/RiddleBenchmark/baseline.json");
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"  FAILURES ({failures.Count}):");
    foreach (var f in failures) Console.WriteLine($"    - {f}");
}

if (weakCases.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"  WEAK CASES ({weakCases.Count}):");
    foreach (var w in weakCases) Console.WriteLine($"    \u26a0 {w}");
}

if (failures.Count == 0 && weakCases.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("  ALL CHECKS PASSED \u2014 NO WEAK CASES");
}

Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");

// --- Suite summary artifact (stable contract consumed by BenchmarkAll) ---
// If the run itself did not fully succeed we emit "unavailable" —
// a partial run's weak-case total is not comparable to the baseline.
bool runSucceeded = (scenariosOk == scenarios.Count);
int? baselineWeakCasesForSummary = null;
// D1-F2: when the prompt set has changed, the baseline weak-case count is
// not comparable to the current run; force the BenchmarkAll-side verdict
// to "unavailable" by leaving baselineWeakCasesForSummary null.
if (runSucceeded && !promptsChanged && File.Exists(baselinePath))
{
    try
    {
        var b = JsonSerializer.Deserialize<RiddleMetrics>(
            await File.ReadAllTextAsync(baselinePath), jsonOpts);
        if (b is not null && !b.Placeholder)
            baselineWeakCasesForSummary = b.WeakCases;
    }
    catch { /* leave null → verdict stays "unavailable" */ }
}
string regressionVerdict = promptsChanged ? "unavailable"
    : baselineWeakCasesForSummary is null ? "unavailable"
    : current.WeakCases < baselineWeakCasesForSummary.Value ? "improved"
    : current.WeakCases > baselineWeakCasesForSummary.Value ? "regressed"
    : "unchanged";
var summaryPath = Path.Combine(resultsDir, "summary.json");
await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(new
{
    timestampUtc = DateTime.UtcNow.ToString(
        "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
    benchmarkName = "RiddleBenchmark",
    baselineWeakCases = baselineWeakCasesForSummary,
    currentWeakCases = current.WeakCases,
    regressionVerdict,
    promptsCount = scenarios.Count,
    promptsSha256,
    promptsChanged,
}, jsonOpts));

return scenariosOk == scenarios.Count ? 0 : 1;

// --- Helpers ---

static string Delta(int baseline, int current)
{
    var d = current - baseline;
    var sign = d > 0 ? "+" : "";
    return $"{baseline} -> {current} ({sign}{d})";
}

// --- DTOs ---

record Scenario
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public List<TurnPrompt> Turns { get; init; } = new();
}

record TurnPrompt
{
    public string User { get; init; } = "";
    public string? Expect { get; init; }
}

record ChatResponse
{
    public string Response { get; init; } = "";
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

record RiddleMetrics
{
    public int TotalScenarios { get; init; }
    public int ScenariosOk { get; init; }
    public int TurnsTotal { get; init; }
    public int TurnsOk { get; init; }
    public int WeakCases { get; init; }
    public int LeakedTail { get; init; }
    public int LatinRun { get; init; }
    public int MissingRiddlePose { get; init; }
    public int MissingRevealMarker { get; init; }
    public int MissingOfferNext { get; init; }
    public int TooLong { get; init; }
    public bool Placeholder { get; init; }
    // D1-F2: prompt-set identity. PromptsSha256 is null on legacy baselines
    // that pre-date the field; a null/empty value triggers the same
    // "prompts changed" path as a hash mismatch.
    public int PromptsCount { get; init; }
    public string? PromptsSha256 { get; init; }
}

record ScenarioResult
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public int TurnsTotal { get; set; }
    public int TurnsOk { get; set; }
    public bool HardFail { get; set; }
    public List<TurnResult> Turns { get; set; } = new();
    public string? Error { get; set; }
}

record TurnResult
{
    public string User { get; init; } = "";
    public string? Expect { get; init; }
    public string? Response { get; set; }
    public string? Mode { get; set; }
    public int ResponseLen { get; set; }
    public bool HasArmenian { get; set; }
    public bool HasChoiceBlock { get; set; }
    public bool HasChoiceField { get; set; }
    public bool HasLeakedTail { get; set; }
    public bool HasLatinRun { get; set; }
    public string? Error { get; set; }
}
