using System.Diagnostics;
using BenchCommon;

// Top-level benchmark orchestrator.
//
// CLI: first positional arg is baseUrl (default http://localhost:5000).
// Runs each dedicated benchmark via `dotnet run --project <path>` in sequence,
// lets each tool's native output stream through unchanged, and prints one
// combined suite summary at the end.
//
// Each benchmark exposes the same contract (tools/BenchCommon/BenchSetup.cs):
//   * first positional arg = baseUrl
//   * --results-dir <dir>, --label <text>, --provisioning-secret <s>,
//     --write-baseline
//   * exit code 0 = passed, 1 = failed, 3 = INVALID (a claim failed or the
//     first-turn replies are mostly one canned line — it measured nothing)
// This orchestrator forwards the same flags: --results-dir <dir> puts each
// benchmark in <dir>/<BenchName>/ (e.g. <dir>/CalmBenchmark/) and this
// suite's own report + summary.json in <dir>; --label and --write-baseline
// pass straight through; the provisioning secret goes to each child via
// the AREG_PROVISIONING_SECRET environment variable, never its command
// line, so it does not show up in a process listing.
//
// Exit code: 3 if any benchmark was INVALID; else 1 if any failed or did not
// launch; else 0. Benchmarks are independent — one failure does NOT stop the
// rest.

// Force UTF-8 for our stdout so Armenian glyphs survive when we forward
// redirected child output line-by-line (see tee loop below).
Console.OutputEncoding = System.Text.Encoding.UTF8;

var bench = BenchArgs.Parse(args);
var baseUrl = bench.BaseUrl;

// Resolve the tools/ directory by walking up from AppContext.BaseDirectory
// until we find an ancestor that has StoryBenchmark/StoryBenchmark.csproj
// as a child. This is robust against output-layout changes (Debug/Release,
// publish/, RID subfolders, centralized artifacts/), unlike a fixed
// ../../../.. traversal.
var toolsDir = FindToolsDir(AppContext.BaseDirectory)
    ?? throw new Exception(
        $"Could not locate the benchmark tools directory starting from " +
        $"'{AppContext.BaseDirectory}'. Expected an ancestor containing " +
        $"StoryBenchmark/StoryBenchmark.csproj.");

var benchmarks = new (string Name, string ProjectDir)[]
{
    ("Story",     Path.Combine(toolsDir, "StoryBenchmark")),
    ("Game",      Path.Combine(toolsDir, "GameBenchmark")),
    ("Riddle",    Path.Combine(toolsDir, "RiddleBenchmark")),
    ("Calm",      Path.Combine(toolsDir, "CalmBenchmark")),
    ("Curiosity", Path.Combine(toolsDir, "CuriosityBenchmark")),
    ("Mode",      Path.Combine(toolsDir, "ModeBenchmark")),
};

Console.WriteLine(new string('=', 72));
Console.WriteLine("  ARMENIAN AI TOY — BENCHMARK SUITE");
Console.WriteLine(new string('=', 72));
Console.WriteLine($"  Target:     {baseUrl}");
Console.WriteLine($"  Benchmarks: {benchmarks.Length} (sequential, independent)");
Console.WriteLine($"  Tools dir:  {toolsDir}");
if (bench.Label is not null) Console.WriteLine($"  Label:      {bench.Label}");
if (bench.ExplicitResultsDir is not null) Console.WriteLine($"  Results:    {bench.ExplicitResultsDir}");
Console.WriteLine();

var results = new List<RunResult>();
var suiteStart = DateTime.UtcNow;
var suiteSw = Stopwatch.StartNew();

