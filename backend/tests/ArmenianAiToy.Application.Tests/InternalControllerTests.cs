using System.Text.Json;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    private static InternalController NewController(AppDbContext db) =>
        new(db, new InMemoryCuratedStoryLibrary(), new OpenAICostMeter());

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
}
