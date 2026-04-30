using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// D2.3 — End-to-end mode-routing scaffold + device-gate + child-override
// scenarios.
//
// CLI: first positional arg is baseUrl. No --write-baseline support yet —
// the committed baseline ships with Placeholder=true and the first real
// capture is a separate D2.x.bench follow-up slice.
//
// Per-scenario contract:
//   1. Register a fresh device via POST /api/devices/register.
//   2. For "gate" scenarios: link the device to a per-run parent,
//      apply the gate-specific setup (pause / bedtime window /
//      mode-flags), then send the chat request from the device.
//   3. For "child_override" scenarios: link the device to the per-run
//      parent, apply the requested device-level mode flags, create a
//      child, set the child's three-valued mode override, then send
//      a chat request that includes childId in the body. Assertion
//      shape is hard (resp.Mode == expectedMode) when
//      expectedResponseExact is null, otherwise gate-shape (5-invariant
//      canned-response match).
//   4. For "hard" / "soft" scenarios: send POST /api/chat as before.
//   5. Pass conditions:
//        hard: HTTP 200 AND resp.Mode == expectedMode.
//        soft: HTTP 200 only; observed mode recorded but not asserted.
//        gate: HTTP 200 AND resp.Mode == null AND resp.Response ==
//              expectedResponseExact AND resp.ConversationId ==
//              Guid.Empty AND resp.SafetyFlag == 0 (Clean) AND
//              resp.ChoiceA == null AND resp.ChoiceB == null.
//        child_override: hard-shape OR gate-shape, picked per-scenario
//              from the presence of expectedResponseExact.
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

// D2.3: a separate options instance for the child-override PUT body — the
// server's ChildModeOverridesRequest expects all four nullable fields
// to be PRESENT in the body, with `null` denoting Inherit. The default
// `WhenWritingNull` policy on `jsonOpts` would elide null fields.
var jsonOptsKeepNulls = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
};

// Load scenarios early so the self-check runs before any network call.
var scenarios = JsonSerializer.Deserialize<List<Scenario>>(
    await File.ReadAllTextAsync(scenariosPath), jsonOpts)
    ?? throw new Exception("Failed to load scenarios.json");

// --- Inline self-check (runs BEFORE any HttpClient construction) ---
// Every scenario must have a non-empty id and message; ids must be
// unique; expectedMode (when non-null) must be in the allowed set.
// Gate scenarios additionally require kind="gate", a recognized gate
// value, expectedMode==null, and a non-empty expectedResponseExact.
// Child-override scenarios additionally require kind="child_override",
// non-null deviceFlags + childOverride, and exactly one of
// expectedMode / expectedResponseExact non-null.
{
    string[] allowedModes = { "story", "game", "riddle", "curiosity", "calm" };
    string[] allowedGates = { "paused", "bedtime", "mode_disabled" };
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

        bool isGate = string.Equals(s.Kind, "gate", StringComparison.Ordinal);
        bool isChildOverride = string.Equals(s.Kind, "child_override", StringComparison.Ordinal);
        bool hasGateField = !string.IsNullOrEmpty(s.Gate);

        // Gate scenarios:
        if (isGate || hasGateField)
        {
            if (!isGate)
            {
                Console.WriteLine(
                    $"[selfcheck] FAILED: scenario {s.Id} has gate field but kind != 'gate'");
                return 2;
            }
            if (!hasGateField)
            {
                Console.WriteLine(
                    $"[selfcheck] FAILED: scenario {s.Id} kind='gate' but gate field is empty");
                return 2;
            }
            if (Array.IndexOf(allowedGates, s.Gate) < 0)
            {
                Console.WriteLine(
                    $"[selfcheck] FAILED: scenario {s.Id} gate '{s.Gate}' not in allowed set");
                return 2;
            }
            if (s.ExpectedMode is not null)
            {
                Console.WriteLine(
                    $"[selfcheck] FAILED: scenario {s.Id} is a gate but expectedMode is set");
                return 2;
            }
            if (string.IsNullOrEmpty(s.ExpectedResponseExact))
            {
                Console.WriteLine(
                    $"[selfcheck] FAILED: scenario {s.Id} gate has empty expectedResponseExact");
                return 2;
            }
        }

        // Child-override scenarios:
        if (isChildOverride)
        {
            if (s.DeviceFlags is null)
            {
                Console.WriteLine(
                    $"[selfcheck] FAILED: scenario {s.Id} child_override has null deviceFlags");
                return 2;
            }
            if (s.ChildOverride is null)
            {
                Console.WriteLine(
                    $"[selfcheck] FAILED: scenario {s.Id} child_override has null childOverride");
                return 2;
            }
            bool hasMode = !string.IsNullOrEmpty(s.ExpectedMode);
            bool hasText = !string.IsNullOrEmpty(s.ExpectedResponseExact);
            if (hasMode == hasText)
            {
                Console.WriteLine(
                    $"[selfcheck] FAILED: scenario {s.Id} child_override must set exactly one of expectedMode / expectedResponseExact");
                return 2;
            }
        }
    }
}

