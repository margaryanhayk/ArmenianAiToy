using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// CLI: first positional arg is baseUrl; --write-baseline saves baseline.json
// to AppContext.BaseDirectory after the run. Operator copies the generated
// file to the source tools/GameBenchmark/baseline.json and commits.
//
// GameBenchmark runs MULTI-TURN scenarios — each scenario gets a fresh
// device registration so per-conversation state (GameSessions) starts
// empty for every scenario. This is what the generic ModeBenchmark cannot
// do, and is the whole point of splitting Game out into its own tool.
bool writeBaseline = args.Any(a => a == "--write-baseline");
var positional = args.Where(a => !a.StartsWith("--")).ToArray();
var baseUrl = positional.Length > 0 ? positional[0] : "http://localhost:5000";
var promptsPath = Path.Combine(AppContext.BaseDirectory, "prompts.json");
var baselinePath = Path.Combine(AppContext.BaseDirectory, "baseline.json");
var resultsDir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(resultsDir);

// --- Thresholds ---
const int MaxTurnLen = 200;                 // mirrors ResponseQualityGate.game_too_long
const double VarietyJaccardThreshold = 0.7; // adjacent-turn similarity over this = repetitive

var armenianRegex = new Regex(@"[\u0530-\u058F]");
var latinRunRegex = new Regex(@"[A-Za-z]{4,}");
var choiceBlockRegex = new Regex(@"CHOICE_[AB]\s*:", RegexOptions.IgnoreCase);
var leakedTailRegex = new Regex(
    @"\b(?:GAME_TYPE|GAME_DIFFICULTY|GAME_TURN_KIND|STORY_MEMORY|RIDDLE_ANSWER|RIDDLE_CATEGORY|RIDDLE_DIFFICULTY|RIDDLE_TURN_KIND)\s*:",
    RegexOptions.IgnoreCase);

// "Did the model ask the child for permission to continue?" — the v3 prompt
// explicitly bans this. Patterns are lowercased Armenian fragments.
var askingPermissionPatterns = new[]
{
    "\u0578\u0582\u0566\u0578\u0582\u055e\u0574 \u0565\u057d \u0577\u0561\u0580\u0578\u0582\u0576\u0561\u056f\u0565\u056c", // ուզու՞մ ես շարունակել
    "\u0577\u0561\u0580\u0578\u0582\u0576\u0561\u056f\u0565\u055e\u0576\u0584",                                               // շարունակե՞նք
    "\u0569\u0565\u055e \u0578\u0582\u0580\u056b\u0577 \u0562\u0561\u0576",                                                   // թե՞ ուրիշ բան
};

// Celebration phrase pool from the v3 prompt's CELEBRATION ROTATION section.
// Substring match on the Armenian forms — order matters for which phrase is
// detected first when multiple appear.
var celebrationPhrases = new[]
{
    "\u0531\u057a\u0580\u0565\u055b\u057d",                                                                       // Ապրե՛ս
    "\u0540\u0561\u055b, \u0573\u056b\u0577\u057f \u0567",                                                         // Հա՛, ճիշտ է
    "\u053c\u0561\u057e\u0576 \u0565\u057d",                                                                       // Լավն ես
    "\u0532\u0580\u0561\u055b\u057e\u0578",                                                                        // Բրա՛վո
    "\u0540\u0565\u055b\u0575, \u0564\u0578\u0582 \u056f\u0561\u0580\u0578\u0572 \u0565\u057d",                    // Հե՛յ, դու կարող ես
    "\u0547\u0561\u055b\u057f \u056c\u0561\u057e",                                                                 // Շա՛տ լավ
    "\u0540\u056b\u0561\u0576\u0561\u056c\u056b\u055b",                                                            // Հիանալի՛
    "\u0531\u057a\u0580\u056b \u0564\u0578\u0582",                                                                 // Ապրի դու
    "\u054e\u0561\u055c\u0575, \u056b\u0576\u0579 \u056c\u0561\u057e",                                             // Վա՜յ, ինչ լավ
    "\u0543\u056b\u055b\u0577\u057f \u0567\u0580",                                                                 // Ճի՛շտ էր
};

// "Did the model mix two game types in a single response?" — heuristic.
// One distinctive Armenian keyword per game-type. If 2+ appear in one
// response, likely a mixing violation. Keep narrow to avoid false positives.
var typeKeywords = new (string Type, string[] Keywords)[]
{
    ("animal_sound", new[] { "\u0571\u0561\u0575\u0576" }),                     // ձայն
    ("color_find",   new[] { "\u0563\u0578\u0582\u0575\u0576", "\u0563\u0578\u0582\u0575\u0576\u0568" }), // գույն / գույնը
    ("clap_along",   new[] { "\u056e\u0561\u0583" }),                            // ծափ
    ("count_to",     new[] { "\u0570\u0561\u0577\u057e" }),                      // հաշվ (հաշվենք)
    ("body_part",    new[] { "\u0564\u056b\u057a\u0579\u056b\u0580" }),          // դիպչիր
};