foreach (var b in benchmarks)
{
    Console.WriteLine(new string('-', 72));
    Console.WriteLine($"  ▶ {b.Name}Benchmark");
    Console.WriteLine(new string('-', 72));

    if (!Directory.Exists(b.ProjectDir))
    {
        Console.WriteLine($"  [error] project directory not found: {b.ProjectDir}");
        results.Add(new RunResult(b.Name, Launched: false, ExitCode: -1,
            Elapsed: TimeSpan.Zero, Error: "project dir missing",
            BaselineWeakCases: null, CurrentWeakCases: null, Valid: null, InvalidReason: null));
        Console.WriteLine();
        continue;
    }

    // Per-benchmark results dir when --results-dir was given; otherwise the
    // benchmark keeps writing under its own bin/.../results.
    string? benchResultsDir = bench.ExplicitResultsDir is null
        ? null : Path.Combine(bench.ExplicitResultsDir, $"{b.Name}Benchmark");

    // Redirect stdout so we can tee it (forward live + capture for the
    // weak_cases delta-line parser below). Stderr stays inherited so the
    // child's unhandled-exception traces appear in the usual place.
    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        WorkingDirectory = b.ProjectDir,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        StandardOutputEncoding = System.Text.Encoding.UTF8,
    };
    psi.ArgumentList.Add("run");
    psi.ArgumentList.Add("--project");
    psi.ArgumentList.Add(b.ProjectDir);
    psi.ArgumentList.Add("--");
    psi.ArgumentList.Add(baseUrl);
    if (benchResultsDir is not null)
    {
        psi.ArgumentList.Add("--results-dir");
        psi.ArgumentList.Add(benchResultsDir);
    }
    if (bench.Label is not null)
    {
        psi.ArgumentList.Add("--label");
        psi.ArgumentList.Add(bench.Label);
    }
    if (bench.WriteBaseline) psi.ArgumentList.Add("--write-baseline");
    if (bench.ProvisioningSecret is { } secret)
        psi.Environment[BenchArgs.ProvisioningSecretEnv] = secret;

    var captured = new System.Text.StringBuilder();
    var sw = Stopwatch.StartNew();
    try
    {
        using var proc = Process.Start(psi)
            ?? throw new Exception("Process.Start returned null");
        string? line;
        while ((line = await proc.StandardOutput.ReadLineAsync()) is not null)
        {
            Console.WriteLine(line);
            captured.AppendLine(line);
        }
        await proc.WaitForExitAsync();
        sw.Stop();
        // Primary: read the structured summary.json artifact the benchmark
        // writes just before exit (stable contract: baselineWeakCases,
        // currentWeakCases, regressionVerdict, valid, invalidReason).
        // Filtered by mtime ≥ suite start so a stale prior-run artifact
        // cannot mislead.
        var summary = TryReadSummaryArtifact(b.ProjectDir, benchResultsDir, suiteStart);
        var (baseWc, curWc) = (summary.Baseline, summary.Current);
        // Fallback: parse the stdout weak_cases delta line. Kept for the
        // case where the child crashed before writing summary.json (or any
        // tool that has not yet adopted the artifact). Gated by exit==0 —
        // a partial/crashed run's numbers are not trustworthy.
        if (baseWc is null && curWc is null && proc.ExitCode == 0)
            (baseWc, curWc) = ParseWeakCasesDelta(captured.ToString());
        // Exit 3 is the benchmarks' INVALID code; summary.json saying
        // valid:false counts the same even if the exit code disagreed.
        bool? valid = proc.ExitCode == 3 ? false : summary.Valid;
        string? invalidReason = summary.InvalidReason
            ?? (valid == false ? "invalid, but no invalidReason in a fresh summary.json" : null);
        results.Add(new RunResult(b.Name, Launched: true, ExitCode: proc.ExitCode,
            Elapsed: sw.Elapsed, Error: null,
            BaselineWeakCases: baseWc, CurrentWeakCases: curWc,
            Valid: valid, InvalidReason: invalidReason,
            LatencyMs: summary.LatencyMs, FirstTurnSignature: summary.FirstTurnSignature));
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.WriteLine($"  [error] failed to launch {b.Name}Benchmark: {ex.Message}");
        results.Add(new RunResult(b.Name, Launched: false, ExitCode: -1,
            Elapsed: sw.Elapsed, Error: ex.Message,
            BaselineWeakCases: null, CurrentWeakCases: null, Valid: null, InvalidReason: null));
    }

    Console.WriteLine();
}

suiteSw.Stop();

// --- Combined summary ---
// An INVALID run counts as a failure everywhere, and is reported apart.
int invalid = results.Count(r => r.Launched && r.Valid == false);
int passed = results.Count(r => r.Launched && r.ExitCode == 0 && r.Valid != false);
int failed = results.Count(r => r.Launched && (r.ExitCode != 0 || r.Valid == false));
int errored = results.Count(r => !r.Launched);
bool overallOk = failed == 0 && errored == 0;
int suiteExitCode = invalid > 0 ? 3 : overallOk ? 0 : 1;
string overallResult = invalid > 0 ? "INVALID" : overallOk ? "PASS" : "FAIL";

