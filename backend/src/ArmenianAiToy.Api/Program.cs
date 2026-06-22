using ArmenianAiToy.Api.Health;
using ArmenianAiToy.Api.Middleware;
using ArmenianAiToy.Api.Observability;
using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.Auth;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Infrastructure;
using ArmenianAiToy.Infrastructure.Data;
using ArmenianAiToy.Infrastructure.OpenAI;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// Infrastructure (DB, OpenAI, services)
builder.Services.AddInfrastructure(builder.Configuration);

// OpenTelemetry — metrics first, traces auto-collected as a side effect.
// - Metrics: AspNetCore + Runtime built-ins plus this service's AppMeter,
//   exported via a Prometheus scrape endpoint wired below
//   (app.UseOpenTelemetryPrometheusScrapingEndpoint).
// - Traces: AspNetCore + HttpClient instrumentations register
//   Activities automatically (no custom spans in this slice). In
//   Development we pipe them to the console so `dotnet run` makes
//   them visible; no OTLP endpoint is assumed, and no trace export
//   happens in Production yet.
// Two latency histograms live on AppMeter (chat gate + moderation);
// their explicit bucket boundaries are wired below via AddView. No
// ChatService / ModeDetector spans, no high-cardinality tags — see
// Telemetry/AppMeter.cs for the rule.

// Shared explicit bucket boundaries (seconds) for the OpenAI latency
// histograms. 10 ms → 30 s spans the useful range between a breaker
// short-circuit (near zero) and the 30 s adapter timeout ceiling.
// Keeping the two histograms on identical boundaries makes dashboards
// and alerts trivially comparable.
var openAiLatencyBuckets = new double[]
{
    0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30
};

var otel = builder.Services.AddOpenTelemetry();
otel.WithMetrics(m => m
    .AddAspNetCoreInstrumentation()
    .AddRuntimeInstrumentation()
    .AddMeter(AppMeter.Name)
    .AddView(
        instrumentName: "aat_chat_openai_duration_seconds",
        metricStreamConfiguration: new ExplicitBucketHistogramConfiguration
        {
            Boundaries = openAiLatencyBuckets
        })
    .AddView(
        instrumentName: "aat_moderation_classify_duration_seconds",
        metricStreamConfiguration: new ExplicitBucketHistogramConfiguration
        {
            Boundaries = openAiLatencyBuckets
        })
    .AddView(
        // Voice Q&A end-to-end latency — the "dead air" metric. Shares the
        // OpenAI latency buckets so dashboards stay comparable.
        instrumentName: "aat_story_qa_duration_seconds",
        metricStreamConfiguration: new ExplicitBucketHistogramConfiguration
        {
            Boundaries = openAiLatencyBuckets
        })
    .AddPrometheusExporter());
otel.WithTracing(t =>
{
    t.AddAspNetCoreInstrumentation()
     .AddHttpClientInstrumentation();
    if (builder.Environment.IsDevelopment())
    {
        t.AddConsoleExporter();
    }
});

// JWT authentication for parent endpoints.
// Multi-key rotation: the validator accepts the full ordered list from
// Jwt:Keys (or the legacy scalar Jwt:Key fallback). New tokens are
// signed with the primary (first) key only — see ParentService.GenerateJwt.
// The helper fails fast at startup if no key is configured or if any
// configured entry equals the publicly-known legacy insecure default,
// so a misconfigured instance cannot silently sign with a known secret.
var jwtKeys = JwtKeys.ResolveOrderedKeys(builder.Configuration);
var jwtSigningKeys = jwtKeys
    .Select(k => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(k)))
    .ToList();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ArmenianAiToy",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ArmenianAiToy",
            // IssuerSigningKeys (list) accepts any key on the set; a token
            // still signed by a previous key keeps working for its lifetime
            // during rotation. Replaces the old single-key IssuerSigningKey.
            IssuerSigningKeys = jwtSigningKeys
        };
    });

// CORS (#037): permissive ONLY in Development. In any other environment, allow
// only the origins explicitly listed in Cors:AllowedOrigins (empty => no cross-
// origin access). The parent dashboard, admin console, and the device are all
// SAME-ORIGIN, so a strict policy does not affect them — it only stops arbitrary
// websites scripting the public endpoints (register / login-probe) from a
// victim's browser. AllowAnyOrigin in prod was a standing CSRF-adjacent risk.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            var origins = builder.Configuration
                .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
            }
            // No origins configured in a non-Development environment => default
            // deny (no CORS headers emitted): cross-origin browser calls blocked.
        }
    });
});

// Host filtering (#061): permissive ONLY in Development. In any other
// environment, restrict to the hostnames explicitly listed in AllowedHosts
// (semicolon-separated). A non-Development environment left unpinned stays
// permissive but logs a loud startup warning (see below) — failing closed on
// a forgotten config key would be a worse outage than a permissive filter.
// Closes the "AllowedHosts *" Host-header / cache-poisoning gap. Mirrors the
// #037 CORS posture. Overrides the framework's default AllowedHosts-config
// binding because this Configure runs last.
var hostFiltering = HostFilteringConfig.Resolve(
    builder.Environment.IsDevelopment(), builder.Configuration["AllowedHosts"]);
