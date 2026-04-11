using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var promptsPath = Path.Combine(AppContext.BaseDirectory, "prompts.json");
var resultsDir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(resultsDir);

// --- Thresholds for weak-case detection ---
const int MinResponseLen = 100;   // 3 Armenian sentences ~100+ chars
const int MaxResponseLen = 800;   // 5 sentences should be well under 800
const int MaxChoiceLen = 60;      // 3-7 Armenian words ~15-50 chars, 60 generous

var armenianRegex = new Regex(@"[\u0530-\u058F]");

var jsonOpts = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };

// --- Step 1: Register a fresh device ---
Console.WriteLine($"Benchmark target: {baseUrl}");
Console.WriteLine("Registering device...");

var regBody = new { macAddress = $"BENCH-{DateTime.UtcNow:HHmmss}" };
var regResp = await http.PostAsJsonAsync("/api/devices/register", regBody);
regResp.EnsureSuccessStatusCode();
var device = await regResp.Content.ReadFromJsonAsync<DeviceReg>(jsonOpts)
    ?? throw new Exception("Device registration returned null");

http.DefaultRequestHeaders.Add("X-Device-Id", device.DeviceId.ToString());
http.DefaultRequestHeaders.Add("X-Api-Key", device.ApiKey);
Console.WriteLine($"Device: {device.DeviceId}\n");

// --- Step 2: Load prompts ---
var prompts = JsonSerializer.Deserialize<List<Prompt>>(
    await File.ReadAllTextAsync(promptsPath), jsonOpts)
    ?? throw new Exception("Failed to load prompts.json");

Console.WriteLine($"Loaded {prompts.Count} prompts\n");
Console.WriteLine("ID   | Start | ChA | ChB | Diff | SSID | Cont | SameConv | SameSS | ContCh | Weak");
Console.WriteLine("-----|-------|-----|-----|------|------|------|----------|--------|--------|-----");

var results = new List<TestResult>();
int startOk = 0, choiceOk = 0, contOk = 0, sameSessionOk = 0;
var failures = new List<string>();
var weakCases = new List<string>();

