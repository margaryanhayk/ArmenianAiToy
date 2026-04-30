using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// CLI: first positional arg is baseUrl; --write-baseline saves baseline.json
// to AppContext.BaseDirectory after the run. Operator copies the generated
// file to the source tools/CuriosityBenchmark/baseline.json and commits.
//
// CuriosityBenchmark runs scenario-based prompts — each scenario gets a
// fresh device registration so any cross-turn state in ChatService starts
// empty for every scenario. Curiosity v2 itself is a one-turn overlay
// (no per-conversation state record), so the multi-turn shape mostly
// probes whether consecutive curiosity questions stay short, concrete,
// and don't drift into encyclopedia tone.
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
// Mirrors ResponseQualityGate.curiosity_too_long. Curiosity v2 bumped
// this from 200 to 240 to fit one optional analogy or fun-fact clause.
const int MaxTurnLen = 240;

var armenianRegex = new Regex(@"[\u0530-\u058F]");
var latinRunRegex = new Regex(@"[A-Za-z]{4,}");
var choiceBlockRegex = new Regex(@"CHOICE_[AB]\s*:", RegexOptions.IgnoreCase);
var leakedTailRegex = new Regex(
    @"\b(?:GAME_TYPE|GAME_DIFFICULTY|GAME_TURN_KIND|RIDDLE_ANSWER|RIDDLE_CATEGORY|RIDDLE_DIFFICULTY|RIDDLE_TURN_KIND|CALM_TURN_INDEX|STORY_MEMORY)\s*:",
    RegexOptions.IgnoreCase);

// Armenian question/exclamation marks. The Curiosity v2 contract forbids
// the question mark in answers (the model must not ask back); post-
// processing in ChatService replaces `?` and `՞` with `։`. Any survivor
// is a real regression.
const char ArmenianQuestion = '\u055E';  // ՞

// Encyclopedia opener phrases — banned by the Curiosity v2 ANTI-
// ENCYCLOPEDIA section. Matched case-insensitively against the trimmed
// start of the response. These are the openers the prompt explicitly
// calls out: «Այս երևույթը...», «Գիտնականները...», «Գոյություն ունի...»,
// «Այս հարցը...».
var encyclopediaOpeners = new[]
{
    "\u0561\u0575\u057d \u0565\u0580\u0587\u0578\u0582\u0575\u0569\u0568",                        // այս երևույթը
    "\u0563\u056b\u057f\u0576\u0561\u056f\u0561\u0576\u0576\u0565\u0580\u0568",                  // գիտնականները
    "\u0563\u0578\u0575\u0578\u0582\u0569\u0575\u0578\u0582\u0576 \u0578\u0582\u0576\u056b",  // գոյություն ունի
    "\u0561\u0575\u057d \u0570\u0561\u0580\u0581\u0568",                                          // այս հարցը
};

// Chained-cause stems. The v2 prompt explicitly bans «նախ ... հետո ... ապա ...».
// We flag a turn that contains BOTH «նախ» and one of («հետո» or «ապա») in the
// same response — a single «հետո» on its own is normal Armenian.
const string ChainNakh = "\u0576\u0561\u056d";       // նախ
const string ChainHeto = "\u0570\u0565\u057f\u0578"; // հետո (stem)
const string ChainApa  = "\u0561\u057a\u0561";       // ապա

var jsonOpts = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

Console.WriteLine($"CuriosityBenchmark target: {baseUrl}");

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
int tooLong = 0;
int encyclopediaOpener = 0;
int chainedCause = 0;
int lengthGrowing = 0;

Console.WriteLine("ID    | Turns | OkN | Hard | Label");
Console.WriteLine("------|-------|-----|------|--------------------------");

