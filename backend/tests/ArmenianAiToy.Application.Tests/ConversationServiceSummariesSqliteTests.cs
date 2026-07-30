using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Regression guard for the E1.3 Modes aggregation in
/// <c>GetConversationSummariesAsync</c>: the InMemory provider accepted a
/// projection shape that the REAL SQLite provider could not translate,
/// which surfaced as an HTTP 500 on GET /api/conversations/summary in
/// production while every InMemory-based test stayed green. This test
/// runs the summary query against SQLite in-memory (same pattern as
/// AuditControllerTests) so a translation break fails CI, not the
/// parent dashboard.
/// </summary>
public class ConversationServiceSummariesSqliteTests
{
    private static async Task<(ConversationService Service, AppDbContext Db, SqliteConnection Conn)>
        CreateServiceAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var logger = Substitute.For<ILogger<ConversationService>>();
        return (new ConversationService(db, logger), db, conn);
    }

    private static Device NewDevice() => new()
    {
        Id = Guid.NewGuid(),
        MacAddress = "AA:BB:CC:DD:EE:99",
        ApiKey = "dtk_test",
        RegisteredAt = DateTime.UtcNow
    };

    [Fact]
    public async Task GetConversationSummariesAsync_OnSqlite_TranslatesAndReturnsDistinctModes()
    {
        var (service, db, conn) = await CreateServiceAsync();
        try
        {
            var device = NewDevice();
            db.Devices.Add(device);
            var t0 = new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);
            var conv = new Conversation
            {
                Id = Guid.NewGuid(),
                DeviceId = device.Id,
                StartedAt = t0
            };
            db.Conversations.Add(conv);
            db.Messages.AddRange(
                new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, Role = MessageRole.User, Content = "u1", Timestamp = t0.AddSeconds(1), SafetyFlag = SafetyFlag.Clean },
                new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, Role = MessageRole.Assistant, Content = "a1", Timestamp = t0.AddSeconds(2), SafetyFlag = SafetyFlag.Clean, Mode = "story" },
                new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, Role = MessageRole.Assistant, Content = "a2", Timestamp = t0.AddSeconds(3), SafetyFlag = SafetyFlag.Clean, Mode = "story" },
                new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, Role = MessageRole.Assistant, Content = "a3", Timestamp = t0.AddSeconds(4), SafetyFlag = SafetyFlag.Clean, Mode = "calm" });
            await db.SaveChangesAsync();

            var rows = await service.GetConversationSummariesAsync(device.Id);

            var row = Assert.Single(rows);
            Assert.NotNull(row.Modes);
            Assert.Equal(2, row.Modes!.Count);
            Assert.Contains("story", row.Modes);
            Assert.Contains("calm", row.Modes);
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }
}
