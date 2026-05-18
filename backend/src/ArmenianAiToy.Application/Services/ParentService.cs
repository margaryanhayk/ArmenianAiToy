using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Auth;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Notifications;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;

namespace ArmenianAiToy.Application.Services;

public class ParentService : IParentService
{
    /// <summary>
    /// Current terms-of-service version recorded on newly registered
    /// Parent rows. Bump this when the parent-facing terms text changes.
    /// A bump should be accompanied by a separate re-acknowledgement flow
    /// for existing parents (not in scope for C1).
    /// </summary>
    public const string CurrentTermsVersion = "1.0";

    private readonly DbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ParentService> _logger;
    private readonly Func<string, string> _hashPassword;
    private readonly INotifier _notifier;
    private readonly IGoogleIdTokenValidator? _googleValidator;
    private readonly IAudioBlobStore _blobStore;

    /// <summary>
    /// Standard constructor used by the DI container. Optional
    /// parameters exist purely so the existing ParentService-construct-
    /// ing tests don't have to thread substitutes through:
    /// <list type="bullet">
    ///   <item><description><paramref name="hashPassword"/> — defaults
    ///   to <c>BCrypt.Net.BCrypt.HashPassword</c>. Exists so the
    ///   anti-enumeration tests (register + password-reset) can inject
    ///   a counting spy to prove the "hash runs on both paths" timing
    ///   invariant without a full integration harness.</description></item>
    ///   <item><description><paramref name="notifier"/> — defaults to
    ///   <see cref="NullNotifier.Instance"/>. Real DI always registers
    ///   <c>LoggingNotifier</c>; the null fallback exists only so
    ///   unit tests for unrelated flows (pause, bedtime, mode flags,
    ///   etc.) can construct a bare ParentService without threading a
    ///   notifier substitute everywhere.</description></item>
    ///   <item><description><paramref name="blobStore"/> — C2.2a audio
    ///   cleanup seam. Defaults to a private no-op
    ///   <see cref="NullAudioBlobStore"/> so existing tests for
    ///   non-destructive parent flows (pause, bedtime, mode flags,
    ///   etc.) construct ParentService unchanged. The four destructive
    ///   parent paths (DeleteConversation / DeleteChild / UnlinkDevice
    ///   orphan / DeleteAccount orphan) call into this store post-DB-
    ///   commit; tests that exercise those paths inject a real or
    ///   counting double.</description></item>
    /// </list>
    /// Forgot-password-specific tests pass an explicit
    /// <see cref="INotifier"/> substitute.
    /// </summary>
    public ParentService(
        DbContext db,
        IConfiguration config,
        ILogger<ParentService> logger,
        Func<string, string>? hashPassword = null,
        INotifier? notifier = null,
        IGoogleIdTokenValidator? googleValidator = null,
        IAudioBlobStore? blobStore = null)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _hashPassword = hashPassword ?? BCrypt.Net.BCrypt.HashPassword;
        _notifier = notifier ?? NullNotifier.Instance;
        _googleValidator = googleValidator;
        _blobStore = blobStore ?? NullAudioBlobStore.Instance;
    }

    // Private no-op notifier. Used as the safe default when no
    // INotifier is supplied — keeps existing tests that construct
    // ParentService directly from needing a stub.
    private sealed class NullNotifier : INotifier
    {
        public static readonly NullNotifier Instance = new();
        public Task SendPasswordResetAsync(
            string email, string resetToken, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<bool> SendDormancyWarningAsync(
            string email, DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        public Task SendEmailVerificationAsync(
            string email, string verificationToken, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<bool> SendDormantDeviceWarningAsync(
            string parentEmail, string deviceName, DateTime lastSeenAtUtc,
            DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    // Private no-op blob store — safe default for tests that construct
    // ParentService directly and don't exercise C2.2a audio cleanup.
    // Returns the "directory missing" idempotent-success shape so the
    // aggregate audit metadata stays at zeros, just as if no audio had
    // ever been written. Real DI injects LocalDiskAudioBlobStore.
    private sealed class NullAudioBlobStore : IAudioBlobStore
    {
        public static readonly NullAudioBlobStore Instance = new();
        public Task<string> WriteAsync(Guid conversationId, Guid messageId,
            byte[] content, string mimeType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                "NullAudioBlobStore is read/delete-only; ParentService never writes blobs.");
        public Task<(Stream Content, string MimeType)?> ReadAsync(
            Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
            => Task.FromResult<(Stream, string)?>(null);
        public Task<AudioBlobDeleteResult> DeleteConversationAudioAsync(
            Guid conversationId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AudioBlobDeleteResult(
                FilesDeleted: 0, DirectoryMissing: true, Failed: false, ErrorMessage: null));
    }

    /// <summary>
    /// Register a new parent account with anti-enumeration semantics.
    /// The new-email and already-registered-email paths are externally
    /// indistinguishable: both complete without returning an account
    /// existence signal to the caller, and both pay the same BCrypt
    /// latency so the response time cannot be used as an oracle.
    ///
    /// <para>Control flow:</para>
    /// <list type="number">
    ///   <item><description>Request-shape guard: consent must be
    ///   accepted. The controller 400s this earlier; the throw here is
    ///   defense-in-depth.</description></item>
    ///   <item><description><b>Hash the submitted password first</b>
    ///   on every call. This is the mandatory timing normalization —
    ///   it runs before the email-existence check so both paths pay
    ///   the same latency floor.</description></item>
    ///   <item><description>If the email is already registered, return
    ///   silently. Do NOT throw, do NOT persist, do NOT mutate the
    ///   existing row (its <c>PasswordHash</c> stays exactly as it
    ///   was — only the hash computed for THIS call is discarded).
    ///   Logs a server-side "silent no-op" breadcrumb without the
    ///   email.</description></item>
    ///   <item><description>Otherwise, persist a new Parent with the
    ///   already-computed hash, using the consent timestamps from
    ///   <c>DateTime.UtcNow</c>.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>No audit row.</b> Register/login remain deliberately out of
    /// the audit scope documented in CLAUDE.md § Audit events.
    /// </para>
    /// </summary>
    public async Task RegisterAsync(string email, string password, bool acceptedTerms)
    {
        if (!acceptedTerms)
            throw new InvalidOperationException("Terms must be accepted to register.");

        // Mandatory timing normalization — BCrypt runs on both paths.
        // Computing the hash BEFORE the email-existence check means the
        // collision path pays the same latency as the new-email path.
        // The value is discarded on the collision branch below.
        var hash = _hashPassword(password);

        var existing = await _db.Set<Parent>().AnyAsync(p => p.Email == email);
        if (existing)
        {
            // Anti-enumeration: silent no-op on collision. The hash
            // just computed is intentionally discarded; the existing
            // Parent row is not touched. Server-side log is
            // email-less to avoid a log-scraping back-channel.
            _logger.LogInformation(
                "Parent register: silent no-op on collision " +
                "(terms version {TermsVersion})",
                CurrentTermsVersion);
            return;
        }

        var now = DateTime.UtcNow;
        var parent = new Parent
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = hash,
            RegisteredAt = now,
            TermsAcceptedAt = now,
            TermsVersion = CurrentTermsVersion
        };

        // Email-verification token for the new parent. Collision
        // path (above) deliberately does NOT issue a token — that
        // preserves the silent-no-op anti-enum contract at the
        // SMTP layer (server-to-server, not observable to external
        // attackers). New-email path always issues and always
        // sends, so a real user who registered a new account
        // always receives a verification email.
        var rawVerificationToken = GenerateRawResetToken();
        var verificationTokenHash = ComputeTokenHash(rawVerificationToken);
        var verificationTtl = TimeSpan.FromHours(ReadEmailVerificationTtlHours());
        _db.Set<ParentEmailVerificationToken>().Add(new ParentEmailVerificationToken
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            TokenHash = verificationTokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(verificationTtl),
            ConsumedAt = null
        });

        _db.Set<Parent>().Add(parent);
        await _db.SaveChangesAsync();

        // Send AFTER SaveChangesAsync so a DB-write failure
        // short-circuits before the notifier is invoked — same
        // ordering the password-reset flow uses.
        await _notifier.SendEmailVerificationAsync(email, rawVerificationToken);

        _logger.LogInformation(
            "Parent registered: {Email}, terms version {TermsVersion}",
            email, CurrentTermsVersion);
    }

    public async Task<ParentLoginResponse?> LoginAsync(string email, string password)
    {
        // Filter anonymized rows at the query level — their Email is
        // empty-string by construction, so an empty-email login probe
        // would otherwise match one of those rows and BCrypt.Verify
        // against an empty PasswordHash throws rather than returning
        // false. No login-side "IsDisabled" flag is required: the
        // scrubbed row is simply invisible to the login lookup.
        var parent = await _db.Set<Parent>()
            .FirstOrDefaultAsync(p => p.Email == email && p.AnonymizedAt == null);
        if (parent == null || !BCrypt.Net.BCrypt.Verify(password, parent.PasswordHash))
            return null;

        // JWT generation runs BEFORE the LastLoginAt stamp so a
        // misconfig (missing/legacy Jwt:Key) fails fast without
        // persisting a login-success signal the caller never actually
        // received. Only a fully successful credentials + JWT exchange
        // mutates the column. Failed-BCrypt and unknown-email paths
        // have already short-circuited above and leave the DB untouched
        // — same contract as before this slice.
        var token = GenerateJwt(parent);
        parent.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new ParentLoginResponse(token);
    }

    public async Task<bool> LinkDeviceAsync(Guid parentId, Guid deviceId, string apiKey)
    {
        // Fetch by id alone — the credential check is done in-process so the
        // same hashed / legacy-plaintext duality as DeviceService.ValidateDevice
        // applies here. SQL plaintext equality (the pre-hash-at-rest shape)
        // would never match a hashed row, locking parents out of every
        // device registered after the slice landed.
        var device = await _db.Set<Device>()
            .FirstOrDefaultAsync(d => d.Id == deviceId);

        if (device == null)
            return false;

        bool credentialValid;
        if (DeviceApiKeyHasher.IsHash(device.ApiKeyHash))
        {
            credentialValid = DeviceApiKeyHasher.Verify(apiKey, device.ApiKeyHash);
        }
        else
        {
            credentialValid = !string.IsNullOrEmpty(device.ApiKey)
                && DeviceApiKeyHasher.ConstantTimeEquals(apiKey, device.ApiKey);
        }

        if (!credentialValid)
            return false;

        var alreadyLinked = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId);

        if (alreadyLinked)
            return true;

        _db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId,
            DeviceId = deviceId,
            LinkedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation("Parent {ParentId} linked device {DeviceId}", parentId, deviceId);
        return true;
    }

    public async Task<bool> UnlinkDeviceAsync(Guid parentId, Guid deviceId)
    {
        var link = await _db.Set<ParentDevice>()
            .FirstOrDefaultAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId);

        if (link == null)
            return false;

        _db.Set<ParentDevice>().Remove(link);
        await _db.SaveChangesAsync();

        // Orphan-aware cleanup: if this unlink removed the last parent link
        // to the device, the device and its subtree (children, conversations,
        // messages) become unreachable by any parent API. Delete the Device
        // and rely on the existing Cascade FKs to take the subtree with it.
        // Same shape as DeleteAccountAsync's orphan loop, narrowed to a
        // single device.
        var stillLinked = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.DeviceId == deviceId);
        if (stillLinked)
        {
            // No cascade fired → no audio cleanup runs. Audit metadata
            // carries zeros for the audio counts, same as the still-linked
            // contract states.
            TrackAndAddAudit(AuditEvent.ParentDeviceUnlinked(
                parentId, deviceId, orphanCascaded: false));
            await _db.SaveChangesAsync();
            _logger.LogInformation(
                "Parent {ParentId} unlinked device {DeviceId} (still linked to other parents)",
                parentId, deviceId);
            return true;
        }

        // C2.2a — orphan-cascade branch. Snapshot the conversation ids
        // BEFORE the Device delete so the FK cascade doesn't erase the
        // information we need to clean up audio. Counts only — no
        // entity materialization beyond the bare id projection.
        var conversationIds = await _db.Set<Conversation>()
            .Where(c => c.DeviceId == deviceId)
            .Select(c => c.Id)
            .ToListAsync();

        var device = await _db.Set<Device>().FindAsync(deviceId);
        if (device != null)
        {
            _db.Set<Device>().Remove(device);
        }
        // DB-first: commit the device delete (and FK cascade through
        // Children / Conversations / Messages / ParentDevices) before
        // touching the filesystem.
        await _db.SaveChangesAsync();

        var audio = await RunAudioCleanupAsync(conversationIds);

        // orphanCascaded captures whether the device row was actually removed.
        // In the rare "device already gone" race it stays false even though
        // this was the last ParentDevice link.
        TrackAndAddAudit(AuditEvent.ParentDeviceUnlinked(
            parentId, deviceId,
            orphanCascaded: device != null,
            audioConversationsAttempted: audio.Attempted,
            audioFilesDeleted: audio.FilesDeleted,
            audioDeleteFailures: audio.Failures));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} unlinked last link to device {DeviceId}; device and subtree deleted ({AudioFilesDeleted} audio files cleaned, {AudioDeleteFailures} failures)",
            parentId, deviceId, audio.FilesDeleted, audio.Failures);
        return true;
    }

    public async Task<List<Guid>> GetLinkedDeviceIdsAsync(Guid parentId)
    {
        return await _db.Set<ParentDevice>()
            .Where(pd => pd.ParentId == parentId)
            .Select(pd => pd.DeviceId)
            .ToListAsync();
    }

    public async Task<bool> ChangePasswordAsync(Guid parentId, string currentPassword, string newPassword)
    {
        var parent = await _db.Set<Parent>().FirstOrDefaultAsync(p => p.Id == parentId);
        if (parent == null)
            return false;

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, parent.PasswordHash))
            return false;

        parent.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        TrackAndAddAudit(AuditEvent.ParentPasswordChanged(parentId));
        await _db.SaveChangesAsync();

        _logger.LogInformation("Parent {ParentId} changed password", parentId);
        return true;
    }

    // Default TTL for reset tokens. Overridable via
    // Auth:PasswordResetTokenTtlMinutes in config, read through the
    // plain-string IConfiguration indexer (Application project does
    // not reference the Configuration.Binder package — same pattern
    // the retention worker documents).
    private const int DefaultPasswordResetTtlMinutes = 60;

    public async Task RequestPasswordResetAsync(
        string email, CancellationToken cancellationToken = default)
    {
        // Mandatory timing normalization — BCrypt runs on both paths.
        // Computing a throwaway hash BEFORE the email lookup means the
        // unknown-email path pays the same latency as the known-email
        // path. Mirrors the RegisterAsync anti-enumeration slice. The
        // value is discarded in both branches below.
        _ = _hashPassword(email);

        var parent = await _db.Set<Parent>()
            .FirstOrDefaultAsync(p => p.Email == email, cancellationToken);
        if (parent == null)
        {
            // Silent no-op on unknown email — part of the
            // enumeration-resistance contract. No token row, no
            // notifier call, no audit row. Server-side log is
            // email-less to avoid a log-scraping back-channel.
            _logger.LogInformation(
                "Password reset request: silent no-op (unknown email)");
            return;
        }

        // Generate a high-entropy raw token and persist ONLY its hash.
        // A DB exfil therefore does not yield usable tokens.
        var rawToken = GenerateRawResetToken();
        var tokenHash = ComputeTokenHash(rawToken);
        var ttl = TimeSpan.FromMinutes(ReadPasswordResetTtlMinutes());
        var now = DateTime.UtcNow;

        _db.Set<ParentPasswordResetToken>().Add(new ParentPasswordResetToken
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(ttl),
            ConsumedAt = null
        });
        TrackAndAddAudit(AuditEvent.ParentPasswordResetRequested(parent.Id));
        await _db.SaveChangesAsync(cancellationToken);

        // Notifier is called AFTER SaveChangesAsync so a DB write
        // failure short-circuits before we hand out a token the DB
        // does not know about. LoggingNotifier does not actually
        // deliver; a future transport will.
        await _notifier.SendPasswordResetAsync(email, rawToken, cancellationToken);

        _logger.LogInformation(
            "Password reset token issued for parent {ParentId}", parent.Id);
    }

    public async Task<bool> CompletePasswordResetAsync(string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPassword))
            return false;

        var tokenHash = ComputeTokenHash(token);
        var row = await _db.Set<ParentPasswordResetToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        var now = DateTime.UtcNow;
        // Uniform false on any of: unknown / expired / already consumed.
        // Controller maps this to a single 400 response with no reason
        // leak, so the three failure paths are indistinguishable on
        // the wire AND in the audit log (no row written).
        if (row is null || row.ConsumedAt is not null || row.ExpiresAt <= now)
            return false;

        var parent = await _db.Set<Parent>()
            .FirstOrDefaultAsync(p => p.Id == row.ParentId);
        if (parent == null)
            return false; // parent deleted between issue and completion; token is moot

        parent.PasswordHash = _hashPassword(newPassword);
        row.ConsumedAt = now;
        TrackAndAddAudit(AuditEvent.ParentPasswordResetCompleted(parent.Id));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} completed password reset", parent.Id);
        return true;
    }

    private static string GenerateRawResetToken()
    {
        // 32 bytes of CSPRNG, base64-url encoded so the token is safe
        // in a URL / email body without further escaping. URL-safe
        // alphabet; trailing '=' padding stripped since callers do
        // not need to decode the token — we only hash it.
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string ComputeTokenHash(string rawToken)
    {
        // SHA-256 hex. The raw token is already high-entropy (32-byte
        // CSPRNG), so no salt / HMAC is required — the hash exists
        // only so a DB exfil does not expose usable tokens. Constant-
        // time compare is also not required here because lookup is
        // by equality on the hash column (the DB index does the work).
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }

    private int ReadPasswordResetTtlMinutes()
    {
        var raw = _config["Auth:PasswordResetTokenTtlMinutes"];
        if (int.TryParse(raw, out var parsed) && parsed > 0)
            return parsed;
        return DefaultPasswordResetTtlMinutes;
    }

    // Default TTL for email-verification tokens: 7 days. Longer than
    // password-reset because verification is less time-sensitive —
    // a parent registering today may not open the email until the
    // weekend. Overridable via Auth:EmailVerificationTokenTtlHours.
    private const int DefaultEmailVerificationTtlHours = 168;

    private int ReadEmailVerificationTtlHours()
    {
        var raw = _config["Auth:EmailVerificationTokenTtlHours"];
        if (int.TryParse(raw, out var parsed) && parsed > 0)
            return parsed;
        return DefaultEmailVerificationTtlHours;
    }

    /// <summary>
    /// Begin email verification for the given address. Anti-enum
    /// contract: known-unverified / known-verified / unknown email
    /// must be externally indistinguishable. A throwaway BCrypt on
    /// the email normalizes the latency floor across all three
    /// branches (same seam Register and RequestPasswordResetAsync
    /// use). Only the known-unverified branch issues a token and
    /// calls the notifier.
    /// </summary>
    public async Task RequestEmailVerificationAsync(
        string email, CancellationToken cancellationToken = default)
    {
        // Mandatory timing normalization — BCrypt runs on every
        // branch. Value is discarded; it exists only to make the
        // response time invariant across the three eligibility
        // outcomes below.
        _ = _hashPassword(email);

        var parent = await _db.Set<Parent>()
            .FirstOrDefaultAsync(p => p.Email == email, cancellationToken);
        if (parent == null)
        {
            // Unknown-email path — silent. No token, no notifier call,
            // no audit row. Server-side log line is email-less to
            // avoid a log-scraping back-channel.
            _logger.LogInformation(
                "Email verification request: silent no-op (unknown email)");
            return;
        }
        if (parent.EmailVerifiedAt != null)
        {
            // Already-verified path — also silent. A real parent
            // accidentally hitting the verify-request endpoint again
            // does not receive a fresh email (no point), but the
            // HTTP response is identical to the other two branches.
            _logger.LogInformation(
                "Email verification request: silent no-op (already verified) for parent {ParentId}",
                parent.Id);
            return;
        }

        // Known-unverified: issue token + persist hash + notify.
        // Multiple active tokens per parent are permitted (same
        // contract as password-reset), so repeated verify-request
        // calls simply add more tokens; the retention worker's
        // cleanup pass handles the resulting accumulation.
        var rawToken = GenerateRawResetToken();
        var tokenHash = ComputeTokenHash(rawToken);
        var ttl = TimeSpan.FromHours(ReadEmailVerificationTtlHours());
        var now = DateTime.UtcNow;

        _db.Set<ParentEmailVerificationToken>().Add(new ParentEmailVerificationToken
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(ttl),
            ConsumedAt = null
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _notifier.SendEmailVerificationAsync(email, rawToken, cancellationToken);

        _logger.LogInformation(
            "Email verification token issued for parent {ParentId}", parent.Id);
    }

    /// <summary>
    /// Complete email verification with a previously-issued token.
    /// Returns true on success, false on any failure (unknown /
    /// expired / already-consumed / empty). Same uniform-failure
    /// contract as CompletePasswordResetAsync. Writes exactly one
    /// <see cref="AuditEventType.ParentEmailVerified"/> audit row
    /// on success; failures write none.
    /// </summary>
    public async Task<bool> CompleteEmailVerificationAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var tokenHash = ComputeTokenHash(token);
        var row = await _db.Set<ParentEmailVerificationToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        var now = DateTime.UtcNow;
        if (row is null || row.ConsumedAt is not null || row.ExpiresAt <= now)
            return false;

        var parent = await _db.Set<Parent>()
            .FirstOrDefaultAsync(p => p.Id == row.ParentId);
        if (parent == null)
            return false; // parent deleted between issue and completion

        parent.EmailVerifiedAt = now;
        row.ConsumedAt = now;
        TrackAndAddAudit(AuditEvent.ParentEmailVerified(parent.Id));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} verified email", parent.Id);
        return true;
    }

    /// <summary>
    /// Exchange a Google ID token for a parent JWT. See
    /// <see cref="IParentService.GoogleSignInAsync"/> for the full
    /// contract and CLAUDE.md § Google sign-in for the shipped rules.
    /// </summary>
    public async Task<GoogleSignInResult> GoogleSignInAsync(
        string idToken, bool acceptedTerms, CancellationToken cancellationToken = default)
    {
        // Validator is nullable at the service layer so unit tests that
        // construct ParentService directly don't have to thread a stub
        // in when they aren't exercising this path. The production DI
        // container always registers a concrete implementation.
        // Missing validator + empty ClientId are both treated as
        // "feature off" from the service's perspective and collapse
        // to the uniform InvalidToken failure — the controller is the
        // surface that renders "feature off" as a fail-closed 404.
        if (_googleValidator is null)
            return new GoogleSignInResult(GoogleSignInStatus.InvalidToken, null);

        var clientId = _config["GoogleAuth:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return new GoogleSignInResult(GoogleSignInStatus.InvalidToken, null);

        var identity = await _googleValidator.ValidateAsync(idToken, cancellationToken);
        if (identity is null)
            return new GoogleSignInResult(GoogleSignInStatus.InvalidToken, null);

        // Hard reject: Google's email_verified claim is load-bearing.
        // An attacker-controlled unverified external address on a
        // Google profile must never stamp EmailVerifiedAt on a row
        // they don't own.
        if (!identity.EmailVerified)
            return new GoogleSignInResult(GoogleSignInStatus.InvalidToken, null);

        // Defensive audience check mirrored out of the library layer —
        // ValidateAsync already rejects mismatched aud, but an
        // implementation seam might be swapped for a test double that
        // doesn't enforce it. Pinning both sides keeps the contract
        // explicit.
        if (!string.Equals(identity.Audience, clientId, StringComparison.Ordinal))
            return new GoogleSignInResult(GoogleSignInStatus.InvalidToken, null);

        var sub = identity.Subject;
        var email = identity.Email;
        if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(email))
            return new GoogleSignInResult(GoogleSignInStatus.InvalidToken, null);

        // Rule 4: returning user — lookup by GoogleSubject first.
        // Anonymized rows (Email == "") are filtered out by the
        // AnonymizedAt == null predicate so a scrubbed row cannot be
        // resurrected via its stamped subject.
        var bySub = await _db.Set<Parent>()
            .FirstOrDefaultAsync(
                p => p.GoogleSubject == sub && p.AnonymizedAt == null,
                cancellationToken);
        if (bySub is not null)
        {
            var token = GenerateJwt(bySub);
            bySub.LastLoginAt = DateTime.UtcNow;
            TrackAndAddAudit(AuditEvent.ParentGoogleSignIn(
                bySub.Id, firstTime: false, linkedToPasswordAccount: false));
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Parent {ParentId} signed in via Google (returning)", bySub.Id);
            return new GoogleSignInResult(GoogleSignInStatus.Success, token);
        }

        // Rule 5: existing row with matching email — link or reject.
        var byEmail = await _db.Set<Parent>()
            .FirstOrDefaultAsync(
                p => p.Email == email && p.AnonymizedAt == null,
                cancellationToken);
        if (byEmail is not null)
        {
            // Subject collision: the email row is already claimed by a
            // different Google identity. Never overwrite — that would
            // be an account-takeover primitive.
            if (byEmail.GoogleSubject is not null && byEmail.GoogleSubject != sub)
                return new GoogleSignInResult(GoogleSignInStatus.InvalidToken, null);

            // First-time-link terms gate: only enforced when the row
            // has no prior terms acceptance. Password-registered
            // parents always have TermsAcceptedAt set at register
            // time, so in the common "parent already registered, now
            // wants Google" flow this does not trigger.
            if (byEmail.TermsAcceptedAt is null && !acceptedTerms)
                return new GoogleSignInResult(GoogleSignInStatus.TermsRequired, null);

            var now = DateTime.UtcNow;
            byEmail.GoogleSubject = sub;
            // Stamp EmailVerifiedAt ONLY if currently null — never
            // overwrite a pre-existing verification timestamp. An
            // email verified in 2026-04 via the email-verify flow
            // keeps that timestamp when Google-links later.
            if (byEmail.EmailVerifiedAt is null)
                byEmail.EmailVerifiedAt = now;
            if (byEmail.TermsAcceptedAt is null)
            {
                byEmail.TermsAcceptedAt = now;
                byEmail.TermsVersion = CurrentTermsVersion;
            }
            byEmail.LastLoginAt = now;

            var token = GenerateJwt(byEmail);
            TrackAndAddAudit(AuditEvent.ParentGoogleSignIn(
                byEmail.Id, firstTime: true, linkedToPasswordAccount: true));
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Parent {ParentId} linked Google sign-in to existing account", byEmail.Id);
            return new GoogleSignInResult(GoogleSignInStatus.Success, token);
        }

        // Rule 6: new Google-only parent. Terms must be accepted
        // because there is no prior row to inherit acceptance from.
        if (!acceptedTerms)
            return new GoogleSignInResult(GoogleSignInStatus.TermsRequired, null);

        var createdAt = DateTime.UtcNow;
        var parent = new Parent
        {
            Id = Guid.NewGuid(),
            Email = email,
            // Empty-string PasswordHash mirrors the anonymize convention:
            // the row has no usable password and BCrypt.Verify against
            // "" cannot succeed, so /login naturally rejects a bare
            // Google parent with "Invalid email or password". The
            // forgot-password flow can later stamp a real hash if the
            // parent wants a password too.
            PasswordHash = string.Empty,
            GoogleSubject = sub,
            EmailVerifiedAt = createdAt,
            RegisteredAt = createdAt,
            TermsAcceptedAt = createdAt,
            TermsVersion = CurrentTermsVersion,
            LastLoginAt = createdAt
        };
        _db.Set<Parent>().Add(parent);
        TrackAndAddAudit(AuditEvent.ParentGoogleSignIn(
            parent.Id, firstTime: true, linkedToPasswordAccount: false));

        var newToken = GenerateJwt(parent);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Parent {ParentId} created via Google sign-in", parent.Id);
        return new GoogleSignInResult(GoogleSignInStatus.Success, newToken);
    }

    // Default threshold for the derived LinkedDeviceDto.IsDormant flag.
    // Overridable via Dormancy:Devices:NotSeenDays; missing / unparseable
    // config falls back to the shipped default. MinDormancyNotSeenDays
    // clamps a pathological override (0, negative) so a bad config can
    // never flip every device to IsDormant=true at once.
    private const int DefaultDormancyNotSeenDays = 180;
    private const int MinDormancyNotSeenDays = 1;

    private int ReadDormancyNotSeenDays()
    {
        var raw = _config["Dormancy:Devices:NotSeenDays"];
        var parsed = int.TryParse(raw, out var n) ? n : DefaultDormancyNotSeenDays;
        return parsed < MinDormancyNotSeenDays ? MinDormancyNotSeenDays : parsed;
    }

    public async Task<bool> DeleteAccountAsync(Guid parentId, string currentPassword)
    {
        // Re-authenticate: the JWT proves the caller is logged in as this
        // parent, but account deletion is destructive enough to warrant a
        // second factor. Same BCrypt.Verify pattern ChangePasswordAsync
        // uses; returns silent false on wrong password or unknown parent
        // so the caller cannot probe.
        var parent = await _db.Set<Parent>().FirstOrDefaultAsync(p => p.Id == parentId);
        if (parent == null)
            return false;

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, parent.PasswordHash))
            return false;

        // Capture the device ids BEFORE the cascade removes the
        // ParentDevice rows — otherwise we lose the information needed to
        // identify orphaned devices below.
        var linkedDeviceIds = await _db.Set<ParentDevice>()
            .Where(pd => pd.ParentId == parentId)
            .Select(pd => pd.DeviceId)
            .ToListAsync();

        // Delete the Parent. FK ParentDevices → Parents is Cascade, so
        // every ParentDevice row for this parent is removed in the same
        // transaction as the Parent row.
        _db.Set<Parent>().Remove(parent);
        await _db.SaveChangesAsync();

        // Orphan-aware device cleanup: for each device this parent had
        // linked, if no ParentDevice rows remain (i.e. this parent was
        // the last owner), the device and its data (children,
        // conversations, messages) are unreachable forever. Delete the
        // device; existing Cascade FKs handle the subtree.
        //
        // Devices still linked to another parent are preserved — the
        // multi-parent-device semantic of the ParentDevice composite key
        // is respected. UnlinkDeviceAsync applies the same orphan-cleanup
        // rule per device; this loop is the bulk equivalent when the
        // whole account goes away at once.
        int orphanedDevicesDeleted = 0;
        // C2.2a — collect every conversation id under any orphaned
        // device BEFORE its Device row is removed, so the FK cascade
        // doesn't erase the information we need for blob cleanup.
        var orphanConversationIds = new List<Guid>();
        foreach (var deviceId in linkedDeviceIds)
        {
            var stillLinked = await _db.Set<ParentDevice>()
                .AnyAsync(pd => pd.DeviceId == deviceId);
            if (stillLinked)
                continue;

            var conversationIds = await _db.Set<Conversation>()
                .Where(c => c.DeviceId == deviceId)
                .Select(c => c.Id)
                .ToListAsync();
            orphanConversationIds.AddRange(conversationIds);

            var device = await _db.Set<Device>().FindAsync(deviceId);
            if (device != null)
            {
                _db.Set<Device>().Remove(device);
                orphanedDevicesDeleted++;
            }
        }
        // DB-first commit. The Device removals fire the FK cascade
        // through Conversations / Messages / Children / ParentDevices
        // in this transaction. Audio cleanup runs against the
        // pre-collected conversation id list afterward.
        await _db.SaveChangesAsync();

        var audio = await RunAudioCleanupAsync(orphanConversationIds);

        // Audit must be written even when no orphan cleanup was needed, so
        // this SaveChangesAsync runs unconditionally now.
        TrackAndAddAudit(AuditEvent.ParentAccountDeleted(
            parentId, linkedDeviceIds.Count, orphanedDevicesDeleted,
            audioConversationsAttempted: audio.Attempted,
            audioFilesDeleted: audio.FilesDeleted,
            audioDeleteFailures: audio.Failures));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} deleted account ({LinkedDevices} linked devices, {OrphanedDevices} orphaned devices cascaded, {AudioFilesDeleted} audio files cleaned, {AudioDeleteFailures} failures)",
            parentId, linkedDeviceIds.Count, orphanedDevicesDeleted,
            audio.FilesDeleted, audio.Failures);
        return true;
    }

    public async Task<bool> DeleteChildAsync(Guid parentId, Guid childId)
    {
        // Ownership: the parent must own the device the child belongs to.
        // Same shape as SetDevicePauseStateAsync — silent false on a miss,
        // no existence leak for children owned by other parents.
        var child = await _db.Set<Child>().FirstOrDefaultAsync(c => c.Id == childId);
        if (child == null)
            return false;

        var ownsDevice = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == child.DeviceId);
        if (!ownsDevice)
            return false;

        // Service-level cascade for the one non-cascading FK:
        // Conversation.ChildId → Children is NoAction (initial migration), so
        // we must delete the child's conversations before removing the Child
        // row. Messages → Conversations IS Cascade, so Messages go with
        // their Conversations at the DB level in the same SaveChanges.
        //
        // Kept service-level (rather than a schema FK change to Cascade) so
        // the cascade is auditable in the log line below and easy to adjust
        // if product ever prefers detach-over-delete semantics.
        var conversations = await _db.Set<Conversation>()
            .Where(c => c.ChildId == childId)
            .ToListAsync();
        // Snapshot the ids BEFORE we Remove the entities — the change
        // tracker keeps Id readable while the row is queued for delete,
        // but capturing now keeps the audio cleanup loop cleanly
        // decoupled from the EF entity state.
        var conversationIds = conversations.Select(c => c.Id).ToList();
        if (conversations.Count > 0)
            _db.Set<Conversation>().RemoveRange(conversations);

        // C2.2a — DB-first. Remove Child + Conversations and commit;
        // Messages cascade at the FK level. Then run audio cleanup
        // for every conversation the child owned.
        _db.Set<Child>().Remove(child);
        await _db.SaveChangesAsync();

        var audio = await RunAudioCleanupAsync(conversationIds);

        TrackAndAddAudit(AuditEvent.ParentChildDeleted(
            parentId, childId, conversations.Count,
            audioConversationsAttempted: audio.Attempted,
            audioFilesDeleted: audio.FilesDeleted,
            audioDeleteFailures: audio.Failures));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} deleted child {ChildId} on device {DeviceId} ({ConversationCount} conversations cascaded, {AudioFilesDeleted} audio files cleaned, {AudioDeleteFailures} failures)",
            parentId, childId, child.DeviceId, conversations.Count,
            audio.FilesDeleted, audio.Failures);
        return true;
    }

    /// <summary>
    /// Manual parent-driven deletion of a single conversation the parent
    /// owns. Ownership is enforced via the <c>ParentDevice</c> join on
    /// the conversation's device — same silent-false shape as
    /// <see cref="DeleteChildAsync"/>. The controller surfaces a miss as
    /// a 404 indistinguishable from an unknown id, so no existence leak.
    /// <para>
    /// Deletion is by conversation; messages go with it via the schema
    /// FK cascade (same contract the
    /// <c>ParentServiceDeleteChildTests</c> already prove). No
    /// soft-delete, no tombstone, no batch, no interaction with the
    /// retention worker.
    /// </para>
    /// <para>
    /// One <c>ParentConversationDeleted</c> audit row is written in the
    /// same <c>SaveChangesAsync</c>. Failure paths (not found / not
    /// owned) write no audit row.
    /// </para>
    /// </summary>
    public async Task<bool> DeleteConversationAsync(Guid parentId, Guid conversationId)
    {
        var conversation = await _db.Set<Conversation>()
            .FirstOrDefaultAsync(c => c.Id == conversationId);
        if (conversation == null)
            return false;

        var ownsDevice = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == conversation.DeviceId);
        if (!ownsDevice)
            return false;

        // Count messages before the cascade fires so the audit row
        // reflects what was deleted. Counts only — no content loaded.
        var messageCount = await _db.Set<Message>()
            .CountAsync(m => m.ConversationId == conversationId);

        // C2.2a — DB-first ordering. Remove the Conversation row and
        // commit; messages cascade at the DB FK level. Audio blob
        // cleanup runs AFTER this commit so a filesystem failure
        // cannot roll back a parent-initiated delete.
        var deviceId = conversation.DeviceId;
        _db.Set<Conversation>().Remove(conversation);
        await _db.SaveChangesAsync();

        var audio = await RunAudioCleanupAsync(new[] { conversationId });

        // Audit row carries the post-cleanup counts so the durable
        // record matches what hit disk. Two SaveChanges per parent
        // action by design — see CLAUDE.md § Voice chat (C2.2a).
        TrackAndAddAudit(AuditEvent.ParentConversationDeleted(
            parentId, deviceId, conversationId, messageCount,
            audioConversationsAttempted: audio.Attempted,
            audioFilesDeleted: audio.FilesDeleted,
            audioDeleteFailures: audio.Failures));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} deleted conversation {ConversationId} on device {DeviceId} ({MessageCount} messages cascaded, {AudioFilesDeleted} audio files cleaned, {AudioDeleteFailures} failures)",
            parentId, conversationId, deviceId, messageCount,
            audio.FilesDeleted, audio.Failures);
        return true;
    }

    public async Task<bool> SetDevicePauseStateAsync(Guid parentId, Guid deviceId, bool paused)
    {
        // Ownership check: the parent must have a ParentDevice link to this
        // device id. Silent false on missing link — matches UnlinkDeviceAsync's
        // no-existence-leak shape. Same pattern used by the
        // ConversationController read endpoints.
        var linked = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId);
        if (!linked)
            return false;

        var device = await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null)
            return false;

        if (device.IsPaused == paused)
        {
            // Idempotent: already in the requested state. No mutation, no
            // log, no audit row — the system state didn't change, so there
            // is nothing to audit. Parent still sees success.
            return true;
        }

        device.IsPaused = paused;
        TrackAndAddAudit(
            AuditEvent.ParentDevicePauseStateChanged(parentId, deviceId, paused));
        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "Parent {ParentId} set device {DeviceId} paused={Paused}",
            parentId, deviceId, paused);
        return true;
    }

    public async Task<bool> SetBedtimeWindowAsync(Guid parentId, Guid deviceId, TimeOnly? start, TimeOnly? end)
    {
        // Ownership check — same silent-false shape as SetDevicePauseStateAsync.
        var linked = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId);
        if (!linked)
            return false;

        var device = await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null)
            return false;

        // Half-null is normalized to disabled; we do not reject with 400.
        // Keeps the endpoint idempotent for "clear the window".
        if (start is null || end is null)
        {
            device.BedtimeStart = null;
            device.BedtimeEnd = null;
        }
        else
        {
            device.BedtimeStart = start;
            device.BedtimeEnd = end;
        }

        // Audit the post-normalization state so the record matches what is
        // actually persisted on the Device row — half-null inputs become
        // {start:null,end:null} here, not {start:"22:00:00",end:null}.
        TrackAndAddAudit(AuditEvent.ParentBedtimeWindowSet(
            parentId, deviceId, device.BedtimeStart, device.BedtimeEnd));
        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "Parent {ParentId} set bedtime window on device {DeviceId} to {Start}-{End}",
            parentId, deviceId,
            device.BedtimeStart?.ToString() ?? "disabled",
            device.BedtimeEnd?.ToString() ?? "disabled");
        return true;
    }

    public async Task<bool> SetDeviceModeFlagsAsync(
        Guid parentId, Guid deviceId,
        bool story, bool game, bool riddle, bool curiosity)
    {
        // Ownership check — same silent-false shape as SetDevicePauseStateAsync
        // and SetBedtimeWindowAsync.
        var linked = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId);
        if (!linked)
            return false;

        var device = await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null)
            return false;

        device.StoryEnabled = story;
        device.GameEnabled = game;
        device.RiddleEnabled = riddle;
        device.CuriosityEnabled = curiosity;

        TrackAndAddAudit(AuditEvent.ParentDeviceModeFlagsSet(
            parentId, deviceId, story, game, riddle, curiosity));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} set mode flags on device {DeviceId}: story={Story} game={Game} riddle={Riddle} curiosity={Curiosity}",
            parentId, deviceId, story, game, riddle, curiosity);
        return true;
    }

    public async Task<bool> SetChildModeOverridesAsync(
        Guid parentId, Guid childId,
        bool? story, bool? game, bool? riddle, bool? curiosity)
    {
        // Ownership shape mirrors DeleteChildAsync: parent must own the
        // device the child belongs to. Silent false on a miss — no
        // existence leak for children owned by other parents.
        var child = await _db.Set<Child>().FirstOrDefaultAsync(c => c.Id == childId);
        if (child == null)
            return false;

        var ownsDevice = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == child.DeviceId);
        if (!ownsDevice)
            return false;

        child.StoryEnabled = story;
        child.GameEnabled = game;
        child.RiddleEnabled = riddle;
        child.CuriosityEnabled = curiosity;

        TrackAndAddAudit(AuditEvent.ChildModeOverridesSet(
            parentId, childId, story, game, riddle, curiosity));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} set child {ChildId} mode overrides: story={Story} game={Game} riddle={Riddle} curiosity={Curiosity}",
            parentId, childId, story, game, riddle, curiosity);
        return true;
    }

    public async Task<List<LinkedDeviceDto>> GetLinkedDeviceDetailsAsync(Guid parentId)
    {
        var links = await _db.Set<ParentDevice>()
            .Where(pd => pd.ParentId == parentId)
            .Join(_db.Set<Device>(), pd => pd.DeviceId, d => d.Id, (pd, d) => new { pd.LinkedAt, Device = d })
            .ToListAsync();

        if (links.Count == 0)
            return new List<LinkedDeviceDto>();

        var deviceIds = links.Select(l => l.Device.Id).ToList();

        var childrenByDevice = (await _db.Set<Child>()
            .Where(c => deviceIds.Contains(c.DeviceId))
            .ToListAsync())
            .GroupBy(c => c.DeviceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lastConversationByDevice = await _db.Set<Conversation>()
            .Where(c => deviceIds.Contains(c.DeviceId))
            .GroupBy(c => c.DeviceId)
            .Select(g => new { DeviceId = g.Key, LastStartedAt = g.Max(c => c.StartedAt) })
            .ToDictionaryAsync(x => x.DeviceId, x => x.LastStartedAt);

        // Single computation site for IsDormant. Snapshot UtcNow and the
        // threshold once per call so every device in this response is
        // evaluated against the same cutoff (no drift across the loop).
        // Reporting-only: this flag does NOT feed into chat gates,
        // retention, or any behavior change.
        var nowUtc = DateTime.UtcNow;
        var dormancyCutoff = nowUtc.AddDays(-ReadDormancyNotSeenDays());

        return links.Select(l => new LinkedDeviceDto(
            l.Device.Id,
            l.Device.Name,
            l.Device.LastSeenAt,
            l.LinkedAt,
            lastConversationByDevice.TryGetValue(l.Device.Id, out var lastConv) ? lastConv : null,
            childrenByDevice.TryGetValue(l.Device.Id, out var children)
                ? children.Select(c => new LinkedDeviceChildDto(
                    c.Id, c.Name, c.GetAge(), c.Gender,
                    c.StoryEnabled, c.GameEnabled, c.RiddleEnabled, c.CuriosityEnabled)).ToList()
                : new List<LinkedDeviceChildDto>(),
            l.Device.IsPaused,
            l.Device.BedtimeStart,
            l.Device.BedtimeEnd,
            l.Device.StoryEnabled,
            l.Device.GameEnabled,
            l.Device.RiddleEnabled,
            l.Device.CuriosityEnabled,
            IsDormant: l.Device.LastSeenAt <= dormancyCutoff
        )).ToList();
    }

    /// <summary>
    /// Devices + small self-scoped dormancy summary. The summary is
    /// observational — device counts are aggregated from the already-
    /// derived <see cref="LinkedDeviceDto.IsDormant"/> booleans returned
    /// by <see cref="GetLinkedDeviceDetailsAsync"/> (single derivation
    /// site), and <c>LastLoginAt</c> is the raw nullable column on
    /// <c>Parent</c>. No threshold policy is applied to the parent-level
    /// signal; a "parent is dormant" boolean would be misleading at this
    /// stage and is deliberately omitted.
    /// </summary>
    public async Task<LinkedDevicesResponse> GetLinkedDeviceDetailsWithSummaryAsync(Guid parentId)
    {
        var devices = await GetLinkedDeviceDetailsAsync(parentId);

        // Two-column projection in one round-trip — fetches LastLoginAt
        // AND EmailVerifiedAt from the Parent row. The intermediate
        // anonymous-type projection avoids materializing the full
        // Parent entity onto this process. Both fields are nullable
        // and collapse to "not available yet" / "not verified" on
        // the dashboard side.
        var profile = await _db.Set<Parent>()
            .Where(p => p.Id == parentId)
            .Select(p => new { p.LastLoginAt, p.EmailVerifiedAt })
            .FirstOrDefaultAsync();

        var summary = new DormancySummaryDto(
            TotalDevices: devices.Count,
            DormantDevices: devices.Count(d => d.IsDormant),
            LastLoginAt: profile?.LastLoginAt,
            EmailVerifiedAt: profile?.EmailVerifiedAt);

        return new LinkedDevicesResponse(devices, summary);
    }

    /// <summary>
    /// Minimal authenticated-parent profile read used by the
    /// dashboard's verification-visibility surface to know the
    /// parent's email (so the "Send verification email" button can
    /// pass it to <c>POST /api/parents/verify-request</c>) and the
    /// verification timestamp. Single-row two-column projection.
    /// Returns null when the parent row no longer exists — controller
    /// maps null to a 404, matching the silent-miss convention for
    /// parent-owned reads. Anonymized parents (Email == "") are
    /// filtered at the query level so a stale-JWT-against-anonymized-
    /// row case behaves identically to a missing row.
    /// </summary>
    public async Task<ParentMeResponse?> GetMeAsync(Guid parentId)
    {
        return await _db.Set<Parent>()
            .Where(p => p.Id == parentId && p.AnonymizedAt == null)
            .Select(p => new ParentMeResponse(p.Email, p.EmailVerifiedAt))
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Collates the authenticated parent's full data export scope and
    /// writes a <c>ParentDataExported</c> audit row in the same
    /// transaction. Returns <c>null</c> when the parent row no longer
    /// exists (JWT was valid but the account was deleted between
    /// token issue and this call) — controller converts this to an
    /// ambiguous 404 / no-body, matching the repo's silent-miss
    /// convention for parent-owned reads.
    /// <para>
    /// Scope invariants enforced here:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Every nested collection is filtered by
    /// <paramref name="parentId"/> at query time (via the
    /// <c>ParentDevice</c> join). No cross-parent bleed.</description></item>
    /// <item><description>Neither <c>Parent.PasswordHash</c> nor
    /// <c>Device.ApiKey</c> is projected — they simply do not appear
    /// on the <see cref="ParentExportProfile"/> /
    /// <see cref="ParentExportDevice"/> records.</description></item>
    /// <item><description>Audit rows included are this parent's own
    /// only (same <c>ActorParentId == parentId</c> filter the audit
    /// read endpoint uses) and deliberately <b>unpaginated</b> — an
    /// export is a complete-history snapshot, not a viewport.</description></item>
    /// </list>
    /// </summary>
    public async Task<ParentExport?> BuildExportAsync(Guid parentId)
    {
        var parent = await _db.Set<Parent>().FirstOrDefaultAsync(p => p.Id == parentId);
        if (parent == null)
            return null;

        var linkedDeviceIds = await _db.Set<ParentDevice>()
            .Where(pd => pd.ParentId == parentId)
            .Select(pd => pd.DeviceId)
            .ToListAsync();

        var devices = linkedDeviceIds.Count == 0
            ? new List<Device>()
            : await _db.Set<Device>()
                .Where(d => linkedDeviceIds.Contains(d.Id))
                .ToListAsync();

        var children = linkedDeviceIds.Count == 0
            ? new List<Child>()
            : await _db.Set<Child>()
                .Where(c => linkedDeviceIds.Contains(c.DeviceId))
                .ToListAsync();

        var conversations = linkedDeviceIds.Count == 0
            ? new List<Conversation>()
            : await _db.Set<Conversation>()
                .Where(c => linkedDeviceIds.Contains(c.DeviceId))
                .Include(c => c.Messages.OrderBy(m => m.Timestamp))
                .OrderByDescending(c => c.StartedAt)
                .ToListAsync();

        // Per-parent audit feed — same filter shape the read endpoint
        // uses, but unpaginated because an export is a full-history
        // snapshot. The AppMeter.AuditEventsWritten tag is bounded and
        // the underlying table is parent-scoped at the row level.
        var auditRows = await _db.Set<AuditEvent>()
            .Where(a => a.ActorParentId == parentId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();

        var conversationsByDevice = conversations
            .GroupBy(c => c.DeviceId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var childrenByDevice = children
            .GroupBy(c => c.DeviceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var deviceExports = devices.Select(d => new ParentExportDevice(
            d.Id,
            d.MacAddress,
            d.Name,
            d.RegisteredAt,
            d.LastSeenAt,
            d.IsPaused,
            d.BedtimeStart,
            d.BedtimeEnd,
            d.TimeZone,
            d.StoryEnabled,
            d.GameEnabled,
            d.RiddleEnabled,
            d.CuriosityEnabled,
            childrenByDevice.TryGetValue(d.Id, out var deviceChildren)
                ? deviceChildren.Select(c => new ParentExportChild(
                    c.Id,
                    c.DeviceId,
                    c.Name,
                    c.Gender,
                    c.DateOfBirth,
                    c.GetAge(),
                    new ParentExportChildModeOverrides(
                        c.StoryEnabled, c.GameEnabled, c.RiddleEnabled, c.CuriosityEnabled)
                )).ToList()
                : new List<ParentExportChild>(),
            conversationsByDevice.TryGetValue(d.Id, out var deviceConvos)
                ? deviceConvos.Select(c => new ConversationDto(
                    c.Id,
                    c.DeviceId,
                    c.StartedAt,
                    c.EndedAt,
                    c.Messages.Count,
                    c.Messages.Any(m => m.SafetyFlag != SafetyFlag.Clean),
                    c.Messages.Select(m => new MessageDto(
                        m.Id,
                        m.Role.ToString().ToLower(),
                        m.Content,
                        m.Timestamp,
                        m.SafetyFlag,
                        AudioAvailable: m.Role == MessageRole.Assistant && m.AudioBlobPath != null
                    )).ToList()
                )).ToList()
                : new List<ConversationDto>()
        )).ToList();

        var auditDtos = auditRows.Select(a => new AuditEventDto(
            a.Id,
            a.Timestamp,
            a.EventType.ToString(),
            a.TargetDeviceId,
            a.TargetChildId,
            a.Metadata is null ? null : JsonNode.Parse(a.Metadata)
        )).ToList();

        int messageCount = conversations.Sum(c => c.Messages.Count);
        // Same-transaction audit write. Counts only — no PII, no content,
        // no identifiers beyond ActorParentId (already carried in the
        // dedicated column). Includes the audit-row count computed BEFORE
        // this write so the number reflects the rows that appear in the
        // export body.
        TrackAndAddAudit(AuditEvent.ParentDataExported(
            parentId,
            devices: devices.Count,
            children: children.Count,
            conversations: conversations.Count,
            messages: messageCount,
            auditEvents: auditRows.Count));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} exported data ({Devices} devices, {Children} children, {Conversations} conversations, {Messages} messages, {AuditEvents} audit events)",
            parentId, devices.Count, children.Count, conversations.Count, messageCount, auditRows.Count);

        return new ParentExport(
            SchemaVersion: "1",
            GeneratedAt: DateTime.UtcNow,
            Parent: new ParentExportProfile(
                parent.Id,
                parent.Email,
                parent.RegisteredAt,
                parent.TermsAcceptedAt,
                parent.TermsVersion,
                parent.LastLoginAt,
                parent.EmailVerifiedAt,
                parent.GoogleSubject),
            Devices: deviceExports,
            AuditEvents: auditDtos,
            ExcludedFields: new[]
            {
                // Reader-facing disclosure of what was intentionally left out.
                // Keep this list in sync with the projection shapes above —
                // any field added to Parent / Device that is credential-like
                // or system-only belongs here.
                "Parent.PasswordHash",
                "Device.ApiKey",
                "Device.ApiKeyHash"
            });
    }

    public async Task<List<AuditEventDto>> GetAuditEventsForParentAsync(
        Guid parentId, int limit, int offset)
    {
        // Ownership is enforced at the query level — there is no code path
        // below that could return another parent's row. Caller is already
        // [Authorize]'d in the controller; the parentId comes from their
        // JWT claim. Caller is also responsible for clamping limit/offset.
        var rows = await _db.Set<AuditEvent>()
            .Where(a => a.ActorParentId == parentId)
            .OrderByDescending(a => a.Timestamp)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return rows.Select(a => new AuditEventDto(
            a.Id,
            a.Timestamp,
            a.EventType.ToString(),
            a.TargetDeviceId,
            a.TargetChildId,
            // Parsed JsonNode so the wire payload is an actual JSON object,
            // not an escaped string. Factory code is the only writer so the
            // blob is always valid JSON (or null).
            a.Metadata is null ? null : JsonNode.Parse(a.Metadata)
        )).ToList();
    }

    /// <summary>
    /// C2.1 — resolve a message id for parent-dashboard audio replay.
    /// Single join query — Message → Conversation → ParentDevice — that
    /// also gates on role and AudioBlobPath. Every miss reason
    /// (unknown message id, conversation owned by another parent,
    /// child/user role, or null AudioBlobPath) collapses to the same
    /// <c>null</c> return so the controller surfaces an identical 404
    /// in every case. No existence leak across families; child WAV
    /// uploads are never replayable here even if their AudioBlobPath
    /// is populated. No DB write, no audit row.
    /// </summary>
    public async Task<(Guid ConversationId, Guid MessageId)?> GetAssistantAudioMessageAsync(
        Guid parentId, Guid messageId)
    {
        var hit = await _db.Set<Message>()
            .Where(m => m.Id == messageId
                     && m.Role == MessageRole.Assistant
                     && m.AudioBlobPath != null)
            .Join(
                _db.Set<Conversation>(),
                m => m.ConversationId,
                c => c.Id,
                (m, c) => new { ConversationId = c.Id, MessageId = m.Id, c.DeviceId })
            .Where(x => _db.Set<ParentDevice>().Any(
                pd => pd.ParentId == parentId && pd.DeviceId == x.DeviceId))
            .Select(x => new { x.ConversationId, x.MessageId })
            .FirstOrDefaultAsync();

        if (hit is null)
            return null;
        return (hit.ConversationId, hit.MessageId);
    }

    // C2.2a — sum-aggregator for the audio-blob delete-cascade hook.
    // Runs per-conversation cleanup AFTER the destructive DB
    // SaveChanges has already committed, then returns the three
    // count-shaped fields the existing parent-driven audit factories
    // now carry (audio_conversations_attempted / audio_files_deleted /
    // audio_delete_failures). IO failures and missing directories are
    // both non-fatal here — the destructive parent action is the
    // source of truth, residue is a future C2.3 sweeper concern.
    // OperationCanceledException is the one exception that is allowed
    // to propagate so cooperative cancellation stays visible.
    private async Task<(int Attempted, int FilesDeleted, int Failures)>
        RunAudioCleanupAsync(IReadOnlyCollection<Guid> conversationIds)
    {
        if (conversationIds.Count == 0)
            return (0, 0, 0);

        int attempted = 0;
        int filesDeleted = 0;
        int failures = 0;
        foreach (var id in conversationIds)
        {
            attempted++;
            try
            {
                var result = await _blobStore.DeleteConversationAudioAsync(id);
                filesDeleted += result.FilesDeleted;
                if (result.Failed)
                    failures++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Defensive — implementations are documented to swallow
                // per-file IO failures, but if a future implementation
                // throws (or the in-memory test double does), do not
                // unwind the parent action. DB delete already committed.
                _logger.LogWarning(ex,
                    "Audio cleanup unexpectedly threw for conversation_id={ConversationId}; counting as failure",
                    id);
                failures++;
            }
        }
        return (attempted, filesDeleted, failures);
    }

    // Central write path for audit rows: Adds the entity to the DbSet
    // AND increments AppMeter.AuditEventsWritten with the event_type tag.
    // Must be called exactly once per audit entity before the
    // SaveChangesAsync that persists it — same-transaction discipline
    // is preserved because the DbSet.Add happens here, not later.
    // The counter is volatile (process-local, scraped via /metrics) and
    // complements — does not replace — the durable AuditEvents row.
    private void TrackAndAddAudit(AuditEvent ev)
    {
        _db.Set<AuditEvent>().Add(ev);
        AppMeter.AuditEventsWritten.Add(1,
            new KeyValuePair<string, object?>("event_type", ev.EventType.ToString()));
    }

    private string GenerateJwt(Parent parent)
    {
        // Sign with the PRIMARY key only — the first element of the
        // ordered list from Jwt:Keys (or the scalar Jwt:Key fallback).
        // Previous keys on the list, if any, are accepted by the
        // validator in Program.cs but never used to sign new tokens.
        // The helper applies the same legacy-insecure-default rejection
        // the old inline guard did, over the full configured set.
        var resolvedKeys = JwtKeys.ResolveOrderedKeys(_config);
        var primary = JwtKeys.PrimaryKey(resolvedKeys);
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(primary));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parent.Id.ToString()),
            new Claim(ClaimTypes.Email, parent.Email)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "ArmenianAiToy",
            audience: _config["Jwt:Audience"] ?? "ArmenianAiToy",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
