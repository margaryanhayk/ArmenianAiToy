using System.Reflection;
using System.Text.Json;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
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
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using System.Security.Claims;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>GET /api/parents/export</c>. Wires the real
/// <c>ParentService</c> over SQLite in-memory (same pattern as the C2/C3
/// and audit read tests) so the scope filter, credential exclusions,
/// cooldown guard, and audit-write behaviour are exercised end-to-end.
/// </summary>
public class ParentControllerExportTests
{
    private static async Task<(ParentController Controller, AppDbContext Db, SqliteConnection Conn, ExportCooldown Cooldown, FakeTimeProvider Clock)>
        CreateControllerAsync(Guid parentId, TimeSpan? cooldownWindow = null)
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
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero));
        var cooldown = new ExportCooldown(
            cooldownWindow ?? TimeSpan.FromSeconds(60), clock);
        var controller = new ParentController(service, cooldown);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parentId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, db, conn, cooldown, clock);
    }

    private static Guid SeedParent(AppDbContext db, string? email = null)
    {
        var id = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = id,
            Email = email ?? ("p-" + Guid.NewGuid().ToString("N")[..8] + "@example.com"),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("secretsauce_hash_marker_xyz"),
            RegisteredAt = DateTime.UtcNow,
            TermsAcceptedAt = DateTime.UtcNow,
            TermsVersion = ParentService.CurrentTermsVersion
        });
        return id;
    }

    private static (Guid DeviceId, Guid ChildId, Guid ConvId, Guid MsgId)
        SeedDeviceWithChildAndConversation(AppDbContext db, string? apiKeyMarker = null)
    {
        var deviceId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        db.Set<Device>().Add(new Device
        {
            Id = deviceId,
            MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Test",
            // Devices.ApiKey has a unique index — default to a per-call
            // random marker so a test can seed multiple devices without
            // tripping the constraint. Pass an explicit marker when a
            // test asserts exclusion of a specific substring.
            ApiKey = apiKeyMarker ?? ("dtk_test_" + Guid.NewGuid().ToString("N")),
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        db.Set<Child>().Add(new Child
        {
            Id = childId,
            Name = "Alice",
            DeviceId = deviceId,
            Gender = Gender.Girl
        });
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId,
            DeviceId = deviceId,
            ChildId = childId,
            StartedAt = DateTime.UtcNow
        });
        db.Set<Message>().Add(new Message
        {
            Id = msgId,
            ConversationId = convId,
            Role = MessageRole.User,
            Content = "hello",
            Timestamp = DateTime.UtcNow,
            SafetyFlag = SafetyFlag.Clean
        });
        return (deviceId, childId, convId, msgId);
    }

    [Fact]
    public void Export_IsProtectedByAuthorizeAttribute()
    {
        // Controller-level unit tests bypass the ASP.NET auth middleware,
        // so "anonymous => 401" is proven declaratively by verifying the
        // [Authorize] attribute is attached to the action — same shape
        // AuditControllerTests uses for GetAudit.
        var method = typeof(ParentController).GetMethod(nameof(ParentController.Export));
        Assert.NotNull(method);
        var attrs = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);
        Assert.NotEmpty(attrs);
    }

    [Fact]
    public async Task Export_IncludesOnlyAuthenticatedParentsOwnScope()
    {
        var parentId = Guid.NewGuid();
        var otherParentId = Guid.NewGuid();
        var (controller, db, conn, _, _) = await CreateControllerAsync(parentId);
        await using var _conn = conn;
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "me@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow
        });
        db.Set<Parent>().Add(new Parent
        {
            Id = otherParentId,
            Email = "other@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow
        });
        var (myDeviceId, _, _, _) = SeedDeviceWithChildAndConversation(db);
        var (otherDeviceId, _, otherConvId, _) = SeedDeviceWithChildAndConversation(db);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = myDeviceId, LinkedAt = DateTime.UtcNow
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = otherParentId, DeviceId = otherDeviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Export();

        var export = GetExport(result);
        Assert.Single(export.Devices);
        Assert.Equal(myDeviceId, export.Devices[0].Id);
        // Other parent's conversation must not leak in anywhere.
        Assert.DoesNotContain(export.Devices.SelectMany(d => d.Conversations), c => c.Id == otherConvId);
    }

    [Fact]
    public async Task Export_DoesNotIncludeParentPasswordHashOrDeviceApiKey()
    {
        // Uses unique marker strings baked into the seeded rows so a
        // raw-substring scan of the serialized response is a reliable
        // way to assert neither field is surfaced, independent of
        // whatever DTO shape someone might refactor to later.
        const string PasswordHashMarker = "$2a$"; // BCrypt prefix
        const string ApiKeyMarker = "dtk_export_test_unique_marker";
        var parentId = Guid.NewGuid();
        var (controller, db, conn, _, _) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "me@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow
        });
        var (deviceId, _, _, _) = SeedDeviceWithChildAndConversation(db, apiKeyMarker: ApiKeyMarker);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Export();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, JsonSerializerOptions.Web);
        Assert.DoesNotContain(PasswordHashMarker, json, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKeyMarker, json, StringComparison.Ordinal);
        // The excludedFields array should document both omissions inline.
        Assert.Contains("Parent.PasswordHash", json, StringComparison.Ordinal);
        Assert.Contains("Device.ApiKey", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_ParentProfileIncludesLastLoginAt()
    {
        // The parent-activity signal slice added LastLoginAt to the
        // Parent entity and to ParentExportProfile. This test pins:
        //   - a never-logged-in parent exports LastLoginAt as null
        //   - a parent with a stamped LastLoginAt exports that exact
        //     value (tick-accurate; the export does not re-compute or
        //     normalize the timestamp).
        var neverLoggedInId = Guid.NewGuid();
        var stampedId = Guid.NewGuid();
        var stamp = new DateTime(2026, 4, 15, 9, 30, 45, DateTimeKind.Utc);

        // Never-logged-in parent path.
        {
            var (controller, db, conn, _, _) = await CreateControllerAsync(neverLoggedInId);
            await using var _conn = conn;
            db.Set<Parent>().Add(new Parent
            {
                Id = neverLoggedInId,
                Email = "never@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
                RegisteredAt = DateTime.UtcNow,
                LastLoginAt = null
            });
            await db.SaveChangesAsync();

            var export = GetExport(await controller.Export());
            Assert.Null(export.Parent.LastLoginAt);
        }

        // Stamped parent path.
        {
            var (controller, db, conn, _, _) = await CreateControllerAsync(stampedId);
            await using var _conn = conn;
            db.Set<Parent>().Add(new Parent
            {
                Id = stampedId,
                Email = "stamped@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
                RegisteredAt = DateTime.UtcNow,
                LastLoginAt = stamp
            });
            await db.SaveChangesAsync();

            var export = GetExport(await controller.Export());
            Assert.Equal(stamp, export.Parent.LastLoginAt);
        }
    }

    [Fact]
    public async Task Export_SuccessWritesParentDataExportedAuditRow()
    {
        var parentId = Guid.NewGuid();
        var (controller, db, conn, _, _) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "me@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow
        });
        var (deviceId, _, _, _) = SeedDeviceWithChildAndConversation(db);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Export();
        Assert.IsType<OkObjectResult>(result);

        var audits = await db.Set<AuditEvent>()
            .Where(a => a.ActorParentId == parentId)
            .ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(AuditEventType.ParentDataExported, audit.EventType);
        Assert.Null(audit.TargetDeviceId);
        Assert.Null(audit.TargetChildId);
        Assert.NotNull(audit.Metadata);
    }

    [Fact]
    public async Task Export_AuditMetadataContainsCountsOnlyNoPii()
    {
        var parentId = Guid.NewGuid();
        var (controller, db, conn, _, _) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        const string EmailMarker = "pii-email-marker@example.com";
        const string ChildNameMarker = "piiChildNameMarkerZZZ";
        const string MessageMarker = "piiMessageContentMarkerZZZ";
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = EmailMarker,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow
        });
        var deviceId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        db.Set<Device>().Add(new Device
        {
            Id = deviceId,
            MacAddress = "mac-pii-test",
            Name = "Test",
            ApiKey = "dtk_pii_test",
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        db.Set<Child>().Add(new Child
        {
            Id = childId, Name = ChildNameMarker, DeviceId = deviceId, Gender = Gender.Girl
        });
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId, DeviceId = deviceId, ChildId = childId, StartedAt = DateTime.UtcNow
        });
        db.Set<Message>().Add(new Message
        {
            Id = Guid.NewGuid(), ConversationId = convId, Role = MessageRole.User,
            Content = MessageMarker, Timestamp = DateTime.UtcNow, SafetyFlag = SafetyFlag.Clean
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Export();
        Assert.IsType<OkObjectResult>(result);

        var audit = await db.Set<AuditEvent>()
            .SingleAsync(a => a.EventType == AuditEventType.ParentDataExported);
        Assert.NotNull(audit.Metadata);
        var metadata = audit.Metadata!;
        // Positive assertions: counts are present as numeric fields.
        Assert.Contains("\"devices\":1", metadata);
        Assert.Contains("\"children\":1", metadata);
        Assert.Contains("\"conversations\":1", metadata);
        Assert.Contains("\"messages\":1", metadata);
        Assert.Contains("\"audit_events\":", metadata);
        // Negative assertions: no PII should have leaked into the audit blob.
        Assert.DoesNotContain(EmailMarker, metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(ChildNameMarker, metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(MessageMarker, metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_EmptyAccount_SucceedsWithValidEnvelopeAndAuditWithZeroCounts()
    {
        var parentId = Guid.NewGuid();
        var (controller, db, conn, _, _) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "empty@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow,
            TermsAcceptedAt = DateTime.UtcNow,
            TermsVersion = ParentService.CurrentTermsVersion
        });
        await db.SaveChangesAsync();

        var result = await controller.Export();

        var export = GetExport(result);
        Assert.Equal("1", export.SchemaVersion);
        Assert.Equal(parentId, export.Parent.Id);
        Assert.Empty(export.Devices);
        // Audit row written even on empty-account path; counts should all be 0
        // except audit_events (which is 0 too — no prior audit rows existed).
        var audit = await db.Set<AuditEvent>()
            .SingleAsync(a => a.EventType == AuditEventType.ParentDataExported);
        Assert.Contains("\"devices\":0", audit.Metadata);
        Assert.Contains("\"children\":0", audit.Metadata);
        Assert.Contains("\"conversations\":0", audit.Metadata);
        Assert.Contains("\"messages\":0", audit.Metadata);
        Assert.Contains("\"audit_events\":0", audit.Metadata);
    }

    [Fact]
    public async Task Export_RepeatedCallWithinCooldown_Returns429WithRetryAfter()
    {
        var parentId = Guid.NewGuid();
        var (controller, db, conn, _, clock) = await CreateControllerAsync(
            parentId, cooldownWindow: TimeSpan.FromSeconds(60));
        await using var _ = conn;
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "cd@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // First call succeeds.
        var first = await controller.Export();
        Assert.IsType<OkObjectResult>(first);

        // Second call within the cooldown window — short-circuited.
        clock.Advance(TimeSpan.FromSeconds(5));
        var second = await controller.Export();
        var rejected = Assert.IsType<ObjectResult>(second);
        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.StatusCode);
        Assert.True(controller.Response.Headers.ContainsKey("Retry-After"));

        // After advancing past the cooldown, a new export succeeds.
        clock.Advance(TimeSpan.FromSeconds(60));
        var third = await controller.Export();
        Assert.IsType<OkObjectResult>(third);
    }

    [Fact]
    public async Task Export_ContentDispositionUsesTimestampOnlyFilename()
    {
        var parentId = Guid.NewGuid();
        var (controller, db, conn, _, _) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        const string EmailMarker = "secret-leak@example.com";
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = EmailMarker,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Export();
        Assert.IsType<OkObjectResult>(result);

        var header = controller.Response.Headers.ContentDisposition.ToString();
        Assert.Contains("attachment;", header);
        Assert.Contains("areg-export-", header);
        // Filename must not encode the parent's email or any PII.
        Assert.DoesNotContain(EmailMarker, header, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- helpers ----------

    // Pulls the ParentExport out of the OkObjectResult. The controller
    // returns the DTO directly (Ok(export)) so a cast is enough — no
    // anonymous-type reflection gymnastics required.
    private static ParentExport GetExport(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<ParentExport>(ok.Value);
    }

    // --- GoogleSubject surfacing -------------------------------------

    [Fact]
    public async Task Export_PasswordOnlyParent_GoogleSubjectIsNull()
    {
        // Password-registered parent has no Google link; the exported
        // profile must carry GoogleSubject explicitly as null — not
        // missing, not empty — so downstream consumers reading the
        // field get the expected nullable-string shape.
        var parentId = Guid.NewGuid();
        var (controller, db, conn, _, _) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "password-only@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Export();

        var export = GetExport(result);
        Assert.Null(export.Parent.GoogleSubject);
        // excludedFields contract must remain exactly the shipped pair
        // even with the new GoogleSubject field added. Pinning this
        // makes a future "let's put GoogleSubject back in exclusions"
        // edit fail loudly.
        Assert.Equal(
            new[] { "Parent.PasswordHash", "Device.ApiKey", "Device.ApiKeyHash" },
            export.ExcludedFields);
    }

    // --- #035 audio DSAR disclosure ----------------------------------

    [Fact]
    public async Task Export_DisclosesAudioOmission_WithAccessPath()
    {
        // #035 — voice recordings are not embedded in the JSON; the export
        // must DISCLOSE that (GDPR Art.15/20 honesty) and point to the
        // assistant-audio access path, rather than silently dropping it.
        var parentId = Guid.NewGuid();
        var (controller, db, conn, _, _) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "dsar@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var export = GetExport(await controller.Export());

        Assert.NotNull(export.AudioDisclosure);
        Assert.Contains("/api/parents/messages/", export.AudioDisclosure.AssistantAudioEndpoint);
        Assert.False(string.IsNullOrWhiteSpace(export.AudioDisclosure.Note));
        Assert.False(string.IsNullOrWhiteSpace(export.AudioDisclosure.ChildAudioStatus));
        // The disclosure is additive — the credential-exclusion contract is intact.
        Assert.Equal(
            new[] { "Parent.PasswordHash", "Device.ApiKey", "Device.ApiKeyHash" },
            export.ExcludedFields);
    }

    // --- #067 retention disclosure -----------------------------------

    [Fact]
    public async Task Export_DisclosesDataRetentionPolicy()
    {
        // #067 — the export must surface the retention policy ("deleted after
        // N days") so a parent has storage-limitation transparency. The test
        // config has no Retention override, so it resolves to the shipped
        // default (enabled, 90 days).
        var parentId = Guid.NewGuid();
        var (controller, db, conn, _, _) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "retention@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var export = GetExport(await controller.Export());

        Assert.NotNull(export.DataRetention);
        Assert.True(export.DataRetention.Enabled);
        Assert.Equal(90, export.DataRetention.MessageRetentionDays);
        Assert.False(string.IsNullOrWhiteSpace(export.DataRetention.Description));
    }

    [Fact]
    public async Task Export_GoogleLinkedParent_GoogleSubjectIsSurfaced()
    {
        // Parent row with a stamped GoogleSubject — the export body
        // must round-trip the exact value. Verified via both the DTO
        // assertion and a JSON-substring check, so a refactor to a
        // different export shape would still be caught.
        const string SubjectMarker = "1234567890-gsub-export-test";
        var parentId = Guid.NewGuid();
        var (controller, db, conn, _, _) = await CreateControllerAsync(parentId);
        await using var _ = conn;
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "linked@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw12345678"),
            GoogleSubject = SubjectMarker,
            EmailVerifiedAt = DateTime.UtcNow.AddDays(-1),
            RegisteredAt = DateTime.UtcNow.AddDays(-10)
        });
        await db.SaveChangesAsync();

        var result = await controller.Export();

        var export = GetExport(result);
        Assert.Equal(SubjectMarker, export.Parent.GoogleSubject);

        var json = System.Text.Json.JsonSerializer.Serialize(
            export, System.Text.Json.JsonSerializerOptions.Web);
        Assert.Contains(SubjectMarker, json, StringComparison.Ordinal);
        // Negative: neither credential-material marker leaks alongside.
        Assert.DoesNotContain("$2a$", json, StringComparison.Ordinal);
    }
}
