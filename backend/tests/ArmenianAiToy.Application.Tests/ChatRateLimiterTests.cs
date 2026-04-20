using ArmenianAiToy.Api.RateLimiting;
using System.Threading.RateLimiting;

namespace ArmenianAiToy.Application.Tests;

public class ChatRateLimiterTests
{
    [Fact]
    public void BuildPartition_UsesProvidedKeyAsPartitionKey()
    {
        // Pins the partition-key contract: whatever key is passed in (which
        // in production is the X-Device-Id header value or "anonymous")
        // becomes the partition identifier. Guards against a future refactor
        // that might accidentally rekey by path or IP.
        var partition = ChatRateLimiter.BuildPartition(
            "device-xyz-123", permitLimit: 30, windowSeconds: 60);

        Assert.Equal("device-xyz-123", partition.PartitionKey);
    }

    [Fact]
    public async Task BuildPartition_RejectsRequestsAfterPermitLimit_WithinWindow()
    {
        // Pins the PermitLimit contract end-to-end: N requests succeed, the
        // (N+1)th is rejected while still inside the same window. Using
        // permitLimit: 3 keeps the test fast and deterministic — the real
        // production limit (30) is tested by the same mechanism.
        var partition = ChatRateLimiter.BuildPartition(
            "test-device", permitLimit: 3, windowSeconds: 60);
        using var limiter = partition.Factory(partition.PartitionKey);

        for (int i = 0; i < 3; i++)
        {
            using var lease = await limiter.AcquireAsync(1);
            Assert.True(lease.IsAcquired, $"request {i + 1} should be permitted");
        }

        using var rejected = await limiter.AcquireAsync(1);
        Assert.False(rejected.IsAcquired);
    }
}
