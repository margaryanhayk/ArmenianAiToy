using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// CLI: first positional arg is baseUrl; --write-baseline saves baseline.json
// to AppContext.BaseDirectory after the run. Operator copies the generated
// file to the source tools/CalmBenchmark/baseline.json and commits.
//
// CalmBenchmark runs MULTI-TURN scenarios — each scenario gets a fresh
// device registration so per-conversation state (the Calm TurnIndex on
// ActiveModeEntry) starts at 1 for every scenario. That's what the
// generic ModeBenchmark cannot do, and is what this tool is for: probe
// the Calm v2 wind-down arc (Turn 1 / 2 / 3+ directive behaviour) plus
// the structural contract every Calm turn has to honour (no question,
// no exclamation, mode=="calm", grounding anchor present, no companion-
// language leak, no echoed fear word).
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
// Mirrors ModeBenchmark's CalmMaxLen — the Calm surface accepts
// somewhat longer turns on the opener but the WIND-DOWN ARC directive
// forces later turns down. We only flag `too_long` above this cap;
// the arc check below enforces the downward trajectory.
const int MaxTurnLen = 400;

var armenianRegex = new Regex(@"[\u0530-\u058F]");
var latinRunRegex = new Regex(@"[A-Za-z]{4,}");
var choiceBlockRegex = new Regex(@"CHOICE_[AB]\s*:", RegexOptions.IgnoreCase);
var leakedTailRegex = new Regex(
    @"\b(?:CALM_TURN_INDEX|GAME_TYPE|GAME_DIFFICULTY|GAME_TURN_KIND|RIDDLE_ANSWER|RIDDLE_CATEGORY|RIDDLE_DIFFICULTY|RIDDLE_TURN_KIND|STORY_MEMORY)\s*:",
    RegexOptions.IgnoreCase);

// Armenian question/exclamation marks. The Calm mode contract forbids
// both in any form, and ChatService post-processing replaces `?`, `՞`,
// `!`, `՜` with Armenian period `։`. If any survive, that's a real
// regression.
const char ArmenianQuestion = '\u055E';  // ՞
const char ArmenianExclaim  = '\u055C';  // ՜

// Grounding-anchor stems. Matched against the lower-cased response.
// These cover every inflection of the Calm v2 prompt's 10-anchor pool
// plus a few common variants (e.g. «Շնչիր դանդաղ» as well as
// «շնչում ես դանդաղ»).
var groundingStems = new[]
{
    "\u057e\u0565\u0580\u0574\u0561\u056f", // վերմակ (blanket)
    "\u0562\u0561\u0580\u0571\u056b\u056f", // բարձիկ (pillow)
    "\u0577\u0576\u0579\u0578\u0582\u0574", // շնչում (breathing, present)
    "\u0577\u0576\u0579\u056b\u0580",         // շնչիր (breathe, imperative)
    "\u057d\u0565\u0576\u0575\u0561\u056f", // սենյակ (room)
    "\u0574\u0561\u0580\u0574\u056b\u0576", // մարմին (body)
    "\u0578\u057f\u0584",                     // ոտք (feet)
    "\u0571\u0565\u057c\u0584",               // ձեռք (hands)
    "\u0561\u0579\u0584",                     // աչք (eyes)
    "\u056c\u0578\u0582\u0575\u057d",         // լույս (light)
    "\u057a\u0561\u057f\u0565\u0580\u0568", // պատերը (the walls)
};

// Banned "emotional companion" phrases (lower-cased substrings).
// Lifted directly from the Calm v2 prompt's ANTI-COMPANION-LANGUAGE
// section so this check is tied to the same rules the model reads.
var companionBannedPhrases = new[]
{
    "\u0574\u056b\u0577\u057f \u0584\u0565\u0566 \u0570\u0565\u057f",        // միշտ քեզ հետ
    "\u0565\u057d \u0574\u056b\u0577\u057f",                                    // ես միշտ
    "\u0579\u0565\u0574 \u0569\u0578\u0572\u0576\u056b",                        // չեմ թողնի
    "\u056b\u0574 \u057d\u056b\u0580\u0565\u056c\u056b",                        // իմ սիրելի
    "\u056b\u0574 \u0569\u0561\u0576\u056f",                                    // իմ թանկ
    "\u056b\u0574 \u0583\u0578\u0584\u0580\u056b\u056f",                        // իմ փոքրիկ
    "\u0565\u057d \u0584\u0578 \u0568\u0576\u056f\u0565\u0580",                 // ես քո ընկեր
};

// Fear-word stems the model must NOT echo back (Calm v2 BEDTIME-
// DISTRESS SHAPE). Checked case-insensitively against the response.
var fearStems = new[]
{
    "\u057e\u0561\u056d",       // վախ (fear)
    "\u057d\u0561\u0580\u057d\u0561\u0583", // սարսափ (terror)
};

var jsonOpts = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

