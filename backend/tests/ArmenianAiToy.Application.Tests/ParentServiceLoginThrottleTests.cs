using ArmenianAiToy.Application.Auth;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #040 — LoginAsync honors the per-account throttle: after the threshold of
/// failed logins, even the CORRECT password is rejected with the same uniform
/// null (401) for the cooldown, and access returns after it expires.
/// </summary>
public class ParentServiceLoginThrottleTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Parent>().HasKey(p => p.Id);
            b.Entity<Parent>().Ignore(p => p.ParentDevices);
        }
    }

    private const string Email = "throttle@example.com";
    private const string Password = "correct-horse-battery";

    private static (ParentService Svc, TestDbContext Db) Create(LoginAttemptThrottle throttle)
    {
        var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
        config["Jwt:Issuer"].Returns("TestIssuer");
        config["Jwt:Audience"].Returns("TestAudience");
        var svc = new ParentService(db, config, Substitute.For<ILogger<ParentService>>(),
            loginThrottle: throttle);
        db.Set<Parent>().Add(new Parent
        {
            Id = Guid.NewGuid(),
            Email = Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            RegisteredAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return (svc, db);
    }

    [Fact]
    public async Task RepeatedFailures_LockOutEvenTheCorrectPassword_ThenRecover()
    {
        var clock = new FakeTimeProvider();
        var throttle = new LoginAttemptThrottle(clock, maxFailures: 3,
            window: TimeSpan.FromMinutes(15), cooldown: TimeSpan.FromMinutes(15));
        var (svc, _) = Create(throttle);

        // Three wrong-password attempts trip the lockout.
        for (var i = 0; i < 3; i++)
            Assert.Null(await svc.LoginAsync(Email, "wrong"));

        // Now even the CORRECT password is refused (uniform null) during cooldown.
        Assert.Null(await svc.LoginAsync(Email, Password));

        // After the cooldown elapses, the correct password works again.
        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        var ok = await svc.LoginAsync(Email, Password);
        Assert.NotNull(ok);
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsTheFailureCounter()
    {
        var clock = new FakeTimeProvider();
        var throttle = new LoginAttemptThrottle(clock, maxFailures: 3,
            window: TimeSpan.FromMinutes(15), cooldown: TimeSpan.FromMinutes(15));
        var (svc, _) = Create(throttle);

        Assert.Null(await svc.LoginAsync(Email, "wrong"));
        Assert.Null(await svc.LoginAsync(Email, "wrong"));     // 2 failures
        Assert.NotNull(await svc.LoginAsync(Email, Password)); // success resets

        // A fresh failure run does not immediately lock (counter was reset).
        Assert.Null(await svc.LoginAsync(Email, "wrong"));
        Assert.Null(await svc.LoginAsync(Email, "wrong"));
        Assert.NotNull(await svc.LoginAsync(Email, Password)); // still not locked
    }
}
