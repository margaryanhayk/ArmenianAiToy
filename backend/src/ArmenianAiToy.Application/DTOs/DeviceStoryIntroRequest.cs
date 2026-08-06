namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Body for the single-flag device toggles: <c>story-intro</c> (the spoken
/// «Հեքիաթ՝ …, հեղինակ՝ …» opener), <c>story-pauses</c>,
/// <c>variant-endings</c>, and <c>bedtime-music</c>. Named for the first of
/// them and deliberately SHARED rather than cloned per endpoint — the shape is
/// one nullable bool, and four identical records would be four places for the
/// nullable-means-400 contract to drift.
/// <para>
/// Nullable so a missing/empty body is a 400 at the controller rather than a
/// silent "false" (fail-safe: a truncated request must not flip the flag).
/// </para>
/// </summary>
public record DeviceStoryIntroRequest(bool? Enabled);