Console.WriteLine($"ModeBenchmark target: {baseUrl}");
Console.WriteLine($"Loaded {scenarios.Count} scenarios\n");

int hardCount = scenarios.Count(s =>
    s.ExpectedMode is not null
    && !string.Equals(s.Kind, "gate", StringComparison.Ordinal)
    && !string.Equals(s.Kind, "child_override", StringComparison.Ordinal));
int softCount = scenarios.Count(s =>
    s.ExpectedMode is null
    && !string.Equals(s.Kind, "gate", StringComparison.Ordinal)
    && !string.Equals(s.Kind, "child_override", StringComparison.Ordinal));
int gateCount = scenarios.Count(s => string.Equals(s.Kind, "gate", StringComparison.Ordinal));
int childOverrideCount = scenarios.Count(s =>
    string.Equals(s.Kind, "child_override", StringComparison.Ordinal));
int gateTripsExpected = gateCount;
int gateTripsObserved = 0;
int childOverrideMatches = 0;
int scenariosOk = 0;
int modeMatches = 0;
var failures = new List<string>();
var results = new List<ScenarioResult>();

// --- Per-run parent for gate / child-override scenario setup ---
// Registered + logged in ONCE before the scenario loop. Reused across
// all scenarios that need parent endpoints (link, pause, bedtime,
// mode-flags, child create, child override). Skipped entirely when
// no such scenarios are present.
HttpClient? parentHttp = null;
bool needsParent = gateCount > 0 || childOverrideCount > 0;
if (needsParent)
{
    parentHttp = new HttpClient
    {
        BaseAddress = new Uri(baseUrl),
        Timeout = TimeSpan.FromSeconds(30),
    };
    var parentEmail = $"mbench-{DateTime.UtcNow:yyyyMMddHHmmssfff}@example.invalid";
    var parentPassword = $"MBenchPass-{Guid.NewGuid():N}";
    try
    {
        await RegisterParentAsync(parentHttp, parentEmail, parentPassword);
        var jwt = await LoginParentAsync(parentHttp, parentEmail, parentPassword, jsonOpts);
        parentHttp.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
        Console.WriteLine($"Registered + logged in parent: {parentEmail}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[fatal] Parent setup failed — gate/child-override scenarios cannot run: {ex.Message}");
        return 1;
    }
}

Console.WriteLine("ID    | Kind  | Expected      | Observed   | Status");
Console.WriteLine("------|-------|---------------|------------|-------");

