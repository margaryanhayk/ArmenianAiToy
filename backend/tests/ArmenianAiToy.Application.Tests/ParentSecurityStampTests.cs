using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using ArmenianAiToy.Application.Auth;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// N10 — issue-side contract of <c>Parent.SecurityStamp</c>: minted at
/// registration (password and Google), carried into every parent JWT as
/// the <see cref="ParentSecurityStampClaim.Name"/> claim, rotated on
/// password change and password-reset completion, and NOT rotated on an
/// ordinary login. Real SQLite in-memory <see cref="AppDbContext"/> so
/// the column round-trips through the real model. The validation side
/// (rejecting a stale token) is pinned in <c>ParentTokenValidationTests</c>.
/// </summary>
public class ParentSecurityStampTests
{
    private const string JwtKey = "TestSecretKeyThatIsLongEnoughForHmacSha256Validation!";

    private sealed class FakeGoogleValidator : IGoogleIdTokenValidator
    {
        public GoogleIdentity? NextResult { get; set; }
        public Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
            => Task.FromResult(NextResult);
    }

    private sealed record Harness(
        ParentService Service,
        AppDbContext Db,
        SqliteConnection Conn,
        ILogger<ParentService> Logger,
        FakeGoogleValidator Google) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Conn.DisposeAsync();
        }
    }

    private static async Task<Harness> CreateHarnessAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns(JwtKey);
        config["GoogleAuth:ClientId"].Returns("client-id-test");
        var logger = Substitute.For<ILogger<ParentService>>();
        var google = new FakeGoogleValidator();
        var service = new ParentService(db, config, logger, googleValidator: google);
        return new Harness(service, db, conn, logger, google);
    }

    private static string? StampClaim(string jwt)
        => new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims
            .FirstOrDefault(c => c.Type == ParentSecurityStampClaim.Name)?.Value;

    private static async Task<Parent> RegisterAsync(Harness h, string email = "stamp@example.com", string password = "password123")
    {
        await h.Service.RegisterAsync(email, password, acceptedTerms: true);
        return await h.Db.Set<Parent>().AsNoTracking().SingleAsync(p => p.Email == email);
    }

    private static void AssertNeverLogged(ILogger<ParentService> logger, string secret)
    {
        foreach (var call in logger.ReceivedCalls())
        {
            foreach (var arg in call.GetArguments())
            {
                Assert.DoesNotContain(secret, arg?.ToString() ?? "");
                if (arg is IEnumerable<KeyValuePair<string, object?>> state)
                    foreach (var kv in state)
                        Assert.DoesNotContain(secret, kv.Value?.ToString() ?? "");
            }
        }
    }

    // ────────────────────────────────────────────────────────────
    // Minting
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSecurityStamp_Is32LowercaseHex_AndUnique()
    {
        var a = ParentService.GenerateSecurityStamp();
        var b = ParentService.GenerateSecurityStamp();
        Assert.Matches("^[0-9a-f]{32}$", a);
        Assert.Matches("^[0-9a-f]{32}$", b);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Register_MintsStamp_AndNeverLogsIt()
    {
        await using var h = await CreateHarnessAsync();
        var parent = await RegisterAsync(h);

        Assert.Matches("^[0-9a-f]{32}$", parent.SecurityStamp);
        AssertNeverLogged(h.Logger, parent.SecurityStamp);
    }

    [Fact]
    public async Task Register_TwoParents_GetDistinctStamps()
    {
        await using var h = await CreateHarnessAsync();
        var a = await RegisterAsync(h, "a@example.com");
        var b = await RegisterAsync(h, "b@example.com");
        Assert.NotEqual(a.SecurityStamp, b.SecurityStamp);
    }

    [Fact]
    public async Task GoogleSignUp_MintsStamp_AndTokenCarriesIt()
    {
        await using var h = await CreateHarnessAsync();
        h.Google.NextResult = new GoogleIdentity("sub-n10", "g@example.com", true, "client-id-test");

        var result = await h.Service.GoogleSignInAsync("id-token", acceptedTerms: true);

        Assert.Equal(GoogleSignInStatus.Success, result.Status);
        var parent = await h.Db.Set<Parent>().AsNoTracking().SingleAsync(p => p.GoogleSubject == "sub-n10");
        Assert.Matches("^[0-9a-f]{32}$", parent.SecurityStamp);
        Assert.Equal(parent.SecurityStamp, StampClaim(result.Token!));
        AssertNeverLogged(h.Logger, parent.SecurityStamp);
    }

    // ────────────────────────────────────────────────────────────
    // Token carries the stamp; login does not rotate it
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_TokenCarriesCurrentStamp_AndDoesNotRotate()
    {
        await using var h = await CreateHarnessAsync();
        var before = await RegisterAsync(h);

        var first = await h.Service.LoginAsync("stamp@example.com", "password123");
        var second = await h.Service.LoginAsync("stamp@example.com", "password123");

        Assert.NotNull(first);
        Assert.NotNull(second);
        var after = await h.Db.Set<Parent>().AsNoTracking().SingleAsync(p => p.Id == before.Id);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.SecurityStamp, StampClaim(first!.Token));
        Assert.Equal(before.SecurityStamp, StampClaim(second!.Token));
    }

    // ────────────────────────────────────────────────────────────
    // Rotation
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_RotatesStamp_ReturnsTokenWithNewStamp()
    {
        await using var h = await CreateHarnessAsync();
        var before = await RegisterAsync(h);
        var oldToken = (await h.Service.LoginAsync("stamp@example.com", "password123"))!.Token;

        var freshToken = await h.Service.ChangePasswordAsync(before.Id, "password123", "newPassword456");

        Assert.NotNull(freshToken);
        var after = await h.Db.Set<Parent>().AsNoTracking().SingleAsync(p => p.Id == before.Id);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.Matches("^[0-9a-f]{32}$", after.SecurityStamp);
        Assert.Equal(after.SecurityStamp, StampClaim(freshToken!));
        Assert.Equal(before.SecurityStamp, StampClaim(oldToken)); // old token still names the OLD stamp
        AssertNeverLogged(h.Logger, after.SecurityStamp);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_DoesNotRotate()
    {
        await using var h = await CreateHarnessAsync();
        var before = await RegisterAsync(h);

        Assert.Null(await h.Service.ChangePasswordAsync(before.Id, "wrong", "newPassword456"));

        var after = await h.Db.Set<Parent>().AsNoTracking().SingleAsync(p => p.Id == before.Id);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
    }

    [Fact]
    public async Task CompletePasswordReset_RotatesStamp()
    {
        await using var h = await CreateHarnessAsync();
        var before = await RegisterAsync(h);
        const string rawToken = "reset-raw-token-n10";
        h.Db.Set<ParentPasswordResetToken>().Add(new ParentPasswordResetToken
        {
            Id = Guid.NewGuid(),
            ParentId = before.Id,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        });
        await h.Db.SaveChangesAsync();

        Assert.True(await h.Service.CompletePasswordResetAsync(rawToken, "newPassword456"));

        var after = await h.Db.Set<Parent>().AsNoTracking().SingleAsync(p => p.Id == before.Id);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.Matches("^[0-9a-f]{32}$", after.SecurityStamp);
    }

    [Fact]
    public async Task CompletePasswordReset_ExpiredToken_DoesNotRotate()
    {
        await using var h = await CreateHarnessAsync();
        var before = await RegisterAsync(h);
        const string rawToken = "reset-raw-token-expired";
        h.Db.Set<ParentPasswordResetToken>().Add(new ParentPasswordResetToken
        {
            Id = Guid.NewGuid(),
            ParentId = before.Id,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddHours(-1)
        });
        await h.Db.SaveChangesAsync();

        Assert.False(await h.Service.CompletePasswordResetAsync(rawToken, "newPassword456"));

        var after = await h.Db.Set<Parent>().AsNoTracking().SingleAsync(p => p.Id == before.Id);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
    }
}
