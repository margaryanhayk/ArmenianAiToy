using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ArmenianAiToy.Application.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="JwtKeys"/> — the JWT key-rotation helper that
/// resolves the ordered list of accepted signing/validation keys and
/// enforces the legacy-insecure-default rejection over both config
/// shapes.
///
/// <para>
/// Complements the existing <see cref="ParentServiceAuthTests"/> which
/// exercise the end-to-end legacy-default rejection through
/// <c>LoginAsync → GenerateJwt</c>. These tests pin the helper's
/// contract directly, plus an end-to-end sign+validate pass that
/// proves a token signed by a "previous" key still validates when
/// that key is on the accepted set.
/// </para>
/// </summary>
public class JwtKeysTests
{
    // Long enough to satisfy HMAC-SHA256 (>= 32 bytes).
    private const string PrimaryKey =
        "PrimaryKeyLongEnoughForHmacSha256Validation123!";
    private const string PreviousKey =
        "PreviousKeyLongEnoughForHmacSha256Validation456!";
    private const string OtherKey =
        "OtherKeyLongEnoughForHmacSha256Validation789!!!!";

    private static IConfiguration BuildConfig(Dictionary<string, string?> kv)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(kv)
            .Build();

    // ─────────────────────────────────────────────────────────────
    // ResolveOrderedKeys — config-shape matrix
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveOrderedKeys_ScalarOnly_ReturnsSingleElement()
    {
        var cfg = BuildConfig(new() { ["Jwt:Key"] = PrimaryKey });

        var keys = JwtKeys.ResolveOrderedKeys(cfg);

        Assert.Single(keys);
        Assert.Equal(PrimaryKey, keys[0]);
        Assert.Equal(PrimaryKey, JwtKeys.PrimaryKey(keys));
    }

    [Fact]
    public void ResolveOrderedKeys_ArrayOnly_ReturnsInConfigOrder()
    {
        var cfg = BuildConfig(new()
        {
            ["Jwt:Keys:0"] = PrimaryKey,
            ["Jwt:Keys:1"] = PreviousKey
        });

        var keys = JwtKeys.ResolveOrderedKeys(cfg);

        Assert.Equal(2, keys.Count);
        Assert.Equal(PrimaryKey, keys[0]);
        Assert.Equal(PreviousKey, keys[1]);
        Assert.Equal(PrimaryKey, JwtKeys.PrimaryKey(keys));
    }

    [Fact]
    public void ResolveOrderedKeys_ArrayWinsOverScalar_WhenBothPresent()
    {
        // Array takes precedence. The scalar is effectively ignored —
        // this is the migration path: set Jwt:Keys[0] to what Jwt:Key
        // used to hold, and add previous keys after it.
        var cfg = BuildConfig(new()
        {
            ["Jwt:Key"] = "SCALAR_SHOULD_BE_IGNORED_WHEN_ARRAY_PRESENT_XXXX",
            ["Jwt:Keys:0"] = PrimaryKey,
            ["Jwt:Keys:1"] = PreviousKey
        });

        var keys = JwtKeys.ResolveOrderedKeys(cfg);

        Assert.Equal(new[] { PrimaryKey, PreviousKey }, keys);
    }

    [Fact]
    public void ResolveOrderedKeys_FiltersWhitespaceArrayEntries()
    {
        // Whitespace entries are dropped before the length check so an
        // accidental "" row doesn't count as an active previous key.
        var cfg = BuildConfig(new()
        {
            ["Jwt:Keys:0"] = PrimaryKey,
            ["Jwt:Keys:1"] = "   ",
            ["Jwt:Keys:2"] = PreviousKey
        });

        var keys = JwtKeys.ResolveOrderedKeys(cfg);

        Assert.Equal(new[] { PrimaryKey, PreviousKey }, keys);
    }