Console.WriteLine($"CalmBenchmark target: {baseUrl}");

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
int missingGroundingAnchor = 0;
int companionLanguageLeak = 0;
int echoedFearWord = 0;
int arcNotWindingDown = 0;

Console.WriteLine("ID    | Turns | OkN | Hard | Label");
Console.WriteLine("------|-------|-----|------|--------------------------");

foreach (var scenario in scenarios)
{
    var sResult = new ScenarioResult { Id = scenario.Id, Label = scenario.Label };

    using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };

    DeviceReg device;
    try
    {
        var regBody = new { macAddress = $"KBENCH-{scenario.Id}-{DateTime.UtcNow:HHmmssfff}" };
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
        turnResult.HasExclamation = text.Contains('!') || text.Contains(ArmenianExclaim);

        // --- Hard failures ---
        bool hard = false;
        if (!turnResult.HasArmenian)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': no Armenian");
            hard = true;
        }
        if (turnResult.HasChoiceBlock || turnResult.HasChoiceField)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': choice block leaked into calm mode");
            hard = true;
        }
        if (turnResult.HasLeakedTail)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': leaked tail block (CALM_TURN_INDEX/GAME_*/RIDDLE_*/STORY_MEMORY)");
            leakedTail++;
            hard = true;
        }
        if (turnResult.HasLatinRun)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': 4+ Latin letter run");
            latinRun++;
            hard = true;
        }
        if (resp.Mode != "calm")
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': mode='{resp.Mode}' (expected calm)");
            hard = true;
        }
        if (turnResult.HasQuestion)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': calm response contains question mark");
            hard = true;
        }
        if (turnResult.HasExclamation)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': calm response contains exclamation mark");
            hard = true;
        }

        // --- Weak signals ---
        if (text.Length > MaxTurnLen)
        {
            tooLong++;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': length {text.Length} > {MaxTurnLen}");
        }

        // Grounding anchor — at least one stem from the pool must appear.
        if (!groundingStems.Any(s => lower.Contains(s)))
        {
            missingGroundingAnchor++;
            turnResult.MissingGroundingAnchor = true;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': no grounding anchor from the Calm v2 pool");
        }

        // Companion-language leak — any banned first-person companion phrase.
        var bannedHit = companionBannedPhrases.FirstOrDefault(p => lower.Contains(p));
        if (bannedHit is not null)
        {
            companionLanguageLeak++;
            turnResult.CompanionLeak = true;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': companion-language leak — matched banned phrase");
        }

        // Echoed fear word — the v2 BEDTIME-DISTRESS SHAPE forbids
        // echoing «վախ» or «սարսափ» in the response. Applied universally:
        // any Calm reply that introduces these stems is a soft drift.
        if (fearStems.Any(s => lower.Contains(s)))
        {
            echoedFearWord++;
            turnResult.EchoedFearWord = true;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': echoed fear-word stem (vakh/sarsap)");
        }

        if (!hard) { turnsOk++; sTurnsOk++; }
        else { sHardFail = true; }

        sResult.Turns.Add(turnResult);
    }

    sResult.TurnsTotal = scenario.Turns.Count;
    sResult.TurnsOk = sTurnsOk;
    sResult.HardFail = sHardFail;
    if (!sHardFail) scenariosOk++;

    // --- Per-scenario arc check ---
    // Calm v2 WIND-DOWN ARC: "Turn 3+: exactly 1 short sentence.
    // Simpler than the previous turn — never longer." Enforced by
    // comparing consecutive response lengths starting at turn 3.
    // Under the exactly-1-sentence regime at turn 3+, natural
    // anchor-to-anchor length variance is within a short clause
    // (~15 chars), so ignore drift inside that noise floor; flag
    // only growth meaningful enough to represent a real arc regression.
    const int ArcGrowthTolerance = 15;
    for (int i = 2; i < sResult.Turns.Count; i++)
    {
        var prev = sResult.Turns[i - 1];
        var curr = sResult.Turns[i];
        if (curr.ResponseLen > 0 && prev.ResponseLen > 0
            && curr.ResponseLen > prev.ResponseLen + ArcGrowthTolerance)
        {
            arcNotWindingDown++;
            curr.ArcNotWindingDown = true;
            weakCases.Add($"{scenario.Id} turn '{curr.User}' (#{i + 1}): length {curr.ResponseLen} > previous turn's {prev.ResponseLen}");
        }
    }

    Console.WriteLine($"{scenario.Id,5} | {scenario.Turns.Count,5} | {sTurnsOk,3} | {(sHardFail ? "X" : " "),4} | {scenario.Label}");
    results.Add(sResult);
}