// --- Save suite-level report (markdown) ---
// Default: under the source project so humans can find it without digging
// through bin/ (gitignored via root .gitignore). With --results-dir: in
// that directory, beside the per-benchmark folders.
var reportsDir = bench.ExplicitResultsDir ?? Path.Combine(toolsDir, "BenchmarkAll", "results");
Directory.CreateDirectory(reportsDir);
var reportStamp = DateTime.UtcNow;
var reportPath = Path.Combine(reportsDir, $"run_{reportStamp:yyyyMMdd_HHmmss}.md");

var md = new System.Text.StringBuilder();
md.AppendLine("# BenchmarkAll Suite Report");
md.AppendLine();
md.AppendLine($"- **Timestamp (UTC):** {reportStamp:yyyy-MM-dd HH:mm:ss}");
md.AppendLine($"- **Target:** {baseUrl}");
md.AppendLine($"- **Duration:** {suiteSw.Elapsed.TotalSeconds:F1}s");
md.AppendLine($"- **Overall:** {overallResult}");
if (bench.Label is not null) md.AppendLine($"- **Label:** {bench.Label}");
if (BenchSummary.GitHead() is { } mdHead) md.AppendLine($"- **Git head:** {mdHead}");
md.AppendLine();
md.AppendLine("## Benchmarks");
md.AppendLine();
md.AppendLine("| Benchmark | Status | Valid | Exit | Elapsed |");
md.AppendLine("|---|---|---|---|---|");
foreach (var r in results)
{
    string rowExit = r.Launched ? r.ExitCode.ToString() : "—";
    string rowElapsed = r.Elapsed == TimeSpan.Zero ? "—" : $"{r.Elapsed.TotalSeconds:F1}s";
    md.AppendLine($"| {r.Name}Benchmark | {Status(r)} | {ValidLabel(r)} | {rowExit} | {rowElapsed} |");
}
if (results.Any(r => r.InvalidReason is not null))
{
    md.AppendLine();
    md.AppendLine("### Invalid runs");
    md.AppendLine();
    foreach (var r in results)
        if (r.InvalidReason is not null)
            md.AppendLine($"- **{r.Name}Benchmark:** {r.InvalidReason}");
}
if (results.Any(r => r.Error is not null))
{
    md.AppendLine();
    md.AppendLine("### Launch errors");
    md.AppendLine();
    foreach (var r in results)
        if (r.Error is not null)
            md.AppendLine($"- **{r.Name}Benchmark:** {r.Error}");
}
md.AppendLine();
md.AppendLine("## Totals");
md.AppendLine();
md.AppendLine($"- Passed: {passed}");
md.AppendLine($"- Failed: {failed} (of which invalid: {invalid})");
md.AppendLine($"- Errored: {errored}");
md.AppendLine($"- Total: {results.Count}");
md.AppendLine();
md.AppendLine("## Regression signal (weak_cases vs committed baseline)");
md.AppendLine();
md.AppendLine("| Benchmark | Verdict | Baseline | Current |");
md.AppendLine("|---|---|---|---|");
foreach (var r in results)
{
    var verdict = RowVerdict(r);
    var b2 = r.BaselineWeakCases?.ToString() ?? "—";
    var c2 = r.CurrentWeakCases?.ToString() ?? "—";
    md.AppendLine($"| {r.Name}Benchmark | {verdict} | {b2} | {c2} |");
}
await File.WriteAllTextAsync(reportPath, md.ToString());

