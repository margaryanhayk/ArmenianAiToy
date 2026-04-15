using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// CLI: first positional arg is baseUrl; --write-baseline saves baseline.json
// to AppContext.BaseDirectory after the run. Operator copies the generated
// file to the source tools/StoryBenchmark/baseline.json and commits.
bool writeBaseline = args.Any(a => a == "--write-baseline");
var positional = args.Where(a => !a.StartsWith("--")).ToArray();
var baseUrl = positional.Length > 0 ? positional[0] : "http://localhost:5000";
var promptsPath = Path.Combine(AppContext.BaseDirectory, "prompts.json");
var baselinePath = Path.Combine(AppContext.BaseDirectory, "baseline.json");
var resultsDir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(resultsDir);

// --- Thresholds for weak-case detection ---
const int MinResponseLen = 100;   // 3 Armenian sentences ~100+ chars
const int MaxResponseLen = 800;   // 5 sentences should be well under 800
const int MaxChoiceLen = 60;      // 3-7 Armenian words ~15-50 chars, 60 generous

// E3 thresholds (additive signals for E1/E2 validation)
const int MinTokenLen = 4;        // ignore short stop-tokens when comparing
const double RecapOverlapThreshold = 0.6;  // Jaccard overlap on first-sentence
                                           // Armenian tokens ≥ 0.6 is recap

var armenianRegex = new Regex(@"[\u0530-\u058F]");
var armenianOnlyRegex = new Regex(@"^[\u0530-\u058F]+$");