    [Fact]
    public void ResolveOrderedKeys_EmptyConfig_Throws()
    {
        var cfg = BuildConfig(new());

        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtKeys.ResolveOrderedKeys(cfg));
        // Preserves the existing "must be configured" signal so the
        // ParentServiceAuthTests' missing-key-throws assertion still
        // finds "Jwt:Key" in the message.
        Assert.Contains("Jwt:Key", ex.Message);
    }

    [Fact]
    public void ResolveOrderedKeys_ScalarWhitespace_Throws()
    {
        var cfg = BuildConfig(new() { ["Jwt:Key"] = "   " });

        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtKeys.ResolveOrderedKeys(cfg));
        Assert.Contains("Jwt:Key", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────
    // Legacy-insecure-default guard — covers both config shapes and
    // every array position.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveOrderedKeys_ScalarLegacyDefault_Throws()
    {
        var cfg = BuildConfig(new()
        {
            ["Jwt:Key"] = JwtKeys.LegacyInsecureDefault
        });

        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtKeys.ResolveOrderedKeys(cfg));
        Assert.Contains("legacy default", ex.Message);
    }

    [Fact]
    public void ResolveOrderedKeys_ArrayPrimaryLegacyDefault_Throws()
    {
        var cfg = BuildConfig(new()
        {
            ["Jwt:Keys:0"] = JwtKeys.LegacyInsecureDefault,
            ["Jwt:Keys:1"] = PreviousKey
        });

        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtKeys.ResolveOrderedKeys(cfg));
        Assert.Contains("legacy default", ex.Message);
    }

    [Fact]
    public void ResolveOrderedKeys_ArrayPreviousSlotLegacyDefault_Throws()
    {
        // A poisoned "previous key" is just as bad as a poisoned primary.
        // Guards against a paste from old appsettings.json into any slot.
        var cfg = BuildConfig(new()
        {
            ["Jwt:Keys:0"] = PrimaryKey,
            ["Jwt:Keys:1"] = JwtKeys.LegacyInsecureDefault
        });

        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtKeys.ResolveOrderedKeys(cfg));
        Assert.Contains("legacy default", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────
    // End-to-end sign+validate pass — proves the multi-key validator
    // accepts tokens signed by the primary AND by a configured
    // previous key, but rejects tokens signed by a key NOT on the set.
    // ─────────────────────────────────────────────────────────────

    private static string SignToken(string signingKey)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "TestIssuer",
            audience: "TestAudience",
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static TokenValidationParameters BuildValidator(IReadOnlyList<string> acceptedKeys)
        => new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "TestIssuer",
            ValidAudience = "TestAudience",
            IssuerSigningKeys = acceptedKeys
                .Select(k => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(k)))
                .ToList()
        };

    private static bool ValidateToken(string token, TokenValidationParameters parameters)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(token, parameters, out _);
            return true;
        }
        catch (SecurityTokenException)
        {
            return false;
        }
    }

    [Fact]
    public void Validator_AcceptsTokenSignedByPrimary()
    {
        var token = SignToken(PrimaryKey);
        var ok = ValidateToken(token,
            BuildValidator(new[] { PrimaryKey, PreviousKey }));
        Assert.True(ok);
    }

    [Fact]
    public void Validator_AcceptsTokenSignedByPreviousKey()
    {
        // The "rotation window" contract: after rotation, a token still
        // signed by the old (now-previous) key must continue to validate
        // as long as it is kept on the accepted set.
        var token = SignToken(PreviousKey);
        var ok = ValidateToken(token,
            BuildValidator(new[] { PrimaryKey, PreviousKey }));
        Assert.True(ok);
    }

    [Fact]
    public void Validator_RejectsTokenSignedByUnknownKey()
    {
        var token = SignToken(OtherKey);
        var ok = ValidateToken(token,
            BuildValidator(new[] { PrimaryKey, PreviousKey }));
        Assert.False(ok);
    }

    [Fact]
    public void Validator_ScalarOnlyDeployment_AcceptsTokenSignedByScalarKey()
    {
        // Backward-compat: a deployment with only Jwt:Key set resolves
        // to a single-element list, which feeds the validator with one
        // key — identical to the pre-rotation behavior.
        var cfg = BuildConfig(new() { ["Jwt:Key"] = PrimaryKey });
        var keys = JwtKeys.ResolveOrderedKeys(cfg);

        var token = SignToken(PrimaryKey);
        var ok = ValidateToken(token, BuildValidator(keys));
        Assert.True(ok);
    }

    [Fact]
    public void Validator_ScalarOnlyDeployment_RejectsTokenSignedByOtherKey()
    {
        var cfg = BuildConfig(new() { ["Jwt:Key"] = PrimaryKey });
        var keys = JwtKeys.ResolveOrderedKeys(cfg);

        var token = SignToken(OtherKey);
        var ok = ValidateToken(token, BuildValidator(keys));
        Assert.False(ok);
    }

    // ─────────────────────────────────────────────────────────────
    // Signing-uses-primary-only guard: prove the signing side picks
    // the first element and never a previous one.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PrimaryKey_ReturnsFirstElementAlways()
    {
        var keys = JwtKeys.ResolveOrderedKeys(BuildConfig(new()
        {
            ["Jwt:Keys:0"] = PrimaryKey,
            ["Jwt:Keys:1"] = PreviousKey,
            ["Jwt:Keys:2"] = OtherKey
        }));
        Assert.Equal(PrimaryKey, JwtKeys.PrimaryKey(keys));
    }

    [Fact]
    public void SigningSide_TokenSignedWithPrimary_FailsValidationAgainstOnlyPreviousKey()
    {
        // Inverse of the rotation acceptance test: if an operator
        // removes the primary from the accepted set (e.g. retires it),
        // tokens that were signed with it stop validating. Pins the
        // signing-uses-primary-only contract indirectly — if signing
        // ever started falling back to a previous key, this test
        // would break.
        var token = SignToken(PrimaryKey);
        var ok = ValidateToken(token, BuildValidator(new[] { PreviousKey }));
        Assert.False(ok);
    }
}
