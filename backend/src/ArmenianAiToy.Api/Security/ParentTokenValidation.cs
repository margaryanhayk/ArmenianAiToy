using ArmenianAiToy.Application.Auth;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ArmenianAiToy.Api.Security;

/// <summary>
/// N10 — the <c>OnTokenValidated</c> handler for parent JWTs, extracted
/// from <c>Program.cs</c> so it can be exercised in a unit test with a real
/// <see cref="TokenValidatedContext"/> and a real <see cref="AppDbContext"/>.
///
/// <para>
/// One DB round-trip per authenticated request, same as before this slice
/// (the existence check was already there): the projection now returns the
/// row's <c>SecurityStamp</c> instead of a bare <c>Any</c>, and
/// <see cref="ParentSecurityStampClaim.Decide"/> turns that plus the
/// token's <c>sst</c> claim into accept / reject. Every rejection goes
/// through <c>ctx.Fail</c>, so the wire shape is the JWT bearer
/// challenge's plain 401 — identical to the pre-N10 "account no longer
/// exists" rejection; a client cannot tell "stamp rotated" from "token
/// expired" and does not need to (both mean: log in again).
/// </para>
/// </summary>
public static class ParentTokenValidation
{
    public const string AccountGoneReason = "Account no longer exists.";
    public const string StampReason = "Session is no longer valid.";

    /// <summary>Reads <c>Jwt:RequireSecurityStamp</c> (default true).</summary>
    public static bool RequireSecurityStamp(IConfiguration config)
        => ParentSecurityStampClaim.ParseRequire(config[ParentSecurityStampClaim.RequireConfigKey]);

    /// <summary>
    /// Resolves the request-scoped <see cref="AppDbContext"/> from
    /// <c>ctx.HttpContext.RequestServices</c>, loads the parent's current
    /// stamp (anonymized rows count as gone — the row exists but its
    /// credential material is scrubbed) and fails the context on any
    /// non-accept decision.
    /// </summary>
    public static async Task ValidateAsync(TokenValidatedContext ctx, bool requireStamp)
    {
        var sub = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(sub, out var parentId))
        {
            ctx.Fail("Invalid subject.");
            return;
        }

        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        // Single query: null when the row is missing or anonymized.
        var currentStamp = await db.Set<Parent>()
            .Where(p => p.Id == parentId && p.AnonymizedAt == null)
            .Select(p => p.SecurityStamp)
            .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);

        var claimStamp = ctx.Principal?.FindFirst(ParentSecurityStampClaim.Name)?.Value;
        switch (ParentSecurityStampClaim.Decide(claimStamp, currentStamp, requireStamp))
        {
            case ParentSecurityStampClaim.Decision.Accept:
                return;
            case ParentSecurityStampClaim.Decision.AccountGone:
                ctx.Fail(AccountGoneReason);
                return;
            default:
                // Missing or mismatched stamp — one reason string for
                // both so the (already body-less) 401 leaks nothing
                // about which it was.
                ctx.Fail(StampReason);
                return;
        }
    }
}
