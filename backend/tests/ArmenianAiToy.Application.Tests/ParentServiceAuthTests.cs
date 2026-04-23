using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for ParentService authentication operations:
/// RegisterAsync, LoginAsync, LinkDeviceAsync.
/// Uses EF Core InMemory.
/// </summary>
public class ParentServiceAuthTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Parent>().HasKey(p => p.Id);
            modelBuilder.Entity<Parent>().Ignore(p => p.ParentDevices);

            modelBuilder.Entity<Device>().HasKey(d => d.Id);
            modelBuilder.Entity<Device>().Ignore(d => d.Conversations);
            modelBuilder.Entity<Device>().Ignore(d => d.ParentDevices);

            modelBuilder.Entity<ParentDevice>().HasKey(pd => new { pd.ParentId, pd.DeviceId });
            modelBuilder.Entity<AuditEvent>().HasKey(a => a.Id);
        }
    }

    private static (ParentService Service, TestDbContext Db) CreateService(
        string? jwtKey = "TestSecretKeyThatIsLongEnoughForHmacSha256Validation!")
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(options);
        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns(jwtKey);
        config["Jwt:Issuer"].Returns("TestIssuer");
        config["Jwt:Audience"].Returns("TestAudience");
        var logger = Substitute.For<ILogger<ParentService>>();
        return (new ParentService(db, config, logger), db);
    }

    // --- RegisterAsync ---

    [Fact]
    public async Task RegisterAsync_Success_PersistsRowAndConsent()
    {
        // Post-anti-enumeration slice: RegisterAsync no longer returns
        // the minted id (so the id itself cannot be used as an
        // enumeration signal on the wire). The happy-path contract is
        // therefore "the row exists with the expected fields" rather
        // than "the returned id is a valid Guid." Consent fields
        // (TermsAcceptedAt, TermsVersion) and the BCrypt hash are still
        // assertable directly on the persisted row.
        var (service, db) = CreateService();
        var before = DateTime.UtcNow;

        await service.RegisterAsync("test@example.com", "password123", acceptedTerms: true);

        var parent = await db.Set<Parent>().FirstOrDefaultAsync(p => p.Email == "test@example.com");
        Assert.NotNull(parent);
        Assert.NotEqual("password123", parent!.PasswordHash); // hashed, not plaintext
        Assert.True(BCrypt.Net.BCrypt.Verify("password123", parent.PasswordHash));
        // C1: consent fields recorded on success.
        Assert.NotNull(parent.TermsAcceptedAt);
        Assert.True(parent.TermsAcceptedAt >= before && parent.TermsAcceptedAt <= DateTime.UtcNow);
        Assert.Equal(ParentService.CurrentTermsVersion, parent.TermsVersion);
    }

    [Fact]
    public async Task RegisterAsync_TermsNotAccepted_ThrowsAndDoesNotPersist()
    {
        // C1 service-level guard: even if a caller somehow reaches the
        // service with acceptedTerms=false (e.g. a test harness or a future
        // non-controller entry point), the service must refuse and leave
        // the Parents table untouched.
        var (service, db) = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RegisterAsync("test@example.com", "password123", acceptedTerms: false));

        Assert.Contains("Terms must be accepted", ex.Message);
        Assert.Equal(0, await db.Set<Parent>().CountAsync());
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_IsSilentNoOpAndDoesNotMutateExistingRow()
    {
        // Post-anti-enumeration slice: a duplicate email must not throw,
        // must not create a second row, and must not mutate the
        // existing row (in particular its PasswordHash — overwriting it
        // would be a silent account takeover). The ONLY observable
        // effect of the collision path is a server-side log line (no
        // email) and a discarded hash computation.
        var (service, db) = CreateService();
        var existingId = Guid.NewGuid();
        var originalHash = BCrypt.Net.BCrypt.HashPassword("original-pass");
        var originalRegisteredAt = DateTime.UtcNow.AddDays(-3);
        db.Set<Parent>().Add(new Parent
        {
            Id = existingId,
            Email = "existing@example.com",
            PasswordHash = originalHash,
            RegisteredAt = originalRegisteredAt
        });
        await db.SaveChangesAsync();

        // Must NOT throw.
        await service.RegisterAsync("existing@example.com", "attacker-guess", acceptedTerms: true);

        // Exactly one row for the email — no duplicate inserted.
        Assert.Equal(1, await db.Set<Parent>().CountAsync(p => p.Email == "existing@example.com"));

        // Existing row is untouched in every observable field.
        var parent = await db.Set<Parent>().FindAsync(existingId);
        Assert.NotNull(parent);
        Assert.Equal(existingId, parent!.Id);
        Assert.Equal("existing@example.com", parent.Email);
        Assert.Equal(originalHash, parent.PasswordHash);
        Assert.Equal(originalRegisteredAt, parent.RegisteredAt);
    }

    // --- LoginAsync ---

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsToken()
    {
        var (service, db) = CreateService();
        db.Set<Parent>().Add(new Parent
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpass"),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.LoginAsync("user@example.com", "correctpass");

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.Token));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsNull()
    {
        var (service, db) = CreateService();
        db.Set<Parent>().Add(new Parent
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpass"),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.LoginAsync("user@example.com", "wrongpass");

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_NonExistentEmail_ReturnsNull()
    {
        var (service, _) = CreateService();

        var result = await service.LoginAsync("nobody@example.com", "anypass");

        Assert.Null(result);
    }

    // --- LoginAsync LastLoginAt stamp (parent-activity signal slice) ---

    [Fact]
    public async Task LoginAsync_Success_StampsLastLoginAt()
    {
        // Pins the canonical parent-activity signal contract: a
        // successful credentials-plus-JWT exchange overwrites
        // Parent.LastLoginAt with DateTime.UtcNow and persists. Failed
        // branches (asserted in the sibling tests below) must never
        // touch the column.
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpass"),
            RegisteredAt = DateTime.UtcNow.AddDays(-30),
            LastLoginAt = null // never logged in before this call
        });
        await db.SaveChangesAsync();
        var before = DateTime.UtcNow;

        var result = await service.LoginAsync("user@example.com", "correctpass");

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.Token)); // response shape unchanged
        var stamped = await db.Set<Parent>()
            .AsNoTracking()
            .FirstAsync(p => p.Id == parentId);
        Assert.NotNull(stamped.LastLoginAt);
        Assert.InRange(stamped.LastLoginAt!.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_DoesNotStampLastLoginAt()
    {
        // Failed-BCrypt path must leave LastLoginAt untouched. A
        // pre-existing stamp proves "was this specific row mutated,"
        // which a brute-force attacker probing this email would try to
        // exploit to mask activity or force a write storm.
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        var preExistingStamp = DateTime.UtcNow.AddDays(-5);
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpass"),
            RegisteredAt = DateTime.UtcNow.AddDays(-30),
            LastLoginAt = preExistingStamp
        });
        await db.SaveChangesAsync();

        var result = await service.LoginAsync("user@example.com", "wrongpass");

        Assert.Null(result);
        var unchanged = await db.Set<Parent>()
            .AsNoTracking()
            .FirstAsync(p => p.Id == parentId);
        Assert.Equal(preExistingStamp, unchanged.LastLoginAt);
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_DoesNotCreateOrStampAnything()
    {
        // Unknown-email path must short-circuit before any DB write.
        // A regression that accidentally created a Parent row on
        // unknown-email would both leak account-existence (via a
        // follow-up Parents count) and silently populate LastLoginAt
        // for an account the caller does not own.
        var (service, db) = CreateService();

        var result = await service.LoginAsync("nobody@example.com", "anypass");

        Assert.Null(result);
        Assert.Equal(0, await db.Set<Parent>().CountAsync());
    }

    [Fact]
    public async Task LoginAsync_Success_DoesNotWriteAuditRow()
    {
        // Login stays out of audit scope (CLAUDE.md § Audit events). The
        // LastLoginAt stamp is the ONLY side effect of a successful
        // login; a regression that accidentally wired
        // `TrackAndAddAudit` into LoginAsync would re-open the audit
        // write-path for an endpoint that has deliberately stayed out.
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpass"),
            RegisteredAt = DateTime.UtcNow.AddDays(-30)
        });
        await db.SaveChangesAsync();

        var result = await service.LoginAsync("user@example.com", "correctpass");

        Assert.NotNull(result);
        Assert.Equal(0, await db.Set<AuditEvent>().CountAsync());
    }

    [Fact]
    public async Task LoginAsync_WhenJwtKeyMissing_Throws()
    {
        // A misconfigured instance must not silently fall back to a universal
        // signing secret. Valid credentials reach GenerateJwt, which fails fast.
        var (service, db) = CreateService(jwtKey: null);
        db.Set<Parent>().Add(new Parent
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpass"),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LoginAsync("user@example.com", "correctpass"));
        Assert.Contains("Jwt:Key", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_WhenJwtKeyIsLegacyDefault_Throws()
    {
        // The legacy default literal is publicly known (shipped in history);
        // explicitly reject it so a paste from an old appsettings.json fails.
        var (service, db) = CreateService(
            jwtKey: "ArmenianAiToyDefaultSecretKeyThatShouldBeChanged123!");
        db.Set<Parent>().Add(new Parent
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpass"),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LoginAsync("user@example.com", "correctpass"));
        Assert.Contains("legacy default", ex.Message);
    }

    // --- LinkDeviceAsync ---

    [Fact]
    public async Task LinkDeviceAsync_ValidDeviceAndApiKey_ReturnsTrue()
    {
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Test Device",
            ApiKey = "test-api-key-123",
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.Set<Parent>().Add(new Parent { Id = parentId, Email = "p@x.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow });
        db.Set<Device>().Add(device);
        await db.SaveChangesAsync();

        var result = await service.LinkDeviceAsync(parentId, device.Id, "test-api-key-123");

        Assert.True(result);
        var link = await db.Set<ParentDevice>().FindAsync(parentId, device.Id);
        Assert.NotNull(link);
    }

    [Fact]
    public async Task LinkDeviceAsync_BadApiKey_ReturnsFalse()
    {
        var (service, db) = CreateService();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Test Device",
            ApiKey = "correct-key",
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.Set<Device>().Add(device);
        await db.SaveChangesAsync();

        var result = await service.LinkDeviceAsync(Guid.NewGuid(), device.Id, "wrong-key");

        Assert.False(result);
    }

    [Fact]
    public async Task LinkDeviceAsync_AlreadyLinked_ReturnsTrueWithoutDuplicate()
    {
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Test Device",
            ApiKey = "key-123",
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.Set<Parent>().Add(new Parent { Id = parentId, Email = "p@x.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow });
        db.Set<Device>().Add(device);
        db.Set<ParentDevice>().Add(new ParentDevice { ParentId = parentId, DeviceId = device.Id, LinkedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await service.LinkDeviceAsync(parentId, device.Id, "key-123");

        Assert.True(result);
        Assert.Equal(1, await db.Set<ParentDevice>().CountAsync(pd => pd.ParentId == parentId && pd.DeviceId == device.Id));
    }

    [Fact]
    public async Task LinkDeviceAsync_NonExistentDevice_ReturnsFalse()
    {
        var (service, _) = CreateService();

        var result = await service.LinkDeviceAsync(Guid.NewGuid(), Guid.NewGuid(), "any-key");

        Assert.False(result);
    }

    // --- UnlinkDeviceAsync ---

    [Fact]
    public async Task UnlinkDeviceAsync_WhenLinked_RemovesRow_ReturnsTrue()
    {
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var removed = await service.UnlinkDeviceAsync(parentId, deviceId);

        Assert.True(removed);
        Assert.Null(await db.Set<ParentDevice>().FindAsync(parentId, deviceId));
    }

    [Fact]
    public async Task UnlinkDeviceAsync_WhenNotLinked_ReturnsFalse()
    {
        var (service, db) = CreateService();

        var removed = await service.UnlinkDeviceAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(removed);
        // Nothing was added, nothing should have been removed either.
        Assert.Equal(0, await db.Set<ParentDevice>().CountAsync());
    }

    [Fact]
    public async Task UnlinkDeviceAsync_DoesNotRemoveOtherParentsLinks()
    {
        // Shared device linked to two parents. Unlink one — the other must
        // retain their link. Guards against a future refactor that
        // accidentally widens the delete predicate to drop by DeviceId alone.
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var parentA = Guid.NewGuid();
        var parentB = Guid.NewGuid();
        db.Set<ParentDevice>().AddRange(
            new ParentDevice { ParentId = parentA, DeviceId = deviceId, LinkedAt = DateTime.UtcNow },
            new ParentDevice { ParentId = parentB, DeviceId = deviceId, LinkedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var removed = await service.UnlinkDeviceAsync(parentA, deviceId);

        Assert.True(removed);
        Assert.Null(await db.Set<ParentDevice>().FindAsync(parentA, deviceId));
        Assert.NotNull(await db.Set<ParentDevice>().FindAsync(parentB, deviceId));
    }
}
