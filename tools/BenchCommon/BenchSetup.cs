using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

// Shared setup for the six mode benchmarks (C186). Linked as source into
// every benchmark csproj with <Compile Include="..\BenchCommon\*.cs" /> —
// no NuGet package, no ProjectReference — so each tool stays a single
// `dotnet run` and nothing here ships with the backend.
//
// Why it exists: since the unclaimed-device gate shipped, an unclaimed toy
// only ever hears the canned resting line. Calm/Curiosity/Riddle never
// claimed their devices, ModeBenchmark's hard/soft scenarios did not
// either, Story used the legacy /devices/link, and Game only WARNed on a
// failed claim — so several runs "passed" while measuring nothing
// (tools/quality-evidence/armenian-safety-live-20260923.md). Everything
// below exists to make that failure loud: a claim that does not stick, or
// a run whose first-turn replies are mostly byte-identical, is INVALID
// (exit 3, summary.json valid:false), never a pass or an ordinary fail.
//
// Secrets: the provisioning secret, the parent JWT and every device API
// key stay in memory only. Nothing in this file prints, logs or writes
// one; summary.json carries the label, git head and latency, never a key.
namespace BenchCommon;

/// <summary>
/// Shared CLI contract for every benchmark (and BenchmarkAll):
/// first positional arg = baseUrl; <c>--write-baseline</c> (boolean);
/// <c>--results-dir &lt;dir&gt;</c>; <c>--label &lt;text&gt;</c>;
/// <c>--provisioning-secret &lt;s&gt;</c> (else env
/// <c>AREG_PROVISIONING_SECRET</c>; else none, which needs a backend with
/// <c>Devices:AllowOpenRegistration=true</c>). Value flags also accept
/// <c>--flag=value</c>.
/// </summary>
public sealed class BenchArgs
{
    public const string ProvisioningSecretEnv = "AREG_PROVISIONING_SECRET";
    static readonly string[] ValueFlags = { "--results-dir", "--label", "--provisioning-secret" };

    public string BaseUrl { get; private init; } = "http://localhost:5000";
    public bool WriteBaseline { get; private init; }
    /// <summary>Absolute <c>--results-dir</c>, or null when not given.</summary>
    public string? ExplicitResultsDir { get; private init; }
    /// <summary>Where run_*.json/md and summary.json go: <c>--results-dir</c>,
    /// else <c>bin/.../results</c> as before (gitignored).</summary>
    public string ResultsDir => ExplicitResultsDir ?? Path.Combine(AppContext.BaseDirectory, "results");
    public string? Label { get; private init; }
    /// <summary>Never printed. Null when neither the flag nor the env var is set.</summary>
    internal string? ProvisioningSecret { get; private init; }
    public bool HasProvisioningSecret => ProvisioningSecret is not null;

    /// <summary>Parses <paramref name="args"/>; a value flag with no value
    /// exits 2 (the benchmarks' existing "cannot run" code).</summary>
    public static BenchArgs Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var positional = new List<string>();
        bool writeBaseline = false;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            var eq = a.IndexOf('=');
            var name = a.StartsWith("--") && eq > 0 ? a[..eq] : a;
            if (Array.IndexOf(ValueFlags, name) >= 0)
            {
                string? value = eq > 0 && a.StartsWith("--") ? a[(eq + 1)..]
                    : i + 1 < args.Length ? args[++i] : null;
                if (value is null)
                {
                    Console.Error.WriteLine($"[usage] {name} needs a value");
                    Environment.Exit(2);
                }
                values[name] = value!;
            }
            else if (a == "--write-baseline") writeBaseline = true;
            else if (!a.StartsWith("--")) positional.Add(a);
        }

        string? secret = values.TryGetValue("--provisioning-secret", out var s) ? s
            : Environment.GetEnvironmentVariable(ProvisioningSecretEnv);
        return new BenchArgs
        {
            BaseUrl = positional.Count > 0 ? positional[0] : "http://localhost:5000",
            WriteBaseline = writeBaseline,
            ExplicitResultsDir = values.TryGetValue("--results-dir", out var dir)
                && !string.IsNullOrWhiteSpace(dir) ? Path.GetFullPath(dir) : null,
            Label = values.TryGetValue("--label", out var label)
                && !string.IsNullOrWhiteSpace(label) ? label : null,
            ProvisioningSecret = string.IsNullOrEmpty(secret) ? null : secret,
        };
    }
}

