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
/// Regression guard for the login-after-normalization bug: accounts
/// created BEFORE email normalization existed were stored with the email
/// exactly as typed (possibly mixed-case or with stray whitespace).
/// LoginAsync normalizes the SUBMITTED email to trim+lowercase, so a
/// plain `p.Email == email` comparison stopped matching those legacy rows
/// — a parent could no longer log in with the right password.
///
/// The fix compares the STORED email normalized too
/// (`p.Email.Trim().ToLower() == email`). This test runs against REAL
/// SQLite (not the InMemory provider, which would client-evaluate the
/// Trim/ToLower and hide a translation failure) to prove both that the
/// query translates AND that the legacy row now authenticates.
/// </summary>
public class ParentServiceLoginLegacyEmailSqliteTests
{
    private static async Task<(ParentService Service, AppDbContext Db, SqliteConnection Conn)>
        CreateAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
        config["Jwt:Issuer"].Returns("TestIssuer");
        config["Jwt:Audience"].Returns("TestAudience");
        var logger = Substitute.For<ILogger<ParentService>>();
        return (new ParentService(db, config, logger), db, conn);
    }

    [Theory]
    [InlineData("Hayk@Example.COM")]     // legacy mixed-case
    [InlineData("  hayk@example.com  ")] // legacy stray whitespace
    [InlineData("MiXeD@Domain.Com")]     // both
    public async Task LoginAsync_LegacyStoredEmail_AuthenticatesWithNormalizedInput(string storedEmail)
    {
        var (service, db, conn) = await CreateAsync();
        try
        {
            db.Set<Parent>().Add(new Parent
            {
                Id = Guid.NewGuid(),
                Email = storedEmail, // stored verbatim, as pre-normalization rows were
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-horse-battery"),
                RegisteredAt = DateTime.UtcNow,
                TermsAcceptedAt = DateTime.UtcNow,
                TermsVersion = ParentService.CurrentTermsVersion
            });
            await db.SaveChangesAsync();

            // The user types their email normally; the service normalizes it.
            var result = await service.LoginAsync(storedEmail.Trim().ToLowerInvariant(), "correct-horse-battery");

            Assert.NotNull(result);                 // login succeeds
            Assert.False(string.IsNullOrEmpty(result!.Token));

            // Wrong password on the same legacy row still fails.
            var bad = await service.LoginAsync(storedEmail.Trim().ToLowerInvariant(), "nope");
            Assert.Null(bad);
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }
}
