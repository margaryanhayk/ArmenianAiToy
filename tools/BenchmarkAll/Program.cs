using System.Diagnostics;

// Top-level benchmark orchestrator.
//
// CLI: first positional arg is baseUrl (default http://localhost:5000).
// Runs each dedicated benchmark via `dotnet run --project <path>` in sequence,
// lets each tool's native output stream through unchanged, and prints one
// combined suite summary at the end.
//
// Each benchmark already exposes the same contract:
//   * first positional arg = baseUrl
//   * exit code 0 on success, non-zero on failure
// so this orchestrator adds no new shared code — it just collects exit codes.
//
// Exit code: 0 if every benchmark returned 0 and every benchmark launched;
// 1 otherwise. Benchmarks are independent — one failure does NOT stop the rest.

// Force UTF-8 for our stdout so Armenian glyphs survive when we forward
// redirected child output line-by-line (see tee loop below).
Console.OutputEncoding = System.Text.Encoding.UTF8;

var positional = args.Where(a => !a.StartsWith("--")).ToArray();
var baseUrl = positional.Length > 0 ? positional[0] : "http://localhost:5000";

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
};

Console.WriteLine(new string('=', 72));
Console.WriteLine("  ARMENIAN AI TOY — BENCHMARK SUITE");
Console.WriteLine(new string('=', 72));
Console.WriteLine($"  Target:     {baseUrl}");
Console.WriteLine($"  Benchmarks: {benchmarks.Length} (sequential, independent)");
Console.WriteLine($"  Tools dir:  {toolsDir}");
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
            BaselineWeakCases: null, CurrentWeakCases: null));
        Console.WriteLine();
        continue;
    }

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
        // currentWeakCases, regressionVerdict). Filtered by mtime ≥ suite
        // start so a stale prior-run artifact cannot mislead.
        var (baseWc, curWc) = TryReadSummaryArtifact(b.ProjectDir, suiteStart);
        // Fallback: parse the stdout weak_cases delta line. Kept for the
        // case where the child crashed before writing summary.json (or any
        // tool that has not yet adopted the artifact). Gated by exit==0 —
        // a partial/crashed run's numbers are not trustworthy.
        if (baseWc is null && curWc is null && proc.ExitCode == 0)
            (baseWc, curWc) = ParseWeakCasesDelta(captured.ToString());
        results.Add(new RunResult(b.Name, Launched: true, ExitCode: proc.ExitCode,
            Elapsed: sw.Elapsed, Error: null,
            BaselineWeakCases: baseWc, CurrentWeakCases: curWc));
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.WriteLine($"  [error] failed to launch {b.Name}Benchmark: {ex.Message}");
        results.Add(new RunResult(b.Name, Launched: false, ExitCode: -1,
            Elapsed: sw.Elapsed, Error: ex.Message,
            BaselineWeakCases: null, CurrentWeakCases: null));
    }

    Console.WriteLine();
}

suiteSw.Stop();

// --- Combined summary ---
int passed = results.Count(r => r.Launched && r.ExitCode == 0);
int failed = results.Count(r => r.Launched && r.ExitCode != 0);
int errored = results.Count(r => !r.Launched);
bool overallOk = failed == 0 && errored == 0;

// --- Save suite-level report (markdown) ---
// Written under the source project so humans can find it without digging
// through bin/. Directory is gitignored via root .gitignore.
var reportsDir = Path.Combine(toolsDir, "BenchmarkAll", "results");
Directory.CreateDirectory(reportsDir);
var reportStamp = DateTime.UtcNow;
var reportPath = Path.Combine(reportsDir, $"run_{reportStamp:yyyyMMdd_HHmmss}.md");