builder.Services.Configure<HostFilteringOptions>(options =>
{
    options.AllowedHosts = hostFiltering.Hosts;
});

// Per-device rate limit for /api/chat. Cost-containment layer — sits ahead
// of DeviceAuthMiddleware so rejected requests never hit the DB lookup or
// the OpenAI pipeline. See ChatRateLimiter for the keying rationale.
var chatPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Chat:PermitLimit")
    ?? ChatRateLimiter.DefaultPermitLimit;
var chatWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Chat:WindowSeconds")
    ?? ChatRateLimiter.DefaultWindowSeconds;
// Per-caller-IP rate limit for parent auth / account-sensitive endpoints
// (register / login / password / delete-account). Separate policy from chat
// so the two buckets don't share quota. See AuthRateLimiter for the keying
// rationale and the explicit note on ForwardedHeaders middleware being a
// deploy-slice concern, not this slice's job.
var authPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Auth:PermitLimit")
    ?? AuthRateLimiter.DefaultPermitLimit;
var authWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Auth:WindowSeconds")
    ?? AuthRateLimiter.DefaultWindowSeconds;
// Per-caller-IP rate limit for the header-less /api/story-audio stream.
// Separate bucket again — story streaming must not share quota with chat
// or auth. See StoryAudioRateLimiter for keying + the looser default.
var storyAudioPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:StoryAudio:PermitLimit")
    ?? StoryAudioRateLimiter.DefaultPermitLimit;
var storyAudioWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:StoryAudio:WindowSeconds")
    ?? StoryAudioRateLimiter.DefaultWindowSeconds;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ChatRateLimiter.PolicyName, ctx =>
        ChatRateLimiter.PolicyFactory(ctx, chatPermitLimit, chatWindowSeconds));
    options.AddPolicy(AuthRateLimiter.PolicyName, ctx =>
        AuthRateLimiter.PolicyFactory(ctx, authPermitLimit, authWindowSeconds));
    options.AddPolicy(StoryAudioRateLimiter.PolicyName, ctx =>
        StoryAudioRateLimiter.PolicyFactory(ctx, storyAudioPermitLimit, storyAudioWindowSeconds));
    options.OnRejected = async (context, cancellationToken) =>
    {
        // Count the rejection before mutating the response — if writing
        // the body fails for any reason, the metric still reflects the
        // fact that the limiter tripped. Shared counter across both
        // policies (chat + auth + story-audio); bounded `policy` tag
        // ({chat, auth, story_audio})
        // derived from the matched endpoint's [EnableRateLimiting]
        // metadata via RateLimitRejectionPolicy.ResolvePolicyTag. Stays
        // within the AppMeter no-high-cardinality invariant — no
        // device_id / ip / route / email tag.
        var policyTag = RateLimitRejectionPolicy.ResolvePolicyTag(context.HttpContext);
        AppMeter.RateLimitRejected.Add(1,
            new KeyValuePair<string, object?>("policy", policyTag));
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        }
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Please slow down." }, cancellationToken);
    };
});

var app = builder.Build();

// #061 — surface an unpinned Host filter in non-Development environments.
if (hostFiltering.Warning is not null)
    app.Logger.LogWarning("{HostFilteringWarning}", hostFiltering.Warning);

// Apply any unapplied EF Core migrations. Replaces the previous
// EnsureCreated() call — migrations are now the single source of truth
// for the schema. First-pull-after-this-commit policy: delete any
// legacy dev DB file (armenian_ai_toy.db*) before running, because it
// predates the __EFMigrationsHistory table. See CLAUDE.md § Database
// migrations for the baseline-adoption alternative used for staging/prod.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// #039 — proxy-aware client IP, opt-in. OFF by default (ForwardedHeaders:Enabled
// false): X-Forwarded-For is NOT processed, RemoteIpAddress stays the direct TCP
// peer, and the per-IP rate limiters are unchanged. When an operator runs behind
// a TLS-terminating proxy they enable it and list the proxy IP(s) in
// ForwardedHeaders:KnownProxies; this then rewrites RemoteIpAddress from XFF
// (trusting ONLY those proxies) so every limiter keys on the real client IP with
// no limiter-code change. Registered FIRST so all downstream middleware (CORS,
// rate limiter, device auth, controllers) sees the corrected client IP.
var forwardedHeaders = ForwardedHeadersConfig.TryBuild(builder.Configuration);
if (forwardedHeaders is not null)
    app.UseForwardedHeaders(forwardedHeaders);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Rate limiter runs BEFORE device-auth middleware so 429s short-circuit the
// DB validation + OpenAI pipeline. Per-endpoint policy is opted-in via
// [EnableRateLimiting] on ChatController.
app.UseRateLimiter();

