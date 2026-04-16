using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// CLI: first positional arg is baseUrl; --write-baseline saves baseline.json
// to AppContext.BaseDirectory after the run. Operator copies the generated
// file to the source tools/ModeBenchmark/baseline.json and commits.
bool writeBaseline = args.Any(a => a == "--write-baseline");
var positional = args.Where(a => !a.StartsWith("--")).ToArray();
var baseUrl = positional.Length > 0 ? positional[0] : "http://localhost:5000";
var promptsPath = Path.Combine(AppContext.BaseDirectory, "prompts.json");
var baselinePath = Path.Combine(AppContext.BaseDirectory, "baseline.json");
var resultsDir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(resultsDir);

// --- Mode-specific thresholds ---
const int GameMaxLen = 200;
const int CuriosityMaxLen = 200;
const int CalmMaxLen = 400;

var armenianRegex = new Regex(@"[\u0530-\u058F]");
var latinRunRegex = new Regex(@"[A-Za-z]{4,}");
var choiceBlockRegex = new Regex(@"CHOICE_[AB]\s*:", RegexOptions.IgnoreCase);

var jsonOpts = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };

// --- Step 1: Register a fresh device ---
Console.WriteLine($"ModeBenchmark target: {baseUrl}");
Console.WriteLine("Registering device...");

var regBody = new { macAddress = $"MBENCH-{DateTime.UtcNow:HHmmss}" };
var regResp = await http.PostAsJsonAsync("/api/devices/register", regBody);
regResp.EnsureSuccessStatusCode();
var device = await regResp.Content.ReadFromJsonAsync<DeviceReg>(jsonOpts)
    ?? throw new Exception("Device registration returned null");

http.DefaultRequestHeaders.Add("X-Device-Id", device.DeviceId.ToString());
http.DefaultRequestHeaders.Add("X-Api-Key", device.ApiKey);
Console.WriteLine($"Device: {device.DeviceId}\n");

// --- Step 2: Load prompts ---
var prompts = JsonSerializer.Deserialize<List<ModePrompt>>(
    await File.ReadAllTextAsync(promptsPath), jsonOpts)
    ?? throw new Exception("Failed to load prompts");
Console.WriteLine($"Loaded {prompts.Count} prompts\n");

// --- Step 3: Run prompts ---
var results = new List<ModeTestResult>();
var failures = new List<string>();
var weakCases = new List<string>();

// Per-mode counters
int gameTotal = 0, gameOk = 0;
int riddleTotal = 0, riddleOk = 0;
int curiosityTotal = 0, curiosityOk = 0;
int calmTotal = 0, calmOk = 0;
int leakedChoiceBlock = 0, latinRunCount = 0;

// Table header
Console.WriteLine("ID   | Mode      | OK  | Len | Arm | Weak");
Console.WriteLine("-----|-----------|-----|-----|-----|-----");