foreach (var scenario in scenarios)
{
    bool isGate = string.Equals(scenario.Kind, "gate", StringComparison.Ordinal);
    bool isChildOverride = string.Equals(scenario.Kind, "child_override", StringComparison.Ordinal);
    bool isHard = !isGate && !isChildOverride && scenario.ExpectedMode is not null;
    bool isSoft = !isGate && !isChildOverride && !isHard;

    string runtimeKind = isGate ? "gate"
        : isChildOverride ? "child"
        : isHard ? "hard"
        : "soft";

    var sResult = new ScenarioResult
    {
        Id = scenario.Id,
        Message = scenario.Message,
        ExpectedMode = scenario.ExpectedMode,
        Kind = runtimeKind,
        Gate = scenario.Gate,
        ExpectedResponseExact = scenario.ExpectedResponseExact,
        DeviceFlags = scenario.DeviceFlags,
        ChildOverride = scenario.ChildOverride,
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
        Console.WriteLine($" {scenario.Id} | {runtimeKind,-5} | reg-fail");
        continue;
    }
    http.DefaultRequestHeaders.Add("X-Device-Id", device.DeviceId.ToString());
    http.DefaultRequestHeaders.Add("X-Api-Key", device.ApiKey);

    // Gate-specific setup BEFORE the chat call.
    if (isGate)
    {
        try
        {
            await LinkDeviceAsync(parentHttp!, device.DeviceId, device.ApiKey);
            switch (scenario.Gate)
            {
                case "paused":
                    await PauseDeviceAsync(parentHttp!, device.DeviceId);
                    break;
                case "bedtime":
                    var (start, end) = ComputeBedtimeWindowAroundNow();
                    await SetBedtimeWindowAsync(parentHttp!, device.DeviceId, start, end);
                    break;
                case "mode_disabled":
                    // Disable Story specifically — the trigger message is a
                    // Story trigger, so this gate fires.
                    await SetDeviceModeFlagsAsync(parentHttp!, device.DeviceId,
                        story: false, game: true, riddle: true, curiosity: true);
                    break;
                default:
                    sResult.Error = $"unknown gate '{scenario.Gate}'";
                    failures.Add($"{scenario.Id}: unknown gate '{scenario.Gate}'");
                    results.Add(sResult);
                    Console.WriteLine($" {scenario.Id} | gate  | unknown-gate");
                    continue;
            }
        }
        catch (Exception ex)
        {
            sResult.Error = $"gate setup failed: {ex.Message}";
            failures.Add($"{scenario.Id}: gate setup failed — {ex.Message}");
            results.Add(sResult);
            Console.WriteLine($" {scenario.Id} | gate  | setup-fail");
            continue;
        }
    }

    // Child-override-specific setup BEFORE the chat call.
    Guid? createdChildId = null;
    if (isChildOverride)
    {
        try
        {
            await LinkDeviceAsync(parentHttp!, device.DeviceId, device.ApiKey);
            // Apply device-level flags from the scenario.
            await SetDeviceModeFlagsAsync(parentHttp!, device.DeviceId,
                scenario.DeviceFlags!.Story,
                scenario.DeviceFlags.Game,
                scenario.DeviceFlags.Riddle,
                scenario.DeviceFlags.Curiosity);
            // Create a child belonging to this device.
            createdChildId = await CreateChildAsync(
                parentHttp!, device.DeviceId, "BenchKid", jsonOpts);
            // Set the per-child override (three-valued; null = Inherit).
            await SetChildModeOverridesAsync(
                parentHttp!, createdChildId.Value,
                scenario.ChildOverride!.Story,
                scenario.ChildOverride.Game,
                scenario.ChildOverride.Riddle,
                scenario.ChildOverride.Curiosity,
                jsonOptsKeepNulls);
            sResult.CreatedChildId = createdChildId;
        }
        catch (Exception ex)
        {
            sResult.Error = $"child-override setup failed: {ex.Message}";
            failures.Add($"{scenario.Id}: child-override setup failed — {ex.Message}");
            results.Add(sResult);
            Console.WriteLine($" {scenario.Id} | child | setup-fail");
            continue;
        }
    }

    ChatResponseShape? resp = null;
    int httpStatus = 0;
    try
    {
        // Conditional chat body — child-override scenarios pass childId.
        HttpResponseMessage httpResp;
        if (isChildOverride && createdChildId is not null)
        {
            var body = new { message = scenario.Message, childId = createdChildId.Value };
            httpResp = await http.PostAsJsonAsync("/api/chat", body);
        }
        else
        {
            var body = new { message = scenario.Message };
            httpResp = await http.PostAsJsonAsync("/api/chat", body);
        }
        httpStatus = (int)httpResp.StatusCode;
        if (!httpResp.IsSuccessStatusCode)
        {
            sResult.HttpStatus = httpStatus;
            sResult.Error = $"HTTP {httpStatus}";
            failures.Add($"{scenario.Id}: HTTP {httpStatus}");
            results.Add(sResult);
            Console.WriteLine($" {scenario.Id} | {runtimeKind,-5} | HTTP {httpStatus}");
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
        Console.WriteLine($" {scenario.Id} | {runtimeKind,-5} | error");
        continue;
    }

    sResult.HttpStatus = httpStatus;
    sResult.ObservedMode = resp?.Mode;
    var responseText = resp?.Response ?? "";
    sResult.ResponseSnippet = responseText.Length > 120
        ? responseText.Substring(0, 120) + "…"
        : responseText;

    bool ok;
    if (isGate)
    {
        // Five-invariant gate-trip assertion. ALL must hold.
        bool modeNull           = resp?.Mode is null;
        bool textMatches        = string.Equals(
            resp?.Response, scenario.ExpectedResponseExact, StringComparison.Ordinal);
        bool conversationEmpty  = resp?.ConversationId == Guid.Empty;
        bool safetyClean        = (resp?.SafetyFlag ?? -1) == 0;
        bool noChoiceA          = string.IsNullOrEmpty(resp?.ChoiceA);
        bool noChoiceB          = string.IsNullOrEmpty(resp?.ChoiceB);
        ok = httpStatus == 200 && modeNull && textMatches
             && conversationEmpty && safetyClean && noChoiceA && noChoiceB;
        if (ok)
        {
            gateTripsObserved++;
        }
        else
        {
            failures.Add(
                $"{scenario.Id}: gate={scenario.Gate} expected canned response, " +
                $"got mode={resp?.Mode ?? "(null)"}, " +
                $"text='{(responseText.Length > 60 ? responseText.Substring(0, 60) + "…" : responseText)}', " +
                $"conv={resp?.ConversationId}, safety={resp?.SafetyFlag}");
        }
    }
    else if (isChildOverride)
    {
        // Sub-shape selector: gate-shape when expectedResponseExact is set,
        // hard-shape otherwise. Either way, success increments
        // childOverrideMatches and scenariosOk — but NOT modeMatches and
        // NOT gateTripsObserved (those are scoped to their own kinds).
        if (!string.IsNullOrEmpty(scenario.ExpectedResponseExact))
        {
            bool modeNull           = resp?.Mode is null;
            bool textMatches        = string.Equals(
                resp?.Response, scenario.ExpectedResponseExact, StringComparison.Ordinal);
            bool conversationEmpty  = resp?.ConversationId == Guid.Empty;
            bool safetyClean        = (resp?.SafetyFlag ?? -1) == 0;
            bool noChoiceA          = string.IsNullOrEmpty(resp?.ChoiceA);
            bool noChoiceB          = string.IsNullOrEmpty(resp?.ChoiceB);
            ok = httpStatus == 200 && modeNull && textMatches
                 && conversationEmpty && safetyClean && noChoiceA && noChoiceB;
            if (!ok)
            {
                failures.Add(
                    $"{scenario.Id}: child_override expected canned response, " +
                    $"got mode={resp?.Mode ?? "(null)"}, " +
                    $"text='{(responseText.Length > 60 ? responseText.Substring(0, 60) + "…" : responseText)}'");
            }
        }
        else
        {
            ok = httpStatus == 200
                 && string.Equals(resp?.Mode, scenario.ExpectedMode, StringComparison.Ordinal);
            if (!ok)
            {
                failures.Add(
                    $"{scenario.Id}: child_override expected mode='{scenario.ExpectedMode}' got '{resp?.Mode ?? "(null)"}'");
            }
        }
        if (ok) childOverrideMatches++;
    }
    else if (isHard)
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

    var expectedLabel = isGate
        ? scenario.Gate ?? "(gate)"
        : isChildOverride
            ? (scenario.ExpectedMode ?? "(canned)")
            : (scenario.ExpectedMode ?? "(any)");
    var observedLabel = resp?.Mode ?? "(null)";
    var statusLabel = ok ? "PASS" : "FAIL";
    Console.WriteLine(
        $" {scenario.Id} | {runtimeKind,-5} | {expectedLabel,-13} | {observedLabel,-10} | {statusLabel}");
}

// Always dispose the parent client after all scenarios run.
parentHttp?.Dispose();

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("  MODE BENCHMARK SUMMARY");
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine($"  Total scenarios:        {scenarios.Count}  (hard {hardCount} / soft {softCount} / gate {gateCount} / child {childOverrideCount})");
Console.WriteLine($"  Scenarios passed:       {scenariosOk}/{scenarios.Count}");
Console.WriteLine($"  Mode matches:           {modeMatches}/{hardCount}");
Console.WriteLine($"  Gate trips:             {gateTripsObserved}/{gateTripsExpected}");
Console.WriteLine($"  Child override matches: {childOverrideMatches}/{childOverrideCount}");

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
md.AppendLine($"**Scenarios:** {scenarios.Count}  (hard {hardCount} / soft {softCount} / gate {gateCount} / child {childOverrideCount})");
md.AppendLine();
md.AppendLine("| ID | Kind | Message | Expected | Observed | Status |");
md.AppendLine("|----|------|---------|----------|----------|--------|");
foreach (var r in results)
{
    var kind = r.Kind ?? "soft";
    var expected = string.Equals(kind, "gate", StringComparison.Ordinal)
        ? (r.Gate ?? "(gate)")
        : string.Equals(kind, "child", StringComparison.Ordinal)
            ? (r.ExpectedMode ?? "(canned)")
            : (r.ExpectedMode ?? "(any)");
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
    gateScenarios = gateCount,
    gateTripsExpected,
    gateTripsObserved,
    childOverrideScenarios = childOverrideCount,
    childOverrideMatches,
}, jsonOpts));

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"  FAILURES ({failures.Count}):");
    foreach (var f in failures) Console.WriteLine($"    - {f}");
}