foreach (var scenario in scenarios)
{
    var sResult = new ScenarioResult { Id = scenario.Id, Label = scenario.Label };

    using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };

    DeviceReg device;
    try
    {
        var regBody = new { macAddress = $"CUBENCH-{scenario.Id}-{DateTime.UtcNow:HHmmssfff}" };
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
        var turnResult = new TurnResult { User = turn.User };

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
        var lower = text.ToLowerInvariant();
        var trimmedLower = lower.TrimStart();
        turnResult.Response = text;
        turnResult.Mode = resp.Mode;
        turnResult.ResponseLen = text.Length;
        turnResult.HasArmenian = armenianRegex.IsMatch(text);
        turnResult.HasChoiceBlock = choiceBlockRegex.IsMatch(text);
        turnResult.HasChoiceField = !string.IsNullOrWhiteSpace(resp.ChoiceA)
                                 || !string.IsNullOrWhiteSpace(resp.ChoiceB);
        turnResult.HasLeakedTail = leakedTailRegex.IsMatch(text);
        turnResult.HasLatinRun = latinRunRegex.IsMatch(text);
        turnResult.HasQuestion = text.Contains('?') || text.Contains(ArmenianQuestion);

        // --- Hard failures ---
        bool hard = false;
        if (!turnResult.HasArmenian)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': no Armenian");
            hard = true;
        }
        if (turnResult.HasChoiceBlock || turnResult.HasChoiceField)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': choice block leaked into curiosity mode");
            hard = true;
        }
        if (turnResult.HasLeakedTail)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': leaked tail block (GAME_*/RIDDLE_*/CALM_TURN_INDEX/STORY_MEMORY)");
            leakedTail++;
            hard = true;
        }
        if (turnResult.HasLatinRun)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': 4+ Latin letter run");
            latinRun++;
            hard = true;
        }
        if (resp.Mode != "curiosity")
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': mode='{resp.Mode}' (expected curiosity)");
            hard = true;
        }
        if (turnResult.HasQuestion)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': curiosity response contains question mark");
            hard = true;
        }

        // --- Weak signals ---
        if (text.Length > MaxTurnLen)
        {
            tooLong++;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': length {text.Length} > {MaxTurnLen}");
        }

        // Encyclopedia opener
        var openerHit = encyclopediaOpeners.FirstOrDefault(o => trimmedLower.StartsWith(o, StringComparison.Ordinal));
        if (openerHit is not null)
        {
            encyclopediaOpener++;
            turnResult.EncyclopediaOpener = true;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': encyclopedia opener — response begins with banned framing");
        }

        // Chained cause
        if (lower.Contains(ChainNakh) && (lower.Contains(ChainHeto) || lower.Contains(ChainApa)))
        {
            chainedCause++;
            turnResult.ChainedCause = true;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': chained-cause language («նախ ... հետո ... ապա»)");
        }

        if (!hard) { turnsOk++; sTurnsOk++; }
        else { sHardFail = true; }

        sResult.Turns.Add(turnResult);
    }

    sResult.TurnsTotal = scenario.Turns.Count;
    sResult.TurnsOk = sTurnsOk;
    sResult.HardFail = sHardFail;
    if (!sHardFail) scenariosOk++;

    // --- Per-scenario length-growing check ---
    // Curiosity has no wind-down arc, but consecutive answers should stay
    // roughly the same length — a steadily growing curve suggests the
    // model is creeping toward verbosity. Flag only meaningful growth
    // (> LengthGrowthTolerance chars); small turn-to-turn variance from
    // naturally harder questions is not verbosity creep.
    const int LengthGrowthTolerance = 15;
    for (int i = 1; i < sResult.Turns.Count; i++)
    {
        var prev = sResult.Turns[i - 1];
        var curr = sResult.Turns[i];
        if (curr.ResponseLen > 0 && prev.ResponseLen > 0
            && curr.ResponseLen > prev.ResponseLen + LengthGrowthTolerance)
        {
            lengthGrowing++;
            curr.LengthGrowing = true;
            weakCases.Add($"{scenario.Id} turn '{curr.User}' (#{i + 1}): length {curr.ResponseLen} > previous turn's {prev.ResponseLen}");
        }
    }

    Console.WriteLine($"{scenario.Id,5} | {scenario.Turns.Count,5} | {sTurnsOk,3} | {(sHardFail ? "X" : " "),4} | {scenario.Label}");
    results.Add(sResult);
}

// --- Summary ---
Console.WriteLine();
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine("  CURIOSITY BENCHMARK SUMMARY");
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine($"  Scenarios pass:        {scenariosOk}/{scenarios.Count}");
Console.WriteLine($"  Turns pass:            {turnsOk}/{totalTurns}");
Console.WriteLine();
Console.WriteLine($"  Weak cases:            {weakCases.Count}");
Console.WriteLine($"  Leaked tail:           {leakedTail}");
Console.WriteLine($"  Latin run:             {latinRun}");
Console.WriteLine($"  Too long:              {tooLong}");
Console.WriteLine($"  Encyclopedia opener:   {encyclopediaOpener}");
Console.WriteLine($"  Chained cause:         {chainedCause}");
Console.WriteLine($"  Length growing:        {lengthGrowing}");