var md = new System.Text.StringBuilder();
md.AppendLine("# BenchmarkAll Suite Report");
md.AppendLine();
md.AppendLine($"- **Timestamp (UTC):** {reportStamp:yyyy-MM-dd HH:mm:ss}");
md.AppendLine($"- **Target:** {baseUrl}");
md.AppendLine($"- **Duration:** {suiteSw.Elapsed.TotalSeconds:F1}s");
md.AppendLine($"- **Overall:** {(overallOk ? "PASS" : "FAIL")}");
md.AppendLine();
md.AppendLine("## Benchmarks");
md.AppendLine();
md.AppendLine("| Benchmark | Status | Exit | Elapsed |");
md.AppendLine("|---|---|---|---|");
foreach (var r in results)
{
    string rowStatus = !r.Launched ? "ERROR"
                    : r.ExitCode == 0 ? "OK"
                    : "FAIL";
    string rowExit = r.Launched ? r.ExitCode.ToString() : "—";
    string rowElapsed = r.Elapsed == TimeSpan.Zero ? "—" : $"{r.Elapsed.TotalSeconds:F1}s";
    md.AppendLine($"| {r.Name}Benchmark | {rowStatus} | {rowExit} | {rowElapsed} |");
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
md.AppendLine($"- Failed: {failed}");
md.AppendLine($"- Errored: {errored}");
md.AppendLine($"- Total: {results.Count}");
md.AppendLine();
md.AppendLine("## Regression signal (weak_cases vs committed baseline)");
md.AppendLine();
md.AppendLine("| Benchmark | Verdict | Baseline | Current |");
md.AppendLine("|---|---|---|---|");
foreach (var r in results)
{
    var verdict = Verdict(r.BaselineWeakCases, r.CurrentWeakCases);
    var b2 = r.BaselineWeakCases?.ToString() ?? "—";
    var c2 = r.CurrentWeakCases?.ToString() ?? "—";
    md.AppendLine($"| {r.Name}Benchmark | {verdict} | {b2} | {c2} |");
}
await File.WriteAllTextAsync(reportPath, md.ToString());

// --- Save suite-level report (JSON) ---
// Same run → same filename stem as the markdown report; only extension differs.
// Schema is intentionally small and stable so later tooling can diff runs
// without parsing markdown. Unlaunched benchmarks get exitCode=null.
var jsonPath = Path.Combine(reportsDir, $"run_{reportStamp:yyyyMMdd_HHmmss}.json");
var reportData = new
{
    timestampUtc = reportStamp.ToString(
        "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
    baseUrl,
    overallResult = overallOk ? "PASS" : "FAIL",
    totalDurationSeconds = Math.Round(suiteSw.Elapsed.TotalSeconds, 1),
    perBenchmark = results.Select(r => new
    {
        name = $"{r.Name}Benchmark",
        status = !r.Launched ? "ERROR" : r.ExitCode == 0 ? "OK" : "FAIL",
        exitCode = r.Launched ? (int?)r.ExitCode : null,
        elapsedSeconds = Math.Round(r.Elapsed.TotalSeconds, 1),
        error = r.Error,
        regression = new
        {
            verdict = Verdict(r.BaselineWeakCases, r.CurrentWeakCases),
            baselineWeakCases = r.BaselineWeakCases,
            currentWeakCases = r.CurrentWeakCases,
        },
    }).ToArray(),
    totals = new
    {
        passed,
        failed,
        errored,
        total = results.Count,
    },
};
var jsonOpts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
await File.WriteAllTextAsync(
    jsonPath, System.Text.Json.JsonSerializer.Serialize(reportData, jsonOpts));

Console.WriteLine(new string('=', 72));
Console.WriteLine("  BENCHMARK SUITE SUMMARY");
Console.WriteLine(new string('=', 72));
Console.WriteLine($"  Target:   {baseUrl}");
Console.WriteLine($"  Duration: {suiteSw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine();
Console.WriteLine("  Benchmark             Status   Exit        Elapsed");
Console.WriteLine("  --------------------  -------  ----------  --------");
foreach (var r in results)
{
    string status = !r.Launched ? "ERROR"
                  : r.ExitCode == 0 ? "OK"
                  : "FAIL";
    string exitStr = r.Launched ? r.ExitCode.ToString() : "—";
    string elapsedStr = r.Elapsed == TimeSpan.Zero ? "—" : $"{r.Elapsed.TotalSeconds:F1}s";
    Console.WriteLine($"  {r.Name + "Benchmark",-20}  {status,-7}  {exitStr,-10}  {elapsedStr}");
    if (r.Error is not null)
        Console.WriteLine($"    └─ {r.Error}");
}
Console.WriteLine();
Console.WriteLine($"  Passed: {passed}   Failed: {failed}   Errored: {errored}   Total: {results.Count}");
Console.WriteLine();
Console.WriteLine("  Regression signal (weak_cases vs committed baseline):");
Console.WriteLine("  Benchmark             Verdict       Baseline  Current");
Console.WriteLine("  --------------------  ------------  --------  -------");
foreach (var r in results)
{
    var verdict = Verdict(r.BaselineWeakCases, r.CurrentWeakCases);
    var b2 = r.BaselineWeakCases?.ToString() ?? "—";
    var c2 = r.CurrentWeakCases?.ToString() ?? "—";
    Console.WriteLine($"  {r.Name + "Benchmark",-20}  {verdict,-12}  {b2,-8}  {c2}");
}
Console.WriteLine();
Console.WriteLine($"  Report MD:   {reportPath}");
Console.WriteLine($"  Report JSON: {jsonPath}");
Console.WriteLine(new string('=', 72));

return overallOk ? 0 : 1;

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
//   {tool}/bin/**/results/summary.json
//   { timestampUtc, benchmarkName,
//     baselineWeakCases: int|null, currentWeakCases: int,
//     regressionVerdict: "improved"|"regressed"|"unchanged"|"unavailable" }
// We only accept an artifact whose mtime is ≥ the suite-start timestamp, so
// a stale summary.json from a prior run can't pollute the current verdict.
static (int? baseline, int? current) TryReadSummaryArtifact(
    string projectDir, DateTime suiteStartUtc)
{
    try
    {
        var binDir = Path.Combine(projectDir, "bin");
        if (!Directory.Exists(binDir)) return (null, null);
        var candidates = Directory.GetFiles(
            binDir, "summary.json", SearchOption.AllDirectories);
        var fresh = candidates
            .Where(f => File.GetLastWriteTimeUtc(f) >= suiteStartUtc)
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();
        if (fresh is null) return (null, null);
        using var stream = File.OpenRead(fresh);
        using var doc = System.Text.Json.JsonDocument.Parse(stream);
        var root = doc.RootElement;
        int? b = root.TryGetProperty("baselineWeakCases", out var bEl)
            && bEl.ValueKind != System.Text.Json.JsonValueKind.Null
                ? bEl.GetInt32() : null;
        int? c = root.TryGetProperty("currentWeakCases", out var cEl)
            && cEl.ValueKind != System.Text.Json.JsonValueKind.Null
                ? cEl.GetInt32() : null;
        return (b, c);
    }
    catch { return (null, null); }
}

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
    int? BaselineWeakCases, int? CurrentWeakCases);