foreach (var prompt in prompts)
{
    var result = new ModeTestResult { Id = prompt.Id, Mode = prompt.Mode, Message = prompt.Message };
    bool hasWeak = false;

    ChatResponse? resp = null;
    try
    {
        var body = new { message = prompt.Message };
        var httpResp = await http.PostAsJsonAsync("/api/chat", body);
        httpResp.EnsureSuccessStatusCode();
        resp = await httpResp.Content.ReadFromJsonAsync<ChatResponse>(jsonOpts);
    }
    catch (Exception ex)
    {
        result.Error = ex.Message;
        results.Add(result);
        failures.Add($"{prompt.Id}: request failed — {ex.Message}");
        PrintRow(prompt.Id, prompt.Mode, false, 0, false, false);
        continue;
    }

    if (resp is null)
    {
        result.Error = "null response";
        results.Add(result);
        failures.Add($"{prompt.Id}: null response");
        PrintRow(prompt.Id, prompt.Mode, false, 0, false, false);
        continue;
    }

    result.RawResponse = resp;
    result.ResponseText = resp.Response;
    result.ResponseLen = resp.Response?.Length ?? 0;
    result.HasArmenian = !string.IsNullOrWhiteSpace(resp.Response) && armenianRegex.IsMatch(resp.Response);
    result.HasChoiceBlock = !string.IsNullOrWhiteSpace(resp.Response) && choiceBlockRegex.IsMatch(resp.Response);
    result.HasLatinRun = !string.IsNullOrWhiteSpace(resp.Response) && latinRunRegex.IsMatch(resp.Response);

    bool hasResponse = !string.IsNullOrWhiteSpace(resp.Response);

    // Check for choice block presence in raw response or extracted fields
    bool hasChoiceFields = !string.IsNullOrWhiteSpace(resp.ChoiceA) || !string.IsNullOrWhiteSpace(resp.ChoiceB);
    bool choiceBlockDetected = result.HasChoiceBlock || hasChoiceFields;

    // Punctuation checks
    bool hasQuestion = hasResponse && (resp.Response!.Contains('?') || resp.Response.Contains('\u055E'));
    bool hasExclamation = hasResponse && (resp.Response!.Contains('!') || resp.Response.Contains('\u055C'));

    result.HasQuestion = hasQuestion;
    result.HasExclamation = hasExclamation;

    // --- Per-mode validation ---
    bool modeOk = false;

    switch (prompt.Mode)
    {
        case "game":
            gameTotal++;
            modeOk = hasResponse && result.HasArmenian && !choiceBlockDetected;
            if (modeOk) gameOk++;

            if (!hasResponse) failures.Add($"{prompt.Id}: empty response");
            if (!result.HasArmenian && hasResponse) failures.Add($"{prompt.Id}: no Armenian");
            if (choiceBlockDetected) { failures.Add($"{prompt.Id}: choice block in game mode"); }

            // Weak: too wordy
            if (hasResponse && result.ResponseLen > GameMaxLen)
            {
                result.WeakFlags.Add("game_too_wordy");
                weakCases.Add($"{prompt.Id}: game response too long ({result.ResponseLen} chars)");
                hasWeak = true;
            }
            break;

        case "riddle":
            riddleTotal++;
            modeOk = hasResponse && result.HasArmenian && !choiceBlockDetected;
            if (modeOk) riddleOk++;

            if (!hasResponse) failures.Add($"{prompt.Id}: empty response");
            if (!result.HasArmenian && hasResponse) failures.Add($"{prompt.Id}: no Armenian");
            if (choiceBlockDetected) { failures.Add($"{prompt.Id}: choice block in riddle mode"); }

            // Weak: riddle should contain a question
            if (hasResponse && !hasQuestion)
            {
                result.WeakFlags.Add("riddle_no_question");
                weakCases.Add($"{prompt.Id}: riddle has no question mark");
                hasWeak = true;
            }
            break;

        case "curiosity":
            curiosityTotal++;
            modeOk = hasResponse && result.HasArmenian && !choiceBlockDetected;
            if (modeOk) curiosityOk++;

            if (!hasResponse) failures.Add($"{prompt.Id}: empty response");
            if (!result.HasArmenian && hasResponse) failures.Add($"{prompt.Id}: no Armenian");
            if (choiceBlockDetected) { failures.Add($"{prompt.Id}: choice block in curiosity mode"); }

            // Weak: too long
            if (hasResponse && result.ResponseLen > CuriosityMaxLen)
            {
                result.WeakFlags.Add("curiosity_too_long");
                weakCases.Add($"{prompt.Id}: curiosity response too long ({result.ResponseLen} chars)");
                hasWeak = true;
            }
            // Weak: has question (curiosity should not ask back)
            if (hasQuestion)
            {
                result.WeakFlags.Add("curiosity_has_question");
                weakCases.Add($"{prompt.Id}: curiosity response contains question");
                hasWeak = true;
            }
            break;

        case "calm":
            calmTotal++;
            modeOk = hasResponse && result.HasArmenian && !choiceBlockDetected && !hasQuestion && !hasExclamation;
            if (modeOk) calmOk++;

            if (!hasResponse) failures.Add($"{prompt.Id}: empty response");
            if (!result.HasArmenian && hasResponse) failures.Add($"{prompt.Id}: no Armenian");
            if (choiceBlockDetected) { failures.Add($"{prompt.Id}: choice block in calm mode"); }
            if (hasQuestion) failures.Add($"{prompt.Id}: calm response has question");
            if (hasExclamation) failures.Add($"{prompt.Id}: calm response has exclamation");

            // Weak: too long
            if (hasResponse && result.ResponseLen > CalmMaxLen)
            {
                result.WeakFlags.Add("calm_too_long");
                weakCases.Add($"{prompt.Id}: calm response too long ({result.ResponseLen} chars)");
                hasWeak = true;
            }
            break;
    }

    result.ModeOk = modeOk;

    // Cross-mode weak signals
    if (choiceBlockDetected)
    {
        result.WeakFlags.Add("leaked_choice_block");
        leakedChoiceBlock++;
        if (!weakCases.Any(w => w.Contains(prompt.Id) && w.Contains("choice block")))
        {
            weakCases.Add($"{prompt.Id}: leaked choice block in {prompt.Mode} mode");
        }
        hasWeak = true;
    }
    if (result.HasLatinRun)
    {
        result.WeakFlags.Add("latin_run");
        latinRunCount++;
        weakCases.Add($"{prompt.Id}: 4+ Latin letter run in response");
        hasWeak = true;
    }

    results.Add(result);
    PrintRow(prompt.Id, prompt.Mode, modeOk, result.ResponseLen, result.HasArmenian, hasWeak);
}

