using ArmenianAiToy.Application.Auth;
using ArmenianAiToy.Application.DTOs;
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
/// Tests for <see cref="ParentService.GoogleSignInAsync"/>. Uses real
/// SQLite in-memory so the Parent filtered-unique-index on
/// <c>GoogleSubject</c> and the AnonymizedAt filter semantics are
/// exercised against the same schema the migration builds in prod.
/// All tests swap a <see cref="FakeGoogleValidator"/> for the
/// <see cref="IGoogleIdTokenValidator"/> seam — no test makes a real
/// network call to Google.
/// </summary>
public class ParentServiceGoogleSignInTests
{
    private const string ValidClientId = "test-client-id.apps.googleusercontent.com";

    private sealed class FakeGoogleValidator : IGoogleIdTokenValidator
    {
        public int CallCount { get; private set; }
        public string? LastIdToken { get; private set; }
        public GoogleIdentity? NextResult { get; set; }

        public Task<GoogleIdentity?> ValidateAsync(
            string idToken, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastIdToken = idToken;
            return Task.FromResult(NextResult);
        }
    }

    private sealed record Harness(
        ParentService Service,
        AppDbContext Db,
        SqliteConnection Conn,
        FakeGoogleValidator Validator) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Conn.DisposeAsync();
        }
    }

    private static async Task<Harness> CreateHarnessAsync(
        string clientId = ValidClientId)
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
        config["GoogleAuth:ClientId"].Returns(clientId);
        var logger = Substitute.For<ILogger<ParentService>>();
        var validator = new FakeGoogleValidator();
        var service = new ParentService(
            db, config, logger,
            hashPassword: null,
            notifier: null,
            googleValidator: validator);
        return new Harness(service, db, conn, validator);
    }

    private static GoogleIdentity Identity(
        string sub = "sub-001",
        string email = "alice@example.com",
        bool emailVerified = true,
        string audience = ValidClientId)
        => new(sub, email, emailVerified, audience);

    // --- Validator seam contract -------------------------------------

    [Fact]
    public async Task GoogleSignIn_ValidatorSeam_IsExercised_NoRealNetwork()
    {
        // Pins the test-double contract itself: ValidateAsync must be
        // called exactly once per sign-in, and whatever the harness
        // returns is what the service acts on. A future refactor that
        // quietly bypasses the seam would fail here.
        await using var h = await CreateHarnessAsync();
        h.Validator.NextResult = Identity();

        var result = await h.Service.GoogleSignInAsync("token-1", acceptedTerms: true);

        Assert.Equal(GoogleSignInStatus.Success, result.Status);
        Assert.Equal(1, h.Validator.CallCount);
        Assert.Equal("token-1", h.Validator.LastIdToken);
    }

    [Fact]
    public async Task GoogleSignIn_ValidatorReturnsNull_RejectsUniformly()
    {
        // Invalid token path: validator returns null for invalid
        // signature / expired / malformed / audience mismatch. All
        // collapse to InvalidToken with zero side effects.
        await using var h = await CreateHarnessAsync();
        h.Validator.NextResult = null;

        var result = await h.Service.GoogleSignInAsync("junk", acceptedTerms: true);

        Assert.Equal(GoogleSignInStatus.InvalidToken, result.Status);
        Assert.Null(result.Token);
        Assert.Equal(0, await h.Db.Set<Parent>().CountAsync());
        Assert.Equal(0, await h.Db.Set<AuditEvent>().CountAsync());
    }

    // --- Claim validation --------------------------------------------

    [Fact]
    public async Task GoogleSignIn_EmailVerifiedFalse_Rejects_NoSideEffects()
    {
        // Unverified-email path must never stamp EmailVerifiedAt on a
        // row the claimant does not demonstrably own. Uniform failure.
        await using var h = await CreateHarnessAsync();
        h.Validator.NextResult = Identity(emailVerified: false);

        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: true);

        Assert.Equal(GoogleSignInStatus.InvalidToken, result.Status);
        Assert.Equal(0, await h.Db.Set<Parent>().CountAsync());
        Assert.Equal(0, await h.Db.Set<AuditEvent>().CountAsync());
    }

    [Fact]
    public async Task GoogleSignIn_AudienceMismatch_Rejects_NoSideEffects()
    {
        // Defensive in-service audience check: even if a swapped
        // validator forgets to enforce aud (the lib normally does),
        // the service must still reject.
        await using var h = await CreateHarnessAsync();
        h.Validator.NextResult = Identity(audience: "different-client.apps.googleusercontent.com");

        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: true);

        Assert.Equal(GoogleSignInStatus.InvalidToken, result.Status);
        Assert.Equal(0, await h.Db.Set<Parent>().CountAsync());
    }

    [Fact]
    public async Task GoogleSignIn_EmptyClientIdConfigured_Rejects()
    {
        // Feature-off path at the service layer — an empty ClientId
        // means every sign-in attempt is a uniform InvalidToken, even
        // with a nominally valid identity. The controller will have
        // short-circuited this with a 404 before we get here; this
        // is the belt-and-braces case.
        await using var h = await CreateHarnessAsync(clientId: "");
        h.Validator.NextResult = Identity();

        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: true);

        Assert.Equal(GoogleSignInStatus.InvalidToken, result.Status);
    }

    // --- Returning user (GoogleSubject match) ------------------------

    [Fact]
    public async Task GoogleSignIn_ExistingSubject_SignsInDirectly_DoesNotReStamp()
    {
        // Returning Google user — match by sub. LastLoginAt stamps;
        // EmailVerifiedAt is not re-stamped (its timestamp predates
        // this call and must survive). Audit row has first_time=false.
        await using var h = await CreateHarnessAsync();
        var parentId = Guid.NewGuid();
        var originalVerifiedAt = DateTime.UtcNow.AddDays(-30);
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "alice@example.com",
            PasswordHash = string.Empty,
            GoogleSubject = "sub-001",
            EmailVerifiedAt = originalVerifiedAt,
            RegisteredAt = DateTime.UtcNow.AddDays(-60),
            TermsAcceptedAt = DateTime.UtcNow.AddDays(-60),
            TermsVersion = ParentService.CurrentTermsVersion
        });
        await h.Db.SaveChangesAsync();
        h.Validator.NextResult = Identity(sub: "sub-001");

        var before = DateTime.UtcNow;
        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: false);

        Assert.Equal(GoogleSignInStatus.Success, result.Status);
        Assert.False(string.IsNullOrEmpty(result.Token));

        var parent = await h.Db.Set<Parent>().FindAsync(parentId);
        Assert.NotNull(parent);
        Assert.NotNull(parent!.LastLoginAt);
        Assert.True(parent.LastLoginAt >= before);
        // EmailVerifiedAt must be preserved exactly (not re-stamped).
        Assert.Equal(originalVerifiedAt, parent.EmailVerifiedAt);

        var audit = await h.Db.Set<AuditEvent>().SingleAsync();
        Assert.Equal(AuditEventType.ParentGoogleSignIn, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Contains("\"first_time\":false", audit.Metadata);
        Assert.Contains("\"linked_to_password_account\":false", audit.Metadata);
    }

    // --- Email-match linking -----------------------------------------

    [Fact]
    public async Task GoogleSignIn_EmailMatch_NoGoogleSubject_LinksAndStampsSubject()
    {
        // Password-registered parent signs in with Google for the
        // first time: row is linked (GoogleSubject stamped once),
        // EmailVerifiedAt stamped only if null, LastLoginAt stamped,
        // audit first_time=true linked=true.
        await using var h = await CreateHarnessAsync();
        var parentId = Guid.NewGuid();
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "alice@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pwd"),
            RegisteredAt = DateTime.UtcNow.AddDays(-10),
            TermsAcceptedAt = DateTime.UtcNow.AddDays(-10),
            TermsVersion = ParentService.CurrentTermsVersion,
            EmailVerifiedAt = null
        });
        await h.Db.SaveChangesAsync();
        h.Validator.NextResult = Identity(sub: "sub-linked-001");

        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: true);

        Assert.Equal(GoogleSignInStatus.Success, result.Status);
        var parent = await h.Db.Set<Parent>().FindAsync(parentId);
        Assert.Equal("sub-linked-001", parent!.GoogleSubject);
        Assert.NotNull(parent.EmailVerifiedAt);
        Assert.NotNull(parent.LastLoginAt);

        var audit = await h.Db.Set<AuditEvent>().SingleAsync();
        Assert.Contains("\"first_time\":true", audit.Metadata);
        Assert.Contains("\"linked_to_password_account\":true", audit.Metadata);
    }

    [Fact]
    public async Task GoogleSignIn_EmailMatch_SecondSignIn_UsesSubLookupNotReStamp()
    {
        // After linking, subsequent Google sign-ins must go through
        // the sub-lookup branch (first_time=false), not the email-
        // match branch again. GoogleSubject must not be re-written.
        await using var h = await CreateHarnessAsync();
        var parentId = Guid.NewGuid();
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "alice@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pwd"),
            RegisteredAt = DateTime.UtcNow.AddDays(-10),
            TermsAcceptedAt = DateTime.UtcNow.AddDays(-10),
            EmailVerifiedAt = null
        });
        await h.Db.SaveChangesAsync();

        h.Validator.NextResult = Identity(sub: "sub-stable-777");
        await h.Service.GoogleSignInAsync("t1", acceptedTerms: true);
        h.Validator.NextResult = Identity(sub: "sub-stable-777");
        await h.Service.GoogleSignInAsync("t2", acceptedTerms: false);

        var parent = await h.Db.Set<Parent>().FindAsync(parentId);
        Assert.Equal("sub-stable-777", parent!.GoogleSubject);
        var audits = await h.Db.Set<AuditEvent>()
            .OrderBy(a => a.Timestamp)
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains("\"first_time\":true", audits[0].Metadata);
        Assert.Contains("\"first_time\":false", audits[1].Metadata);
    }

    [Fact]
    public async Task GoogleSignIn_EmailMatch_ConflictingNonNullSubject_Rejects()
    {
        // Parent row is already claimed by a different Google
        // identity — never overwrite (that would be an account
        // takeover primitive). Uniform InvalidToken failure.
        await using var h = await CreateHarnessAsync();
        var parentId = Guid.NewGuid();
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "alice@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pwd"),
            GoogleSubject = "sub-original-owner",
            RegisteredAt = DateTime.UtcNow.AddDays(-10),
            TermsAcceptedAt = DateTime.UtcNow.AddDays(-10)
        });
        await h.Db.SaveChangesAsync();
        h.Validator.NextResult = Identity(sub: "sub-different-attacker");

        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: true);

        Assert.Equal(GoogleSignInStatus.InvalidToken, result.Status);
        var parent = await h.Db.Set<Parent>().FindAsync(parentId);
        Assert.Equal("sub-original-owner", parent!.GoogleSubject);
        Assert.Equal(0, await h.Db.Set<AuditEvent>().CountAsync());
    }

    [Fact]
    public async Task GoogleSignIn_EmailMatch_PreexistingEmailVerifiedAt_IsPreserved()
    {
        // Parent already verified via the email-verify flow before
        // Google-linking. Linking must NOT overwrite that timestamp
        // with a later one.
        await using var h = await CreateHarnessAsync();
        var parentId = Guid.NewGuid();
        var originalVerifiedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "alice@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pwd"),
            RegisteredAt = DateTime.UtcNow.AddDays(-20),
            TermsAcceptedAt = DateTime.UtcNow.AddDays(-20),
            EmailVerifiedAt = originalVerifiedAt
        });
        await h.Db.SaveChangesAsync();
        h.Validator.NextResult = Identity();

        await h.Service.GoogleSignInAsync("token", acceptedTerms: true);

        var parent = await h.Db.Set<Parent>().FindAsync(parentId);
        Assert.Equal(originalVerifiedAt, parent!.EmailVerifiedAt);
    }

    // --- New parent creation -----------------------------------------

    [Fact]
    public async Task GoogleSignIn_NewEmail_AcceptedTermsTrue_CreatesParentAndAudits()
    {
        // First-ever visit — no sub match, no email match, terms
        // accepted. Create a Google-only Parent row.
        await using var h = await CreateHarnessAsync();
        h.Validator.NextResult = Identity(
            sub: "sub-new-user", email: "brand-new@example.com");
        var before = DateTime.UtcNow;

        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: true);

        Assert.Equal(GoogleSignInStatus.Success, result.Status);
        var parent = await h.Db.Set<Parent>().SingleAsync();
        Assert.Equal("brand-new@example.com", parent.Email);
        Assert.Equal("sub-new-user", parent.GoogleSubject);
        Assert.Equal(string.Empty, parent.PasswordHash);
        Assert.NotNull(parent.EmailVerifiedAt);
        Assert.True(parent.EmailVerifiedAt >= before);
        Assert.NotNull(parent.TermsAcceptedAt);
        Assert.Equal(ParentService.CurrentTermsVersion, parent.TermsVersion);
        Assert.NotNull(parent.LastLoginAt);

        var audit = await h.Db.Set<AuditEvent>().SingleAsync();
        Assert.Equal(AuditEventType.ParentGoogleSignIn, audit.EventType);
        Assert.Contains("\"first_time\":true", audit.Metadata);
        Assert.Contains("\"linked_to_password_account\":false", audit.Metadata);
    }

    [Fact]
    public async Task GoogleSignIn_NewEmail_AcceptedTermsFalse_TermsRequired_NoRowCreated()
    {
        // Unknown-email signup without terms acceptance must not
        // create a Parent row.
        await using var h = await CreateHarnessAsync();
        h.Validator.NextResult = Identity(sub: "sub-new", email: "unknown@example.com");

        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: false);

        Assert.Equal(GoogleSignInStatus.TermsRequired, result.Status);
        Assert.Null(result.Token);
        Assert.Equal(0, await h.Db.Set<Parent>().CountAsync());
        Assert.Equal(0, await h.Db.Set<AuditEvent>().CountAsync());
    }

    [Fact]
    public async Task GoogleSignIn_EmailMatch_ExistingTermsAccepted_CanLinkWithoutReAccept()
    {
        // A password-registered parent (TermsAcceptedAt != null) must
        // be able to link their Google identity even when
        // acceptedTerms=false on the wire — prior acceptance stands.
        await using var h = await CreateHarnessAsync();
        var parentId = Guid.NewGuid();
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "alice@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pwd"),
            RegisteredAt = DateTime.UtcNow.AddDays(-10),
            TermsAcceptedAt = DateTime.UtcNow.AddDays(-10),
            TermsVersion = ParentService.CurrentTermsVersion
        });
        await h.Db.SaveChangesAsync();
        h.Validator.NextResult = Identity(sub: "sub-link-existing");

        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: false);

        Assert.Equal(GoogleSignInStatus.Success, result.Status);
        var parent = await h.Db.Set<Parent>().FindAsync(parentId);
        Assert.Equal("sub-link-existing", parent!.GoogleSubject);
    }

    // --- Anonymized rows are not matched -----------------------------

    [Fact]
    public async Task GoogleSignIn_AnonymizedEmailMatch_IsNotReused_CreatesFreshRow()
    {
        // Anonymized rows have Email=="" by construction, so an email
        // match cannot hit them anyway — but pin the invariant at the
        // query-filter level. The fresh Google sign-in must create a
        // new Parent row, not resurrect the anonymized one.
        await using var h = await CreateHarnessAsync();
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = Guid.NewGuid(),
            Email = string.Empty,
            PasswordHash = string.Empty,
            AnonymizedAt = DateTime.UtcNow.AddDays(-7),
            RegisteredAt = DateTime.UtcNow.AddDays(-400)
        });
        await h.Db.SaveChangesAsync();
        h.Validator.NextResult = Identity(sub: "sub-fresh", email: "anyone@example.com");

        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: true);

        Assert.Equal(GoogleSignInStatus.Success, result.Status);
        Assert.Equal(2, await h.Db.Set<Parent>().CountAsync());
        var fresh = await h.Db.Set<Parent>()
            .FirstAsync(p => p.Email == "anyone@example.com");
        Assert.Equal("sub-fresh", fresh.GoogleSubject);
    }

    // --- Audit cardinality -------------------------------------------

    [Fact]
    public async Task GoogleSignIn_SuccessWritesExactlyOneAuditRow()
    {
        // Defensive cardinality pin: however many code paths reach
        // success, only ONE audit row must be written per sign-in.
        await using var h = await CreateHarnessAsync();
        h.Validator.NextResult = Identity();

        await h.Service.GoogleSignInAsync("token", acceptedTerms: true);

        Assert.Equal(1, await h.Db.Set<AuditEvent>()
            .CountAsync(a => a.EventType == AuditEventType.ParentGoogleSignIn));
    }

    // --- JWT interop -------------------------------------------------

    [Fact]
    public async Task GoogleSignIn_IssuedJwt_AuthorizesExistingParentEndpoint()
    {
        // The JWT issued by Google sign-in must be indistinguishable
        // from a password-login JWT: both go through GenerateJwt with
        // the same issuer / audience / signing key. Proxy-check this
        // by confirming the token round-trips through the same
        // JwtSecurityTokenHandler parse the login path uses.
        await using var h = await CreateHarnessAsync();
        h.Validator.NextResult = Identity();

        var result = await h.Service.GoogleSignInAsync("token", acceptedTerms: true);
        Assert.Equal(GoogleSignInStatus.Success, result.Status);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(result.Token);
        Assert.NotNull(parsed);
        // Same NameIdentifier claim shape as password login.
        Assert.Contains(parsed.Claims,
            c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier);
        Assert.Contains(parsed.Claims,
            c => c.Type == System.Security.Claims.ClaimTypes.Email
                 && c.Value == "alice@example.com");
    }
}
