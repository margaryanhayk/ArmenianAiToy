using System.ClientModel;

namespace ArmenianAiToy.Infrastructure.OpenAI;

/// <summary>
/// Pure classifier for OpenAI SDK exceptions. No state, no IO — exception
/// in, <see cref="OpenAIFailureKind"/> out. Trivially unit-testable.
/// <para>
/// Classification rules (first match wins):
/// <list type="bullet">
///   <item>HTTP 429 (<see cref="ClientResultException.Status"/>) =&gt; <see cref="OpenAIFailureKind.RateLimited"/>.</item>
///   <item>HTTP 401 / 403 =&gt; <see cref="OpenAIFailureKind.AuthFailure"/>.</item>
///   <item>HTTP 500–599 =&gt; <see cref="OpenAIFailureKind.UpstreamServerError"/>.</item>
///   <item><see cref="OperationCanceledException"/> (including
///     <see cref="TaskCanceledException"/>) /
///     <see cref="TimeoutException"/> =&gt; <see cref="OpenAIFailureKind.Timeout"/>.</item>
///   <item>Everything else =&gt; <see cref="OpenAIFailureKind.Other"/>.</item>
/// </list>
/// </para>
/// </summary>
public static class OpenAIFailureClassifier
{
    public static OpenAIFailureKind Classify(Exception exception)
    {
        switch (exception)
        {
            case ClientResultException cre:
                if (cre.Status == 429) return OpenAIFailureKind.RateLimited;
                if (cre.Status == 401 || cre.Status == 403) return OpenAIFailureKind.AuthFailure;
                if (cre.Status >= 500 && cre.Status <= 599) return OpenAIFailureKind.UpstreamServerError;
                return OpenAIFailureKind.Other;

            // Raw-HttpClient adapters (Gemini) throw HttpRequestException
            // with StatusCode populated — same status mapping as the SDK
            // exception above. A null StatusCode (DNS/connect refusals)
            // stays Other: connection failures are not provably transient
            // and retrying them buys latency, not success.
            case System.Net.Http.HttpRequestException hre when hre.StatusCode is not null:
            {
                var status = (int)hre.StatusCode.Value;
                if (status == 429) return OpenAIFailureKind.RateLimited;
                if (status == 401 || status == 403) return OpenAIFailureKind.AuthFailure;
                if (status >= 500 && status <= 599) return OpenAIFailureKind.UpstreamServerError;
                return OpenAIFailureKind.Other;
            }

            case TimeoutException:
            case OperationCanceledException: // covers TaskCanceledException too
                return OpenAIFailureKind.Timeout;

            default:
                return OpenAIFailureKind.Other;
        }
    }
}
