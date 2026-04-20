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
    public async Task RegisterAsync_Success_ReturnsIdAndPersistsConsent()
    {
        var (service, db) = CreateService();
        var before = DateTime.UtcNow;

        var id = await service.RegisterAsync("test@example.com", "password123", acceptedTerms: true);

        Assert.NotEqual(Guid.Empty, id);
        var parent = await db.Set<Parent>().FindAsync(id);
        Assert.NotNull(parent);
        Assert.Equal("test@example.com", parent!.Email);
        Assert.NotEqual("password123", parent.PasswordHash); // hashed, not plaintext
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
    public async Task RegisterAsync_DuplicateEmail_ThrowsInvalidOperation()
    {
        var (service, db) = CreateService();
        db.Set<Parent>().Add(new Parent
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass"),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RegisterAsync("existing@example.com", "newpass", acceptedTerms: true));

        Assert.Contains("already registered", ex.Message);
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