Console.WriteLine("═══════════════════════════════════════");

return runSucceeded ? 0 : 1;

// --- Helpers (parent-side endpoints used by gate / child-override scenarios) ---

static async Task RegisterParentAsync(HttpClient http, string email, string password)
{
    var body = new { email, password, acceptedTerms = true };
    var resp = await http.PostAsJsonAsync("/api/parents/register", body);
    resp.EnsureSuccessStatusCode();
}

static async Task<string> LoginParentAsync(
    HttpClient http, string email, string password, JsonSerializerOptions jsonOpts)
{
    var body = new { email, password };
    var resp = await http.PostAsJsonAsync("/api/parents/login", body);
    resp.EnsureSuccessStatusCode();
    var login = await resp.Content.ReadFromJsonAsync<ParentLoginShape>(jsonOpts)
        ?? throw new Exception("parent login returned null");
    if (string.IsNullOrWhiteSpace(login.Token))
        throw new Exception("parent login response had no token");
    return login.Token;
}

static async Task LinkDeviceAsync(HttpClient parentHttp, Guid deviceId, string apiKey)
{
    var body = new { deviceId, apiKey };
    var resp = await parentHttp.PostAsJsonAsync("/api/parents/devices/link", body);
    resp.EnsureSuccessStatusCode();
}