var jsonOpts = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

Console.WriteLine($"GameBenchmark target: {baseUrl}");

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
int continueVarietyLow = 0;
int celebrationRepeat = 0;
int askingPermission = 0;
int mixingTypes = 0;

Console.WriteLine("ID    | Turns | OkN | Hard | Label");
Console.WriteLine("------|-------|-----|------|--------------------------");

foreach (var scenario in scenarios)
{
    var sResult = new ScenarioResult { Id = scenario.Id, Label = scenario.Label };

    // Fresh HttpClient + device per scenario so GameSessions starts clean.
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };

    DeviceReg device;
    try
    {
        var regBody = new { macAddress = $"GBENCH-{scenario.Id}-{DateTime.UtcNow:HHmmssfff}" };
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
    string? prevResponse = null;
    string? prevCelebration = null;

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
            failures.Add($"{scenario.Id} turn '{turn.User}': choice block leaked into game mode");
            hard = true;
        }
        if (turnResult.HasLeakedTail)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': leaked tail block (GAME_*/RIDDLE_*/STORY_MEMORY)");
            leakedTail++;
            hard = true;
        }
        if (turnResult.HasLatinRun)
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': 4+ Latin letter run");
            latinRun++;
            hard = true;
        }
        if (resp.Mode != "game")
        {
            failures.Add($"{scenario.Id} turn '{turn.User}': mode='{resp.Mode}' (expected game)");
            hard = true;
        }
        if (text.Length > MaxTurnLen)
        {
            // Length over the cap is a weak signal in ModeBenchmark; mirror that
            // here — we don't fail the scenario, but we do count it.
            weakCases.Add($"{scenario.Id} turn '{turn.User}': length {text.Length} > {MaxTurnLen}");
        }

        if (!hard) { turnsOk++; sTurnsOk++; }
        else { sHardFail = true; }

        // Per-scenario weak signals (still recorded even on hard-fail turns).

        // asking_permission_to_continue
        var lower = text.ToLowerInvariant();
        if (askingPermissionPatterns.Any(p => lower.Contains(p)))
        {
            turnResult.AskedPermission = true;
            askingPermission++;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': asked permission to continue");
        }

        // mixing_two_types
        int typesPresent = typeKeywords.Count(t => t.Keywords.Any(k => text.Contains(k)));
        if (typesPresent >= 2)
        {
            turnResult.MixedTypes = true;
            mixingTypes++;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': mixed {typesPresent} game-type signals");
        }

        // continue_variety: adjacent-turn token-overlap
        if (prevResponse is not null)
        {
            double j = JaccardTokens(prevResponse, text);
            turnResult.JaccardWithPrev = j;
            if (j > VarietyJaccardThreshold)
            {
                continueVarietyLow++;
                weakCases.Add($"{scenario.Id} turn '{turn.User}': adjacent-turn similarity {j:F2} > {VarietyJaccardThreshold}");
            }
        }
        prevResponse = text;

        // celebration_repeat: pick the first matching celebration; flag if same as previous
        var celebration = celebrationPhrases.FirstOrDefault(p => text.Contains(p));
        if (celebration is not null && celebration == prevCelebration)
        {
            turnResult.CelebrationRepeat = true;
            celebrationRepeat++;
            weakCases.Add($"{scenario.Id} turn '{turn.User}': celebration '{celebration}' repeated from prior turn");
        }
        prevCelebration = celebration;

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
Console.WriteLine("  GAME BENCHMARK SUMMARY");
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine($"  Scenarios pass:       {scenariosOk}/{scenarios.Count}");
Console.WriteLine($"  Turns pass:           {turnsOk}/{totalTurns}");
Console.WriteLine();
Console.WriteLine($"  Weak cases:           {weakCases.Count}");
Console.WriteLine($"  Leaked tail:          {leakedTail}");
Console.WriteLine($"  Latin run:            {latinRun}");
Console.WriteLine($"  Variety low:          {continueVarietyLow}");
Console.WriteLine($"  Celebration repeat:   {celebrationRepeat}");
Console.WriteLine($"  Asking permission:    {askingPermission}");
Console.WriteLine($"  Mixing types:         {mixingTypes}");

// --- Save results ---
var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
var resultsJson = Path.Combine(resultsDir, $"run_{timestamp}.json");
var resultsMd = Path.Combine(resultsDir, $"run_{timestamp}.md");

await File.WriteAllTextAsync(resultsJson, JsonSerializer.Serialize(results, jsonOpts));