// --- Save suite-level report (JSON) ---
// Same run → same filename stem as the markdown report; only extension differs.
// Schema is intentionally small and stable so later tooling can diff runs
// without parsing markdown. Unlaunched benchmarks get exitCode=null.
// The same object is also written as summary.json beside it, so a caller
// that passed --results-dir finds the suite verdict at a fixed path. It
// carries the same top-level keys every benchmark's summary.json has
// (valid, invalidReason, firstTurnSignature, label, gitHead, latencyMs),
// so one reader works on every summary.json in the tree.
var jsonPath = Path.Combine(reportsDir, $"run_{reportStamp:yyyyMMdd_HHmmss}.json");
var reportData = new
{
    timestampUtc = reportStamp.ToString(
        "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
    baseUrl,
    overallResult,
    exitCode = suiteExitCode,
    valid = invalid == 0,
    invalidReason = invalid == 0 ? null : string.Join("; ", results
        .Where(r => r.InvalidReason is not null)
        .Select(r => $"{r.Name}Benchmark: {r.InvalidReason}")),
    // Per-benchmark only (perBenchmark[].firstTurnSignature): signatures
    // from different modes are different canned lines, not one value.
    firstTurnSignature = (object?)null,
    label = bench.Label,
    gitHead = BenchSummary.GitHead(),
    // Percentiles cannot be merged across benchmarks without the raw
    // samples, so the suite-level value is null; each benchmark's own
    // percentiles are in perBenchmark[].latencyMs.
    latencyMs = (object?)null,
    totalDurationSeconds = Math.Round(suiteSw.Elapsed.TotalSeconds, 1),
    perBenchmark = results.Select(r => new
    {
        name = $"{r.Name}Benchmark",
        status = Status(r),
        exitCode = r.Launched ? (int?)r.ExitCode : null,
        valid = r.Valid,
        invalidReason = r.InvalidReason,
        firstTurnSignature = r.FirstTurnSignature,
        latencyMs = r.LatencyMs,
        elapsedSeconds = Math.Round(r.Elapsed.TotalSeconds, 1),
        error = r.Error,
        regression = new
        {
            verdict = RowVerdict(r),
            baselineWeakCases = r.BaselineWeakCases,
            currentWeakCases = r.CurrentWeakCases,
        },
    }).ToArray(),
    totals = new
    {
        passed,
        failed,
        invalid,
        errored,
        total = results.Count,
    },
};
var jsonOpts = new System.Text.Json.JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
var reportJson = System.Text.Json.JsonSerializer.Serialize(reportData, jsonOpts);
await File.WriteAllTextAsync(jsonPath, reportJson);
await File.WriteAllTextAsync(Path.Combine(reportsDir, "summary.json"), reportJson);

Console.WriteLine(new string('=', 72));
Console.WriteLine("  BENCHMARK SUITE SUMMARY");
Console.WriteLine(new string('=', 72));
Console.WriteLine($"  Target:   {baseUrl}");
Console.WriteLine($"  Duration: {suiteSw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine();
Console.WriteLine("  Benchmark             Status   Valid  Exit        Elapsed");
Console.WriteLine("  --------------------  -------  -----  ----------  --------");
foreach (var r in results)
{
    string exitStr = r.Launched ? r.ExitCode.ToString() : "—";
    string elapsedStr = r.Elapsed == TimeSpan.Zero ? "—" : $"{r.Elapsed.TotalSeconds:F1}s";
    Console.WriteLine($"  {r.Name + "Benchmark",-20}  {Status(r),-7}  {ValidLabel(r),-5}  {exitStr,-10}  {elapsedStr}");
    if (r.Error is not null)
        Console.WriteLine($"    └─ {r.Error}");
    if (r.InvalidReason is not null)
        Console.WriteLine($"    └─ INVALID: {r.InvalidReason}");
}
Console.WriteLine();
Console.WriteLine($"  Passed: {passed}   Failed: {failed} (invalid: {invalid})   Errored: {errored}   Total: {results.Count}");
Console.WriteLine();
Console.WriteLine("  Regression signal (weak_cases vs committed baseline):");
Console.WriteLine("  Benchmark             Verdict       Baseline  Current");
Console.WriteLine("  --------------------  ------------  --------  -------");
foreach (var r in results)
{
    var verdict = RowVerdict(r);
    var b2 = r.BaselineWeakCases?.ToString() ?? "—";
    var c2 = r.CurrentWeakCases?.ToString() ?? "—";
    Console.WriteLine($"  {r.Name + "Benchmark",-20}  {verdict,-12}  {b2,-8}  {c2}");
}
Console.WriteLine();
Console.WriteLine($"  Report MD:   {reportPath}");
Console.WriteLine($"  Report JSON: {jsonPath}");
Console.WriteLine(new string('=', 72));

return suiteExitCode;

static string? FindToolsDir(string start)
{
    var dir = new DirectoryInfo(start);
    for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
    {
        var probe = Path.Combine(dir.FullName, "StoryBenchmark", "StoryBenchmark.csproj");
        if (File.Exists(probe)) return dir.FullName;
    }
    return null;
}

// Structured summary artifact contract (primary regression source).
//   {--results-dir}/{Name}Benchmark/summary.json, else
//   {tool}/bin/**/results/summary.json
//   { timestampUtc, benchmarkName,
//     baselineWeakCases: int|null, currentWeakCases: int,
//     regressionVerdict: "improved"|"regressed"|"unchanged"|"unavailable",
//     valid: bool, invalidReason: string|null,
//     firstTurnSignature: {reply, count, of}|null, label, gitHead,
//     latencyMs: {p50, p90, p99, max, n} }
// We only accept an artifact whose mtime is ≥ the suite-start timestamp, so
// a stale summary.json from a prior run can't pollute the current verdict.
static SummaryRead TryReadSummaryArtifact(
    string projectDir, string? resultsDir, DateTime suiteStartUtc)
{
    var none = new SummaryRead(null, null, null, null, null, null);
    try
    {
        string? fresh;
        if (resultsDir is not null)
        {
            var path = Path.Combine(resultsDir, "summary.json");
            fresh = File.Exists(path) && File.GetLastWriteTimeUtc(path) >= suiteStartUtc ? path : null;
        }
        else
        {
            var binDir = Path.Combine(projectDir, "bin");
            if (!Directory.Exists(binDir)) return none;
            var candidates = Directory.GetFiles(
                binDir, "summary.json", SearchOption.AllDirectories);
            fresh = candidates
                .Where(f => File.GetLastWriteTimeUtc(f) >= suiteStartUtc)
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .FirstOrDefault();
        }
        if (fresh is null) return none;
        using var stream = File.OpenRead(fresh);
        using var doc = System.Text.Json.JsonDocument.Parse(stream);
        var root = doc.RootElement;
        int? b = root.TryGetProperty("baselineWeakCases", out var bEl)
            && bEl.ValueKind != System.Text.Json.JsonValueKind.Null
                ? bEl.GetInt32() : null;
        int? c = root.TryGetProperty("currentWeakCases", out var cEl)
            && cEl.ValueKind != System.Text.Json.JsonValueKind.Null
                ? cEl.GetInt32() : null;
        bool? v = root.TryGetProperty("valid", out var vEl)
            && vEl.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                ? vEl.GetBoolean() : null;
        string? why = root.TryGetProperty("invalidReason", out var wEl)
            && wEl.ValueKind == System.Text.Json.JsonValueKind.String
                ? wEl.GetString() : null;
        // Carried through verbatim (JsonNode.Parse("null") is null).
        var lat = root.TryGetProperty("latencyMs", out var lEl)
            ? System.Text.Json.Nodes.JsonNode.Parse(lEl.GetRawText()) : null;
        var sig = root.TryGetProperty("firstTurnSignature", out var sEl)
            ? System.Text.Json.Nodes.JsonNode.Parse(sEl.GetRawText()) : null;
        return new SummaryRead(b, c, v, why, lat, sig);
    }
    catch { return none; }
}

static string Status(RunResult r) =>
    !r.Launched ? "ERROR"
    : r.Valid == false ? "INVALID"
    : r.ExitCode == 0 ? "OK"
    : "FAIL";

static string ValidLabel(RunResult r) =>
    r.Valid switch { true => "yes", false => "NO", null => "—" };

// An INVALID run measured nothing, so its weak-case delta is not a verdict:
// it shows as "invalid" and counts as a failure.
static string RowVerdict(RunResult r) =>
    r.Valid == false ? "invalid" : Verdict(r.BaselineWeakCases, r.CurrentWeakCases);

// Fallback stdout parser. Each benchmark prints a line:
//     "    weak_cases:<padding>{baseline} -> {current} ({signed-delta})"
// via an identical templated Delta() helper. Used only when the structured
// artifact is absent (child crashed before writing, or an older tool build).
static (int? baseline, int? current) ParseWeakCasesDelta(string stdout)
{
    foreach (var line in stdout.Split('\n'))
    {
        var m = System.Text.RegularExpressions.Regex.Match(line,
            @"^\s*weak_cases:\s*(-?\d+)\s*->\s*(-?\d+)\s*\(");
        if (m.Success)
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }
    return (null, null);
}

static string Verdict(int? baseline, int? current)
{
    if (baseline is null || current is null) return "unavailable";
    if (current < baseline) return "improved";
    if (current > baseline) return "regressed";
    return "unchanged";
}

record RunResult(
    string Name, bool Launched, int ExitCode, TimeSpan Elapsed, string? Error,
    int? BaselineWeakCases, int? CurrentWeakCases, bool? Valid, string? InvalidReason,
    System.Text.Json.Nodes.JsonNode? LatencyMs = null,
    System.Text.Json.Nodes.JsonNode? FirstTurnSignature = null);

record SummaryRead(
    int? Baseline, int? Current, bool? Valid, string? InvalidReason,
    System.Text.Json.Nodes.JsonNode? LatencyMs, System.Text.Json.Nodes.JsonNode? FirstTurnSignature);
