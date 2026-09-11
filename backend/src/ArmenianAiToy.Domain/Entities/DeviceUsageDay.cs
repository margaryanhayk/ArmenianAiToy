namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// Usage-tier METERING FOUNDATION (2026-09-11, shipped behind
/// <c>Usage:Tiers:Enabled</c>, default OFF). One row per (device, UTC day),
/// upserted wherever the existing OpenAI cost estimator already records a
/// turn — see <c>ChatController</c>/<c>AudioChatController</c>/
/// <c>StoryQaController</c>. Written UNCONDITIONALLY on that cost-recording
/// path (independent of the feature flag): the flag gates GATING and the
/// parent-facing surface, not the counting itself — the whole point of a
/// "foundation" is that the meter runs before anything is decided about it.
/// <para>
/// <see cref="Questions"/> counts one per recorded turn (a chat message, an
/// audio turn, an in-story question, a reflection answer) — the same three
/// paid-service touches <c>docs/usage-tiers-brainstorm.md</c> § 1
/// enumerates. <see cref="EstimatedUsd"/> is the same
/// <c>OpenAICostEstimator</c> figure the flat daily cap already uses, kept
/// here too so a monthly view does not require re-deriving it.
/// </para>
/// <para>
/// Best-effort, same partial-write discipline as <c>StoryPlay</c> /
/// <c>GamePlay</c>: a write failure here must never break the turn that
/// triggered it (the caller wraps it in the existing cost-recording
/// try/catch). <see cref="DayUtc"/> is a date-only UTC value (time
/// component always midnight) so the unique (DeviceId, DayUtc) index gives
/// exactly one row per device per day to upsert into.
/// </para>
/// </summary>
public class DeviceUsageDay
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public DateTime DayUtc { get; set; }
    public int Questions { get; set; }
    public decimal EstimatedUsd { get; set; }

    public Device? Device { get; set; }
}
