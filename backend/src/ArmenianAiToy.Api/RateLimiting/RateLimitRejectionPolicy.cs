using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace ArmenianAiToy.Api.RateLimiting;

/// <summary>
/// Small helper that derives the <c>policy</c> tag value for a
/// rate-limit rejection. Exists so the shared <c>OnRejected</c> handler
/// in <c>Program.cs</c> can tag <c>aat_rate_limit_rejected_total</c>
/// with whichever of the two named policies actually tripped — without
/// coupling the metric to routes, IPs, device ids, or any other
/// high-cardinality dimension.
///
/// <para>
/// <b>Bounded value space:</b> the returned string is guaranteed to be
/// exactly one of <see cref="ChatTag"/> or <see cref="AuthTag"/>. The
/// AppMeter contract's no-high-cardinality invariant is preserved by
/// construction — this slice's map is two values total.
/// </para>
///
/// <para>
/// <b>Inference approach:</b> endpoint-routing runs before the
/// rate-limiter middleware (the limiter acts on
/// <c>[EnableRateLimiting]</c> metadata), so by the time
/// <c>OnRejected</c> fires the matched endpoint has resolved and its
/// <see cref="EnableRateLimitingAttribute"/> is readable. We look
/// there, not at the request path — paths can change, but the
/// attribute IS the thing that binds the endpoint to a limiter policy.
/// If, defensively, no attribute is present or the policy name is
/// unrecognized, we fall back to <see cref="ChatTag"/> (the older of
/// the two policies); that keeps the value space bounded and loudly
/// surfaces a misconfigured endpoint only at call sites that read the
/// tag.
/// </para>
/// </summary>
public static class RateLimitRejectionPolicy
{
    public const string ChatTag = "chat";
    public const string AuthTag = "auth";

    public static string ResolvePolicyTag(HttpContext context)
    {
        var policyName = context.GetEndpoint()
            ?.Metadata
            .GetMetadata<EnableRateLimitingAttribute>()
            ?.PolicyName;

        return policyName == AuthRateLimiter.PolicyName
            ? AuthTag
            : ChatTag;
    }
}
