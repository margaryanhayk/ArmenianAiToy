using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Multi-toy labeling — ParentService.SetDeviceNameAsync: ownership-gated
/// rename, name validation (trim / empty / over-length), idempotent no-op, and
/// the audit row (which records the action only, never the chosen name).
/// </summary>
public class ParentServiceSetDeviceNameTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Parent>().HasKey(p => p.Id);
            b.Entity<Parent>().Ignore(p => p.ParentDevices);
            b.Entity<Device>().HasKey(d => d.Id);
            b.Entity<Device>().Ignore(d => d.Conversations);
            b.Entity<Device>().Ignore(d => d.ParentDevices);
            b.Entity<ParentDevice>().HasKey(pd => new { pd.ParentId, pd.DeviceId });
            b.Entity<AuditEvent>().HasKey(a => a.Id);
        }
    }

    private static (ParentService Service, TestDbContext Db) CreateService()
    {
        var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new ParentService(db, Substitute.For<IConfiguration>(),
            Substitute.For<ILogger<ParentService>>()), db);
    }

    private static (Guid ParentId, Guid DeviceId) SeedLinked(TestDbContext db, string name = "Toy-1234")
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent { Id = parentId, Email = "p@x.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow });
        db.Set<Device>().Add(new Device
        {
            Id = deviceId, MacAddress = "aa:bb", Name = name,
            RegisteredAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
        });
        db.Set<ParentDevice>().Add(new ParentDevice { ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow });
        db.SaveChanges();
        return (parentId, deviceId);
    }

    [Fact]
    public async Task OwnedDevice_Renames_Persists_AndAudits()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinked(db);

        Assert.True(await service.SetDeviceNameAsync(parentId, deviceId, "  Anna's toy  "));

        var device = await db.Set<Device>().FindAsync(deviceId);
        Assert.Equal("Anna's toy", device!.Name);                 // trimmed
        var audit = Assert.Single(await db.Set<AuditEvent>().ToListAsync());
        Assert.Equal(AuditEventType.ParentDeviceRenamed, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
        // The chosen name must NOT appear in the audit metadata (PII discipline).
        Assert.DoesNotContain("Anna", audit.Metadata ?? "");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyName_Fails_NoMutation_NoAudit(string name)
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinked(db, name: "Original");

        Assert.False(await service.SetDeviceNameAsync(parentId, deviceId, name));
        Assert.Equal("Original", (await db.Set<Device>().FindAsync(deviceId))!.Name);
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task OverLengthName_Fails()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinked(db, name: "Original");
        var tooLong = new string('x', 61);   // > MaxLength (60)

        Assert.False(await service.SetDeviceNameAsync(parentId, deviceId, tooLong));
        Assert.Equal("Original", (await db.Set<Device>().FindAsync(deviceId))!.Name);
    }

    [Fact]
    public async Task NotLinked_Fails_NoMutation_NoAudit()
    {
        var (service, db) = CreateService();
        var (_, deviceId) = SeedLinked(db, name: "Original");

        Assert.False(await service.SetDeviceNameAsync(Guid.NewGuid(), deviceId, "Hijack"));
        Assert.Equal("Original", (await db.Set<Device>().FindAsync(deviceId))!.Name);
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task SameName_Idempotent_NoAudit()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinked(db, name: "Anna's toy");

        Assert.True(await service.SetDeviceNameAsync(parentId, deviceId, "Anna's toy"));
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }
}
