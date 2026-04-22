using System.Reflection;
using System.Text.Json;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Anti-enumeration contract tests for
/// <c>POST /api/parents/register</c>. Runs the real
/// <see cref="ParentService"/> over a real SQLite in-memory DB, wired
/// into the real controller, so the new-email and already-registered-
/// email paths are compared as a client would observe them.
///
/// <para>
/// Also pins the mandatory "BCrypt hashes on both paths" invariant via
/// a counting spy injected through the ParentService optional
/// <c>hashPassword</c> constructor parameter. A version of this slice
/// that skipped BCrypt on the collision path would turn the timing
/// channel back into an oracle, so this test is blocking.
/// </para>
/// </summary>
public class ParentControllerRegisterAntiEnumerationTests
{
    private sealed record Harness(
        ParentController Controller,
        ParentService Service,
        AppDbContext Db,
        SqliteConnection Conn,
        Counter HashCount) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Conn.DisposeAsync();
    }

    // Tiny mutable counter so the harness record can be `sealed` and the
    // test asserts can read post-call values by reference.
    private sealed class Counter
    {
        public int Value;
    }

    private static async Task<Harness> CreateHarnessAsync()
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

        var counter = new Counter();
        // Counting spy in place of BCrypt.HashPassword. We still return a
        // plausible hash string so downstream persistence works, but the
        // spy itself is what pins the "hash ran on both paths" invariant.
        // Login tests below still use the real BCrypt path via a separate
        // harness method so end-to-end verify behavior is unaffected.
        string Hash(string pw)
        {
            counter.Value++;
            return "bcrypt-like-spy-hash::" + pw;
        }

        var service = new ParentService(db, config, logger, hashPassword: Hash);
        var controller = new ParentController(service, new ExportCooldown());
        return new Harness(controller, service, db, conn, counter);
    }

    private static string SerializeBody(IActionResult result)
    {
        var created = Assert.IsType<CreatedResult>(result);
        // Normalize the body via JSON so we compare observable wire
        // shape, not CLR object identity or anonymous-type runtime
        // boxing details.
        return JsonSerializer.Serialize(created.Value, JsonSerializerOptions.Web);
    }

    // ─────────────────────────────────────────────────────────────
    // Wire-level indistinguishability — the core of the slice.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_NewEmail_Returns201WithNeutralBody()
    {
        await using var h = await CreateHarnessAsync();
        var request = new ParentRegisterRequest(
            "new@example.com", "strongpass9", AcceptedTerms: true);

        var result = await h.Controller.Register(request);

        var created = Assert.IsType<CreatedResult>(result);
        Assert.Equal(201, created.StatusCode);
        var body = SerializeBody(result);
        // Exact neutral-body shape. `parentId` MUST NOT appear.
        Assert.Equal("{\"registered\":true}", body);
        Assert.DoesNotContain("parentId", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_ExistingEmail_Returns201WithIdenticalBody()
    {
        await using var h = await CreateHarnessAsync();
        // Seed an existing account directly so we don't count a hash
        // call toward the "both paths hash" invariant below.
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("original-pass"),
            RegisteredAt = DateTime.UtcNow.AddDays(-3)
        });
        await h.Db.SaveChangesAsync();

        var newRequest = new ParentRegisterRequest(
            "really-new@example.com", "strongpass9", AcceptedTerms: true);
        var dupRequest = new ParentRegisterRequest(
            "existing@example.com", "attacker-guess9", AcceptedTerms: true);

        var newResult = await h.Controller.Register(newRequest);
        var dupResult = await h.Controller.Register(dupRequest);

        // Same status, same serialized body, byte-for-byte.
        Assert.Equal(((CreatedResult)newResult).StatusCode,
                     ((CreatedResult)dupResult).StatusCode);
        Assert.Equal(SerializeBody(newResult), SerializeBody(dupResult));
    }

    [Fact]
    public async Task Register_ExistingEmail_DoesNotCreateDuplicateRow()
    {
        await using var h = await CreateHarnessAsync();
        var existingId = Guid.NewGuid();
        var originalHash = BCrypt.Net.BCrypt.HashPassword("original-pass");
        var originalRegisteredAt = DateTime.UtcNow.AddDays(-3);
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = existingId,
            Email = "existing@example.com",
            PasswordHash = originalHash,
            RegisteredAt = originalRegisteredAt
        });
        await h.Db.SaveChangesAsync();

        var request = new ParentRegisterRequest(
            "existing@example.com", "attacker-guess9", AcceptedTerms: true);

        await h.Controller.Register(request);

        // Exactly one row for the email.
        Assert.Equal(1, await h.Db.Set<Parent>().AsNoTracking()
            .CountAsync(p => p.Email == "existing@example.com"));

        // Existing row is untouched — especially its password hash,
        // which if overwritten would be a silent takeover.
        var parent = await h.Db.Set<Parent>().AsNoTracking()
            .FirstAsync(p => p.Id == existingId);
        Assert.Equal(originalHash, parent.PasswordHash);
        Assert.Equal(originalRegisteredAt, parent.RegisteredAt);
    }

    // ─────────────────────────────────────────────────────────────
    // Mandatory timing invariant. Blocking.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_HashesPasswordOnBothPaths()
    {
        // Seed an existing account with a REAL BCrypt hash (not via
        // the spy) so the seed step doesn't contribute to the counter.
        await using var h = await CreateHarnessAsync();
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("original-pass"),
            RegisteredAt = DateTime.UtcNow.AddDays(-3)
        });
        await h.Db.SaveChangesAsync();
        Assert.Equal(0, h.HashCount.Value);

        // New-email path: should hash exactly once.
        await h.Controller.Register(new ParentRegisterRequest(
            "new@example.com", "strongpass9", AcceptedTerms: true));
        Assert.Equal(1, h.HashCount.Value);

        // Collision path: MUST also hash exactly once. This is the
        // mandatory timing-normalization invariant. If this assertion
        // fails, the register response latency becomes an oracle for
        // account existence even if the status and body are identical.
        await h.Controller.Register(new ParentRegisterRequest(
            "existing@example.com", "attacker-guess9", AcceptedTerms: true));
        Assert.Equal(2, h.HashCount.Value);
    }

    // ─────────────────────────────────────────────────────────────
    // Silent collision must not break the legitimate user.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_LoginAfterSilentCollision_StillWorksWithOriginalPassword()
    {
        // This test uses the REAL BCrypt path (no spy hash) because
        // LoginAsync calls BCrypt.Verify against a stored hash —
        // matching a spy-hash string would require coupling the
        // verify-side too and shrinks this slice's scope beyond the
        // prompt. Ship a dedicated harness instead.
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
        var logger = Substitute.For<ILogger<ParentService>>();
        var service = new ParentService(db, config, logger);

        // First register the legit user via the normal path.
        await service.RegisterAsync(
            "owner@example.com", "owner-real-pass9", acceptedTerms: true);

        // Now an attacker attempts to register with the same email.
        await service.RegisterAsync(
            "owner@example.com", "attacker-guess9", acceptedTerms: true);

        // The legit user can still log in with their original password.
        var ok = await service.LoginAsync("owner@example.com", "owner-real-pass9");
        Assert.NotNull(ok);
        Assert.False(string.IsNullOrEmpty(ok!.Token));

        // And the attacker's guessed password does NOT log in.
        var bad = await service.LoginAsync("owner@example.com", "attacker-guess9");
        Assert.Null(bad);

        await conn.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────
    // Pre-existing 400 validation behavior must still hold — the
    // slice only changed collision handling, not request-shape gates.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_EmptyFields_StillReturns400()
    {
        await using var h = await CreateHarnessAsync();
        var result = await h.Controller.Register(
            new ParentRegisterRequest("", "anyPassw0rd", AcceptedTerms: true));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Register_ShortPassword_StillReturns400()
    {
        await using var h = await CreateHarnessAsync();
        var result = await h.Controller.Register(
            new ParentRegisterRequest("new@example.com", "short12", AcceptedTerms: true));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Register_TermsNotAccepted_StillReturns400()
    {
        await using var h = await CreateHarnessAsync();
        var result = await h.Controller.Register(
            new ParentRegisterRequest("new@example.com", "strongpass9", AcceptedTerms: false));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ─────────────────────────────────────────────────────────────
    // The auth limiter must remain attached to Register.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Register_StillCarriesAuthRateLimiterAttribute()
    {
        var method = typeof(ParentController).GetMethod(nameof(ParentController.Register));
        Assert.NotNull(method);
        var attrs = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .ToArray();
        Assert.Contains(attrs, a => a.PolicyName == AuthRateLimiter.PolicyName);
    }
}
