using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// One child answer to a story's post-story reflection question («what did
/// the story teach us?»), captured on the voice path. APPEND-ONLY by
/// design (owner decision 2026-08-03): a child who listens to the same
/// story many times gets one row per listen, never an overwrite — the
/// parent dashboard shows how the child's understanding grows across
/// repeat listens.
///
/// <para>
/// Children's-data discipline: rows are parent-visible (read endpoint +
/// export), die with the device (FK cascade), and carry the same
/// <see cref="SafetyFlag"/> the conversation record uses. The transcript
/// is exactly what the existing reflection endpoint already persists into
/// the conversation record — this table adds the per-story, per-question
/// grouping that conversations cannot express.
/// </para>
/// </summary>
public class StoryReflectionAnswer
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    /// <summary>Curated story id the answer belongs to (lookup key).</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>Which reflection question was asked (0-based, matches
    /// <c>CuratedStory.ReflectionQuestions</c>).</summary>
    public int QuestionIndex { get; set; }

    /// <summary>The child's transcribed answer (Whisper STT). Same
    /// persistence discipline as conversation messages — stored verbatim,
    /// flagged when moderation blocked it.</summary>
    public string AnswerText { get; set; } = string.Empty;

    public SafetyFlag SafetyFlag { get; set; } = SafetyFlag.Clean;

    public DateTime CreatedAtUtc { get; set; }
}
