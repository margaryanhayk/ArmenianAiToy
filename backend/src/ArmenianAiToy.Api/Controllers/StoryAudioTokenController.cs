using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// Mints a short-lived, signed access token for the header-less
/// <c>GET /api/story-audio</c> stream. Route is under <c>/api/chat</c>,
/// so <c>DeviceAuthMiddleware</c> authenticates the caller with the
/// usual <c>X-Device-Id</c> / <c>X-Api-Key</c> headers (which the
/// firmware CAN send on a normal HTTP GET — it's only the audio-stream
/// client that cannot). The device fetches a token here, then appends it
/// to the story-audio URL as <c>?token=...</c>.
///
/// <para>
/// When <c>StoryAudio:SigningKey</c> is empty the feature is OFF: the
/// endpoint returns <c>{ token: null, enforced: false }</c> so the
/// firmware knows it can stream without a token (dev/bench). Enforcement
/// is the single config flip — set the key and the story-audio stream
/// starts rejecting token-less requests.
/// </para>
/// </summary>
[ApiController]
[Route("api/chat/story-audio-token")]
[EnableRateLimiting(ChatRateLimiter.PolicyName)]
public class StoryAudioTokenController : ControllerBase
{
    // #038 — shortened from 3600s (1h). A leaked token is usable for at most
    // this window; 15 min comfortably covers a story + in-story Q&A session,
    // and the device can re-mint via this device-authed endpoint if a long
    // unattended session outlives it. Override with StoryAudio:TokenTtlSeconds.
    private const int DefaultTtlSeconds = 900;

    private readonly ICuratedStoryLibrary _library;
    private readonly IConfiguration _config;

    public StoryAudioTokenController(ICuratedStoryLibrary library, IConfiguration config)
    {
        _library = library;
        _config = config;
    }

    /// <summary>Issues a token bound to <paramref name="storyId"/>.
    /// 404 for an unknown story (same shape as the story-audio stream),
    /// so this endpoint is not a story-existence oracle beyond what the
    /// stream already reveals to an authenticated device.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public IActionResult Get([FromQuery] string storyId)
    {
        if (_library.GetById(storyId) is null)
        {
            return NotFound(new { error = "Unknown story." });
        }

        var signingKey = _config["StoryAudio:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            // Enforcement off — stream is open; no token needed.
            return Ok(new { token = (string?)null, enforced = false, expiresInSeconds = 0 });
        }

        var ttlSeconds = DefaultTtlSeconds;
        if (int.TryParse(_config["StoryAudio:TokenTtlSeconds"], out var configured) && configured > 0)
        {
            ttlSeconds = configured;
        }

        // #038 — optionally bind the token to the caller's source IP so a
        // leaked token cannot be replayed from elsewhere. Opt-in
        // (StoryAudio:BindTokenToIp, default off) because a client whose IP
        // changes mid-session (NAT / mobile) would otherwise lose access; the
        // IP is the proxy-corrected RemoteIpAddress (#039). When off, an
        // unbound token is issued and the stream enforces no IP.
        string? boundIp = null;
        if (bool.TryParse(_config["StoryAudio:BindTokenToIp"], out var bind) && bind)
        {
            boundIp = HttpContext?.Connection?.RemoteIpAddress?.ToString();
        }

        var token = StoryAudioToken.Issue(
            storyId, DateTimeOffset.UtcNow.AddSeconds(ttlSeconds), signingKey, boundIp);
        return Ok(new { token, enforced = true, expiresInSeconds = ttlSeconds });
    }
}