foreach (var prompt in prompts)
{
    var result = new TestResult { Id = prompt.Id, Message = prompt.Message };

    // --- Step 3: Send start request ---
    ChatResponse? startResp;
    try
    {
        var body = new { message = prompt.Message };
        var resp = await http.PostAsJsonAsync("/api/chat", body);
        resp.EnsureSuccessStatusCode();
        startResp = await resp.Content.ReadFromJsonAsync<ChatResponse>(jsonOpts);
    }
    catch (Exception ex)
    {
        result.StartError = ex.Message;
        results.Add(result);
        failures.Add($"{prompt.Id}: start failed \u2014 {ex.Message}");
        PrintRow(prompt.Id, false, false, false, false, false, false, false, false, false, false);
        continue;
    }

    if (startResp is null)
    {
        result.StartError = "null response";
        results.Add(result);
        failures.Add($"{prompt.Id}: start returned null");
        PrintRow(prompt.Id, false, false, false, false, false, false, false, false, false, false);
        continue;
    }

    result.StartResponse = startResp;
    result.StartText = startResp.Response;
    result.StartChoiceA = startResp.ChoiceA;
    result.StartChoiceB = startResp.ChoiceB;

    bool hasResponse = !string.IsNullOrWhiteSpace(startResp.Response);
    bool hasChoiceA = !string.IsNullOrWhiteSpace(startResp.ChoiceA);
    bool hasChoiceB = !string.IsNullOrWhiteSpace(startResp.ChoiceB);
    bool choicesDiff = hasChoiceA && hasChoiceB && startResp.ChoiceA != startResp.ChoiceB;
    bool hasSsid = startResp.StorySessionId.HasValue;
    bool hasConvId = startResp.ConversationId != Guid.Empty;

    result.StartOk = hasResponse && hasConvId;
    result.ChoiceAOk = hasChoiceA;
    result.ChoiceBOk = hasChoiceB;
    result.ChoicesDifferent = choicesDiff;
    result.HasStorySessionId = hasSsid;

    // Text metrics
    result.StartResponseLen = startResp.Response?.Length ?? 0;
    result.ChoiceALen = startResp.ChoiceA?.Length ?? 0;
    result.ChoiceBLen = startResp.ChoiceB?.Length ?? 0;
    result.StartHasArmenian = hasResponse && armenianRegex.IsMatch(startResp.Response);

    if (result.StartOk) startOk++;
    if (hasChoiceA && hasChoiceB && choicesDiff) choiceOk++;

    if (!hasResponse) failures.Add($"{prompt.Id}: empty response");
    if (!hasChoiceA) failures.Add($"{prompt.Id}: missing choiceA");
    if (!hasChoiceB) failures.Add($"{prompt.Id}: missing choiceB");
    if (!choicesDiff && hasChoiceA && hasChoiceB) failures.Add($"{prompt.Id}: choiceA == choiceB");
    if (!hasSsid) failures.Add($"{prompt.Id}: missing storySessionId");

    // Weak-case flags for start
    bool hasWeak = false;
    if (result.StartResponseLen < MinResponseLen && hasResponse)
    {
        result.WeakFlags.Add("start_too_short");
        weakCases.Add($"{prompt.Id}: start too short ({result.StartResponseLen} chars)");
        hasWeak = true;
    }
    if (result.StartResponseLen > MaxResponseLen)
    {
        result.WeakFlags.Add("start_too_long");
        weakCases.Add($"{prompt.Id}: start too long ({result.StartResponseLen} chars)");
        hasWeak = true;
    }
    if (hasChoiceA && result.ChoiceALen > MaxChoiceLen)
    {
        result.WeakFlags.Add("choiceA_too_long");
        weakCases.Add($"{prompt.Id}: choiceA too long ({result.ChoiceALen} chars)");
        hasWeak = true;
    }
    if (hasChoiceB && result.ChoiceBLen > MaxChoiceLen)
    {
        result.WeakFlags.Add("choiceB_too_long");
        weakCases.Add($"{prompt.Id}: choiceB too long ({result.ChoiceBLen} chars)");
        hasWeak = true;
    }
    if (!choicesDiff && hasChoiceA && hasChoiceB)
    {
        result.WeakFlags.Add("choices_identical");
        hasWeak = true;
    }
    if (!result.StartHasArmenian && hasResponse)
    {
        result.WeakFlags.Add("no_armenian_start");
        weakCases.Add($"{prompt.Id}: no Armenian letters in start");
        hasWeak = true;
    }

    // --- Step 4: Send continuation if possible ---
    bool contSuccess = false;
    bool sameConv = false;
    bool sameSs = false;
    bool contHasChoices = false;

    if (hasChoiceA && hasSsid)
    {
        try
        {
            var contBody = new
            {
                message = startResp.ChoiceA,
                storySessionId = startResp.StorySessionId,
                selectedChoice = "A"
            };
            var contResp = await http.PostAsJsonAsync("/api/chat", contBody);
            contResp.EnsureSuccessStatusCode();
            var cont = await contResp.Content.ReadFromJsonAsync<ChatResponse>(jsonOpts);

            if (cont is not null)
            {
                result.ContinuationResponse = cont;
                result.ContinuationText = cont.Response;
                result.ContinuationChoiceA = cont.ChoiceA;
                result.ContinuationChoiceB = cont.ChoiceB;

                bool contHasResponse = !string.IsNullOrWhiteSpace(cont.Response);
                bool contDifferent = cont.Response != startResp.Response;
                sameConv = cont.ConversationId == startResp.ConversationId;
                sameSs = cont.StorySessionId.HasValue
                    && cont.StorySessionId == startResp.StorySessionId;
                contHasChoices = !string.IsNullOrWhiteSpace(cont.ChoiceA)
                    && !string.IsNullOrWhiteSpace(cont.ChoiceB);

                contSuccess = contHasResponse && contDifferent;
                result.ContinuationOk = contSuccess;
                result.SameConversationId = sameConv;
                result.SameStorySessionId = sameSs;
                result.ContinuationHasChoices = contHasChoices;

                // Continuation text metrics
                result.ContinuationResponseLen = cont.Response?.Length ?? 0;
                result.ContinuationHasArmenian = contHasResponse && armenianRegex.IsMatch(cont.Response);

                if (!contHasResponse) failures.Add($"{prompt.Id}: continuation empty");
                if (!contDifferent) failures.Add($"{prompt.Id}: continuation same as start");
                if (!sameConv) failures.Add($"{prompt.Id}: conversationId changed");
                if (!sameSs) failures.Add($"{prompt.Id}: storySessionId changed");
                if (!contHasChoices) failures.Add($"{prompt.Id}: continuation missing choices");

                // Weak-case flags for continuation
                if (!contDifferent && contHasResponse)
                {
                    result.WeakFlags.Add("continuation_identical");
                    weakCases.Add($"{prompt.Id}: continuation identical to start");
                    hasWeak = true;
                }
                if (!result.ContinuationHasArmenian && contHasResponse)
                {
                    result.WeakFlags.Add("no_armenian_continuation");
                    weakCases.Add($"{prompt.Id}: no Armenian letters in continuation");
                    hasWeak = true;
                }
            }
        }
        catch (Exception ex)
        {
            result.ContinuationError = ex.Message;
            failures.Add($"{prompt.Id}: continuation failed \u2014 {ex.Message}");
        }
    }

    if (contSuccess) contOk++;
    if (sameSs) sameSessionOk++;

    PrintRow(prompt.Id, result.StartOk, hasChoiceA, hasChoiceB, choicesDiff,
        hasSsid, contSuccess, sameConv, sameSs, contHasChoices, hasWeak);
    results.Add(result);
}

