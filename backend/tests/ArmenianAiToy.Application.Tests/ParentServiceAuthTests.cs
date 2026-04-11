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

    private static (ParentService Service, TestDbContext Db) CreateService()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(options);
        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
        config["Jwt:Issuer"].Returns("TestIssuer");
        config["Jwt:Audience"].Returns("TestAudience");
        var logger = Substitute.For<ILogger<ParentService>>();
        return (new ParentService(db, config, logger), db);
    }

    // --- RegisterAsync ---

    [Fact]
    public async Task RegisterAsync_Success_ReturnsIdAndPersists()
    {
        var (service, db) = CreateService();

        var id = await service.RegisterAsync("test@example.com", "password123");

        Assert.NotEqual(Guid.Empty, id);
        var parent = await db.Set<Parent>().FindAsync(id);
        Assert.NotNull(parent);
        Assert.Equal("test@example.com", parent!.Email);
        Assert.NotEqual("password123", parent.PasswordHash); // hashed, not plaintext
        Assert.True(BCrypt.Net.BCrypt.Verify("password123", parent.PasswordHash));
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
            () => service.RegisterAsync("existing@example.com", "newpass"));

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
}