// --- Save results ---
var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
var resultsJson = Path.Combine(resultsDir, $"run_{timestamp}.json");
var resultsMd = Path.Combine(resultsDir, $"run_{timestamp}.md");

await File.WriteAllTextAsync(resultsJson, JsonSerializer.Serialize(results, jsonOpts));

var md = new System.Text.StringBuilder();
md.AppendLine("# CuriosityBenchmark Results");
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
md.AppendLine($"| Too long | {tooLong} |");
md.AppendLine($"| Encyclopedia opener | {encyclopediaOpener} |");
md.AppendLine($"| Chained cause | {chainedCause} |");
md.AppendLine($"| Length growing | {lengthGrowing} |");
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
var current = new CuriosityMetrics
{
    TotalScenarios = scenarios.Count,
    ScenariosOk = scenariosOk,
    TurnsTotal = totalTurns,
    TurnsOk = turnsOk,
    WeakCases = weakCases.Count,
    LeakedTail = leakedTail,
    LatinRun = latinRun,
    TooLong = tooLong,
    EncyclopediaOpener = encyclopediaOpener,
    ChainedCause = chainedCause,
    LengthGrowing = lengthGrowing,
    Placeholder = false,
    PromptsCount = scenarios.Count,
    PromptsSha256 = promptsSha256,
};

bool promptsChanged = false;

if (File.Exists(baselinePath))
{
    try
    {
        var baseline = JsonSerializer.Deserialize<CuriosityMetrics>(
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
            Console.WriteLine($"    scenarios_ok:          {Delta(baseline.ScenariosOk, current.ScenariosOk)}");
            Console.WriteLine($"    turns_ok:              {Delta(baseline.TurnsOk, current.TurnsOk)}");
            Console.WriteLine($"    weak_cases:            {Delta(baseline.WeakCases, current.WeakCases)}");
            Console.WriteLine($"    leaked_tail:           {Delta(baseline.LeakedTail, current.LeakedTail)}");
            Console.WriteLine($"    latin_run:             {Delta(baseline.LatinRun, current.LatinRun)}");
            Console.WriteLine($"    too_long:              {Delta(baseline.TooLong, current.TooLong)}");
            Console.WriteLine($"    encyclopedia_opener:   {Delta(baseline.EncyclopediaOpener, current.EncyclopediaOpener)}");
            Console.WriteLine($"    chained_cause:         {Delta(baseline.ChainedCause, current.ChainedCause)}");
            Console.WriteLine($"    length_growing:        {Delta(baseline.LengthGrowing, current.LengthGrowing)}");
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
    Console.WriteLine($"  the generated file to tools/CuriosityBenchmark/baseline.json and commit.");
}

if (writeBaseline)
{
    await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(current, jsonOpts));
    Console.WriteLine();
    Console.WriteLine($"  Baseline written: {baselinePath}");
    Console.WriteLine($"  Copy to: tools/CuriosityBenchmark/baseline.json");
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
        var b = JsonSerializer.Deserialize<CuriosityMetrics>(
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
    benchmarkName = "CuriosityBenchmark",
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

record CuriosityMetrics
{
    public int TotalScenarios { get; init; }
    public int ScenariosOk { get; init; }
    public int TurnsTotal { get; init; }
    public int TurnsOk { get; init; }
    public int WeakCases { get; init; }
    public int LeakedTail { get; init; }
    public int LatinRun { get; init; }
    public int TooLong { get; init; }
    public int EncyclopediaOpener { get; init; }
    public int ChainedCause { get; init; }
    public int LengthGrowing { get; init; }
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
    public string? Response { get; set; }
    public string? Mode { get; set; }
    public int ResponseLen { get; set; }
    public bool HasArmenian { get; set; }
    public bool HasChoiceBlock { get; set; }
    public bool HasChoiceField { get; set; }
    public bool HasLeakedTail { get; set; }
    public bool HasLatinRun { get; set; }
    public bool HasQuestion { get; set; }
    public bool EncyclopediaOpener { get; set; }
    public bool ChainedCause { get; set; }
    public bool LengthGrowing { get; set; }
    public string? Error { get; set; }
}
