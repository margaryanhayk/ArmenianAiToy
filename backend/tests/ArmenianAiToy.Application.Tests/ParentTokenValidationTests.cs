using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.Auth;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// N10 — validation-side contract: the exact <c>OnTokenValidated</c>
/// handler <c>Program.cs</c> wires (<see cref="ParentTokenValidation"/>)
/// run against a real <see cref="TokenValidatedContext"/>, a principal
/// produced by validating a REAL token from <c>ParentService</c> with
/// the same key, and a real SQLite in-memory <see cref="AppDbContext"/>
/// resolved through <c>HttpContext.RequestServices</c> — the same
/// plumbing the JWT bearer middleware uses, minus the HTTP hop (no
/// TestHost package is available; adding one is a NuGet hard stop).
/// Plus the pure <see cref="ParentSecurityStampClaim.Decide"/> matrix and
/// the rollback-switch parser.
/// </summary>
public class ParentTokenValidationTests
{
    private const string JwtKey = "TestSecretKeyThatIsLongEnoughForHmacSha256Validation!";

    private sealed record Harness(ParentService Service, AppDbContext Db, SqliteConnection Conn) : IAsyncDisposable
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
        // NSubstitute auto-returns "" (not null) for string members, which
        // would defeat ParentService's `?? "ArmenianAiToy"` defaults.
        config["Jwt:Issuer"].Returns("ArmenianAiToy");
        config["Jwt:Audience"].Returns("ArmenianAiToy");
        var logger = Substitute.For<ILogger<ParentService>>();
        return new Harness(new ParentService(db, config, logger), db, conn);
    }

    private static async Task<(Guid Id, string Token)> RegisterAndLoginAsync(Harness h, string email = "v@example.com")
    {
        await h.Service.RegisterAsync(email, "password123", acceptedTerms: true);
        var id = (await h.Db.Set<Parent>().AsNoTracking().SingleAsync(p => p.Email == email)).Id;
        var login = await h.Service.LoginAsync(email, "password123");
        return (id, login!.Token);
    }

    /// <summary>Validates the JWT exactly as the middleware would (signature, issuer, audience, lifetime).</summary>
    private static ClaimsPrincipal ValidateJwt(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "ArmenianAiToy",
            ValidAudience = "ArmenianAiToy",
            IssuerSigningKeys = new[] { new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)) }
        };
        return new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);
    }

    /// <summary>A token signed with the same key but WITHOUT the stamp claim — what every pre-N10 token looks like.</summary>
    private static string LegacyTokenWithoutStamp(Guid parentId, string email)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var token = new JwtSecurityToken(
            issuer: "ArmenianAiToy", audience: "ArmenianAiToy",
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, parentId.ToString()),
                new Claim(ClaimTypes.Email, email)
            },
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static TokenValidatedContext BuildContext(AppDbContext db, ClaimsPrincipal principal)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(http, scheme, new JwtBearerOptions()) { Principal = principal };
    }

    private static async Task<AuthenticateResult?> RunAsync(Harness h, string token, bool requireStamp = true)
    {
        var ctx = BuildContext(h.Db, ValidateJwt(token));
        await ParentTokenValidation.ValidateAsync(ctx, requireStamp);
        return ctx.Result;
    }

    private static void AssertAccepted(AuthenticateResult? result)
        => Assert.Null(result); // no Fail() call ⇒ Result untouched ⇒ the middleware proceeds

    private static void AssertRejected(AuthenticateResult? result, string reason)
    {
        Assert.NotNull(result);
        Assert.False(result!.Succeeded);
        Assert.Equal(reason, result.Failure!.Message);
    }

    // ────────────────────────────────────────────────────────────
    // End-to-end: issue → mutate → validate
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FreshLoginToken_IsAccepted()
    {
        await using var h = await CreateHarnessAsync();
        var (_, token) = await RegisterAndLoginAsync(h);
        AssertAccepted(await RunAsync(h, token));
    }

    [Fact]
    public async Task TokenIssuedBeforePasswordChange_IsRejectedAfter_AndFreshOneWorks()
    {
        await using var h = await CreateHarnessAsync();
        var (id, oldToken) = await RegisterAndLoginAsync(h);
        AssertAccepted(await RunAsync(h, oldToken));

        var freshToken = await h.Service.ChangePasswordAsync(id, "password123", "newPassword456");

        AssertRejected(await RunAsync(h, oldToken), ParentTokenValidation.StampReason);
        AssertAccepted(await RunAsync(h, freshToken!));
        // A brand-new login with the new password also works.
        var relogin = await h.Service.LoginAsync("v@example.com", "newPassword456");
        AssertAccepted(await RunAsync(h, relogin!.Token));
    }

    [Fact]
    public async Task TokenIssuedBeforePasswordResetCompletion_IsRejectedAfter()
    {
        await using var h = await CreateHarnessAsync();
        var (id, oldToken) = await RegisterAndLoginAsync(h);
        const string raw = "reset-raw-n10-validate";
        h.Db.Set<ParentPasswordResetToken>().Add(new ParentPasswordResetToken
        {
            Id = Guid.NewGuid(),
            ParentId = id,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        });
        await h.Db.SaveChangesAsync();

        Assert.True(await h.Service.CompletePasswordResetAsync(raw, "newPassword456"));

        AssertRejected(await RunAsync(h, oldToken), ParentTokenValidation.StampReason);
        var relogin = await h.Service.LoginAsync("v@example.com", "newPassword456");
        AssertAccepted(await RunAsync(h, relogin!.Token));
    }

    [Fact]
    public async Task TokenAfterAccountDeletion_IsRejected()
    {
        await using var h = await CreateHarnessAsync();
        var (id, token) = await RegisterAndLoginAsync(h);

        Assert.True(await h.Service.DeleteAccountAsync(id, "password123"));

        AssertRejected(await RunAsync(h, token), ParentTokenValidation.AccountGoneReason);
    }

    [Fact]
    public async Task TokenAfterAnonymization_IsRejected()
    {
        await using var h = await CreateHarnessAsync();
        var (id, token) = await RegisterAndLoginAsync(h);
        var row = await h.Db.Set<Parent>().SingleAsync(p => p.Id == id);
        row.AnonymizedAt = DateTime.UtcNow; // the retention pass also rotates the stamp; AnonymizedAt alone must suffice
        await h.Db.SaveChangesAsync();

        AssertRejected(await RunAsync(h, token), ParentTokenValidation.AccountGoneReason);
    }

    [Fact]
    public async Task SecondLogin_DoesNotInvalidateFirstToken()
    {
        await using var h = await CreateHarnessAsync();
        var (_, first) = await RegisterAndLoginAsync(h);
        var second = await h.Service.LoginAsync("v@example.com", "password123");

        AssertAccepted(await RunAsync(h, first));
        AssertAccepted(await RunAsync(h, second!.Token));
    }

    // ────────────────────────────────────────────────────────────
    // Compatibility switch: Jwt:RequireSecurityStamp
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LegacyTokenWithoutClaim_RequireTrue_IsRejected()
    {
        await using var h = await CreateHarnessAsync();
        var (id, _) = await RegisterAndLoginAsync(h);
        var legacy = LegacyTokenWithoutStamp(id, "v@example.com");

        AssertRejected(await RunAsync(h, legacy, requireStamp: true), ParentTokenValidation.StampReason);
    }

    [Fact]
    public async Task LegacyTokenWithoutClaim_RequireFalse_IsAccepted()
    {
        await using var h = await CreateHarnessAsync();
        var (id, _) = await RegisterAndLoginAsync(h);
        var legacy = LegacyTokenWithoutStamp(id, "v@example.com");

        AssertAccepted(await RunAsync(h, legacy, requireStamp: false));
    }

    [Fact]
    public async Task StaleClaim_RequireFalse_IsStillRejected()
    {
        // The switch tolerates ABSENT claims only; a token that carries a
        // rotated-away stamp is rejected regardless.
        await using var h = await CreateHarnessAsync();
        var (id, oldToken) = await RegisterAndLoginAsync(h);
        await h.Service.ChangePasswordAsync(id, "password123", "newPassword456");

        AssertRejected(await RunAsync(h, oldToken, requireStamp: false), ParentTokenValidation.StampReason);
    }

    [Fact]
    public async Task LegacyTokenForDeletedAccount_RequireFalse_IsStillRejected()
    {
        await using var h = await CreateHarnessAsync();
        var (id, _) = await RegisterAndLoginAsync(h);
        var legacy = LegacyTokenWithoutStamp(id, "v@example.com");
        Assert.True(await h.Service.DeleteAccountAsync(id, "password123"));

        AssertRejected(await RunAsync(h, legacy, requireStamp: false), ParentTokenValidation.AccountGoneReason);
    }

    [Fact]
    public async Task InvalidSubject_IsRejected()
    {
        await using var h = await CreateHarnessAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "not-a-guid") }, "Bearer"));
        var ctx = BuildContext(h.Db, principal);

        await ParentTokenValidation.ValidateAsync(ctx, requireStamp: true);

        AssertRejected(ctx.Result, "Invalid subject.");
    }

    // ────────────────────────────────────────────────────────────
    // Pure decision + switch parsing
    // ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("abc", "abc", true, ParentSecurityStampClaim.Decision.Accept)]
    [InlineData("abc", "abc", false, ParentSecurityStampClaim.Decision.Accept)]
    [InlineData("abc", "xyz", true, ParentSecurityStampClaim.Decision.StampMismatch)]
    [InlineData("abc", "xyz", false, ParentSecurityStampClaim.Decision.StampMismatch)]
    [InlineData("ABC", "abc", true, ParentSecurityStampClaim.Decision.StampMismatch)] // ordinal
    [InlineData(null, "abc", true, ParentSecurityStampClaim.Decision.StampMissing)]
    [InlineData("", "abc", true, ParentSecurityStampClaim.Decision.StampMissing)]
    [InlineData(null, "abc", false, ParentSecurityStampClaim.Decision.Accept)]
    [InlineData("", "abc", false, ParentSecurityStampClaim.Decision.Accept)]
    [InlineData("abc", null, true, ParentSecurityStampClaim.Decision.AccountGone)]
    [InlineData("abc", null, false, ParentSecurityStampClaim.Decision.AccountGone)]
    [InlineData(null, null, false, ParentSecurityStampClaim.Decision.AccountGone)]
    [InlineData("abc", "", true, ParentSecurityStampClaim.Decision.StampMismatch)] // empty row stamp never matches
    public void Decide_Matrix(string? claim, string? current, bool require, ParentSecurityStampClaim.Decision expected)
        => Assert.Equal(expected, ParentSecurityStampClaim.Decide(claim, current, require));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("0", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData(" false ", false)]
    public void ParseRequire_OnlyLiteralFalseTurnsItOff(string? raw, bool expected)
        => Assert.Equal(expected, ParentSecurityStampClaim.ParseRequire(raw));

    [Fact]
    public void RequireSecurityStamp_ReadsConfigKey_DefaultTrue()
    {
        var empty = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Assert.True(ParentTokenValidation.RequireSecurityStamp(empty));

        var off = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Jwt:RequireSecurityStamp"] = "false" }).Build();
        Assert.False(ParentTokenValidation.RequireSecurityStamp(off));
    }
}