/// <summary>A setup step (parent, device registration, claim) failed — the
/// run cannot measure anything and is INVALID, not failed.</summary>
public sealed class BenchSetupException(string message) : Exception(message);

/// <summary>
/// HTTP helpers for the setup calls. Parent register/login, device
/// register and device claim all share the per-IP <c>auth</c> rate bucket
/// (10 per 60 s by default). One parent plus two calls per device means a
/// run of five or more scenarios needs more than ten of them, so a 429 is
/// honoured per its Retry-After instead of failing.
/// </summary>
static class BenchHttp
{
    /// <summary>Total wait budget per call. The auth limiter is a 60 s fixed
    /// window, so one honoured Retry-After always fits.</summary>
    public const int MaxRateLimitWaitSeconds = 70;

    public static async Task<HttpResponseMessage> SendWithBackoffAsync(
        HttpClient http, Func<HttpRequestMessage> makeRequest, string what)
    {
        var waited = TimeSpan.Zero;
        while (true)
        {
            using var request = makeRequest();
            var response = await http.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.TooManyRequests) return response;

            // The server truncates Retry-After to whole seconds; +1 s lands
            // safely past the fixed-window boundary.
            var delay = (RetryAfter(response) ?? TimeSpan.FromSeconds(10)) + TimeSpan.FromSeconds(1);
            response.Dispose();
            if (waited + delay > TimeSpan.FromSeconds(MaxRateLimitWaitSeconds))
                throw new BenchSetupException(
                    $"{what}: HTTP 429 (auth rate limit) and waiting {delay.TotalSeconds:F0}s more " +
                    $"would exceed the {MaxRateLimitWaitSeconds}s budget");
            Console.WriteLine($"  [rate-limit] {what}: HTTP 429, waiting {delay.TotalSeconds:F0}s per Retry-After");
            await Task.Delay(delay);
            waited += delay;
        }
    }

    static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var h = response.Headers.RetryAfter;
        if (h?.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (h?.Date is { } date)
        {
            var d = date - DateTimeOffset.UtcNow;
            return d < TimeSpan.Zero ? TimeSpan.Zero : d;
        }
        return null;
    }

    public static async Task<string> DescribeFailureAsync(HttpResponseMessage response)
    {
        string body;
        try { body = await response.Content.ReadAsStringAsync(); }
        catch { body = ""; }
        body = body.ReplaceLineEndings(" ").Trim();
        if (body.Length > 160) body = body[..160] + "…";
        return body.Length == 0
            ? $"HTTP {(int)response.StatusCode}"
            : $"HTTP {(int)response.StatusCode} {body}";
    }

    public static HttpRequestMessage Json(HttpMethod method, string path, object body) =>
        new(method, path) { Content = JsonContent.Create(body) };
}

/// <summary>
/// One throwaway parent per run (register + log in once), reused to claim
/// every device the run registers — a parent per scenario would burn two
/// more auth-bucket calls each. <see cref="Http"/> carries the parent's
/// Bearer token for the parent endpoints a benchmark needs (pause,
/// bedtime window, mode flags, children).
/// </summary>
public sealed class BenchParent : IDisposable
{
    public string Email { get; }
    /// <summary>Authorized client for /api/parents/* and /api/children.</summary>
    public HttpClient Http { get; }
    internal BenchArgs Args { get; }
    // Anonymous client for /api/devices/register, so the parent's Bearer
    // token never rides on a device-provisioning request.
    internal HttpClient Anonymous { get; }