// --- Step 4: Summary ---
int totalOk = gameOk + riddleOk + curiosityOk + calmOk;
int total = prompts.Count;

Console.WriteLine();
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine("  MODE BENCHMARK SUMMARY");
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine($"  Total prompts:     {total}");
Console.WriteLine($"  Overall pass:      {totalOk}/{total} ({Pct(totalOk, total)})");
Console.WriteLine();
Console.WriteLine($"  Game:              {gameOk}/{gameTotal} ({Pct(gameOk, gameTotal)})");
Console.WriteLine($"  Riddle:            {riddleOk}/{riddleTotal} ({Pct(riddleOk, riddleTotal)})");
Console.WriteLine($"  Curiosity:         {curiosityOk}/{curiosityTotal} ({Pct(curiosityOk, curiosityTotal)})");
Console.WriteLine($"  Calm:              {calmOk}/{calmTotal} ({Pct(calmOk, calmTotal)})");
Console.WriteLine();
Console.WriteLine($"  Weak cases:        {weakCases.Count}");
Console.WriteLine($"  Leaked choice:     {leakedChoiceBlock}");
Console.WriteLine($"  Latin run:         {latinRunCount}");

// --- Step 5: Save results ---
var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
var resultsJson = Path.Combine(resultsDir, $"run_{timestamp}.json");
var resultsMd = Path.Combine(resultsDir, $"run_{timestamp}.md");

await File.WriteAllTextAsync(resultsJson,
    JsonSerializer.Serialize(results, jsonOpts));

// Markdown report
var md = new System.Text.StringBuilder();
md.AppendLine("# ModeBenchmark Results");
md.AppendLine();
md.AppendLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
md.AppendLine($"**Target:** {baseUrl}");
md.AppendLine($"**Prompts:** {total}");
md.AppendLine();
md.AppendLine("| Mode | Pass | Total |");
md.AppendLine("|------|------|-------|");
md.AppendLine($"| Game | {gameOk} | {gameTotal} |");
md.AppendLine($"| Riddle | {riddleOk} | {riddleTotal} |");
md.AppendLine($"| Curiosity | {curiosityOk} | {curiosityTotal} |");
md.AppendLine($"| Calm | {calmOk} | {calmTotal} |");
md.AppendLine($"| **Total** | **{totalOk}** | **{total}** |");
md.AppendLine();
if (weakCases.Count > 0)
{
    md.AppendLine("## Weak Cases");
    foreach (var w in weakCases) md.AppendLine($"- {w}");
    md.AppendLine();
}
if (failures.Count > 0)
{
    md.AppendLine("## Failures");
    foreach (var f in failures) md.AppendLine($"- {f}");
}
await File.WriteAllTextAsync(resultsMd, md.ToString());

Console.WriteLine();
Console.WriteLine($"  Results JSON:      {resultsJson}");
Console.WriteLine($"  Results markdown:  {resultsMd}");

// --- Step 6: Baseline comparison ---
var currentMetrics = new ModeMetrics
{
    Total = total,
    GameTotal = gameTotal, GameOk = gameOk,
    RiddleTotal = riddleTotal, RiddleOk = riddleOk,
    CuriosityTotal = curiosityTotal, CuriosityOk = curiosityOk,
    CalmTotal = calmTotal, CalmOk = calmOk,
    WeakCases = weakCases.Count,
    LeakedChoiceBlock = leakedChoiceBlock,
    LatinRun = latinRunCount,
    Placeholder = false,
};

