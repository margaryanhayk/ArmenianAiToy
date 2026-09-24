using System.Text.Json;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Api.Observability;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the operator console "System" and "Logs" tabs (2026-09-24): the
/// report mirrors what DependencyInjection actually serves, never carries a
/// credential value, raises the checks an operator must act on; spend sums
/// the durable per-day table; the log buffer is newest-first, level-
/// filtered, bounded, and redacts credential-shaped text before storing it.
/// </summary>
public class InternalSystemControllerTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static string Json(object o) => JsonSerializer.Serialize(o);

    // ---------------- SystemStatusReport ----------------

    [Fact]
    public void Report_ProductionShape_ShowsEffectiveModelsPerPath()
    {
        var r = SystemStatusReport.Build(Config(
            ("AI:ChatProvider", "gemini"),
            ("Gemini:Model", "gemini-3.6-flash"),
            ("OpenAI:TranscriptionModel", "gpt-transcribe"),
            ("OpenAI:TtsModel", "gpt-4o-mini-tts")), isDevelopment: false, Now);

        var chat = Assert.Single(r.Models, m => m.Path.StartsWith("Chat"));
        Assert.Equal(("gemini", "gemini-3.6-flash"), (chat.Provider, chat.Model));
        Assert.All(r.Models.Where(m => m.Path.StartsWith("Speech-to-text")),
            m => Assert.Equal("gpt-transcribe", m.Model)); // voice-intent + story-qa fall back
        Assert.Equal("gpt-4o-mini-tts", Assert.Single(r.Models, m => m.Path.StartsWith("Text-to-speech")).Model);
        Assert.Equal("omni-moderation-latest", Assert.Single(r.Models, m => m.Path.StartsWith("Safety")).Model);
        Assert.Contains(r.Checks, c => c.Code == "gemini_terms");
        Assert.DoesNotContain(r.Checks, c => c.Code == "model_retirement");
    }

    [Fact]
    public void Report_Defaults_MirrorDependencyInjection()
    {
        var r = SystemStatusReport.Build(Config(), isDevelopment: false, Now);

        Assert.Equal(("openai", "gpt-4o-mini"),
            (r.Models[0].Provider, r.Models[0].Model));
        Assert.All(r.Models.Where(m => m.Path.StartsWith("Speech-to-text")),
            m => Assert.Equal("whisper-1", m.Model));
        Assert.Equal("tts-1", Assert.Single(r.Models, m => m.Path.StartsWith("Text-to-speech")).Model);
        Assert.Equal(3, r.Checks.Count(c => c.Code == "model_retirement"));
    }

    [Fact]
    public void Report_RaisesOperatorChecks()
    {
        var r = SystemStatusReport.Build(Config(
            ("OpenAI:DailyCostCap:Enabled", "false"),
            ("AI:TtsProvider", "elevenlabs")), isDevelopment: false, Now);
        var codes = r.Checks.Select(c => c.Code).ToHashSet();

        Assert.Contains("audio_store", codes);
        Assert.Contains("forwarded_headers", codes);
        Assert.Contains("alerts_off", codes);
        Assert.Contains("cost_cap_off", codes);
        Assert.Contains("global_cap_off", codes);
        Assert.Contains("elevenlabs_terms", codes);
    }

    [Fact]
    public void Report_FullyConfigured_HasNoWarnings()
    {
        var r = SystemStatusReport.Build(Config(
            ("OpenAI:TranscriptionModel", "gpt-transcribe"),
            ("Audio:BlobStoreRoot", "/data/audio-blobs"),
            ("ForwardedHeaders:Enabled", "true"),
            ("Alerts:WebhookUrl", "https://hooks.example/x"),
            ("OpenAI:DailyCostCap:Enabled", "true"),
            ("OpenAI:DailyCostCap:Global", "5")), isDevelopment: false, Now);

        Assert.Empty(r.Checks);
    }

    [Fact]
    public void Report_NeverCarriesACredentialValue()
    {
        const string openAi = "sk-test-SECRETVALUE-123456";
        const string gemini = "AIzaSECRETVALUEgemini0000000000";
        const string eleven = "el-SECRETVALUE-eleven";
        const string webhook = "https://hooks.slack.com/services/SECRETVALUE";
        var r = SystemStatusReport.Build(Config(
            ("OpenAI:ApiKey", openAi), ("GEMINI_API_KEY", gemini),
            ("ElevenLabs:ApiKey", eleven), ("Alerts:WebhookUrl", webhook),
            ("Gemini:Vertex:ServiceAccountJson", "{\"private_key\":\"SECRETVALUE\"}"),
            ("Metrics:ScrapeToken", "SECRETVALUE")), isDevelopment: false, Now);

        Assert.DoesNotContain("SECRETVALUE", Json(r));
        Assert.True(r.Configured["openaiKey"]);
        Assert.True(r.Configured["geminiKey"]);   // env-name fallback, as in DI
        Assert.True(r.Configured["elevenLabsKey"]);
        Assert.True(r.Configured["alertsWebhook"]);
    }

    [Fact]
    public void Report_UnknownProvider_DoesNotThrow()
    {
        var r = SystemStatusReport.Build(Config(("AI:ChatProvider", "mystery")), isDevelopment: false, Now);
        Assert.Equal("mystery", r.Models[0].Provider);
    }

    // ---------------- controller ----------------

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static InternalSystemController NewController(AppDbContext db, IConfiguration? config = null, RecentLogBuffer? logs = null)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Production");
        return new InternalSystemController(db, config ?? Config(), env, new OpenAICostMeter(),
            Options.Create(new OpenAIDailyCostCapOptions()), openAiGate: null, logs: logs);
    }

    [Fact]
    public async Task System_SumsDurableSpendPerDay_AndNeverLeaksKeys()
    {
        using var db = NewDb();
        var today = DateTime.UtcNow.Date;
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        db.DeviceUsageDays.AddRange(
            new DeviceUsageDay { Id = Guid.NewGuid(), DeviceId = a, DayUtc = today, Questions = 3, EstimatedUsd = 0.01m },
            new DeviceUsageDay { Id = Guid.NewGuid(), DeviceId = b, DayUtc = today, Questions = 2, EstimatedUsd = 0.02m },
            new DeviceUsageDay { Id = Guid.NewGuid(), DeviceId = a, DayUtc = today.AddDays(-1), Questions = 1, EstimatedUsd = 0.005m },
            new DeviceUsageDay { Id = Guid.NewGuid(), DeviceId = a, DayUtc = today.AddDays(-40), Questions = 9, EstimatedUsd = 9m });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await NewController(db,
            Config(("OpenAI:ApiKey", "sk-SECRETVALUE-abcdefgh"))).System(default));
        using var doc = JsonDocument.Parse(Json(ok.Value!));
        var spend = doc.RootElement.GetProperty("spend");

        Assert.Equal(0.035m, spend.GetProperty("last30DaysUsd").GetDecimal()); // 40-day-old row excluded
        var days = spend.GetProperty("days");
        Assert.Equal(2, days.GetArrayLength());
        Assert.Equal(5, days[0].GetProperty("questions").GetInt32());          // newest first
        Assert.Equal(2, days[0].GetProperty("activeToys").GetInt32());
        Assert.DoesNotContain("SECRETVALUE", doc.RootElement.GetRawText());
    }

    [Fact]
    public async Task System_RanksTopToysBySpend_WithNames()
    {
        using var db = NewDb();
        var today = DateTime.UtcNow.Date;
        Guid cheap = Guid.NewGuid(), pricey = Guid.NewGuid();
        db.Devices.Add(new Device { Id = pricey, Name = "Kitchen Areg", MacAddress = "AA" });
        db.DeviceUsageDays.AddRange(
            new DeviceUsageDay { Id = Guid.NewGuid(), DeviceId = cheap, DayUtc = today, Questions = 1, EstimatedUsd = 0.01m },
            new DeviceUsageDay { Id = Guid.NewGuid(), DeviceId = pricey, DayUtc = today, Questions = 4, EstimatedUsd = 0.20m },
            new DeviceUsageDay { Id = Guid.NewGuid(), DeviceId = pricey, DayUtc = today.AddDays(-2), Questions = 2, EstimatedUsd = 0.10m });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await NewController(db).System(default));
        using var doc = JsonDocument.Parse(Json(ok.Value!));
        var top = doc.RootElement.GetProperty("spend").GetProperty("topToys");

        Assert.Equal(2, top.GetArrayLength());
        Assert.Equal("Kitchen Areg", top[0].GetProperty("name").GetString());
        Assert.Equal(0.30m, top[0].GetProperty("estimatedUsd").GetDecimal());
        Assert.Equal(6, top[0].GetProperty("questions").GetInt32());
        Assert.Equal(JsonValueKind.Null, top[1].GetProperty("name").ValueKind); // unknown device: id only
    }

    [Fact]
    public void Backup_NewestSnapshot_IsFound_AndMissingDirectoryIsReported()
    {
        using var db = NewDb();
        var dir = Path.Combine(Path.GetTempPath(), "areg-backup-test-" + Guid.NewGuid().ToString("N"));
        var cfg = Config(("Backup:Database:DirectoryPath", dir));

        var missing = InternalSystemController.NewestBackup(cfg, db);
        Assert.Equal(dir, missing.Directory);
        Assert.Null(missing.NewestAtUtc);

        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "areg-backup-2026-09-23.db"), "old");
            File.SetLastWriteTimeUtc(Path.Combine(dir, "areg-backup-2026-09-23.db"), DateTime.UtcNow.AddDays(-1));
            File.WriteAllText(Path.Combine(dir, "areg-backup-2026-09-24.db"), "newest");
            File.WriteAllText(Path.Combine(dir, "areg-uploads-2026-09-24.zip"), "not a db snapshot");

            var found = InternalSystemController.NewestBackup(cfg, db);
            Assert.Equal(6, found.SizeBytes);
            Assert.True(DateTime.UtcNow - found.NewestAtUtc < TimeSpan.FromMinutes(5));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task System_StaleBackup_RaisesCheck()
    {
        using var db = NewDb();
        var dir = Path.Combine(Path.GetTempPath(), "areg-backup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var f = Path.Combine(dir, "areg-backup-2026-09-20.db");
            File.WriteAllText(f, "x");
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-3));

            var ok = Assert.IsType<OkObjectResult>(await NewController(db,
                Config(("Backup:Database:DirectoryPath", dir))).System(default));
            Assert.Contains("backup_stale", Json(ok.Value!));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ElevenLabs_NoKey_ReportsUnavailable_WithoutCallingOut()
    {
        using var db = NewDb();
        var ok = Assert.IsType<OkObjectResult>(await NewController(db).ElevenLabsQuota(default));
        Assert.False(JsonDocument.Parse(Json(ok.Value!)).RootElement.GetProperty("available").GetBoolean());
    }

    [Fact]
    public void ElevenLabs_ParsesLiveSubscriptionShape()
    {
        // Field names checked against the live API on 2026-09-24.
        using var doc = JsonDocument.Parse(
            "{\"tier\":\"creator\",\"status\":\"active\",\"character_count\":45984," +
            "\"character_limit\":284450,\"next_character_count_reset_unix\":1791223848}");
        using var parsed = JsonDocument.Parse(Json(InternalSystemController.ParseElevenLabsSubscription(doc.RootElement)));
        var p = parsed.RootElement;

        Assert.True(p.GetProperty("available").GetBoolean());
        Assert.Equal(238466, p.GetProperty("charactersLeft").GetInt64());
        Assert.Equal("creator", p.GetProperty("tier").GetString());
        Assert.Equal(2026, p.GetProperty("resetsAtUtc").GetDateTime().Year);
    }

    [Fact]
    public void ElevenLabs_UnexpectedShape_IsUnavailable_NotAnException()
    {
        using var doc = JsonDocument.Parse("{\"detail\":\"nope\"}");
        using var parsed = JsonDocument.Parse(Json(InternalSystemController.ParseElevenLabsSubscription(doc.RootElement)));
        Assert.False(parsed.RootElement.GetProperty("available").GetBoolean());
    }

    // ---------------- RecentLogBuffer ----------------

    [Fact]
    public void LogBuffer_IsNewestFirst_LevelFiltered_AndBounded()
    {
        var buffer = new RecentLogBuffer(capacity: 3);
        var logger = buffer.CreateLogger("Test");
        logger.LogInformation("ignored — below Warning");
        logger.LogWarning("w1");
        logger.LogError("e1");
        logger.LogWarning("w2");
        logger.LogError("e2"); // evicts w1

        Assert.Equal(new[] { "e2", "w2", "e1" }, buffer.Snapshot(LogLevel.Warning, 10).Select(e => e.Message));
        Assert.Equal(new[] { "e2", "e1" }, buffer.Snapshot(LogLevel.Error, 10).Select(e => e.Message));
        Assert.Single(buffer.Snapshot(LogLevel.Warning, 1));
    }

    [Theory]
    [InlineData("Authorization: Bearer abc.def-ghi=", "Authorization: Bearer ***")]
    [InlineData("call failed with sk-proj-ABCDEFGH12345678 set", "call failed with *** set")]
    [InlineData("GET /x?key=AIzaSomething&y=1", "GET /x?key=***&y=1")]
    [InlineData("token=abc password=hunter2", "token=*** password=***")]
    [InlineData("raw AIzaSyA1234567890abcdefghijklmno here", "raw *** here")]
    [InlineData("Model retirement: whisper-1 removes 2027-02-26", "Model retirement: whisper-1 removes 2027-02-26")]
    public void LogBuffer_RedactsCredentialShapedText(string input, string expected)
        => Assert.Equal(expected, RecentLogBuffer.Redact(input));

    [Fact]
    public void LogBuffer_RedactsBeforeStoring_IncludingExceptionText()
    {
        var buffer = new RecentLogBuffer();
        buffer.CreateLogger("Test").LogError(
            new InvalidOperationException("upstream said Bearer SECRETVALUE"), "failed key={Key}", "SECRETVALUE");

        var e = Assert.Single(buffer.Snapshot(LogLevel.Warning, 10));
        Assert.DoesNotContain("SECRETVALUE", e.Message + e.Exception);
        Assert.StartsWith("InvalidOperationException", e.Exception);
    }

    [Fact]
    public void Logs_Endpoint_FiltersByLevel_AndClampsLimit()
    {
        var buffer = new RecentLogBuffer();
        var logger = buffer.CreateLogger("Test");
        for (var i = 0; i < 3; i++) logger.LogWarning("w{I}", i);
        logger.LogError("boom");
        using var db = NewDb();
        var controller = NewController(db, logs: buffer);

        using var errors = JsonDocument.Parse(Json(Assert.IsType<OkObjectResult>(controller.Logs("error")).Value!));
        Assert.Equal(1, errors.RootElement.GetProperty("entries").GetArrayLength());
        using var all = JsonDocument.Parse(Json(Assert.IsType<OkObjectResult>(controller.Logs(null, limit: 0)).Value!));
        Assert.Equal(1, all.RootElement.GetProperty("entries").GetArrayLength()); // limit clamps to ≥1
    }
}