static async Task PauseDeviceAsync(HttpClient parentHttp, Guid deviceId)
{
    var resp = await parentHttp.PostAsync(
        $"/api/parents/devices/{deviceId}/pause", content: null);
    resp.EnsureSuccessStatusCode();
}

static async Task SetBedtimeWindowAsync(
    HttpClient parentHttp, Guid deviceId, TimeOnly start, TimeOnly end)
{
    var body = new
    {
        start = start.ToString("HH:mm:ss"),
        end   = end.ToString("HH:mm:ss"),
    };
    var resp = await parentHttp.PutAsJsonAsync(
        $"/api/parents/devices/{deviceId}/bedtime-window", body);
    resp.EnsureSuccessStatusCode();
}

static async Task SetDeviceModeFlagsAsync(
    HttpClient parentHttp, Guid deviceId,
    bool story, bool game, bool riddle, bool curiosity)
{
    var body = new { story, game, riddle, curiosity };
    var resp = await parentHttp.PutAsJsonAsync(
        $"/api/parents/devices/{deviceId}/mode-flags", body);
    resp.EnsureSuccessStatusCode();
}

// D2.3: create a child belonging to <deviceId>. Gender is sent as the
// integer literal 0 (Boy in the Domain enum) — System.Text.Json
// serializes enums as ints by default, and the bench uses an int
// literal directly to avoid taking a runtime dependency on the
// Domain assembly.
static async Task<Guid> CreateChildAsync(
    HttpClient parentHttp, Guid deviceId, string name, JsonSerializerOptions jsonOpts)
{
    var body = new { deviceId, name, gender = 0 };
    var resp = await parentHttp.PostAsJsonAsync("/api/children", body);
    resp.EnsureSuccessStatusCode();
    var created = await resp.Content.ReadFromJsonAsync<CreateChildResponseShape>(jsonOpts)
        ?? throw new Exception("create child returned null");
    if (created.ChildId == Guid.Empty)
        throw new Exception("create child returned empty childId");
    return created.ChildId;
}

