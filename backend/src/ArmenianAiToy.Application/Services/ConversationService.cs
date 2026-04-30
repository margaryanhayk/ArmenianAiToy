using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Application.Services;

public class ConversationService : IConversationService
{
    private readonly DbContext _db;
    private readonly ILogger<ConversationService> _logger;

    // A conversation is "active" if started within the last 30 minutes
    private static readonly TimeSpan ConversationTimeout = TimeSpan.FromMinutes(30);

    // Local convention for the parent summary endpoint only.
    // No prior snippet/preview helper exists in the repo, so this length lives here.
    private const int SnippetMaxLength = 120;

    public ConversationService(DbContext db, ILogger<ConversationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Conversation> GetOrCreateActiveConversationAsync(Guid deviceId, Guid? childId)
    {
        var cutoff = DateTime.UtcNow - ConversationTimeout;

        var active = await _db.Set<Conversation>()
            .Where(c => c.DeviceId == deviceId && c.EndedAt == null && c.StartedAt > cutoff)
            .OrderByDescending(c => c.StartedAt)
            .FirstOrDefaultAsync();

        if (active != null)
            return active;

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            ChildId = childId,
            StartedAt = DateTime.UtcNow
        };

        _db.Set<Conversation>().Add(conversation);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New conversation {ConversationId} for device {DeviceId}", conversation.Id, deviceId);
        return conversation;
    }

