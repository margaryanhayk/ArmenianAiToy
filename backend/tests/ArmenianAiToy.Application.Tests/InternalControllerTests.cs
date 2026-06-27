using System.Text.Json;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the superuser console controller (<c>/api/internal/*</c>).
/// Read-only god view. Pins the cross-cutting aggregation AND the two
/// secret invariants: parents never leak PasswordHash, devices never leak
/// ApiKey / ApiKeyHash. The token gate itself is covered by
/// <see cref="InternalAdminAuthTests"/>.
/// </summary>
public class InternalControllerTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static InternalController NewController(
        AppDbContext db, IAiChatClient? ai = null, IModerationService? moderation = null,
        IConfiguration? config = null) =>
        new(db, new InMemoryCuratedStoryLibrary(), new OpenAICostMeter(),
            new LibraryStoryQuestionService(ai ?? Substitute.For<IAiChatClient>()),
            moderation ?? SafeModeration(),
            config ?? new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<InternalController>>());

    private static IModerationService SafeModeration()
    {
        var m = Substitute.For<IModerationService>();
        m.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        return m;
    }

    private static T Value<T>(IActionResult r) => (T)((OkObjectResult)r).Value!;

    // Serialize the way ASP.NET serializes controller results (camelCase), so
    // these assertions validate the actual wire contract admin.html consumes.
    private static string Json(IActionResult r) =>
        JsonSerializer.Serialize(((OkObjectResult)r).Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static Device Dev(string name = "Bench") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
        RegisteredAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow
    };

    // ── Overview counts ────────────────────────────────────────────

    [Fact]
    public async Task Overview_CountsEntitiesAndFlagged()
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        var conv = new Conversation { Id = Guid.NewGuid(), DeviceId = d.Id, StartedAt = DateTime.UtcNow };
        db.Conversations.Add(conv);
        db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, Role = MessageRole.User, Content = "hi", Timestamp = DateTime.UtcNow, SafetyFlag = SafetyFlag.Clean });
        db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, Role = MessageRole.User, Content = "bad", Timestamp = DateTime.UtcNow, SafetyFlag = SafetyFlag.Blocked });
        db.Parents.Add(new Parent { Id = Guid.NewGuid(), Email = "a@b.c", PasswordHash = "x", RegisteredAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await NewController(db).Overview(default);
        var o = Value<AdminOverviewDto>(result);

        Assert.Equal(1, o.Devices);
        Assert.Equal(1, o.Parents);
        Assert.Equal(1, o.Conversations);
        Assert.Equal(2, o.Messages);
        Assert.Equal(1, o.FlaggedMessages);
    }

    // ── Secret invariants (KEYSTONE) ───────────────────────────────

    [Fact]
    public async Task Parents_NeverExposePasswordHash()
    {
        var db = NewDb();
        const string Marker = "SUPER-SECRET-HASH-MARKER-9f3a";
        db.Parents.Add(new Parent
        {
            Id = Guid.NewGuid(), Email = "p@x.c", PasswordHash = Marker,
            RegisteredAt = DateTime.UtcNow, GoogleSubject = "google-sub-marker-should-not-leak"
        });
        await db.SaveChangesAsync();

        var json = Json(await NewController(db).Parents(default));

        Assert.DoesNotContain(Marker, json);
        Assert.DoesNotContain("google-sub-marker-should-not-leak", json); // raw subject never leaks
        Assert.Contains("\"googleLinked\":true", json);                   // only the bool
        Assert.Contains("p@x.c", json);                                   // email is a safe field
    }

    [Fact]
    public async Task Devices_NeverExposeApiKeyOrHash()
    {
        var db = NewDb();
        var d = Dev();
        d.ApiKey = "PLAINTEXT-APIKEY-MARKER-7b21";
        d.ApiKeyHash = "v1:pbkdf2-sha256:HASH-MARKER-44ce";
        db.Devices.Add(d);
        await db.SaveChangesAsync();

        var json = Json(await NewController(db).Devices(default));

        Assert.DoesNotContain("PLAINTEXT-APIKEY-MARKER-7b21", json);
        Assert.DoesNotContain("HASH-MARKER-44ce", json);
        Assert.Contains(d.Name, json); // safe identity field present
    }

    // ── Flagged spans all devices ──────────────────────────────────

    [Fact]
    public async Task Flagged_ReturnsNonCleanAcrossAllDevices()
    {
        var db = NewDb();
        var d1 = Dev("D1"); var d2 = Dev("D2");
        db.Devices.AddRange(d1, d2);
        var c1 = new Conversation { Id = Guid.NewGuid(), DeviceId = d1.Id, StartedAt = DateTime.UtcNow };
        var c2 = new Conversation { Id = Guid.NewGuid(), DeviceId = d2.Id, StartedAt = DateTime.UtcNow };
        db.Conversations.AddRange(c1, c2);
        db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = c1.Id, Role = MessageRole.User, Content = "clean", Timestamp = DateTime.UtcNow, SafetyFlag = SafetyFlag.Clean });
        db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = c1.Id, Role = MessageRole.User, Content = "flag-d1", Timestamp = DateTime.UtcNow, SafetyFlag = SafetyFlag.Flagged });
        db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = c2.Id, Role = MessageRole.User, Content = "blk-d2", Timestamp = DateTime.UtcNow, SafetyFlag = SafetyFlag.Blocked });
        await db.SaveChangesAsync();

        var json = Json(await NewController(db).Flagged(50, 0, default));

        Assert.Contains("flag-d1", json);
        Assert.Contains("blk-d2", json);
        Assert.DoesNotContain("clean", json);
    }

    // ── Global audit shows system-actor rows parents can't see ─────

    [Fact]
    public async Task Audit_IncludesSystemActorRows()
    {
        var db = NewDb();
        db.AuditEvents.Add(AuditEvent.ConversationsPurgedByRetention(3, 9, DateTime.UtcNow, 500)); // ActorParentId == null
        db.AuditEvents.Add(AuditEvent.ParentPasswordChanged(Guid.NewGuid()));                       // a parent row
        await db.SaveChangesAsync();

        var json = Json(await NewController(db).Audit(50, 0, default));

        Assert.Contains("ConversationsPurgedByRetention", json); // system-actor event visible to admin
        Assert.Contains("ParentPasswordChanged", json);
    }

    // ── Pagination guard ───────────────────────────────────────────

    [Fact]
    public async Task Flagged_InvalidPagination_Returns400()
    {
        var db = NewDb();
        Assert.IsType<BadRequestObjectResult>(await NewController(db).Flagged(0, 0, default));
        Assert.IsType<BadRequestObjectResult>(await NewController(db).Flagged(50, -1, default));
    }

    // ── Stories surface from the runtime library ───────────────────

    [Fact]
    public void Stories_ListsCuratedLibrary()
    {
        var db = NewDb();
        var result = NewController(db).Stories();
        var json = Json(result);
        // The in-memory curated library always carries little-cloud.
        Assert.Contains(InMemoryCuratedStoryLibrary.LittleCloudId, json);
    }

    [Fact]
    public void Stories_MarksConfiguredDefault()
    {
        var db = NewDb();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Story:DefaultStoryId", InMemoryCuratedStoryLibrary.LittleCloudId),
        }).Build();
        var result = NewController(db, config: config).Stories();
        using var doc = JsonDocument.Parse(Json(result));
        var arr = doc.RootElement.GetProperty("stories");
        Assert.Equal(InMemoryCuratedStoryLibrary.LittleCloudId, arr[0].GetProperty("id").GetString()); // default sorts first
        Assert.True(arr[0].GetProperty("isDefault").GetBoolean());
        for (var i = 1; i < arr.GetArrayLength(); i++)
            Assert.False(arr[i].GetProperty("isDefault").GetBoolean());
    }

    // ── Story-QA tuning playground (Phase 2) ───────────────────────

    private sealed class FixedAi : IAiChatClient
    {
        private readonly string _answer;
        public FixedAi(string answer) => _answer = answer;
        public Task<string> GetCompletionAsync(string systemPrompt, List<(string Role, string Content)> messages)
            => Task.FromResult(_answer);
    }

    [Fact]
    public async Task StoryQaTest_SafeInput_RunsPipeline_ReturnsAnswer()
    {
        var db = NewDb();
        // A clean, short Armenian answer grounded in the little-cloud story.
        var ai = new FixedAi("Փոքրիկ ամպիկը երկնքի ընկերն է։");
        var controller = NewController(db, ai);

        var result = await controller.StoryQaTest(
            new AdminStoryQaTestRequest(InMemoryCuratedStoryLibrary.LittleCloudId, 0, "Ո՞վ է փոքրիկ ամպիկը"),
            default);

        var dto = Value<AdminStoryQaTestResult>(result);
        Assert.True(dto.InputSafe);
        Assert.False(string.IsNullOrWhiteSpace(dto.Answer));
        // Either the model answer passed the filter (answered) or it fell back —
        // both are valid pipeline outcomes; the point is the pipeline ran safely.
        Assert.Contains(dto.Outcome, new[] { "answered", "answer_fallback" });
    }

    [Fact]
    public async Task StoryQaTest_UnsafeInput_BlocksBeforeGpt_ReturnsFallback()
    {
        var db = NewDb();
        var blocking = Substitute.For<IModerationService>();
        blocking.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: false, FlaggedCategories: new List<string> { "violence" }));
        // If GPT were ever called the FixedAi would answer — but input
        // moderation must short-circuit first.
        var ai = new FixedAi("should not be used");
        var controller = NewController(db, ai, blocking);

        var result = await controller.StoryQaTest(
            new AdminStoryQaTestRequest(InMemoryCuratedStoryLibrary.LittleCloudId, 0, "ինչ-որ վտանգավոր բան"),
            default);

        var dto = Value<AdminStoryQaTestResult>(result);
        Assert.False(dto.InputSafe);
        Assert.Equal("input_blocked", dto.Outcome);
        Assert.Equal(StoryAnswerFilter.SafeFallback, dto.Answer);
        Assert.DoesNotContain("should not be used", dto.Answer);
    }

    [Fact]
    public async Task StoryQaTest_UnknownStory_Returns404()
    {
        var db = NewDb();
        var result = await NewController(db).StoryQaTest(
            new AdminStoryQaTestRequest("no-such-story", 0, "բարև"), default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task StoryQaTest_EmptyQuestion_Returns400()
    {
        var db = NewDb();
        var result = await NewController(db).StoryQaTest(
            new AdminStoryQaTestRequest(InMemoryCuratedStoryLibrary.LittleCloudId, 0, "   "), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task StoryQaTest_UpstreamError_Returns502_WithoutLeakingDetail()
    {
        // #059: an upstream failure must NOT echo the raw exception text on the
        // wire. A faulted moderation call drives the catch.
        var db = NewDb();
        var throwing = Substitute.For<IModerationService>();
        throwing.CheckContentAsync(Arg.Any<string>())
            .Returns(Task.FromException<ModerationResult>(
                new InvalidOperationException("SECRET-OPENAI-DETAIL-xyz")));

        var result = await NewController(db, moderation: throwing).StoryQaTest(
            new AdminStoryQaTestRequest(InMemoryCuratedStoryLibrary.LittleCloudId, 0, "բարև"), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, obj.StatusCode);
        Assert.DoesNotContain("SECRET-OPENAI-DETAIL-xyz",
            JsonSerializer.Serialize(obj.Value));
    }

    // ── #013: per-operator access audit on content reads ───────────

    [Fact]
    public async Task ConversationDetail_WritesAccessAudit_WithOperator_AndNullParentActor()
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        var conv = new Conversation { Id = Guid.NewGuid(), DeviceId = d.Id, StartedAt = DateTime.UtcNow };
        db.Conversations.Add(conv);
        db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, Role = MessageRole.User, Content = "hi", Timestamp = DateTime.UtcNow, SafetyFlag = SafetyFlag.Clean });
        await db.SaveChangesAsync();

        var controller = NewController(db);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items["InternalOperator"] = "alice-ops";

        await controller.ConversationDetail(conv.Id, default);

        var audit = await db.AuditEvents.SingleAsync(a => a.EventType == AuditEventType.InternalConsoleAccess);
        Assert.Null(audit.ActorParentId);                  // operator, not a parent -> invisible to parent feeds
        Assert.Contains("alice-ops", audit.Metadata!);     // identity is traceable / revocable (#012)
        Assert.Contains("conversation-detail", audit.Metadata!);
    }

    // ── Phase 3: reversible operator device actions ─────────────────

    private static InternalController OpController(AppDbContext db, string op = "alice-ops")
    {
        var c = NewController(db);
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        c.HttpContext.Items["InternalOperator"] = op;
        return c;
    }

    [Fact]
    public void WhoAmI_ReturnsResolvedOperatorIdentity()
    {
        var ok = Assert.IsType<OkObjectResult>(OpController(NewDb(), "carol-ops").WhoAmI());
        Assert.Contains("carol-ops", JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public async Task RevokeDevice_FlipsFlag_AndWritesActionAudit_WithOperatorAndReason()
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        await db.SaveChangesAsync();

        var res = await OpController(db).RevokeDevice(
            d.Id, new InternalDeviceActionRequest(true, "lost toy reported"), default);

        Assert.IsType<OkObjectResult>(res);
        Assert.True((await db.Devices.FindAsync(d.Id))!.IsRevoked);
        var audit = await db.AuditEvents.SingleAsync(a => a.EventType == AuditEventType.InternalConsoleAction);
        Assert.Null(audit.ActorParentId);            // operator -> invisible to parent feeds
        Assert.Equal(d.Id, audit.TargetDeviceId);    // but the device IS queryable
        Assert.Contains("alice-ops", audit.Metadata!);
        Assert.Contains("lost toy reported", audit.Metadata!);
        Assert.Contains("device_revoke", audit.Metadata!);
    }

    [Fact]
    public async Task RevokeDevice_MissingReason_Returns400_NoChange_NoAudit()
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        await db.SaveChangesAsync();

        var res = await OpController(db).RevokeDevice(
            d.Id, new InternalDeviceActionRequest(true, "   "), default);

        Assert.IsType<BadRequestObjectResult>(res);
        Assert.False((await db.Devices.FindAsync(d.Id))!.IsRevoked);
        Assert.False(await db.AuditEvents.AnyAsync());
    }

    [Fact]
    public async Task RevokeDevice_UnknownDevice_Returns404()
    {
        var db = NewDb();
        var res = await OpController(db).RevokeDevice(
            Guid.NewGuid(), new InternalDeviceActionRequest(true, "x"), default);
        Assert.IsType<NotFoundObjectResult>(res);
    }

    [Fact]
    public async Task RevokeDevice_Idempotent_AlreadyRevoked_WritesNoAudit()
    {
        var db = NewDb();
        var d = Dev();
        d.IsRevoked = true;
        db.Devices.Add(d);
        await db.SaveChangesAsync();

        await OpController(db).RevokeDevice(
            d.Id, new InternalDeviceActionRequest(true, "again"), default);

        Assert.False(await db.AuditEvents.AnyAsync()); // no flip -> no audit
    }

    [Fact]
    public async Task PauseDeviceAction_FlipsFlag_AndWritesAudit()
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        await db.SaveChangesAsync();

        await OpController(db, "bob-ops").PauseDeviceAction(
            d.Id, new InternalDeviceActionRequest(true, "parent asked"), default);

        Assert.True((await db.Devices.FindAsync(d.Id))!.IsPaused);
        var audit = await db.AuditEvents.SingleAsync(a => a.EventType == AuditEventType.InternalConsoleAction);
        Assert.Contains("device_pause", audit.Metadata!);
        Assert.Contains("bob-ops", audit.Metadata!);
    }
}