// D2.3: set the per-child override (three-valued; null = Inherit). The
// server's ChildModeOverridesRequest expects all four nullable fields
// PRESENT in the body. Use a dedicated keep-nulls JsonSerializerOptions
// so the WhenWritingNull policy on the default options does not elide
// Inherit fields.
static async Task SetChildModeOverridesAsync(
    HttpClient parentHttp, Guid childId,
    bool? story, bool? game, bool? riddle, bool? curiosity,
    JsonSerializerOptions jsonOptsKeepNulls)
{
    var body = new { story, game, riddle, curiosity };
    var json = JsonSerializer.Serialize(body, jsonOptsKeepNulls);
    var content = new StringContent(json, Encoding.UTF8, "application/json");
    var resp = await parentHttp.PutAsync(
        $"/api/parents/children/{childId}/mode-flags", content);
    resp.EnsureSuccessStatusCode();
}

// Compute a 6-minute bedtime window centered on wall-clock now, expressed
// in the device's default IANA timezone. Mirrors the server-side fallback:
// if "Asia/Yerevan" doesn't resolve on this host, fall back to UTC — the
// server does the same (BedtimeWindowEvaluator.cs:46–49).
static (TimeOnly start, TimeOnly end) ComputeBedtimeWindowAroundNow()
{
    TimeZoneInfo tz;
    try { tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan"); }
    catch { tz = TimeZoneInfo.Utc; }
    var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
    var start = TimeOnly.FromDateTime(nowLocal.AddMinutes(-1));
    var end = TimeOnly.FromDateTime(nowLocal.AddMinutes(+5));
    return (start, end);
}

// --- DTOs ---

record Scenario
{
    public string Id { get; init; } = "";
    public string Message { get; init; } = "";
    public string? ExpectedMode { get; init; }

    // D2.2 additions — gate scenarios.
    public string? Kind { get; init; }                 // "gate" / "child_override" / null
    public string? Gate { get; init; }                 // "paused" / "bedtime" / "mode_disabled"
    public string? ExpectedResponseExact { get; init; } // canned text the wire MUST match

    // D2.3 additions — child-override scenarios.
    public DeviceFlagsBlock? DeviceFlags { get; init; }
    public ChildOverrideBlock? ChildOverride { get; init; }
}

record DeviceFlagsBlock(bool Story, bool Game, bool Riddle, bool Curiosity);

record ChildOverrideBlock(bool? Story, bool? Game, bool? Riddle, bool? Curiosity);

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

record ParentLoginShape
{
    public string Token { get; init; } = "";
}

record CreateChildResponseShape
{
    public Guid ChildId { get; init; }
}

record ModeMetrics
{
    public int TotalScenarios { get; init; }
    public int HardScenarios { get; init; }
    public int SoftScenarios { get; init; }

    public int GateScenarios { get; init; }
    public int GateTripsExpected { get; init; }
    public int GateTripsObserved { get; init; }

    // D2.3 additions.
    public int ChildOverrideScenarios { get; init; }
    public int ChildOverrideMatches { get; init; }

    public int ScenariosOk { get; init; }
    public int ModeMatches { get; init; }
    public int GateTrips { get; init; }   // legacy placeholder kept alongside GateTripsObserved
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

    public string? Kind { get; init; }                  // "hard" / "soft" / "gate" / "child"
    public string? Gate { get; init; }                  // gate value when applicable
    public string? ExpectedResponseExact { get; init; } // canned text expected (gate / child-override gate-shape)

    // D2.3 additions on the per-scenario result.
    public DeviceFlagsBlock? DeviceFlags { get; init; }
    public ChildOverrideBlock? ChildOverride { get; init; }
    public Guid? CreatedChildId { get; set; }

    public string? ObservedMode { get; set; }
    public string? ResponseSnippet { get; set; }
    public int HttpStatus { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
}