if (File.Exists(baselinePath))
{
    try
    {
        var baseline = JsonSerializer.Deserialize<ModeMetrics>(
            await File.ReadAllTextAsync(baselinePath), jsonOpts);

        if (baseline is not null && !baseline.Placeholder)
        {
            Console.WriteLine();
            Console.WriteLine("  Delta vs baseline (negative = improvement for weak counts)");
            Console.WriteLine($"    game_ok:              {Delta(baseline.GameOk, currentMetrics.GameOk)}");
            Console.WriteLine($"    riddle_ok:            {Delta(baseline.RiddleOk, currentMetrics.RiddleOk)}");
            Console.WriteLine($"    curiosity_ok:         {Delta(baseline.CuriosityOk, currentMetrics.CuriosityOk)}");
            Console.WriteLine($"    calm_ok:              {Delta(baseline.CalmOk, currentMetrics.CalmOk)}");
            Console.WriteLine($"    weak_cases:           {Delta(baseline.WeakCases, currentMetrics.WeakCases)}");
            Console.WriteLine($"    leaked_choice_block:  {Delta(baseline.LeakedChoiceBlock, currentMetrics.LeakedChoiceBlock)}");
            Console.WriteLine($"    latin_run:            {Delta(baseline.LatinRun, currentMetrics.LatinRun)}");
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
    Console.WriteLine($"  the generated file to tools/ModeBenchmark/baseline.json and commit.");
}

if (writeBaseline)
{
    await File.WriteAllTextAsync(
        baselinePath,
        JsonSerializer.Serialize(currentMetrics, jsonOpts));
    Console.WriteLine();
    Console.WriteLine($"  Baseline written: {baselinePath}");
    Console.WriteLine($"  Copy to: tools/ModeBenchmark/baseline.json");
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"  FAILURES ({failures.Count}):");
    foreach (var f in failures)
        Console.WriteLine($"    - {f}");
}

if (weakCases.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"  WEAK CASES ({weakCases.Count}):");
    foreach (var w in weakCases)
        Console.WriteLine($"    \u26a0 {w}");
}

if (failures.Count == 0 && weakCases.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("  ALL CHECKS PASSED \u2014 NO WEAK CASES");
}

Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");

// Exit code: 0 if all mode checks pass, 1 otherwise
return (totalOk == total) ? 0 : 1;

// --- Helpers ---

static string Pct(int n, int d) => d == 0 ? "N/A" : $"{100 * n / d}%";

static string Delta(int baseline, int current)
{
    var d = current - baseline;
    var sign = d > 0 ? "+" : (d < 0 ? "" : "");
    return $"{baseline} -> {current} ({sign}{d})";
}

static void PrintRow(string id, string mode, bool ok, int len, bool arm, bool weak)
{
    static string Y(bool b) => b ? " Y " : " - ";
    var w = weak ? "\u26a0" : " ";
    Console.WriteLine($"{id,4} | {mode,-9} | {Y(ok)} | {len,3} | {Y(arm)} | {w}");
}

// --- DTOs ---

record ModePrompt
{
    public string Id { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Message { get; init; } = "";
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
}

record DeviceReg
{
    public Guid DeviceId { get; init; }
    public string ApiKey { get; init; } = "";
}

record ModeMetrics
{
    public int Total { get; init; }
    public int GameTotal { get; init; }
    public int GameOk { get; init; }
    public int RiddleTotal { get; init; }
    public int RiddleOk { get; init; }
    public int CuriosityTotal { get; init; }
    public int CuriosityOk { get; init; }
    public int CalmTotal { get; init; }
    public int CalmOk { get; init; }
    public int WeakCases { get; init; }
    public int LeakedChoiceBlock { get; init; }
    public int LatinRun { get; init; }
    public bool Placeholder { get; init; }
}

record ModeTestResult
{
    public string Id { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Message { get; init; } = "";
    public bool ModeOk { get; set; }
    public string? ResponseText { get; set; }
    public int ResponseLen { get; set; }
    public bool HasArmenian { get; set; }
    public bool HasChoiceBlock { get; set; }
    public bool HasLatinRun { get; set; }
    public bool HasQuestion { get; set; }
    public bool HasExclamation { get; set; }
    public List<string> WeakFlags { get; set; } = [];
    public string? Error { get; set; }
    public ChatResponse? RawResponse { get; set; }
}