// Strip trailing punctuation (Latin + Armenian verjaket/question/exclam/comma).
var trailingPunct = new[] { '.', ',', '!', '?', ';', ':',
    '\u0589', '\u055E', '\u055C', '\u055D' };

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

    // E3 signal 1: same_first_verb on extracted choices.
    // Structurally weak choice pair — E2 guard should normally prevent this
    // reaching the client, so a non-zero rate here is either E2 missing a
    // case or the regenerated pair also being weak.
    if (hasChoiceA && hasChoiceB && choicesDiff)
    {
        var firstA = FirstArmenianToken(startResp.ChoiceA, trailingPunct);
        var firstB = FirstArmenianToken(startResp.ChoiceB, trailingPunct);
        if (firstA is not null && firstB is not null && firstA == firstB)
        {
            result.WeakFlags.Add("same_first_verb");
            weakCases.Add($"{prompt.Id}: CHOICE_A and CHOICE_B share first token «{firstA}»");
            hasWeak = true;
        }
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

                // E3 signal 2: continuation_no_label_reference.
                // Approximates E1's POST-CHOICE CONTINUATION rule: the
                // continuation should visibly act on the chosen label.
                // Conservative — we only flag when ChoiceA has at least one
                // ≥4-char Armenian token we can look for AND none appear
                // anywhere in the continuation text. Short labels (no
                // ≥4-char tokens) are skipped, not flagged.
                if (contHasResponse && !string.IsNullOrWhiteSpace(startResp.ChoiceA))
                {
                    var labelTokens = ExtractArmenianTokens(
                        startResp.ChoiceA, MinTokenLen, trailingPunct, armenianOnlyRegex);
                    if (labelTokens.Count > 0)
                    {
                        var contLower = cont.Response.ToLowerInvariant();
                        bool anyReferenced = labelTokens.Any(t => contLower.Contains(t));
                        if (!anyReferenced)
                        {
                            result.WeakFlags.Add("continuation_no_label_reference");
                            weakCases.Add(
                                $"{prompt.Id}: continuation does not reference any ≥4-char token from CHOICE_A");
                            hasWeak = true;
                        }
                    }
                }

                // E3 signal 3: start_continuation_recap_overlap.
                // Approximates E1's NO RECAP AFTER CHOICE rule. Take the
                // first sentence of start and the first sentence of
                // continuation (split on verjaket ։). Compute Jaccard
                // overlap on their ≥4-char Armenian tokens. Threshold
                // 0.6 is deliberately lenient — two first sentences that
                // naturally share a character name won't trip it; a
                // paraphrase will.
                if (contHasResponse && hasResponse)
                {
                    var startFirst = FirstSentence(startResp.Response ?? "");
                    var contFirst = FirstSentence(cont.Response ?? "");
                    var startTokens = ExtractArmenianTokens(
                        startFirst, MinTokenLen, trailingPunct, armenianOnlyRegex);
                    var contTokens = ExtractArmenianTokens(
                        contFirst, MinTokenLen, trailingPunct, armenianOnlyRegex);
                    var overlap = JaccardOverlap(startTokens, contTokens);
                    result.StartContinuationRecapOverlap = overlap;
                    if (overlap >= RecapOverlapThreshold
                        && startTokens.Count >= 2 && contTokens.Count >= 2)
                    {
                        result.WeakFlags.Add("start_continuation_recap_overlap");
                        weakCases.Add(
                            $"{prompt.Id}: first-sentence Jaccard overlap {overlap:F2} ≥ {RecapOverlapThreshold:F2}");
                        hasWeak = true;
                    }
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
    md.AppendLine($"| same_first_verb | {results.Count(r => r.WeakFlags.Contains("same_first_verb"))}/{_total} |");
    md.AppendLine($"| continuation_no_label_reference | {results.Count(r => r.WeakFlags.Contains("continuation_no_label_reference"))}/{_contEligible} |");
    md.AppendLine($"| start_continuation_recap_overlap | {results.Count(r => r.WeakFlags.Contains("start_continuation_recap_overlap"))}/{_contEligible} |");
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

// E3 signal summary
int sameFirstVerbCount = results.Count(r => r.WeakFlags.Contains("same_first_verb"));
int noLabelRefCount = results.Count(r => r.WeakFlags.Contains("continuation_no_label_reference"));
int recapCount = results.Count(r => r.WeakFlags.Contains("start_continuation_recap_overlap"));
var overlapVals = results.Where(r => r.ContinuationText is not null).Select(r => r.StartContinuationRecapOverlap).ToList();
double avgOverlap = overlapVals.Count > 0 ? overlapVals.Average() : 0;
Console.WriteLine();
Console.WriteLine("  E1/E2 signals");
Console.WriteLine($"    same_first_verb:                {sameFirstVerbCount}/{total}");
Console.WriteLine($"    continuation_no_label_reference:{noLabelRefCount}/{contEligible}");
Console.WriteLine($"    start_continuation_recap_overlap:{recapCount}/{contEligible}");
Console.WriteLine($"    avg recap-overlap:              {avgOverlap:F3}");

Console.WriteLine($"  Results JSON:           {outputPath}");
Console.WriteLine($"  Results markdown:       {mdPath}");

// Baseline comparison and optional write.
var currentMetrics = new BenchmarkMetrics
{
    Total = total,
    StartOk = startOk,
    ChoiceOk = choiceOk,
    ContOk = contOk,
    ContEligible = contEligible,
    SameSessionOk = sameSessionOk,
    WeakCases = weakCases.Count,
    SameFirstVerb = sameFirstVerbCount,
    ContinuationNoLabelReference = noLabelRefCount,
    StartContinuationRecapOverlap = recapCount,
    AvgRecapOverlap = avgOverlap,
};

if (File.Exists(baselinePath))
{
    try
    {
        var baseline = JsonSerializer.Deserialize<BenchmarkMetrics>(
            await File.ReadAllTextAsync(baselinePath), jsonOpts);
        if (baseline is not null && !baseline.Placeholder)
        {
            Console.WriteLine();
            Console.WriteLine("  Delta vs baseline (negative = improvement for weak counts)");
            Console.WriteLine($"    weak_cases:                       {Delta(baseline.WeakCases, currentMetrics.WeakCases)}");
            Console.WriteLine($"    same_first_verb:                  {Delta(baseline.SameFirstVerb, currentMetrics.SameFirstVerb)}");
            Console.WriteLine($"    continuation_no_label_reference:  {Delta(baseline.ContinuationNoLabelReference, currentMetrics.ContinuationNoLabelReference)}");
            Console.WriteLine($"    start_continuation_recap_overlap: {Delta(baseline.StartContinuationRecapOverlap, currentMetrics.StartContinuationRecapOverlap)}");
            Console.WriteLine($"    avg_recap_overlap:                {currentMetrics.AvgRecapOverlap - baseline.AvgRecapOverlap:+0.000;-0.000;0.000}");
        }
        else if (baseline is not null && baseline.Placeholder)
        {
            Console.WriteLine();
            Console.WriteLine("  Baseline is a placeholder — run with --write-baseline and commit.");
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
    Console.WriteLine($"  the generated file to tools/StoryBenchmark/baseline.json and commit.");
}

if (writeBaseline)
{
    await File.WriteAllTextAsync(
        baselinePath,
        JsonSerializer.Serialize(currentMetrics, jsonOpts));
    Console.WriteLine();
    Console.WriteLine($"  Baseline written: {baselinePath}");
    Console.WriteLine($"  Copy to: tools/StoryBenchmark/baseline.json");
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

// Exit code: 0 if all start+choice checks pass, 1 otherwise
return (startOk == total && choiceOk == total) ? 0 : 1;

// --- Helpers ---

static string Pct(int n, int d) => d == 0 ? "N/A" : $"{100 * n / d}%";

// First whitespace-separated token of <s> that contains Armenian letters,
// lowercased and stripped of trailing punctuation. Null if none.
static string? FirstArmenianToken(string? s, char[] trailing)
{
    if (string.IsNullOrWhiteSpace(s)) return null;
    foreach (var raw in s.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
    {
        var clean = raw.TrimEnd(trailing);
        if (clean.Length == 0) continue;
        // At least one Armenian codepoint (U+0530..U+058F).
        bool hasArmenian = false;
        foreach (var c in clean)
            if (c >= '\u0530' && c <= '\u058F') { hasArmenian = true; break; }
        if (hasArmenian) return clean.ToLowerInvariant();
    }
    return null;
}

// All ≥minLen whitespace-separated tokens of <s> that are entirely Armenian
// (after trailing-punct strip). Lowercased, deduplicated.
static HashSet<string> ExtractArmenianTokens(string? s, int minLen, char[] trailing, Regex armenianOnly)
{
    var set = new HashSet<string>();
    if (string.IsNullOrWhiteSpace(s)) return set;
    foreach (var raw in s.Split(
        [' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':',
         '\u0589', '\u055E', '\u055C', '\u055D', '՝', '«', '»', '"', '(', ')', '—', '-'],
        StringSplitOptions.RemoveEmptyEntries))
    {
        var clean = raw.TrimEnd(trailing);
        if (clean.Length < minLen) continue;
        if (!armenianOnly.IsMatch(clean)) continue;
        set.Add(clean.ToLowerInvariant());
    }
    return set;
}

// Jaccard overlap on two sets. 0 when either is empty.
static double JaccardOverlap(HashSet<string> a, HashSet<string> b)
{
    if (a.Count == 0 || b.Count == 0) return 0;
    int inter = 0;
    foreach (var x in a) if (b.Contains(x)) inter++;
    int union = a.Count + b.Count - inter;
    return union == 0 ? 0 : (double)inter / union;
}

// First sentence of <s>, terminated by Armenian verjaket ։ (U+0589).
// Returns the whole string if no verjaket is present.
static string FirstSentence(string s)
{
    if (string.IsNullOrEmpty(s)) return s;
    var idx = s.IndexOf('\u0589');
    return idx >= 0 ? s.Substring(0, idx + 1) : s;
}

// Signed delta display for count metrics. Improvement = fewer weak cases.
static string Delta(int baseline, int current)
{
    var d = current - baseline;
    var sign = d > 0 ? "+" : (d < 0 ? "" : "");
    return $"{baseline} -> {current} ({sign}{d})";
}

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

// Aggregate metrics snapshot for baseline comparison. Shape is intentionally
// flat so a committed baseline.json is human-readable and diffable.
record BenchmarkMetrics
{
    public int Total { get; init; }
    public int StartOk { get; init; }
    public int ChoiceOk { get; init; }
    public int ContOk { get; init; }
    public int ContEligible { get; init; }
    public int SameSessionOk { get; init; }
    public int WeakCases { get; init; }
    public int SameFirstVerb { get; init; }
    public int ContinuationNoLabelReference { get; init; }
    public int StartContinuationRecapOverlap { get; init; }
    public double AvgRecapOverlap { get; init; }
    // When true, baseline.json is a committed scaffold and should not be
    // compared against. The tool writes concrete baselines via --write-baseline.
    public bool Placeholder { get; init; }
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
    // E3 metric: Jaccard overlap between first sentence of start and
    // first sentence of continuation (≥4-char Armenian tokens only).
    public double StartContinuationRecapOverlap { get; set; }
    // Weak-case flags
    public List<string> WeakFlags { get; set; } = [];
    // Errors and raw responses
    public string? StartError { get; set; }
    public string? ContinuationError { get; set; }
    public ChatResponse? StartResponse { get; set; }
    public ChatResponse? ContinuationResponse { get; set; }
}
