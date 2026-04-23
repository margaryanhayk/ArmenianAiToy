using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the Google sign-in HTTP surface:
/// <c>POST /api/parents/google-login</c> and the public
/// <c>GET /api/parents/google-config</c> UI-visibility probe. Pinned
/// contracts:
/// <list type="bullet">
///   <item><description>feature-off (empty / missing ClientId)
///   returns 404 from <c>GoogleLogin</c> and <c>{ clientId: null }</c>
///   from <c>GetGoogleConfig</c>;</description></item>
///   <item><description>feature-on delegates to the service and maps
///   Success / InvalidToken / TermsRequired to 200 / 401 / 400;</description></item>
///   <item><description><c>[EnableRateLimiting("auth")]</c> is present
///   on the login endpoint (same auth-policy bucket as register /
///   password / account-delete).</description></item>
/// </list>
/// </summary>
public class ParentControllerGoogleLoginTests
{
    private const string ValidClientId = "fake.apps.googleusercontent.com";

    private static (ParentController Controller, IParentService Service)
        CreateController(string? clientId)
    {
        var service = Substitute.For<IParentService>();
        var clock = new FakeTimeProvider(
            new DateTimeOffset(2026, 4, 23, 0, 0, 0, TimeSpan.Zero));
        var cooldown = new ExportCooldown(TimeSpan.FromSeconds(60), clock);
        var config = Substitute.For<IConfiguration>();
        config["GoogleAuth:ClientId"].Returns(clientId);
        var controller = new ParentController(service, cooldown, config);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return (controller, service);
    }

    // --- Feature-off gate --------------------------------------------

    [Fact]
    public async Task GoogleLogin_FeatureOff_EmptyClientId_Returns404_DoesNotCallService()
    {
        var (controller, service) = CreateController(clientId: "");
        var req = new ParentGoogleLoginRequest("some-id-token", AcceptedTerms: true);

        var result = await controller.GoogleLogin(req);

        Assert.IsType<NotFoundResult>(result);
        await service.DidNotReceiveWithAnyArgs()
            .GoogleSignInAsync(default!, default);
    }

    [Fact]
    public async Task GoogleLogin_FeatureOff_NullClientId_Returns404_DoesNotCallService()
    {
        var (controller, service) = CreateController(clientId: null);
        var req = new ParentGoogleLoginRequest("some-id-token", AcceptedTerms: true);

        var result = await controller.GoogleLogin(req);

        Assert.IsType<NotFoundResult>(result);
        await service.DidNotReceiveWithAnyArgs()
            .GoogleSignInAsync(default!, default);
    }

    [Fact]
    public void GoogleConfig_FeatureOff_ReturnsNullClientId()
    {
        var (controller, _) = CreateController(clientId: "");

        var result = controller.GetGoogleConfig() as OkObjectResult;

        Assert.NotNull(result);
        var body = Assert.IsType<GoogleAuthConfigResponse>(result!.Value);
        Assert.Null(body.ClientId);
    }

    [Fact]
    public void GoogleConfig_FeatureOn_ReturnsClientIdVerbatim()
    {
        var (controller, _) = CreateController(clientId: ValidClientId);

        var result = controller.GetGoogleConfig() as OkObjectResult;

        Assert.NotNull(result);
        var body = Assert.IsType<GoogleAuthConfigResponse>(result!.Value);
        Assert.Equal(ValidClientId, body.ClientId);
    }

    // --- Feature-on service-delegation mapping -----------------------

    [Fact]
    public async Task GoogleLogin_ServiceReturnsSuccess_Returns200WithToken()
    {
        var (controller, service) = CreateController(clientId: ValidClientId);
        service.GoogleSignInAsync("gtoken", true)
            .Returns(new GoogleSignInResult(GoogleSignInStatus.Success, "JWT-VALUE"));
        var req = new ParentGoogleLoginRequest("gtoken", AcceptedTerms: true);

        var result = await controller.GoogleLogin(req);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ParentLoginResponse>(ok.Value);
        Assert.Equal("JWT-VALUE", body.Token);
    }

    [Fact]
    public async Task GoogleLogin_ServiceReturnsInvalidToken_Returns401_UniformError()
    {
        var (controller, service) = CreateController(clientId: ValidClientId);
        service.GoogleSignInAsync("gtoken", true)
            .Returns(new GoogleSignInResult(GoogleSignInStatus.InvalidToken, null));
        var req = new ParentGoogleLoginRequest("gtoken", AcceptedTerms: true);

        var result = await controller.GoogleLogin(req);

        var unauth = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.NotNull(unauth.Value);
        // Uniform error message — reasons must not be disambiguated
        // on the wire (invalid token, unverified, aud mismatch, sub
        // conflict all collapse to this).
        var errProp = unauth.Value!.GetType().GetProperty("error")!;
        Assert.Equal("Sign-in failed.", errProp.GetValue(unauth.Value));
    }

    [Fact]
    public async Task GoogleLogin_ServiceReturnsTermsRequired_Returns400()
    {
        var (controller, service) = CreateController(clientId: ValidClientId);
        service.GoogleSignInAsync("gtoken", false)
            .Returns(new GoogleSignInResult(GoogleSignInStatus.TermsRequired, null));
        var req = new ParentGoogleLoginRequest("gtoken", AcceptedTerms: false);

        var result = await controller.GoogleLogin(req);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GoogleLogin_EmptyIdToken_Returns401_DoesNotCallService()
    {
        var (controller, service) = CreateController(clientId: ValidClientId);
        var req = new ParentGoogleLoginRequest(IdToken: "", AcceptedTerms: true);

        var result = await controller.GoogleLogin(req);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await service.DidNotReceiveWithAnyArgs()
            .GoogleSignInAsync(default!, default);
    }

    // --- Rate limiting wiring ---------------------------------------

    [Fact]
    public void GoogleLogin_HasAuthRateLimitAttribute()
    {
        // The Google sign-in endpoint is an authentication surface;
        // it must share the auth-policy bucket with register / login /
        // password-change / delete-account (not the chat bucket).
        var method = typeof(ParentController)
            .GetMethod(nameof(ParentController.GoogleLogin));
        Assert.NotNull(method);
        var attrs = method!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .ToList();
        var attr = Assert.Single(attrs);
        Assert.Equal(AuthRateLimiter.PolicyName, attr.PolicyName);
    }

    [Fact]
    public void GoogleConfig_IsNotRateLimited()
    {
        // The public config probe is static-per-deployment and not an
        // account-existence oracle. Keep it out of the auth bucket so
        // the dashboard can always read it to decide button visibility.
        var method = typeof(ParentController)
            .GetMethod(nameof(ParentController.GetGoogleConfig));
        Assert.NotNull(method);
        var attrs = method!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true);
        Assert.Empty(attrs);
    }
}
