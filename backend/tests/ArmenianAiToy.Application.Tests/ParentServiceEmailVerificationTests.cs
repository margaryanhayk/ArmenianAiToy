using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="ParentService.RequestEmailVerificationAsync"/>
/// and <see cref="ParentService.CompleteEmailVerificationAsync"/>.
/// Uses real SQLite in-memory so the Parent → ParentEmailVerificationToken
/// FK cascade contract is exercised — same harness shape as
/// <c>ParentServicePasswordResetTests</c>.
/// </summary>
public class ParentServiceEmailVerificationTests
{
    private sealed class CountingNotifier : ArmenianAiToy.Application.Notifications.INotifier
    {
        public int VerificationSendCount { get; private set; }
        public string? LastEmail { get; private set; }
        public string? LastRawToken { get; private set; }

        public Task SendPasswordResetAsync(
            string email, string resetToken, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> SendDormancyWarningAsync(
            string email, DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task SendEmailVerificationAsync(
            string email, string verificationToken, CancellationToken cancellationToken = default)
        {
            VerificationSendCount++;
            LastEmail = email;
            LastRawToken = verificationToken;
            return Task.CompletedTask;
        }

        public Task<bool> SendDormantDeviceWarningAsync(
            string parentEmail, string deviceName, DateTime lastSeenAtUtc,
            DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        public Task SendToyJoinedByAnotherParentAsync(
            string parentEmail, string deviceName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class CountingHasher
    {
        public int Calls { get; private set; }
        public string Hash(string value)
        {
            Calls++;
            return BCrypt.Net.BCrypt.HashPassword(value);
        }
    }

    private sealed record Harness(
        ParentService Service,
        AppDbContext Db,
        SqliteConnection Conn,
        CountingNotifier Notifier) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Conn.DisposeAsync();
        }
    }

    private static async Task<Harness> CreateHarnessAsync(
        CountingHasher? hasher = null,
        int? ttlHours = null)
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
        if (ttlHours is not null)
            config["Auth:EmailVerificationTokenTtlHours"].Returns(ttlHours.Value.ToString());
        var logger = Substitute.For<ILogger<ParentService>>();
        var notifier = new CountingNotifier();
        Func<string, string>? hashFn = hasher is null ? null : (Func<string, string>)hasher.Hash;
        var service = new ParentService(db, config, logger, hashFn, notifier);
        return new Harness(service, db, conn, notifier);
    }

    private static Guid SeedParent(
        AppDbContext db,
        string email,
        DateTime? emailVerifiedAt = null)
    {
        var id = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
            RegisteredAt = DateTime.UtcNow.AddDays(-30),
            EmailVerifiedAt = emailVerifiedAt
        });
        db.SaveChanges();
        return id;
    }

    // --- RequestEmailVerificationAsync -------------------------------

    [Fact]
    public async Task RequestEmailVerification_KnownUnverified_IssuesTokenAndCallsNotifier()
    {
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "unverified@example.com", emailVerifiedAt: null);

        await h.Service.RequestEmailVerificationAsync("unverified@example.com");