    BenchParent(BenchArgs args, string email, string jwt)
    {
        Args = args;
        Email = email;
        Http = new HttpClient { BaseAddress = new Uri(args.BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        Anonymous = new HttpClient { BaseAddress = new Uri(args.BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Registers and logs in a fresh parent; throws
    /// <see cref="BenchSetupException"/> on any failure.</summary>
    public static async Task<BenchParent> CreateAsync(BenchArgs args, string emailPrefix)
    {
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@example.invalid";
        var password = $"Bench-{Guid.NewGuid():N}!";
        using var http = new HttpClient { BaseAddress = new Uri(args.BaseUrl), Timeout = TimeSpan.FromSeconds(30) };

        using (var reg = await BenchHttp.SendWithBackoffAsync(http,
            () => BenchHttp.Json(HttpMethod.Post, "/api/parents/register",
                new { email, password, acceptedTerms = true }),
            "parent register"))
        {
            if (!reg.IsSuccessStatusCode)
                throw new BenchSetupException($"parent register failed: {await BenchHttp.DescribeFailureAsync(reg)}");
        }

        using var login = await BenchHttp.SendWithBackoffAsync(http,
            () => BenchHttp.Json(HttpMethod.Post, "/api/parents/login", new { email, password }),
            "parent login");
        if (!login.IsSuccessStatusCode)
            throw new BenchSetupException($"parent login failed: {await BenchHttp.DescribeFailureAsync(login)}");
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var jwt = body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(jwt))
            throw new BenchSetupException("parent login returned no token");

        return new BenchParent(args, email, jwt!);
    }

    public void Dispose()
    {
        Http.Dispose();
        Anonymous.Dispose();
    }
}

/// <summary>A registered device CLAIMED by the run's parent and verified
/// in its device list. <see cref="ApplyTo"/> sets the device-auth
/// headers; the API key itself is never exposed for printing.</summary>
public sealed class BenchDevice
{
    public Guid DeviceId { get; }
    readonly string _apiKey;

    BenchDevice(Guid deviceId, string apiKey)
    {
        DeviceId = deviceId;
        _apiKey = apiKey;
    }

    public void ApplyTo(HttpClient http)
    {
        http.DefaultRequestHeaders.Remove("X-Device-Id");
        http.DefaultRequestHeaders.Remove("X-Api-Key");
        http.DefaultRequestHeaders.Add("X-Device-Id", DeviceId.ToString());
        http.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
    }

    /// <summary>
    /// POST /api/devices/register (with X-Provisioning-Secret when one is
    /// configured) → POST /api/parents/devices/claim with the one-time
    /// claimCode → GET /api/parents/devices must list the device. Any step
    /// failing throws <see cref="BenchSetupException"/>; the caller marks
    /// the run INVALID and stops — an unclaimed device only ever hears the
    /// resting line, so carrying on would measure nothing.
    /// </summary>
    public static async Task<BenchDevice> RegisterAndClaimAsync(BenchParent parent, string macPrefix)
    {
        var mac = $"{macPrefix}-{Guid.NewGuid():N}";
        if (mac.Length > 64) mac = mac[..64];   // register accepts 4–64 chars

        DeviceRegShape reg;
        using (var regResp = await BenchHttp.SendWithBackoffAsync(parent.Anonymous, () =>
        {
            var req = BenchHttp.Json(HttpMethod.Post, "/api/devices/register", new { macAddress = mac });
            if (parent.Args.ProvisioningSecret is { } secret)
                req.Headers.Add("X-Provisioning-Secret", secret);
            return req;
        }, "device register"))
        {
            if (!regResp.IsSuccessStatusCode)
            {
                var why = await BenchHttp.DescribeFailureAsync(regResp);
                var hint = regResp.StatusCode == HttpStatusCode.Unauthorized && !parent.Args.HasProvisioningSecret
                    ? $" (no provisioning secret given: pass --provisioning-secret or set {BenchArgs.ProvisioningSecretEnv}, " +
                      "or run the backend with Devices__AllowOpenRegistration=true)"
                    : "";
                throw new BenchSetupException($"device register failed: {why}{hint}");
            }
            reg = await regResp.Content.ReadFromJsonAsync<DeviceRegShape>(
                      new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                  ?? throw new BenchSetupException("device register returned an empty body");
        }
        if (reg.DeviceId == Guid.Empty || string.IsNullOrEmpty(reg.ApiKey))
            throw new BenchSetupException("device register returned no deviceId/apiKey");
        if (string.IsNullOrEmpty(reg.ClaimCode))
            throw new BenchSetupException("device register returned no claimCode (re-registration?) — cannot claim");

        using (var claim = await BenchHttp.SendWithBackoffAsync(parent.Http,
            () => BenchHttp.Json(HttpMethod.Post, "/api/parents/devices/claim",
                new { deviceId = reg.DeviceId, claimCode = reg.ClaimCode }),
            "device claim"))
        {
            if (!claim.IsSuccessStatusCode)
                throw new BenchSetupException($"device claim failed: {await BenchHttp.DescribeFailureAsync(claim)}");
        }

        using (var list = await parent.Http.GetAsync("/api/parents/devices"))
        {
            if (!list.IsSuccessStatusCode)
                throw new BenchSetupException(
                    $"claim check failed: GET /api/parents/devices {await BenchHttp.DescribeFailureAsync(list)}");
            var body = await list.Content.ReadFromJsonAsync<JsonElement>();
            bool listed = body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("devices", out var devices)
                && devices.ValueKind == JsonValueKind.Array
                && devices.EnumerateArray().Any(d =>
                    d.ValueKind == JsonValueKind.String
                    && Guid.TryParse(d.GetString(), out var id) && id == reg.DeviceId);
            if (!listed)
                throw new BenchSetupException(
                    "device claim did not stick: the device is missing from GET /api/parents/devices");
        }

        return new BenchDevice(reg.DeviceId, reg.ApiKey);
    }

    sealed record DeviceRegShape
    {
        public Guid DeviceId { get; init; }
        public string ApiKey { get; init; } = "";
        public string? ClaimCode { get; init; }
    }
}

/// <summary>Latency percentiles over the measured chat POSTs (setup calls
/// are not timed). Nearest-rank; all null when nothing was measured.</summary>
public sealed record LatencySummary(long? P50, long? P90, long? P99, long? Max, int N);

/// <summary>
/// Stopwatch around every measured POST. The default HttpClient completion
/// option buffers the whole body, so the figure covers the full reply.
/// Requests that throw (timeout, connection refused) record nothing.
/// </summary>
public sealed class BenchLatency
{
    readonly List<long> _samples = new();

    public async Task<(HttpResponseMessage Response, long ElapsedMs)> TimeAsync(
        Func<Task<HttpResponseMessage>> send)
    {
        var sw = Stopwatch.StartNew();
        var response = await send();
        sw.Stop();
        _samples.Add(sw.ElapsedMilliseconds);
        return (response, sw.ElapsedMilliseconds);
    }

    public LatencySummary Summarize()
    {
        if (_samples.Count == 0) return new LatencySummary(null, null, null, null, 0);
        var sorted = _samples.OrderBy(x => x).ToArray();
        long Rank(double p) => sorted[Math.Clamp((int)Math.Ceiling(p / 100.0 * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new LatencySummary(Rank(50), Rank(90), Rank(99), sorted[^1], sorted.Length);
    }
}

/// <summary>
/// Decides whether a run measured anything. INVALID when (a) any setup
/// step failed — parent, device registration, claim, claim check; or
/// (b) first-turn replies were attempted and none came back; or (c) 50%
/// or more of the first-turn replies are byte-identical (at least two of
/// them), since every model-written opener differs. (c) is a canned-line
/// signature with two possible causes the tool cannot tell apart: a gate
/// (unclaimed / paused / cost-cap / fail-closed moderation) or the
/// model-withhold safety fallback (ChatService's SafetyFallbackResponse,
/// or CalmFallbackResponse in Calm) — which on a strict safety-threshold
/// arm is a measurement, not a harness fault. The exit code stays 3
/// either way; <see cref="DominantFirstTurn"/> carries the full reply
/// into summary.json so run_arm.py / report.py can match it against the
/// fallback lines and report a withhold instead of discarding the run.
/// </summary>
public sealed class RunValidity
{
    readonly List<string> _firstTurnReplies = new();
    int _firstTurnsAttempted;

    public string? SetupFailure { get; private set; }
    public bool HasSetupFailure => SetupFailure is not null;
    public bool Valid => InvalidReason is null;
    /// <summary>Null while valid. Recomputed on every read, so it is safe
    /// to read mid-run and final once the last turn is recorded.</summary>
    public string? InvalidReason => Evaluate().Reason;
    /// <summary>The repeated first-turn reply (full text), its count and
    /// the number of replies received — set only when rule (c) is the
    /// reason the run is INVALID, null otherwise.</summary>
    public (string Reply, int Count, int Of)? DominantFirstTurn => Evaluate().Dominant;

    /// <summary>First failure wins; later ones add nothing.</summary>
    public void FailSetup(string reason) => SetupFailure ??= reason;

    /// <summary>Record a scenario's first-turn reply (null when the request
    /// failed). Only replies the model is expected to write belong here —
    /// never a turn whose expected answer is a canned gate line.</summary>
    public void RecordFirstTurn(string? reply)
    {
        _firstTurnsAttempted++;
        if (reply is not null) _firstTurnReplies.Add(reply);
    }

    (string? Reason, (string Reply, int Count, int Of)? Dominant) Evaluate()
    {
        if (SetupFailure is not null)
            return ($"setup failed — {SetupFailure}", null);
        if (_firstTurnsAttempted > 0 && _firstTurnReplies.Count == 0)
            return ($"no first-turn reply received ({_firstTurnsAttempted} attempted)", null);
        if (_firstTurnReplies.Count >= 2)
        {
            var top = _firstTurnReplies
                .GroupBy(r => r, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .First();
            int same = top.Count();
            if (same >= 2 && same * 2 >= _firstTurnReplies.Count)
            {
                var snippet = top.Key.ReplaceLineEndings(" ");
                if (snippet.Length > 80) snippet = snippet[..80] + "…";
                return ($"{same}/{_firstTurnReplies.Count} first-turn replies are byte-identical " +
                        "(canned-line signature: unclaimed / paused / cost-cap / moderation-unavailable, " +
                        "or the model-withhold safety fallback): «" + snippet + "»",
                        (top.Key, same, _firstTurnReplies.Count));
            }
        }
        return (null, null);
    }

    /// <summary>3 = invalid, else the benchmark's own verdict (0 pass, 1 fail).</summary>
    public int ExitCode(bool passed) => !Valid ? 3 : passed ? 0 : 1;

    public void PrintVerdict()
    {
        Console.WriteLine();
        if (Valid)
            Console.WriteLine("  Run validity:      VALID");
        else
            Console.WriteLine($"  RUN INVALID (exit 3): {InvalidReason}");
    }
}

/// <summary>Run identity + the summary.json/markdown additions every
/// benchmark shares.</summary>
public static class BenchSummary
{
    /// <summary>
    /// run_*.json options: camelCase keys so every benchmark's per-turn
    /// record reads the same (latencyMs, reply, safetyFlag, mode), and
    /// Armenian left readable for committed run reports. baseline.json
    /// keeps each tool's own options — its committed shape is unchanged.
    /// </summary>
    public static readonly JsonSerializerOptions ResultsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static string? _gitHead;
    static bool _gitHeadResolved;

    /// <summary>env GIT_HEAD, else <c>git rev-parse --short HEAD</c> run
    /// from the tool's own directory, else null.</summary>
    public static string? GitHead()
    {
        if (_gitHeadResolved) return _gitHead;
        _gitHeadResolved = true;
        var env = Environment.GetEnvironmentVariable("GIT_HEAD");
        if (!string.IsNullOrWhiteSpace(env)) return _gitHead = env.Trim();
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in new[] { "-C", AppContext.BaseDirectory, "rev-parse", "--short", "HEAD" })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd().Trim();
            if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return null; }
            return _gitHead = p.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Serializes the benchmark's own summary object (unchanged fields and
    /// naming) and appends valid, invalidReason, firstTurnSignature, label,
    /// gitHead and latencyMs. invalidReason/firstTurnSignature/label/gitHead
    /// are written as explicit nulls so a consumer can rely on the keys
    /// being present. firstTurnSignature is <c>{reply, count, of}</c> with
    /// the full reply text, set only when the byte-identical first-turn
    /// rule made the run INVALID.
    /// </summary>
    public static async Task WriteAsync(
        string path, object core, JsonSerializerOptions coreOpts,
        BenchArgs args, RunValidity validity, BenchLatency latency)
    {
        var node = JsonSerializer.SerializeToNode(core, coreOpts)?.AsObject() ?? new JsonObject();
        var l = latency.Summarize();
        node["valid"] = validity.Valid;
        node["invalidReason"] = validity.InvalidReason;
        node["firstTurnSignature"] = validity.DominantFirstTurn is { } d
            ? new JsonObject { ["reply"] = d.Reply, ["count"] = d.Count, ["of"] = d.Of }
            : null;
        node["label"] = args.Label;
        node["gitHead"] = GitHead();
        node["latencyMs"] = new JsonObject
        {
            ["p50"] = l.P50,
            ["p90"] = l.P90,
            ["p99"] = l.P99,
            ["max"] = l.Max,
            ["n"] = l.N,
        };
        await File.WriteAllTextAsync(path, node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
    }

    /// <summary>Header lines for the run_*.md report.</summary>
    public static void AppendMarkdownHeader(
        System.Text.StringBuilder md, BenchArgs args, RunValidity validity, BenchLatency latency)
    {
        var l = latency.Summarize();
        md.AppendLine($"**Valid:** {(validity.Valid ? "yes" : "NO — " + validity.InvalidReason)}");
        if (args.Label is not null) md.AppendLine($"**Label:** {args.Label}");
        if (GitHead() is { } head) md.AppendLine($"**Git head:** {head}");
        md.AppendLine(l.N == 0
            ? "**Latency:** no measured turns"
            : $"**Latency (ms, n={l.N}):** p50 {l.P50} / p90 {l.P90} / p99 {l.P99} / max {l.Max}");
    }
}
