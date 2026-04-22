using ArmenianAiToy.Api.Health;
using ArmenianAiToy.Api.Middleware;
using ArmenianAiToy.Api.Observability;
using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Infrastructure;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
// Fail fast if Jwt:Key is missing or set to the legacy insecure literal —
// a misconfigured instance would sign tokens with a universal, publicly
// known secret and every parent account would be trivially impersonable.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey == "ArmenianAiToyDefaultSecretKeyThatShouldBeChanged123!")
    throw new InvalidOperationException(
        "Jwt:Key must be configured and must not be the legacy default. " +
        "Set it via user-secrets (dotnet user-secrets set \"Jwt:Key\" ...) " +
        "or the JWT__KEY environment variable.");
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

// CORS: allow all for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
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
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ChatRateLimiter.PolicyName, ctx =>
        ChatRateLimiter.PolicyFactory(ctx, chatPermitLimit, chatWindowSeconds));
    options.AddPolicy(AuthRateLimiter.PolicyName, ctx =>
        AuthRateLimiter.PolicyFactory(ctx, authPermitLimit, authWindowSeconds));
    options.OnRejected = async (context, cancellationToken) =>
    {
        // Count the rejection before mutating the response — if writing
        // the body fails for any reason, the metric still reflects the
        // fact that the limiter tripped. Shared counter across both
        // policies (chat + auth); no policy tag, because adding one
        // would duplicate the signal that policy-specific logs already
        // carry and would not meet the bounded-cardinality bar the
        // AppMeter contract documents. No device_id / ip tag for the
        // same reason.
        AppMeter.RateLimitRejected.Add(1);
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

// Health check — probes the one real runtime dependency (SQLite). OpenAI
// config is validated at startup and deliberately not re-probed here; see
// HealthProbe class comment for the rationale.
app.MapGet("/api/health", async (AppDbContext db, CancellationToken ct) =>
{
    var dbOk = await HealthProbe.IsDatabaseReachableAsync(db, TimeSpan.FromSeconds(2), ct);
    AppMeter.HealthProbe.Add(1,
        new KeyValuePair<string, object?>("result", dbOk ? "ok" : "unhealthy"));
    var payload = new
    {
        status = dbOk ? "ok" : "unhealthy",
        service = "ArmenianAiToy API",
        database = dbOk ? "ok" : "unreachable"
    };
    return dbOk ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
});

app.Run();
