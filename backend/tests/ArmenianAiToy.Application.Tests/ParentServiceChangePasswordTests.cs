using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>ParentService.ChangePasswordAsync</c> (B1). The contract:
/// returns <c>true</c> only when the current password verifies against the
/// stored BCrypt hash; on any failure path (wrong password, unknown parent)
/// returns <c>false</c> without mutating the stored hash.
/// </summary>
public class ParentServiceChangePasswordTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Parent>().HasKey(p => p.Id);
            modelBuilder.Entity<Parent>().Ignore(p => p.ParentDevices);
            modelBuilder.Entity<AuditEvent>().HasKey(a => a.Id);
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
        var logger = Substitute.For<ILogger<ParentService>>();
        return (new ParentService(db, config, logger), db);
    }

    private static async Task<Guid> SeedParentAsync(TestDbContext db, string password)
    {
        var id = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = id,
            Email = "existing@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task ChangePasswordAsync_CorrectCurrentPassword_UpdatesHash()
    {
        var (service, db) = CreateService();
        var parentId = await SeedParentAsync(db, "oldPassword123");

        var result = await service.ChangePasswordAsync(parentId, "oldPassword123", "newPassword456");

        Assert.True(result);
        var updated = await db.Set<Parent>().FindAsync(parentId);
        Assert.NotNull(updated);
        Assert.True(BCrypt.Net.BCrypt.Verify("newPassword456", updated!.PasswordHash),
            "new password should verify against the stored hash after change");
        Assert.False(BCrypt.Net.BCrypt.Verify("oldPassword123", updated.PasswordHash),
            "old password must no longer verify");
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ReturnsFalseAndDoesNotMutate()
    {
        var (service, db) = CreateService();
        var parentId = await SeedParentAsync(db, "oldPassword123");
        var originalHash = (await db.Set<Parent>().FindAsync(parentId))!.PasswordHash;

        var result = await service.ChangePasswordAsync(parentId, "wrongCurrent", "newPassword456");

        Assert.False(result);
        var unchanged = await db.Set<Parent>().FindAsync(parentId);
        Assert.Equal(originalHash, unchanged!.PasswordHash);
    }

    [Fact]
    public async Task ChangePasswordAsync_UnknownParent_ReturnsFalse()
    {
        var (service, _) = CreateService();

        var result = await service.ChangePasswordAsync(
            Guid.NewGuid(), "anything", "newPassword456");

        Assert.False(result);
    }

    [Fact]
    public async Task ChangePasswordAsync_Success_WritesAuditRow()
    {
        var (service, db) = CreateService();
        var parentId = await SeedParentAsync(db, "oldPassword123");

        Assert.True(await service.ChangePasswordAsync(parentId, "oldPassword123", "newPassword456"));

        var audits = await db.Set<AuditEvent>().ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(AuditEventType.ParentPasswordChanged, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Null(audit.TargetDeviceId);
        Assert.Null(audit.TargetChildId);
        Assert.Null(audit.Metadata);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongPassword_DoesNotWriteAuditRow()
    {
        // Wrong-password failures are deliberately NOT audited in slice 1.
        var (service, db) = CreateService();
        var parentId = await SeedParentAsync(db, "oldPassword123");

        Assert.False(await service.ChangePasswordAsync(parentId, "wrong", "newPassword456"));

        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }
}