// --- Summary ---
Console.WriteLine();
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine("  CALM BENCHMARK SUMMARY");
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine($"  Scenarios pass:            {scenariosOk}/{scenarios.Count}");
Console.WriteLine($"  Turns pass:                {turnsOk}/{totalTurns}");
Console.WriteLine();
Console.WriteLine($"  Weak cases:                {weakCases.Count}");
Console.WriteLine($"  Leaked tail:               {leakedTail}");
Console.WriteLine($"  Latin run:                 {latinRun}");
Console.WriteLine($"  Too long:                  {tooLong}");
Console.WriteLine($"  Missing grounding anchor:  {missingGroundingAnchor}");
Console.WriteLine($"  Companion-language leak:   {companionLanguageLeak}");
Console.WriteLine($"  Echoed fear word:          {echoedFearWord}");
Console.WriteLine($"  Arc not winding down:      {arcNotWindingDown}");

// --- Save results ---
var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
var resultsJson = Path.Combine(resultsDir, $"run_{timestamp}.json");
var resultsMd = Path.Combine(resultsDir, $"run_{timestamp}.md");

await File.WriteAllTextAsync(resultsJson, JsonSerializer.Serialize(results, jsonOpts));

var md = new System.Text.StringBuilder();
md.AppendLine("# CalmBenchmark Results");
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
md.AppendLine($"| Missing grounding anchor | {missingGroundingAnchor} |");
md.AppendLine($"| Companion-language leak | {companionLanguageLeak} |");
md.AppendLine($"| Echoed fear word | {echoedFearWord} |");
md.AppendLine($"| Arc not winding down | {arcNotWindingDown} |");
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
var current = new CalmMetrics
{
    TotalScenarios = scenarios.Count,
    ScenariosOk = scenariosOk,
    TurnsTotal = totalTurns,
    TurnsOk = turnsOk,
    WeakCases = weakCases.Count,
    LeakedTail = leakedTail,
    LatinRun = latinRun,
    TooLong = tooLong,
    MissingGroundingAnchor = missingGroundingAnchor,
    CompanionLanguageLeak = companionLanguageLeak,
    EchoedFearWord = echoedFearWord,
    ArcNotWindingDown = arcNotWindingDown,
    Placeholder = false,
    PromptsCount = scenarios.Count,
    PromptsSha256 = promptsSha256,
};

bool promptsChanged = false;

if (File.Exists(baselinePath))
{
    try
    {
        var baseline = JsonSerializer.Deserialize<CalmMetrics>(
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
            Console.WriteLine($"    scenarios_ok:             {Delta(baseline.ScenariosOk, current.ScenariosOk)}");
            Console.WriteLine($"    turns_ok:                 {Delta(baseline.TurnsOk, current.TurnsOk)}");
            Console.WriteLine($"    weak_cases:               {Delta(baseline.WeakCases, current.WeakCases)}");
            Console.WriteLine($"    leaked_tail:              {Delta(baseline.LeakedTail, current.LeakedTail)}");
            Console.WriteLine($"    latin_run:                {Delta(baseline.LatinRun, current.LatinRun)}");
            Console.WriteLine($"    too_long:                 {Delta(baseline.TooLong, current.TooLong)}");
            Console.WriteLine($"    missing_grounding:        {Delta(baseline.MissingGroundingAnchor, current.MissingGroundingAnchor)}");
            Console.WriteLine($"    companion_leak:           {Delta(baseline.CompanionLanguageLeak, current.CompanionLanguageLeak)}");
            Console.WriteLine($"    echoed_fear_word:         {Delta(baseline.EchoedFearWord, current.EchoedFearWord)}");
            Console.WriteLine($"    arc_not_winding_down:     {Delta(baseline.ArcNotWindingDown, current.ArcNotWindingDown)}");
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
    Console.WriteLine($"  the generated file to tools/CalmBenchmark/baseline.json and commit.");
}

if (writeBaseline)
{
    await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(current, jsonOpts));
    Console.WriteLine();
    Console.WriteLine($"  Baseline written: {baselinePath}");
    Console.WriteLine($"  Copy to: tools/CalmBenchmark/baseline.json");
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
        var b = JsonSerializer.Deserialize<CalmMetrics>(
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
    benchmarkName = "CalmBenchmark",
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

record CalmMetrics
{
    public int TotalScenarios { get; init; }
    public int ScenariosOk { get; init; }
    public int TurnsTotal { get; init; }
    public int TurnsOk { get; init; }
    public int WeakCases { get; init; }
    public int LeakedTail { get; init; }
    public int LatinRun { get; init; }
    public int TooLong { get; init; }
    public int MissingGroundingAnchor { get; init; }
    public int CompanionLanguageLeak { get; init; }
    public int EchoedFearWord { get; init; }
    public int ArcNotWindingDown { get; init; }
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
    public bool HasExclamation { get; set; }
    public bool MissingGroundingAnchor { get; set; }
    public bool CompanionLeak { get; set; }
    public bool EchoedFearWord { get; set; }
    public bool ArcNotWindingDown { get; set; }
    public string? Error { get; set; }
}
