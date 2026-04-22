using System.Security.Cryptography;
using System.Text;
using ArmenianAiToy.Application.Notifications;
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
/// Tests for <c>ParentService.RequestPasswordResetAsync</c> and
/// <c>CompletePasswordResetAsync</c>. Uses real SQLite in-memory so
/// the FK cascade on <c>Parent → ParentPasswordResetToken</c> can be
/// exercised end-to-end, and the unique index on <c>TokenHash</c> is
/// a real schema constraint.
///
/// <para>
/// Injects a counting hash spy (via the existing
/// <c>hashPassword</c> constructor seam) to pin the timing-
/// normalization invariant on the reset-request path, plus an
/// NSubstitute <see cref="INotifier"/> to prove delivery attempts
/// happen only on the known-email path. Matches the style of
/// <c>ParentControllerRegisterAntiEnumerationTests</c>.
/// </para>
/// </summary>
public class ParentServicePasswordResetTests
{
    private sealed record Harness(
        ParentService Service,
        AppDbContext Db,
        SqliteConnection Conn,
        HashCounter Counter,
        INotifier Notifier) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Conn.DisposeAsync();
    }

    private sealed class HashCounter
    {
        public int Value;
    }

    private static async Task<Harness> CreateHarnessAsync(int? ttlMinutes = null)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
        config["Auth:PasswordResetTokenTtlMinutes"]
            .Returns(ttlMinutes?.ToString());
        var logger = Substitute.For<ILogger<ParentService>>();

        var counter = new HashCounter();
        string HashSpy(string pw)
        {
            counter.Value++;
            return "bcrypt-like-spy-hash::" + pw;
        }

        var notifier = Substitute.For<INotifier>();
        var service = new ParentService(db, config, logger,
            hashPassword: HashSpy, notifier: notifier);

        return new Harness(service, db, conn, counter, notifier);
    }

    private static Guid SeedParent(AppDbContext db, string email, string password)
    {
        var id = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RegisteredAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return id;
    }

    // Mirrors ParentService.ComputeTokenHash — SHA-256 hex of UTF-8 bytes.
    private static string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }

    // ─────────────────────────────────────────────────────────────
    // Reset-request — anti-enumeration invariants
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestPasswordReset_KnownEmail_CreatesSingleTokenRowWithHashOnly()
    {
        await using var h = await CreateHarnessAsync();
        var parentId = SeedParent(h.Db, "owner@example.com", "owner-pass9");

        await h.Service.RequestPasswordResetAsync("owner@example.com");

        var rows = await h.Db.Set<ParentPasswordResetToken>().AsNoTracking().ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(parentId, row.ParentId);
        Assert.False(string.IsNullOrWhiteSpace(row.TokenHash));
        // Hash is 64-char hex (SHA-256). A raw 32-byte base64-url token
        // would be ~43 chars; a plain Guid would be 36. 64 is the
        // SHA-256-hex-length tell.
        Assert.Equal(64, row.TokenHash.Length);
        Assert.Null(row.ConsumedAt);
        Assert.True(row.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task RequestPasswordReset_KnownEmail_CallsNotifierWithRawToken()
    {
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "owner@example.com", "owner-pass9");

        await h.Service.RequestPasswordResetAsync("owner@example.com");

        // Notifier was called exactly once with the target email and a
        // non-empty token that, when hashed, matches the stored hash.
        var calls = h.Notifier.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(INotifier.SendPasswordResetAsync))
            .ToList();
        Assert.Single(calls);
        var args = calls[0].GetArguments();
        Assert.Equal("owner@example.com", args[0]);
        var rawToken = (string)args[1]!;
        Assert.False(string.IsNullOrWhiteSpace(rawToken));

        var row = await h.Db.Set<ParentPasswordResetToken>().AsNoTracking().SingleAsync();
        Assert.Equal(HashToken(rawToken), row.TokenHash);
    }

    [Fact]
    public async Task RequestPasswordReset_KnownEmail_WritesExactlyOneAuditRow()
    {
        await using var h = await CreateHarnessAsync();
        var parentId = SeedParent(h.Db, "owner@example.com", "owner-pass9");

        await h.Service.RequestPasswordResetAsync("owner@example.com");

        var audits = await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentPasswordResetRequested)
            .ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Null(audit.TargetDeviceId);
        Assert.Null(audit.TargetChildId);
        // Metadata is deliberately empty — no token, no token hash,
        // no email. Matches the ParentPasswordChanged precedent.
        Assert.Null(audit.Metadata);
    }

    [Fact]
    public async Task RequestPasswordReset_UnknownEmail_DoesNotCreateTokenRow()
    {
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "owner@example.com", "owner-pass9");

        await h.Service.RequestPasswordResetAsync("stranger@example.com");

        Assert.Equal(0, await h.Db.Set<ParentPasswordResetToken>().AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task RequestPasswordReset_UnknownEmail_DoesNotCallNotifier()
    {
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "owner@example.com", "owner-pass9");

        await h.Service.RequestPasswordResetAsync("stranger@example.com");

        Assert.DoesNotContain(h.Notifier.ReceivedCalls(),
            c => c.GetMethodInfo().Name == nameof(INotifier.SendPasswordResetAsync));
    }

    [Fact]
    public async Task RequestPasswordReset_UnknownEmail_WritesNoAuditRow()
    {
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "owner@example.com", "owner-pass9");

        await h.Service.RequestPasswordResetAsync("stranger@example.com");

        Assert.Equal(0, await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentPasswordResetRequested)
            .CountAsync());
    }

    [Fact]
    public async Task RequestPasswordReset_HashesOnBothPaths_TimingNormalization()
    {
        // Pins the mandatory timing-normalization invariant: BCrypt
        // (here stubbed as the counting spy) must run on BOTH the
        // known-email and unknown-email paths. If either path skipped
        // the call, the response-time gap would re-open the
        // account-existence oracle.
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "owner@example.com", "owner-pass9");
        Assert.Equal(0, h.Counter.Value);

        await h.Service.RequestPasswordResetAsync("owner@example.com");
        Assert.Equal(1, h.Counter.Value);

        await h.Service.RequestPasswordResetAsync("stranger@example.com");
        Assert.Equal(2, h.Counter.Value);
    }

    // ─────────────────────────────────────────────────────────────
    // Reset-completion — success + uniform-failure invariants
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompletePasswordReset_ValidToken_SetsNewHashMarksConsumedAndAudits()
    {
        await using var h = await CreateHarnessAsync();
        var parentId = SeedParent(h.Db, "owner@example.com", "old-pass99");

        await h.Service.RequestPasswordResetAsync("owner@example.com");
        var rawToken = (string)h.Notifier.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(INotifier.SendPasswordResetAsync))
            .GetArguments()[1]!;

        var ok = await h.Service.CompletePasswordResetAsync(rawToken, "new-strong-pass9");
        Assert.True(ok);

        // Password was rewritten (through the hash spy so the new hash
        // is the spy-prefix). The original BCrypt hash is gone.
        var parent = await h.Db.Set<Parent>().AsNoTracking()
            .FirstAsync(p => p.Id == parentId);
        Assert.StartsWith("bcrypt-like-spy-hash::", parent.PasswordHash);

        // Token row still exists but is now consumed.
        var row = await h.Db.Set<ParentPasswordResetToken>().AsNoTracking()
            .FirstAsync(t => t.ParentId == parentId);
        Assert.NotNull(row.ConsumedAt);

        var audits = await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentPasswordResetCompleted)
            .ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Null(audit.Metadata);
    }

    [Fact]
    public async Task CompletePasswordReset_TokenReused_ReturnsFalseAndWritesNoSecondAudit()
    {
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "owner@example.com", "old-pass99");
        await h.Service.RequestPasswordResetAsync("owner@example.com");
        var rawToken = (string)h.Notifier.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(INotifier.SendPasswordResetAsync))
            .GetArguments()[1]!;

        Assert.True(await h.Service.CompletePasswordResetAsync(rawToken, "new-strong-pass9"));

        // Second attempt with the same token: uniform false.
        Assert.False(await h.Service.CompletePasswordResetAsync(rawToken, "second-pass9"));

        // Exactly one completion audit row — the second attempt wrote nothing.
        Assert.Equal(1, await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentPasswordResetCompleted)
            .CountAsync());
    }

    [Fact]
    public async Task CompletePasswordReset_TokenExpired_ReturnsFalse()
    {
        // Seed a token directly with ExpiresAt in the past so the
        // test doesn't depend on wall-clock advance.
        await using var h = await CreateHarnessAsync();
        var parentId = SeedParent(h.Db, "owner@example.com", "old-pass99");
        var rawToken = "raw-expired-token-only-for-test";
        var tokenHash = HashToken(rawToken);
        h.Db.Set<ParentPasswordResetToken>().Add(new ParentPasswordResetToken
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            ConsumedAt = null
        });
        await h.Db.SaveChangesAsync();

        var ok = await h.Service.CompletePasswordResetAsync(rawToken, "new-strong-pass9");
        Assert.False(ok);

        // No audit row; password hash unchanged.
        Assert.Equal(0, await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentPasswordResetCompleted)
            .CountAsync());
    }

    [Fact]
    public async Task CompletePasswordReset_TokenUnknown_ReturnsFalse()
    {
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "owner@example.com", "old-pass99");

        var ok = await h.Service.CompletePasswordResetAsync(
            "this-token-was-never-issued", "new-strong-pass9");
        Assert.False(ok);

        Assert.Equal(0, await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentPasswordResetCompleted)
            .CountAsync());
    }

    // ─────────────────────────────────────────────────────────────
    // End-to-end: legitimate login after reset; old password fails.
    // Uses the real BCrypt path (no hash spy) so Verify semantics
    // match the production LoginAsync path.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_ThenLogin_WorksWithNewPassword_AndNotWithOldOne()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
        var logger = Substitute.For<ILogger<ParentService>>();
        var notifier = Substitute.For<INotifier>();
        var service = new ParentService(db, config, logger, notifier: notifier);

        await service.RegisterAsync("owner@example.com", "original-pass9", acceptedTerms: true);
        await service.RequestPasswordResetAsync("owner@example.com");
        var rawToken = (string)notifier.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(INotifier.SendPasswordResetAsync))
            .GetArguments()[1]!;

        Assert.True(await service.CompletePasswordResetAsync(rawToken, "reset-pass9"));

        // New password works.
        Assert.NotNull(await service.LoginAsync("owner@example.com", "reset-pass9"));
        // Old password no longer works.
        Assert.Null(await service.LoginAsync("owner@example.com", "original-pass9"));

        await conn.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────
    // Cascade: deleting a parent removes any pending reset tokens.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountDelete_CascadesPendingResetTokens()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
        var logger = Substitute.For<ILogger<ParentService>>();
        var notifier = Substitute.For<INotifier>();
        var service = new ParentService(db, config, logger, notifier: notifier);

        await service.RegisterAsync("owner@example.com", "original-pass9", acceptedTerms: true);
        await service.RequestPasswordResetAsync("owner@example.com");
        Assert.Equal(1, await db.Set<ParentPasswordResetToken>().AsNoTracking().CountAsync());

        var ok = await service.DeleteAccountAsync(
            (await db.Set<Parent>().AsNoTracking().SingleAsync()).Id,
            "original-pass9");
        Assert.True(ok);

        // Pending token went with the parent via FK cascade.
        Assert.Equal(0, await db.Set<ParentPasswordResetToken>().AsNoTracking().CountAsync());

        await conn.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────
    // Token is stored only as a hash — the raw token string is
    // never present in the DB row even transiently.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestPasswordReset_StoredRow_DoesNotContainRawToken()
    {
        await using var h = await CreateHarnessAsync();
        SeedParent(h.Db, "owner@example.com", "owner-pass9");

        await h.Service.RequestPasswordResetAsync("owner@example.com");

        var rawToken = (string)h.Notifier.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(INotifier.SendPasswordResetAsync))
            .GetArguments()[1]!;
        var row = await h.Db.Set<ParentPasswordResetToken>().AsNoTracking().SingleAsync();

        // Token hash is not the raw token (would be a bug).
        Assert.NotEqual(rawToken, row.TokenHash);
        // And no field contains the raw token as a substring.
        Assert.DoesNotContain(rawToken, row.TokenHash, StringComparison.Ordinal);
    }
}