var md = new System.Text.StringBuilder();
md.AppendLine("# GameBenchmark Results");
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
md.AppendLine($"| Continue variety low | {continueVarietyLow} |");
md.AppendLine($"| Celebration repeat | {celebrationRepeat} |");
md.AppendLine($"| Asking permission | {askingPermission} |");
md.AppendLine($"| Mixing types | {mixingTypes} |");
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
var current = new GameMetrics
{
    TotalScenarios = scenarios.Count,
    ScenariosOk = scenariosOk,
    TurnsTotal = totalTurns,
    TurnsOk = turnsOk,
    WeakCases = weakCases.Count,
    LeakedTail = leakedTail,
    LatinRun = latinRun,
    ContinueVarietyLow = continueVarietyLow,
    CelebrationRepeat = celebrationRepeat,
    AskingPermission = askingPermission,
    MixingTypes = mixingTypes,
    Placeholder = false,
};

if (File.Exists(baselinePath))
{
    try
    {
        var baseline = JsonSerializer.Deserialize<GameMetrics>(
            await File.ReadAllTextAsync(baselinePath), jsonOpts);
        if (baseline is not null && !baseline.Placeholder)
        {
            Console.WriteLine();
            Console.WriteLine("  Delta vs baseline (negative = improvement for weak counts)");
            Console.WriteLine($"    scenarios_ok:        {Delta(baseline.ScenariosOk, current.ScenariosOk)}");
            Console.WriteLine($"    turns_ok:            {Delta(baseline.TurnsOk, current.TurnsOk)}");
            Console.WriteLine($"    weak_cases:          {Delta(baseline.WeakCases, current.WeakCases)}");
            Console.WriteLine($"    leaked_tail:         {Delta(baseline.LeakedTail, current.LeakedTail)}");
            Console.WriteLine($"    latin_run:           {Delta(baseline.LatinRun, current.LatinRun)}");
            Console.WriteLine($"    variety_low:         {Delta(baseline.ContinueVarietyLow, current.ContinueVarietyLow)}");
            Console.WriteLine($"    celebration_repeat:  {Delta(baseline.CelebrationRepeat, current.CelebrationRepeat)}");
            Console.WriteLine($"    asking_permission:   {Delta(baseline.AskingPermission, current.AskingPermission)}");
            Console.WriteLine($"    mixing_types:        {Delta(baseline.MixingTypes, current.MixingTypes)}");
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
    Console.WriteLine($"  the generated file to tools/GameBenchmark/baseline.json and commit.");
}

if (writeBaseline)
{
    await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(current, jsonOpts));
    Console.WriteLine();
    Console.WriteLine($"  Baseline written: {baselinePath}");
    Console.WriteLine($"  Copy to: tools/GameBenchmark/baseline.json");
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
if (runSucceeded && File.Exists(baselinePath))
{
    try
    {
        var b = JsonSerializer.Deserialize<GameMetrics>(
            await File.ReadAllTextAsync(baselinePath), jsonOpts);
        if (b is not null && !b.Placeholder)
            baselineWeakCasesForSummary = b.WeakCases;
    }
    catch { /* leave null → verdict stays "unavailable" */ }
}
string regressionVerdict = baselineWeakCasesForSummary is null ? "unavailable"
    : current.WeakCases < baselineWeakCasesForSummary.Value ? "improved"
    : current.WeakCases > baselineWeakCasesForSummary.Value ? "regressed"
    : "unchanged";
var summaryPath = Path.Combine(resultsDir, "summary.json");
await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(new
{
    timestampUtc = DateTime.UtcNow.ToString(
        "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
    benchmarkName = "GameBenchmark",
    baselineWeakCases = baselineWeakCasesForSummary,
    currentWeakCases = current.WeakCases,
    regressionVerdict,
}, jsonOpts));

return scenariosOk == scenarios.Count ? 0 : 1;

// --- Helpers ---

static double JaccardTokens(string a, string b)
{
    var ta = new HashSet<string>(Regex.Split(a, @"[\s\p{P}]+")
        .Where(t => t.Length >= 3));
    var tb = new HashSet<string>(Regex.Split(b, @"[\s\p{P}]+")
        .Where(t => t.Length >= 3));
    if (ta.Count == 0 && tb.Count == 0) return 0;
    var union = ta.Union(tb).Count();
    if (union == 0) return 0;
    var inter = ta.Intersect(tb).Count();
    return (double)inter / union;
}

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

record GameMetrics
{
    public int TotalScenarios { get; init; }
    public int ScenariosOk { get; init; }
    public int TurnsTotal { get; init; }
    public int TurnsOk { get; init; }
    public int WeakCases { get; init; }
    public int LeakedTail { get; init; }
    public int LatinRun { get; init; }
    public int ContinueVarietyLow { get; init; }
    public int CelebrationRepeat { get; init; }
    public int AskingPermission { get; init; }
    public int MixingTypes { get; init; }
    public bool Placeholder { get; init; }
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
    public bool AskedPermission { get; set; }
    public bool MixedTypes { get; set; }
    public bool CelebrationRepeat { get; set; }
    public double? JaccardWithPrev { get; set; }
    public string? Error { get; set; }
}