// Device auth middleware (for /api/chat, /api/audio endpoints)
app.UseMiddleware<DeviceAuthMiddleware>();

// Serve static files (for web UI testing)
app.UseDefaultFiles();
app.UseStaticFiles();

// Superuser internal console API (/api/internal/*) — fail-closed bearer
// gate, same pattern as the /metrics guard below (see InternalAdminAuth).
// With Internal:AdminToken empty and Internal:AllowUnauthenticated false
// (the appsettings.json defaults) every /api/internal request gets a 404,
// so a fresh deploy exposes nothing. Runs BEFORE MapControllers so a denied
// request never reaches the console controller. 404 (not 401) conceals the
// surface. The per-parent JWT pipeline is unaffected — this guards only the
// operator god-view.
// #012: per-operator identity. Named operators (Internal:Operators = [{Name,
// Token}]) each get their own bearer token; the legacy single Internal:AdminToken
// still works for back-compat / bench. The resolved operator NAME is stashed for
// the #013 access audit so a leaked token is traceable to (and revocable as) one
// operator. Fail-closed default unchanged (no token + no bypass => 404).
var internalAdminToken = builder.Configuration["Internal:AdminToken"];
var internalAllowAnon = builder.Configuration
    .GetValue<bool>("Internal:AllowUnauthenticated");
var internalOperators = builder.Configuration.GetSection("Internal:Operators")
    .Get<List<InternalAdminAuth.OperatorCredential>>() ?? new();
app.Use(async (ctx, next) =>
{
    if (InternalAdminAuth.IsInternalPath(ctx.Request.Path))
    {
        var operatorName = InternalAdminAuth.ResolveOperatorName(
            ctx, internalOperators, internalAdminToken, internalAllowAnon);
        if (operatorName is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        // #013: stash WHO is reading so the console can audit child-data access.
        ctx.Items["InternalOperator"] = operatorName;
        // The console is a live operator view — never let a browser or proxy
        // serve a stale snapshot of the whole-system data.
        ctx.Response.Headers["Cache-Control"] = "no-store";
    }
    await next();
});

app.MapControllers();

// Prometheus scrape surface for the AppMeter + AspNetCore/Runtime
// counters. Guarded by a narrow bearer-token check (see
// MetricsScrapeAuth): fresh deploys are fail-closed on /metrics until
// either Metrics:ScrapeToken is set or Metrics:AllowUnauthenticatedScrape
// is flipped to true. The guard runs BEFORE the OTel scrape middleware
// so rejected requests never touch the exporter. The AppMeter contract
// continues to forbid high-cardinality tags — this guard only changes
// WHO can read the aggregate surface, not what it contains.
var metricsScrapeToken = builder.Configuration["Metrics:ScrapeToken"];
var metricsAllowAnon = builder.Configuration
    .GetValue<bool>("Metrics:AllowUnauthenticatedScrape");
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.Equals(
            MetricsScrapeAuth.MetricsPath, StringComparison.OrdinalIgnoreCase))
    {
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, metricsScrapeToken, metricsAllowAnon);
        if (decision == MetricsScrapeAuth.Decision.Deny)
        {
            // 404 rather than 401: conceals the endpoint from scanners
            // and avoids mimicking a standard auth challenge scheme the
            // app is not actually running. See MetricsScrapeAuth xmldoc.
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
    }
    await next();
});
app.UseOpenTelemetryPrometheusScrapingEndpoint();

// Health check — probes the one real runtime dependency (SQLite). The
// liveness verdict (HTTP 200 vs 503) is DB-only ON PURPOSE: OpenAI is a
// SHARED downstream, so failing liveness during an OpenAI outage would pull
// EVERY instance out of the load balancer at once — a self-inflicted
// fleet-wide outage on a host that is otherwise fine. Instead (#070) we add
// a NON-FATAL `openai` readiness field derived from the reliability gate's
// circuit-breaker state: a passive, zero-cost signal (no probe call, no
// quota burn) that surfaces a sustained outage to dashboards/alerts without
// affecting the LB verdict. See HealthProbe + OpenAIReliabilityGate.IsCircuitOpen.
app.MapGet("/api/health", async (
    AppDbContext db, OpenAIReliabilityGate openAiGate, CancellationToken ct) =>
{
    var dbOk = await HealthProbe.IsDatabaseReachableAsync(db, TimeSpan.FromSeconds(2), ct);
    AppMeter.HealthProbe.Add(1,
        new KeyValuePair<string, object?>("result", dbOk ? "ok" : "unhealthy"));
    var openAiDegraded = openAiGate.IsCircuitOpen();
    var payload = new
    {
        status = dbOk ? "ok" : "unhealthy",
        service = "ArmenianAiToy API",
        database = dbOk ? "ok" : "unreachable",
        // Non-fatal: "degraded" means the breaker is currently open (recent
        // OpenAI failures); the instance is still live and serves cached /
        // gated paths. Does NOT flip the HTTP status.
        openai = openAiDegraded ? "degraded" : "ok"
    };
    return dbOk ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
});

app.Run();
