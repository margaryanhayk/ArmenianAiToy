using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.StoryRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Slice F — custom story requests: submission validation, audit, photo
/// seam, parent-scoped reads, and the local-disk photo store's traversal
/// hardening.
/// </summary>
public class StoryRequestTests
{
    private sealed class TestDb : DbContext
    {
        public TestDb(DbContextOptions<TestDb> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<StoryRequest>(e => e.HasKey(r => r.Id));
            mb.Entity<AuditEvent>(e => e.HasKey(a => a.Id));
        }
    }

    private sealed class RecordingPhotoStore : IStoryRequestPhotoStore
    {
        public readonly List<(Guid RequestId, int Bytes, string ContentType)> Saves = new();
        public bool RejectNext;
        public Task<string?> SaveAsync(
            Guid requestId, byte[] content, string contentType, CancellationToken ct = default)
        {
            if (RejectNext) return Task.FromResult<string?>(null);
            Saves.Add((requestId, content.Length, contentType));
            return Task.FromResult<string?>($"{requestId:N}.jpg");
        }
        public Task<(byte[] Content, string ContentType)?> ReadAsync(
            string photoPath, CancellationToken ct = default)
            => Task.FromResult<(byte[], string)?>(null);
    }

    private static (ParentService Service, TestDb Db, RecordingPhotoStore Photos) Create()
    {
        var db = new TestDb(new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var photos = new RecordingPhotoStore();
        var svc = new ParentService(db, new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<ParentService>>(),
            storyRequestPhotos: photos);
        return (svc, db, photos);
    }

    [Fact]
    public async Task Submit_IdeaText_CreatesRowAndAudit()
    {
        var (svc, db, _) = Create();
        var parentId = Guid.NewGuid();

        var id = await svc.SubmitStoryRequestAsync(parentId, "Հեքիաթ արջուկի մասին", null, null);

        Assert.NotNull(id);
        var row = await db.Set<StoryRequest>().SingleAsync();
        Assert.Equal(parentId, row.ParentId);
        Assert.Equal("idea", row.Type);
        Assert.Equal("new", row.Status);
        Assert.Null(row.PhotoPath);
        var audit = await db.Set<AuditEvent>().SingleAsync();
        Assert.Equal(AuditEventType.ParentStoryRequestSubmitted, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        // The parent's free text must NEVER land in audit metadata.
        Assert.DoesNotContain("արջուկ", audit.Metadata);
    }

    [Fact]
    public async Task Submit_WithPhoto_SavesPhotoAndSetsType()
    {
        var (svc, db, photos) = Create();

        var id = await svc.SubmitStoryRequestAsync(
            Guid.NewGuid(), "Գրքի էջ", [1, 2, 3], "image/jpeg");

        Assert.NotNull(id);
        var row = await db.Set<StoryRequest>().SingleAsync();
        Assert.Equal("book_photo", row.Type);
        Assert.NotNull(row.PhotoPath);
        Assert.Single(photos.Saves);
        Assert.Equal(3, photos.Saves[0].Bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Submit_NothingSaid_ReturnsNull(string? text)
    {
        var (svc, db, _) = Create();
        Assert.Null(await svc.SubmitStoryRequestAsync(Guid.NewGuid(), text, null, null));
        Assert.Empty(await db.Set<StoryRequest>().ToListAsync());
    }

    [Fact]
    public async Task Submit_OverlongText_ReturnsNull()
    {
        var (svc, db, _) = Create();
        var text = new string('ա', ParentService.StoryRequestMaxTextLength + 1);
        Assert.Null(await svc.SubmitStoryRequestAsync(Guid.NewGuid(), text, null, null));
        Assert.Empty(await db.Set<StoryRequest>().ToListAsync());
    }

    // KEYSTONE: a rejected photo (bad content type) fails the WHOLE
    // submission — never a silent text-only row when a page was attached.
    [Fact]
    public async Task Submit_RejectedPhoto_FailsWholeSubmission()
    {
        var (svc, db, photos) = Create();
        photos.RejectNext = true;

        var id = await svc.SubmitStoryRequestAsync(
            Guid.NewGuid(), "text", [1, 2], "application/pdf");

        Assert.Null(id);
        Assert.Empty(await db.Set<StoryRequest>().ToListAsync());
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task GetRequests_OnlyOwn_NewestFirst()
    {
        var (svc, db, _) = Create();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        db.Add(new StoryRequest { Id = Guid.NewGuid(), ParentId = mine, Type = "idea", Text = "old", Status = "new", CreatedAtUtc = DateTime.UtcNow.AddDays(-1), UpdatedAtUtc = DateTime.UtcNow.AddDays(-1) });
        db.Add(new StoryRequest { Id = Guid.NewGuid(), ParentId = mine, Type = "idea", Text = "fresh", Status = "delivered", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        db.Add(new StoryRequest { Id = Guid.NewGuid(), ParentId = theirs, Type = "idea", Text = "not mine", Status = "new", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var rows = await svc.GetStoryRequestsForParentAsync(mine);

        Assert.Equal(2, rows.Count);
        Assert.Equal("fresh", rows[0].Text);
        Assert.Equal("old", rows[1].Text);
        Assert.DoesNotContain(rows, r => r.Text == "not mine");
    }

    // ---- local-disk photo store ----

    private static LocalDiskStoryRequestPhotoStore DiskStore(string root)
        => new(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["StoryRequests:PhotoRoot"] = root }).Build(),
            Substitute.For<ILogger<LocalDiskStoryRequestPhotoStore>>());

    [Fact]
    public async Task DiskStore_RoundTrip_AndAllowlist()
    {
        var root = Path.Combine(Path.GetTempPath(), "areg-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = DiskStore(root);
            var id = Guid.NewGuid();

            Assert.Null(await store.SaveAsync(id, [1], "application/pdf")); // outside allowlist

            var path = await store.SaveAsync(id, [1, 2, 3], "image/jpeg");
            Assert.NotNull(path);
            var read = await store.ReadAsync(path!);
            Assert.NotNull(read);
            Assert.Equal(3, read!.Value.Content.Length);
            Assert.Equal("image/jpeg", read.Value.ContentType);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // KEYSTONE: a tampered DB value can never traverse the filesystem.
    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData("..\\secrets.txt")]
    [InlineData("sub/dir.jpg")]
    [InlineData("")]
    public async Task DiskStore_PathShapedReads_Rejected(string photoPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "areg-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(await DiskStore(root).ReadAsync(photoPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
