using System.Linq;
using ArmenianAiToy.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #060 — the audio-ingesting POST actions cap the request body so a malicious
/// (or buggy) device cannot exhaust server memory by streaming a huge body that
/// the controllers buffer fully before decoding. Enforced declaratively with
/// [RequestSizeLimit]; controller-level unit tests bypass the pipeline, so the
/// binding is proved by attribute presence (same pattern as the auth rate-limit
/// attribute tests).
/// </summary>
public class RequestBodyLimitTests
{
    private static bool HasSizeLimit(Type controller, string method)
    {
        var m = controller.GetMethod(method);
        Assert.NotNull(m);
        return m!.GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: true).Any();
    }

    [Fact]
    public void AudioChat_Chat_HasRequestSizeLimit()
        => Assert.True(HasSizeLimit(typeof(AudioChatController), nameof(AudioChatController.Chat)));

    [Fact]
    public void StoryQa_Ask_HasRequestSizeLimit()
        => Assert.True(HasSizeLimit(typeof(StoryQaController), nameof(StoryQaController.Ask)));

    [Fact]
    public void StoryQa_AnswerReflection_HasRequestSizeLimit()
        => Assert.True(HasSizeLimit(typeof(StoryQaController), nameof(StoryQaController.AnswerReflection)));
}