    public async Task<Message> AddMessageAsync(Guid conversationId, MessageRole role, string content, SafetyFlag flag = SafetyFlag.Clean)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow,
            SafetyFlag = flag
        };

        _db.Set<Message>().Add(message);
        await _db.SaveChangesAsync();
        return message;
    }

    public async Task<List<(string Role, string Content)>> GetRecentMessagesAsync(Guid conversationId, int count = 20)
    {
        var messages = await _db.Set<Message>()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Timestamp)
            .Take(count)
            .OrderBy(m => m.Timestamp)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync();

        return messages.Select(m => (m.Role.ToString().ToLower(), m.Content)).ToList();
    }

    public async Task<List<ConversationDto>> GetConversationHistoryAsync(Guid deviceId, int limit = 10, int offset = 0)
    {
        var conversations = await _db.Set<Conversation>()
            .Where(c => c.DeviceId == deviceId)
            .OrderByDescending(c => c.StartedAt)
            .Skip(offset)
            .Take(limit)
            .Include(c => c.Messages.OrderBy(m => m.Timestamp))
            .ToListAsync();

        return conversations.Select(c => new ConversationDto(
            c.Id,
            c.DeviceId,
            c.StartedAt,
            c.EndedAt,
            c.Messages.Count,
            c.Messages.Any(m => m.SafetyFlag != SafetyFlag.Clean),
            c.Messages.Select(m => new MessageDto(
                m.Id,
                m.Role.ToString().ToLower(),
                m.Content,
                m.Timestamp,
                m.SafetyFlag,
                AudioAvailable: m.Role == MessageRole.Assistant && m.AudioBlobPath != null
            )).ToList()
        )).ToList();
    }

    public async Task<List<ConversationSummaryDto>> GetConversationSummariesAsync(Guid deviceId, int limit = 20, int offset = 0)
    {
        var conversations = await _db.Set<Conversation>()
            .Where(c => c.DeviceId == deviceId)
            .OrderByDescending(c => c.StartedAt)
            .Skip(offset)
            .Take(limit)
            .Select(c => new
            {
                c.Id,
                c.StartedAt,
                c.EndedAt,
                MessageCount = c.Messages.Count(),
                HasFlaggedContent = c.Messages.Any(m => m.SafetyFlag != SafetyFlag.Clean),
                FlaggedMessageCount = c.Messages.Count(m => m.SafetyFlag != SafetyFlag.Clean),
                FirstUserContent = c.Messages
                    .Where(m => m.Role == MessageRole.User)
                    .OrderBy(m => m.Timestamp)
                    .Select(m => m.Content)
                    .FirstOrDefault(),
                LastAssistantContent = c.Messages
                    .Where(m => m.Role == MessageRole.Assistant)
                    .OrderByDescending(m => m.Timestamp)
                    .Select(m => m.Content)
                    .FirstOrDefault(),
                LastAssistantSafetyFlag = c.Messages
                    .Where(m => m.Role == MessageRole.Assistant)
                    .OrderByDescending(m => m.Timestamp)
                    .Select(m => (SafetyFlag?)m.SafetyFlag)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return conversations.Select(c => new ConversationSummaryDto(
            c.Id,
            c.StartedAt,
            c.EndedAt,
            c.MessageCount,
            c.HasFlaggedContent,
            MakeSnippet(c.FirstUserContent),
            MakeSnippet(c.LastAssistantContent),
            c.LastAssistantSafetyFlag,
            c.FlaggedMessageCount
        )).ToList();
    }

    public async Task<List<FlaggedMessageDto>> GetFlaggedMessagesAsync(Guid deviceId, int limit = 20, int offset = 0)
    {
        var rows = await _db.Set<Message>()
            .Join(
                _db.Set<Conversation>().Where(c => c.DeviceId == deviceId),
                m => m.ConversationId,
                c => c.Id,
                (m, c) => new { Message = m, ConversationStartedAt = c.StartedAt })
            .Where(x => x.Message.SafetyFlag != SafetyFlag.Clean)
            .OrderByDescending(x => x.Message.Timestamp)
            .Skip(offset)
            .Take(limit)
            .Select(x => new FlaggedMessageDto(
                x.Message.Id,
                x.Message.ConversationId,
                x.ConversationStartedAt,
                x.Message.Role.ToString().ToLower(),
                x.Message.Content,
                x.Message.Timestamp,
                x.Message.SafetyFlag))
            .ToListAsync();

        return rows;
    }

    private static string? MakeSnippet(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        var trimmed = content.Trim();
        if (trimmed.Length <= SnippetMaxLength)
            return trimmed;
        return trimmed.Substring(0, SnippetMaxLength) + "…";
    }

    public async Task<ConversationDto?> GetConversationByIdAsync(Guid conversationId)
    {
        var conversation = await _db.Set<Conversation>()
            .Where(c => c.Id == conversationId)
            .Include(c => c.Messages.OrderBy(m => m.Timestamp))
            .FirstOrDefaultAsync();

        if (conversation is null)
            return null;

        return new ConversationDto(
            conversation.Id,
            conversation.DeviceId,
            conversation.StartedAt,
            conversation.EndedAt,
            conversation.Messages.Count,
            conversation.Messages.Any(m => m.SafetyFlag != SafetyFlag.Clean),
            conversation.Messages.Select(m => new MessageDto(
                m.Id,
                m.Role.ToString().ToLower(),
                m.Content,
                m.Timestamp,
                m.SafetyFlag,
                AudioAvailable: m.Role == MessageRole.Assistant && m.AudioBlobPath != null
            )).ToList()
        );
    }

    public async Task<TodaySummaryDto> GetTodaySummaryAsync(Guid deviceId, DateTime asOfUtc)
    {
        // Normalize to UTC. Inputs from the controller arrive UTC-typed
        // already (via DateTimeOffset.UtcDateTime); be tolerant of
        // Unspecified-Kind inputs from tests that pass plain DateTime.
        var normalizedAsOf = asOfUtc.Kind == DateTimeKind.Utc
            ? asOfUtc
            : DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var dayStartUtc = new DateTime(
            normalizedAsOf.Year, normalizedAsOf.Month, normalizedAsOf.Day,
            0, 0, 0, DateTimeKind.Utc);

        // Distinct conversation count: any conversation on this device
        // that has at least one message timestamped on or after today.
        var conversationsCount = await _db.Set<Conversation>()
            .CountAsync(c => c.DeviceId == deviceId
                          && c.Messages.Any(m => m.Timestamp >= dayStartUtc));

        // Per-message aggregate counts. The Join scopes Messages by the
        // owning device without depending on Message.Conversation
        // navigation (which is Ignore()'d in the InMemory test contexts).
        var todayMessages = _db.Set<Message>()
            .Join(
                _db.Set<Conversation>().Where(c => c.DeviceId == deviceId),
                m => m.ConversationId,
                c => c.Id,
                (m, c) => m)
            .Where(m => m.Timestamp >= dayStartUtc);

        var messagesCount = await todayMessages.CountAsync();
        var flaggedCount = await todayMessages
            .CountAsync(m => m.SafetyFlag != SafetyFlag.Clean);
        var assistantAudioCount = await todayMessages
            .CountAsync(m => m.Role == MessageRole.Assistant
                           && m.AudioBlobPath != null);

        // Newest 3 conversations with today activity, by StartedAt desc.
        // Per-conversation rollups (MessageCountToday, FlaggedMessageCountToday)
        // count ONLY today's messages — so a conversation that started
        // yesterday but had a turn today contributes only its today
        // messages to the rollup.
        var newestRows = await _db.Set<Conversation>()
            .Where(c => c.DeviceId == deviceId
                     && c.Messages.Any(m => m.Timestamp >= dayStartUtc))
            .OrderByDescending(c => c.StartedAt)
            .Take(3)
            .Select(c => new
            {
                c.Id,
                c.StartedAt,
                FirstUserContent = c.Messages
                    .Where(m => m.Role == MessageRole.User)
                    .OrderBy(m => m.Timestamp)
                    .Select(m => m.Content)
                    .FirstOrDefault(),
                MessageCountToday = c.Messages.Count(m => m.Timestamp >= dayStartUtc),
                FlaggedMessageCountToday = c.Messages.Count(m =>
                    m.Timestamp >= dayStartUtc && m.SafetyFlag != SafetyFlag.Clean),
            })
            .ToListAsync();

        var newest = newestRows
            .Select(r => new TodaySummaryConversationLink(
                r.Id,
                r.StartedAt,
                MakeSnippet(r.FirstUserContent),
                r.MessageCountToday,
                r.FlaggedMessageCountToday))
            .ToList();

        // Flagged top 3: conversations with any flagged-today message,
        // ordered by latest flagged-today timestamp desc. We use
        // OrderByDescending+FirstOrDefault rather than Max so an empty
        // sub-collection (impossible by the Where clause but defensive)
        // does not throw.
        var flaggedRows = await _db.Set<Conversation>()
            .Where(c => c.DeviceId == deviceId
                     && c.Messages.Any(m => m.Timestamp >= dayStartUtc
                                          && m.SafetyFlag != SafetyFlag.Clean))
            .Select(c => new
            {
                c.Id,
                c.StartedAt,
                FirstUserContent = c.Messages
                    .Where(m => m.Role == MessageRole.User)
                    .OrderBy(m => m.Timestamp)
                    .Select(m => m.Content)
                    .FirstOrDefault(),
                MessageCountToday = c.Messages.Count(m => m.Timestamp >= dayStartUtc),
                FlaggedMessageCountToday = c.Messages.Count(m =>
                    m.Timestamp >= dayStartUtc && m.SafetyFlag != SafetyFlag.Clean),
                LatestFlaggedToday = c.Messages
                    .Where(m => m.Timestamp >= dayStartUtc
                             && m.SafetyFlag != SafetyFlag.Clean)
                    .OrderByDescending(m => m.Timestamp)
                    .Select(m => m.Timestamp)
                    .FirstOrDefault(),
            })
            .OrderByDescending(c => c.LatestFlaggedToday)
            .Take(3)
            .ToListAsync();

        var flagged = flaggedRows
            .Select(r => new TodaySummaryConversationLink(
                r.Id,
                r.StartedAt,
                MakeSnippet(r.FirstUserContent),
                r.MessageCountToday,
                r.FlaggedMessageCountToday))
            .ToList();

        return new TodaySummaryDto(
            DeviceId: deviceId,
            AsOfUtc: normalizedAsOf,
            DayStartUtc: dayStartUtc,
            ConversationsCount: conversationsCount,
            MessagesCount: messagesCount,
            FlaggedMessagesCount: flaggedCount,
            AssistantMessagesWithAudio: assistantAudioCount,
            Newest: newest,
            Flagged: flagged);
    }
}