// --- Step 5: Save results ---
var outputPath = Path.Combine(resultsDir, $"run_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(results, jsonOpts));

// --- Step 5b: Generate markdown report ---
var mdPath = Path.ChangeExtension(outputPath, ".md");
{
    var md = new System.Text.StringBuilder();
    md.AppendLine("# Story Benchmark Report");
    md.AppendLine();
    md.AppendLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    md.AppendLine($"**Target:** {baseUrl}");
    md.AppendLine($"**Prompts:** {prompts.Count}");
    md.AppendLine();

    // Summary table
    int _total = prompts.Count;
    int _contEligible = results.Count(r => r.HasStorySessionId && r.ChoiceAOk);
    md.AppendLine("## Summary");
    md.AppendLine();
    md.AppendLine($"| Metric | Result |");
    md.AppendLine($"|--------|--------|");
    md.AppendLine($"| Start success | {startOk}/{_total} |");
    md.AppendLine($"| Choice availability | {choiceOk}/{_total} |");
    md.AppendLine($"| Continuation success | {contOk}/{_contEligible} |");
    md.AppendLine($"| Same session retained | {sameSessionOk}/{_contEligible} |");
    md.AppendLine($"| Weak cases | {weakCases.Count} |");
    md.AppendLine();

    // Weak cases section
    if (weakCases.Count > 0)
    {
        md.AppendLine("## Weak Cases");
        md.AppendLine();
        foreach (var w in weakCases)
            md.AppendLine($"- {w}");
        md.AppendLine();
    }

    // Per-test sections
    md.AppendLine("## Test Cases");
    md.AppendLine();
    foreach (var r in results)
    {
        var flags = r.WeakFlags.Count > 0 ? $" **[{string.Join(", ", r.WeakFlags)}]**" : "";
        md.AppendLine($"### {r.Id}{flags}");
        md.AppendLine();
        md.AppendLine($"**Prompt:** {r.Message}");
        md.AppendLine();

        if (r.StartError is not null)
        {
            md.AppendLine($"**Error:** {r.StartError}");
        }
        else
        {
            md.AppendLine($"**Start response** ({r.StartResponseLen} chars):");
            md.AppendLine($"> {r.StartText}");
            md.AppendLine();
            md.AppendLine($"- **Choice A:** {r.StartChoiceA ?? "(none)"}");
            md.AppendLine($"- **Choice B:** {r.StartChoiceB ?? "(none)"}");
        }
        md.AppendLine();

        if (r.ContinuationText is not null)
        {
            md.AppendLine($"**Continuation** ({r.ContinuationResponseLen} chars):");
            md.AppendLine($"> {r.ContinuationText}");
            md.AppendLine();
            md.AppendLine($"- **Choice A:** {r.ContinuationChoiceA ?? "(none)"}");
            md.AppendLine($"- **Choice B:** {r.ContinuationChoiceB ?? "(none)"}");
        }
        else if (r.ContinuationError is not null)
        {
            md.AppendLine($"**Continuation error:** {r.ContinuationError}");
        }

        md.AppendLine();
        md.AppendLine("---");
        md.AppendLine();
    }

    await File.WriteAllTextAsync(mdPath, md.ToString());
}

// --- Step 6: Print summary ---
int total = prompts.Count;
int contEligible = results.Count(r => r.HasStorySessionId && r.ChoiceAOk);

var startLens = results.Where(r => r.StartResponseLen > 0).Select(r => r.StartResponseLen).ToList();
var contLens = results.Where(r => r.ContinuationResponseLen > 0).Select(r => r.ContinuationResponseLen).ToList();
double avgStartLen = startLens.Count > 0 ? startLens.Average() : 0;
double avgContLen = contLens.Count > 0 ? contLens.Average() : 0;

Console.WriteLine();
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine("  STORY BENCHMARK SUMMARY");
Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
Console.WriteLine($"  Total prompts:          {total}");
Console.WriteLine($"  Start success:          {startOk}/{total} ({Pct(startOk, total)})");
Console.WriteLine($"  Choice availability:    {choiceOk}/{total} ({Pct(choiceOk, total)})");
Console.WriteLine($"  Continuation success:   {contOk}/{contEligible} ({Pct(contOk, contEligible)})");
Console.WriteLine($"  Same session retained:  {sameSessionOk}/{contEligible} ({Pct(sameSessionOk, contEligible)})");
Console.WriteLine();
Console.WriteLine($"  Avg start response:     {avgStartLen:F0} chars");
Console.WriteLine($"  Avg continuation resp:  {avgContLen:F0} chars");
Console.WriteLine($"  Weak cases:             {weakCases.Count}");
Console.WriteLine($"  Results JSON:           {outputPath}");
Console.WriteLine($"  Results markdown:       {mdPath}");

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

// Exit code: 0 if all start+choice checks pass, 1 otherwise
return (startOk == total && choiceOk == total) ? 0 : 1;

// --- Helpers ---

static string Pct(int n, int d) => d == 0 ? "N/A" : $"{100 * n / d}%";

static void PrintRow(string id, bool start, bool chA, bool chB, bool diff,
    bool ssid, bool cont, bool sameConv, bool sameSs, bool contCh, bool weak)
{
    static string Y(bool b) => b ? "  Y  " : "  -  ";
    var w = weak ? " \u26a0" : "  ";
    Console.WriteLine($"{id,4} |{Y(start)}|{Y(chA)}|{Y(chB)}|{Y(diff)}|{Y(ssid)}|{Y(cont)}|{Y(sameConv)}   |{Y(sameSs)}  |{Y(contCh)}| {w}");
}

// --- DTOs ---

record Prompt
{
    public string Id { get; init; } = "";
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

record TestResult
{
    public string Id { get; init; } = "";
    public string Message { get; init; } = "";
    public bool StartOk { get; set; }
    public bool ChoiceAOk { get; set; }
    public bool ChoiceBOk { get; set; }
    public bool ChoicesDifferent { get; set; }
    public bool HasStorySessionId { get; set; }
    public bool ContinuationOk { get; set; }
    public bool SameConversationId { get; set; }
    public bool SameStorySessionId { get; set; }
    public bool ContinuationHasChoices { get; set; }
    // Flat text fields for easy inspection
    public string? StartText { get; set; }
    public string? StartChoiceA { get; set; }
    public string? StartChoiceB { get; set; }
    public string? ContinuationText { get; set; }
    public string? ContinuationChoiceA { get; set; }
    public string? ContinuationChoiceB { get; set; }
    // Text metrics
    public int StartResponseLen { get; set; }
    public int ChoiceALen { get; set; }
    public int ChoiceBLen { get; set; }
    public bool StartHasArmenian { get; set; }
    public int ContinuationResponseLen { get; set; }
    public bool ContinuationHasArmenian { get; set; }
    // Weak-case flags
    public List<string> WeakFlags { get; set; } = [];
    // Errors and raw responses
    public string? StartError { get; set; }
    public string? ContinuationError { get; set; }
    public ChatResponse? StartResponse { get; set; }
    public ChatResponse? ContinuationResponse { get; set; }
}