        Assert.Equal(1, h.Notifier.VerificationSendCount);
        Assert.Equal("unverified@example.com", h.Notifier.LastEmail);
        var tokens = await h.Db.Set<ParentEmailVerificationToken>().AsNoTracking().ToListAsync();
        Assert.Single(tokens);
    }

    [Fact]
    public async Task RequestEmailVerification_KnownVerified_NoTokenNoSend()
    {
        // Anti-enum: response shape identical to unverified/unknown,
        // but the SMTP side effect is suppressed because the parent
        // is already verified.
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "verified@example.com",
            emailVerifiedAt: DateTime.UtcNow.AddDays(-5));

        await h.Service.RequestEmailVerificationAsync("verified@example.com");

        Assert.Equal(0, h.Notifier.VerificationSendCount);
        Assert.Empty(await h.Db.Set<ParentEmailVerificationToken>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RequestEmailVerification_UnknownEmail_NoTokenNoSend()
    {
        await using var h = await CreateHarnessAsync();
        // No parent seeded.

        await h.Service.RequestEmailVerificationAsync("nobody@example.com");

        Assert.Equal(0, h.Notifier.VerificationSendCount);
        Assert.Empty(await h.Db.Set<ParentEmailVerificationToken>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RequestEmailVerification_HashesEmailOnAllThreePaths()
    {
        // Timing-normalization invariant: every branch (known-unverified,
        // known-verified, unknown) pays the same BCrypt cost on the
        // email input. A counting spy proves Hash() fires once per
        // call regardless of eligibility outcome.
        var hasher = new CountingHasher();
        await using var h = await CreateHarnessAsync(hasher: hasher);
        SeedParent(h.Db, "unverified@example.com", emailVerifiedAt: null);
        SeedParent(h.Db, "verified@example.com",
            emailVerifiedAt: DateTime.UtcNow.AddDays(-5));

        await h.Service.RequestEmailVerificationAsync("unverified@example.com");
        await h.Service.RequestEmailVerificationAsync("verified@example.com");
        await h.Service.RequestEmailVerificationAsync("nobody@example.com");

        Assert.Equal(3, hasher.Calls);
    }

    [Fact]
    public async Task RequestEmailVerification_StoresOnlyTokenHashNotRaw()
    {
        // Matches the password-reset slice's pinned invariant: DB
        // exfil must never yield usable tokens.
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "unverified@example.com");

        await h.Service.RequestEmailVerificationAsync("unverified@example.com");

        var rawToken = h.Notifier.LastRawToken;
        Assert.False(string.IsNullOrWhiteSpace(rawToken));
        var storedRow = await h.Db.Set<ParentEmailVerificationToken>().AsNoTracking().SingleAsync();
        // Hash must not equal the raw token.
        Assert.NotEqual(rawToken, storedRow.TokenHash);
        // The raw token must not appear anywhere in the row.
        Assert.DoesNotContain(rawToken!, storedRow.TokenHash);
    }

    // --- CompleteEmailVerificationAsync ------------------------------

    [Fact]
    public async Task CompleteEmailVerification_ValidToken_StampsVerifiedAtAndConsumesAndAudits()
    {
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "unverified@example.com");
        await h.Service.RequestEmailVerificationAsync("unverified@example.com");
        var rawToken = h.Notifier.LastRawToken!;
        var before = DateTime.UtcNow;

        var ok = await h.Service.CompleteEmailVerificationAsync(rawToken);

        Assert.True(ok);
        var parent = await h.Db.Set<Parent>().AsNoTracking().SingleAsync();
        Assert.NotNull(parent.EmailVerifiedAt);
        Assert.InRange(parent.EmailVerifiedAt!.Value, before, DateTime.UtcNow);
        var tokenRow = await h.Db.Set<ParentEmailVerificationToken>().AsNoTracking().SingleAsync();
        Assert.NotNull(tokenRow.ConsumedAt);

        var audit = await h.Db.Set<AuditEvent>().AsNoTracking()
            .SingleAsync(a => a.EventType == AuditEventType.ParentEmailVerified);
        Assert.Equal(parent.Id, audit.ActorParentId);
        Assert.Null(audit.Metadata);
    }

    [Fact]
    public async Task CompleteEmailVerification_UnknownToken_ReturnsFalseAndAuditsNothing()
    {
        await using var h = await CreateHarnessAsync();

        var ok = await h.Service.CompleteEmailVerificationAsync("not-a-token");

        Assert.False(ok);
        Assert.Empty(await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentEmailVerified).ToListAsync());
    }

    [Fact]
    public async Task CompleteEmailVerification_EmptyToken_ReturnsFalse()
    {
        await using var h = await CreateHarnessAsync();
        Assert.False(await h.Service.CompleteEmailVerificationAsync(""));
        Assert.False(await h.Service.CompleteEmailVerificationAsync("   "));
    }

    [Fact]
    public async Task CompleteEmailVerification_ExpiredToken_ReturnsFalse()
    {
        await using var h = await CreateHarnessAsync();
        var parentId = SeedParent(h.Db, "unverified@example.com");
        // Seed an expired token row directly.
        h.Db.Set<ParentEmailVerificationToken>().Add(new ParentEmailVerificationToken
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            TokenHash = HashToken("raw-expired-token"),
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            ConsumedAt = null
        });
        await h.Db.SaveChangesAsync();

        var ok = await h.Service.CompleteEmailVerificationAsync("raw-expired-token");

        Assert.False(ok);
        var parent = await h.Db.Set<Parent>().AsNoTracking().SingleAsync(p => p.Id == parentId);
        Assert.Null(parent.EmailVerifiedAt);
    }

    [Fact]
    public async Task CompleteEmailVerification_ConsumedToken_ReturnsFalseOnSecondAttempt()
    {
        // Single-use: a successfully consumed token cannot redeem
        // again. Mirrors the ParentPasswordResetToken single-use
        // invariant.
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "unverified@example.com");
        await h.Service.RequestEmailVerificationAsync("unverified@example.com");
        var rawToken = h.Notifier.LastRawToken!;

        Assert.True(await h.Service.CompleteEmailVerificationAsync(rawToken));
        Assert.False(await h.Service.CompleteEmailVerificationAsync(rawToken));

        // Only one audit row should exist (first successful attempt).
        Assert.Single(await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentEmailVerified).ToListAsync());
    }

    // --- FK cascade --------------------------------------------------

    [Fact]
    public async Task AccountDelete_CascadesPendingVerificationTokens()
    {
        // Deleting a parent must take their pending verification
        // tokens with them — same contract as ParentPasswordResetToken.
        await using var h = await CreateHarnessAsync();
        var parentId = SeedParent(h.Db, "unverified@example.com");
        await h.Service.RequestEmailVerificationAsync("unverified@example.com");

        Assert.Equal(1, await h.Db.Set<ParentEmailVerificationToken>().AsNoTracking().CountAsync());

        var parent = await h.Db.Set<Parent>().SingleAsync(p => p.Id == parentId);
        h.Db.Set<Parent>().Remove(parent);
        await h.Db.SaveChangesAsync();

        Assert.Equal(0, await h.Db.Set<ParentEmailVerificationToken>().AsNoTracking().CountAsync());
    }

    // Mirrors ComputeTokenHash inside ParentService for seeding
    // pre-computed expired/consumed tokens in the tests above.
    private static string HashToken(string raw)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
