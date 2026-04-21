using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the parent-facing audit read endpoint. Wires the real
/// <c>ParentService</c> over SQLite in-memory (same pattern as the C2/C3
/// service tests) so the ownership filter, ordering, and metadata
/// parsing are exercised end-to-end through the controller.
/// </summary>
public class AuditControllerTests
{
    private static async Task<(AuditController Controller, AppDbContext Db, SqliteConnection Conn)>
        CreateControllerAsync(Guid parentId)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
        var logger = Substitute.For<ILogger<ParentService>>();
        var service = new ParentService(db, config, logger);
        var controller = new AuditController(service);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parentId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, db, conn);
    }

    private static AuditEvent SeedAuditRow(
        AppDbContext db,
        Guid actorParentId,
        DateTime timestamp,
        AuditEventType eventType = AuditEventType.ParentDevicePauseStateChanged,
        Guid? targetDeviceId = null,
        Guid? targetChildId = null,
        string? metadata = "{\"is_paused\":true}")
    {
        var row = new AuditEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = timestamp,
            EventType = eventType,
            ActorParentId = actorParentId,
            TargetDeviceId = targetDeviceId,
            TargetChildId = targetChildId,
            Metadata = metadata
        };
        db.Set<AuditEvent>().Add(row);
        db.SaveChanges();
        return row;
    }

    [Fact]
    public void GetAudit_IsProtectedByAuthorizeAttribute()
    {
        // Controller-level unit tests bypass the ASP.NET auth middleware,
        // so "anonymous => 401" is proven declaratively by verifying the
        // [Authorize] attribute is attached to the action. The integration
        // pipeline (middleware + attribute) rejects unauthenticated calls
        // before they ever reach the action.
        var method = typeof(AuditController).GetMethod(nameof(AuditController.GetAudit));
        Assert.NotNull(method);
        var attrs = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);
        Assert.NotEmpty(attrs);
    }

    [Fact]
    public async Task GetAudit_ReturnsOnlyAuthenticatedParentsOwnRows()
    {
        var parentId = Guid.NewGuid();
        var otherParentId = Guid.NewGuid();
        var (controller, db, conn) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        SeedAuditRow(db, parentId, DateTime.UtcNow.AddMinutes(-1));
        SeedAuditRow(db, parentId, DateTime.UtcNow.AddMinutes(-2));
        SeedAuditRow(db, otherParentId, DateTime.UtcNow.AddMinutes(-3));

        var result = await controller.GetAudit();

        var ok = Assert.IsType<OkObjectResult>(result);
        var events = GetEvents(ok.Value);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.True(e.Id != Guid.Empty));
    }

    [Fact]
    public async Task GetAudit_ReturnsNewestFirst()
    {
        var parentId = Guid.NewGuid();
        var (controller, db, conn) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        var older = SeedAuditRow(db, parentId, DateTime.UtcNow.AddMinutes(-10));
        var newer = SeedAuditRow(db, parentId, DateTime.UtcNow.AddMinutes(-1));
        var middle = SeedAuditRow(db, parentId, DateTime.UtcNow.AddMinutes(-5));

        var result = await controller.GetAudit();

        var ok = Assert.IsType<OkObjectResult>(result);
        var events = GetEvents(ok.Value);
        Assert.Equal(new[] { newer.Id, middle.Id, older.Id }, events.Select(e => e.Id).ToArray());
    }

    [Fact]
    public async Task GetAudit_NegativeOffset_Returns400()
    {
        var parentId = Guid.NewGuid();
        var (controller, _, conn) = await CreateControllerAsync(parentId);
        await using var _conn = conn;

        var result = await controller.GetAudit(limit: 20, offset: -1);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAudit_ZeroLimit_Returns400()
    {
        var parentId = Guid.NewGuid();
        var (controller, _, conn) = await CreateControllerAsync(parentId);
        await using var _conn = conn;

        var result = await controller.GetAudit(limit: 0, offset: 0);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAudit_LargeLimit_IsClampedToOneHundred()
    {
        var parentId = Guid.NewGuid();
        var (controller, db, conn) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        // Seed 150 rows so a clamp to 100 is observable. Timestamps must
        // differ so OrderByDescending is deterministic.
        var baseTime = DateTime.UtcNow.AddHours(-1);
        for (int i = 0; i < 150; i++)
            SeedAuditRow(db, parentId, baseTime.AddSeconds(i));

        var result = await controller.GetAudit(limit: 500, offset: 0);

        var ok = Assert.IsType<OkObjectResult>(result);
        var events = GetEvents(ok.Value);
        Assert.Equal(100, events.Count);
    }

    [Fact]
    public async Task GetAudit_EmptyFeed_Returns200WithEmptyEvents()
    {
        var parentId = Guid.NewGuid();
        var (controller, _, conn) = await CreateControllerAsync(parentId);
        await using var _conn = conn;

        var result = await controller.GetAudit();

        var ok = Assert.IsType<OkObjectResult>(result);
        var events = GetEvents(ok.Value);
        Assert.Empty(events);
    }

    [Fact]
    public async Task GetAudit_MetadataSerializesAsJsonObject_NotAsString()
    {
        var parentId = Guid.NewGuid();
        var (controller, db, conn) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        SeedAuditRow(db, parentId, DateTime.UtcNow,
            metadata: "{\"is_paused\":true}");

        var result = await controller.GetAudit();

        var ok = Assert.IsType<OkObjectResult>(result);
        // Serialize with the Web defaults (camelCase, same config ASP.NET's
        // System.Text.Json formatter uses by default) so the property
        // lookups below match the real wire shape.
        var json = JsonSerializer.Serialize(ok.Value, JsonSerializerOptions.Web);
        using var doc = JsonDocument.Parse(json);
        var metadata = doc.RootElement
            .GetProperty("events")[0]
            .GetProperty("metadata");
        // Serialized shape must be an object whose is_paused field is a
        // JSON boolean, not a quoted string like "{\"is_paused\":true}".
        Assert.Equal(JsonValueKind.Object, metadata.ValueKind);
        Assert.True(metadata.GetProperty("is_paused").GetBoolean());
    }

    [Fact]
    public async Task GetAudit_NullMetadata_SerializesAsJsonNull()
    {
        // ParentPasswordChanged events are written with Metadata == null;
        // these must come back as JSON null, not missing, not empty string.
        var parentId = Guid.NewGuid();
        var (controller, db, conn) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        SeedAuditRow(db, parentId, DateTime.UtcNow,
            eventType: AuditEventType.ParentPasswordChanged,
            metadata: null);

        var result = await controller.GetAudit();

        var ok = Assert.IsType<OkObjectResult>(result);
        // Serialize with the Web defaults (camelCase, same config ASP.NET's
        // System.Text.Json formatter uses by default) so the property
        // lookups below match the real wire shape.
        var json = JsonSerializer.Serialize(ok.Value, JsonSerializerOptions.Web);
        using var doc = JsonDocument.Parse(json);
        var metadata = doc.RootElement
            .GetProperty("events")[0]
            .GetProperty("metadata");
        Assert.Equal(JsonValueKind.Null, metadata.ValueKind);
    }

    [Fact]
    public async Task GetAudit_DoesNotExposeActorParentIdInResponse()
    {
        var parentId = Guid.NewGuid();
        var (controller, db, conn) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        SeedAuditRow(db, parentId, DateTime.UtcNow);

        var result = await controller.GetAudit();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        // Serialization uses default PascalCase properties for anonymous types;
        // guard against both casings defensively.
        Assert.DoesNotContain("ActorParentId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("actorParentId", json, StringComparison.Ordinal);
    }

    // ---------- helpers ----------

    // Pulls the `events` list out of the anonymous `{ events = [...] }`
    // wrapper the controller returns. Uses reflection because the wrapper
    // is an anonymous type; serializing + reparsing would work too but
    // adds noise to the tests that aren't specifically about JSON shape.
    private static List<AuditEventDto> GetEvents(object? value)
    {
        Assert.NotNull(value);
        var prop = value!.GetType().GetProperty("events", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(prop);
        var list = prop!.GetValue(value) as List<AuditEventDto>;
        Assert.NotNull(list);
        return list!;
    }
}
