using System.ClientModel;
using System.ClientModel.Primitives;
using ArmenianAiToy.Infrastructure.OpenAI;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pure-classifier unit tests. Exception in => OpenAIFailureKind out.
/// Tag values are bounded (five), matching the AppMeter no-high-
/// cardinality invariant.
/// </summary>
public class OpenAIFailureClassifierTests
{
    [Theory]
    [InlineData(429, OpenAIFailureKind.RateLimited)]
    [InlineData(401, OpenAIFailureKind.AuthFailure)]
    [InlineData(403, OpenAIFailureKind.AuthFailure)]
    [InlineData(500, OpenAIFailureKind.UpstreamServerError)]
    [InlineData(502, OpenAIFailureKind.UpstreamServerError)]
    [InlineData(503, OpenAIFailureKind.UpstreamServerError)]
    [InlineData(599, OpenAIFailureKind.UpstreamServerError)]
    [InlineData(400, OpenAIFailureKind.Other)]
    [InlineData(404, OpenAIFailureKind.Other)]
    [InlineData(418, OpenAIFailureKind.Other)]
    public void ClientResultException_ClassifiesByStatus(int status, OpenAIFailureKind expected)
    {
        var ex = new ClientResultException("HTTP " + status, new FakePipelineResponse(status));
        Assert.Equal(expected, OpenAIFailureClassifier.Classify(ex));
    }

    [Fact]
    public void OperationCanceledException_IsTimeout()
    {
        Assert.Equal(OpenAIFailureKind.Timeout,
            OpenAIFailureClassifier.Classify(new OperationCanceledException()));
    }

    [Fact]
    public void TaskCanceledException_IsTimeout()
    {
        Assert.Equal(OpenAIFailureKind.Timeout,
            OpenAIFailureClassifier.Classify(new TaskCanceledException()));
    }

    [Fact]
    public void TimeoutException_IsTimeout()
    {
        Assert.Equal(OpenAIFailureKind.Timeout,
            OpenAIFailureClassifier.Classify(new TimeoutException()));
    }

    [Fact]
    public void UnknownException_IsOther()
    {
        Assert.Equal(OpenAIFailureKind.Other,
            OpenAIFailureClassifier.Classify(new InvalidOperationException("something went sideways")));
    }

    // ---------- test helpers ----------

    private sealed class FakePipelineResponse : PipelineResponse
    {
        private readonly int _status;
        public FakePipelineResponse(int status) { _status = status; }
        public override int Status => _status;
        public override string ReasonPhrase => "";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.Empty;
        protected override PipelineResponseHeaders HeadersCore => FakeHeaders.Instance;
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default)
            => new(BinaryData.Empty);
        public override void Dispose() { }
    }

    private sealed class FakeHeaders : PipelineResponseHeaders
    {
        public static readonly FakeHeaders Instance = new();
        public override bool TryGetValue(string name, out string? value) { value = null; return false; }
        public override bool TryGetValues(string name, out IEnumerable<string>? values) { values = null; return false; }
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() { yield break; }
    }
}
