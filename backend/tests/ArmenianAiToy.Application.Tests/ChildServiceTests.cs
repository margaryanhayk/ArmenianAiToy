using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for ChildService — CRUD operations and BuildChildContext prompt generation.
/// Uses EF Core InMemory.
/// </summary>
public class ChildServiceTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Child>().HasKey(c => c.Id);
            modelBuilder.Entity<Child>().Ignore(c => c.Device);
            modelBuilder.Entity<Child>().Ignore(c => c.Conversations);
        }
    }

    private static (ChildService Service, TestDbContext Db) CreateService()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(options);
        return (new ChildService(db), db);
    }

    // --- CreateChildAsync ---

    [Fact]
    public async Task CreateChildAsync_PersistsChildWithCorrectFields()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();

        var child = await service.CreateChildAsync(deviceId, "Arman", Gender.Boy, new DateOnly(2021, 6, 15));

        Assert.NotEqual(Guid.Empty, child.Id);
        Assert.Equal("Arman", child.Name);
        Assert.Equal(Gender.Boy, child.Gender);
        Assert.Equal(new DateOnly(2021, 6, 15), child.DateOfBirth);
        Assert.Equal(deviceId, child.DeviceId);

        var persisted = await db.Set<Child>().FindAsync(child.Id);
        Assert.NotNull(persisted);
        Assert.Equal("Arman", persisted!.Name);
    }

    [Fact]
    public async Task CreateChildAsync_NullDateOfBirth_Accepted()
    {
        var (service, db) = CreateService();

        var child = await service.CreateChildAsync(Guid.NewGuid(), "Ani", Gender.Girl, null);

        Assert.Null(child.DateOfBirth);
        var persisted = await db.Set<Child>().FindAsync(child.Id);
        Assert.NotNull(persisted);
        Assert.Null(persisted!.DateOfBirth);
    }

    // --- GetChildAsync ---

    [Fact]
    public async Task GetChildAsync_ExistingChild_ReturnsChild()
    {
        var (service, db) = CreateService();
        var child = new Child { Id = Guid.NewGuid(), Name = "Gor", Gender = Gender.Boy, DeviceId = Guid.NewGuid() };
        db.Set<Child>().Add(child);
        await db.SaveChangesAsync();

        var result = await service.GetChildAsync(child.Id);

        Assert.NotNull(result);
        Assert.Equal("Gor", result!.Name);
    }

    [Fact]
    public async Task GetChildAsync_NonExistent_ReturnsNull()
    {
        var (service, _) = CreateService();

        var result = await service.GetChildAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    // --- GetDefaultChildForDeviceAsync ---

    [Fact]
    public async Task GetDefaultChildForDeviceAsync_ReturnsAChildFromDevice()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c1 = new Child { Id = Guid.NewGuid(), Name = "First", Gender = Gender.Girl, DeviceId = deviceId };
        var c2 = new Child { Id = Guid.NewGuid(), Name = "Second", Gender = Gender.Boy, DeviceId = deviceId };
        db.Set<Child>().AddRange(c1, c2);
        await db.SaveChangesAsync();

        var result = await service.GetDefaultChildForDeviceAsync(deviceId);

        Assert.NotNull(result);
        Assert.Equal(deviceId, result!.DeviceId);
        Assert.Contains(result.Id, new[] { c1.Id, c2.Id });
    }

    [Fact]
    public async Task GetDefaultChildForDeviceAsync_NoChildren_ReturnsNull()
    {
        var (service, _) = CreateService();

        var result = await service.GetDefaultChildForDeviceAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    // --- GetChildrenByDeviceAsync ---

    [Fact]
    public async Task GetChildrenByDeviceAsync_ReturnsOnlyDeviceChildren()
    {
        var (service, db) = CreateService();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        db.Set<Child>().AddRange(
            new Child { Id = Guid.NewGuid(), Name = "A1", Gender = Gender.Boy, DeviceId = deviceA },
            new Child { Id = Guid.NewGuid(), Name = "A2", Gender = Gender.Girl, DeviceId = deviceA },
            new Child { Id = Guid.NewGuid(), Name = "B1", Gender = Gender.Boy, DeviceId = deviceB });
        await db.SaveChangesAsync();

        var result = await service.GetChildrenByDeviceAsync(deviceA);

        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.Equal(deviceA, c.DeviceId));
    }

    [Fact]
    public async Task GetChildrenByDeviceAsync_NoChildren_ReturnsEmptyList()
    {
        var (service, _) = CreateService();

        var result = await service.GetChildrenByDeviceAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // --- BuildChildContext ---

    [Fact]
    public void BuildChildContext_Boy_ContainsBoyGenderAndName()
    {
        var (service, _) = CreateService();
        var child = new Child
        {
            Id = Guid.NewGuid(),
            Name = "Arman",
            Gender = Gender.Boy,
            DateOfBirth = new DateOnly(2021, 6, 15),
            DeviceId = Guid.NewGuid()
        };

        var context = service.BuildChildContext(child);

        Assert.Contains("Arman", context);
        Assert.Contains("boy", context);
        Assert.Contains("he/him", context);
    }

    [Fact]
    public void BuildChildContext_Girl_ContainsGirlGenderAndName()
    {
        var (service, _) = CreateService();
        var child = new Child
        {
            Id = Guid.NewGuid(),
            Name = "Ani",
            Gender = Gender.Girl,
            DateOfBirth = new DateOnly(2020, 3, 10),
            DeviceId = Guid.NewGuid()
        };

        var context = service.BuildChildContext(child);

        Assert.Contains("Ani", context);
        Assert.Contains("girl", context);
        Assert.Contains("she/her", context);
    }

    [Fact]
    public void BuildChildContext_WithAge_ContainsAgeAndVocabularyGuidance()
    {
        var (service, _) = CreateService();
        var child = new Child
        {
            Id = Guid.NewGuid(),
            Name = "Gor",
            Gender = Gender.Boy,
            DateOfBirth = new DateOnly(2021, 1, 1),
            DeviceId = Guid.NewGuid()
        };

        var context = service.BuildChildContext(child);

        Assert.Contains("years old", context);
        Assert.Contains("Adjust vocabulary complexity", context);
    }

    [Fact]
    public void BuildChildContext_NoDateOfBirth_OmitsAgeSection()
    {
        var (service, _) = CreateService();
        var child = new Child
        {
            Id = Guid.NewGuid(),
            Name = "Ani",
            Gender = Gender.Girl,
            DateOfBirth = null,
            DeviceId = Guid.NewGuid()
        };

        var context = service.BuildChildContext(child);

        Assert.DoesNotContain("years old", context);
        Assert.DoesNotContain("Adjust vocabulary complexity", context);
        Assert.Contains("Ani", context);
        Assert.Contains("CHILD PROFILE", context);
    }

    [Fact]
    public void BuildChildContext_ContainsPersonalizationGuidance()
    {
        var (service, _) = CreateService();
        var child = new Child
        {
            Id = Guid.NewGuid(),
            Name = "Arman",
            Gender = Gender.Boy,
            DeviceId = Guid.NewGuid()
        };

        var context = service.BuildChildContext(child);

        Assert.Contains("Address the child by name", context);
        Assert.Contains("CHILD PROFILE", context);
    }
}
