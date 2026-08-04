using System.Diagnostics.Metrics;

namespace ArmenianAiToy.Application.Telemetry;

/// <summary>
/// Single process-wide OpenTelemetry <see cref="Meter"/> exposing the
/// operational counters for this service. Lives in the Application
/// project so both the API layer (<c>ChatController</c>, rate limiter,
/// health probe, <c>Program.cs</c>) and the Application services
/// (<c>ParentService</c> for audit-write counts) can increment without
/// a layering violation. The <see cref="Meter"/> type itself is in the
/// BCL (<c>System.Diagnostics.DiagnosticSource</c>) — the OpenTelemetry
/// SDK in the API project subscribes to it by name.
///
/// Custom trace spans remain explicit later scope. A minimal
/// latency-histogram follow-on is already present — see
/// <see cref="ChatOpenAIDuration"/> and
/// <see cref="ModerationClassifyDuration"/> — still untagged and still
/// bound by the no-high-cardinality invariant below.
///
/// <para><b>Invariant — no high-cardinality tags.</b> Tag values here
/// are drawn from small, bounded enumerations (<c>paused|bedtime|
/// mode_disabled</c>, <c>ok|unhealthy</c>, the finite
/// <see cref="ArmenianAiToy.Domain.Enums.AuditEventType"/> name set).
/// Do NOT add tags whose value space is device-scoped, parent-scoped,
/// or child-scoped — no <c>device_id</c>, <c>parent_id</c>,
/// <c>child_id</c>, <c>mac_address</c>, <c>model_name</c>, or
/// free-form strings. A new counter (or histogram) that wants such a
/// tag belongs in a different tier (the <c>AuditEvents</c> table, the
/// structured log stream) rather than in Prometheus-scrapable
/// metrics.</para>
/// </summary>
public static class AppMeter
{
    public const string Name = "ArmenianAiToy";

    public static readonly Meter Instance = new(Name, version: "1.0.0");

    /// <summary>
    /// Count of chat-gate short-circuits in <c>ChatController.Chat</c>.
    /// Tag <c>gate</c> is one of <c>paused</c>, <c>bedtime</c>,
    /// <c>mode_disabled</c>.
    /// </summary>
    public static readonly Counter<long> ChatGateTrip =
        Instance.CreateCounter<long>(
            name: "aat_chat_gate_trip_total",
            description: "Count of chat-gate short-circuits by gate kind.");

    /// <summary>
    /// Count of OpenAI upstream failures classified by the
    /// <c>OpenAIReliabilityGate</c>. Incremented inside the gate
    /// immediately after classification, so the <c>kind</c> tag is
    /// applied at the moment the SDK exception is observed.
    /// <c>ChatController</c>'s Path-5 catch no longer increments this
    /// counter — it is responsible only for sanitizing the 502 response
    /// and server-side logging.
    /// <para>
    /// Tag <c>kind</c> is one of: <c>rate_limited</c>, <c>timeout</c>,
    /// <c>upstream_5xx</c>, <c>auth_failure</c>, <c>other</c>. Bounded
    /// enum — safe under the no-high-cardinality invariant.
    /// </para>
    /// </summary>
    public static readonly Counter<long> ChatOpenAIFailure =
        Instance.CreateCounter<long>(
            name: "aat_chat_openai_failure_total",
            description: "Count of OpenAI upstream failures by classified kind.");

    /// <summary>
    /// Count of retry attempts issued by <c>OpenAIReliabilityGate</c>.
    /// No tags — the retryable-kind distribution is inferable by
    /// correlating with <see cref="ChatOpenAIFailure"/>.
    /// </summary>
    public static readonly Counter<long> ChatOpenAIRetry =
        Instance.CreateCounter<long>(
            name: "aat_chat_openai_retry_total",
            description: "Count of retry attempts issued by the OpenAI reliability gate.");

    /// <summary>
    /// Count of closed-to-open transitions of the
    /// <c>OpenAIReliabilityGate</c>'s circuit breaker. No tags.
    /// </summary>
    public static readonly Counter<long> ChatOpenAICircuitTrip =
        Instance.CreateCounter<long>(
            name: "aat_chat_openai_circuit_trip_total",
            description: "Count of OpenAI reliability-gate circuit-breaker trips (closed=>open).");

    /// <summary>
    /// Count of calls rejected because the
    /// <c>OpenAIReliabilityGate</c> circuit was already open. No tags.
    /// </summary>
    public static readonly Counter<long> ChatOpenAICircuitShortCircuit =
        Instance.CreateCounter<long>(
            name: "aat_chat_openai_circuit_short_circuit_total",
            description: "Count of OpenAI calls short-circuited while the breaker is open.");

    /// <summary>
    /// Count of rate-limit rejections across both named policies
    /// (<c>chat</c> on <c>/api/chat</c> and <c>auth</c> on the parent
    /// auth / account-sensitive endpoints). Tag <c>policy</c> is
    /// exactly one of <c>chat</c> or <c>auth</c> — a bounded
    /// two-value enumeration derived from the matched endpoint's
    /// <c>[EnableRateLimiting]</c> metadata by
    /// <c>RateLimitRejectionPolicy.ResolvePolicyTag</c>. The
    /// no-high-cardinality invariant still binds: do NOT add
    /// <c>device_id</c>, <c>ip</c>, <c>route</c>, or any per-caller
    /// tag on this counter; a future third policy would extend
    /// <c>policy</c>'s value space by exactly one rather than
    /// introducing a new tag.
    /// </summary>
    public static readonly Counter<long> RateLimitRejected =
        Instance.CreateCounter<long>(
            name: "aat_rate_limit_rejected_total",
            description: "Count of rate-limit rejections, tagged by policy.");

    /// <summary>
    /// Count of <c>/api/health</c> probes. Tag <c>result</c> is one of
    /// <c>ok</c> (DB reachable) or <c>unhealthy</c> (DB probe failed).
    /// </summary>
    public static readonly Counter<long> HealthProbe =
        Instance.CreateCounter<long>(
            name: "aat_health_probe_total",
            description: "Count of /api/health probes by result.");

    /// <summary>
    /// Count of <c>AuditEvents</c> rows successfully written. Tag
    /// <c>event_type</c> draws from the finite
    /// <see cref="ArmenianAiToy.Domain.Enums.AuditEventType"/> enum
    /// name set. Complements the audit table (durable, queryable via
    /// <c>GET /api/parents/audit</c>); this counter is a volatile
    /// pulse for "are audit writes happening at all," not a
    /// replacement for the table.
    /// </summary>
    public static readonly Counter<long> AuditEventsWritten =
        Instance.CreateCounter<long>(
            name: "aat_audit_events_written_total",
            description: "Count of audit rows written, tagged by event_type.");

    /// <summary>
    /// End-to-end duration (unit = <b>seconds</b>) of
    /// <c>OpenAIReliabilityGate.RunAsync</c>. Covers the entire gated
    /// path: short-circuit checks, the retry loop, backoff delays, and
    /// the final breaker-state update — so a tripped-breaker fail-fast
    /// shows up as a near-zero sample and a retry success shows up as
    /// initial-call + backoff + retry-call summed.
    /// <para>
    /// <b>Untagged in this slice.</b> Splitting by outcome/kind would
    /// duplicate information already present on
    /// <see cref="ChatOpenAIFailure"/>,
    /// <see cref="ChatOpenAIRetry"/>, and the two circuit counters.
    /// Bucket boundaries are set explicitly via an OTel meter view in
    /// <c>Program.cs</c>, not on this instrument.
    /// </para>
    /// <para>
    /// The no-high-cardinality invariant documented on the class still
    /// applies — do NOT add <c>device_id</c>, <c>parent_id</c>,
    /// <c>child_id</c>, <c>mac_address</c>, <c>model_name</c>, or any
    /// free-form string as a tag on this histogram.
    /// </para>
    /// </summary>
    public static readonly Histogram<double> ChatOpenAIDuration =
        Instance.CreateHistogram<double>(
            name: "aat_chat_openai_duration_seconds",
            unit: "s",
            description: "End-to-end duration of the OpenAI chat gated call (incl. retry/backoff).");

    /// <summary>
    /// End-to-end duration (unit = <b>seconds</b>) of
    /// <c>OpenAIModerationAdapter.CheckContentAsync</c>. Covers the
    /// single D1 retry-on-429 path and any fail-closed-to-sentinel
    /// branch. Untagged for the same reason as
    /// <see cref="ChatOpenAIDuration"/>. Bucket boundaries are set
    /// explicitly via an OTel meter view in <c>Program.cs</c>.
    /// <para>
    /// Same no-high-cardinality invariant — no <c>device_id</c>,
    /// <c>parent_id</c>, <c>child_id</c>, <c>mac_address</c>,
    /// <c>model_name</c>, or free-form strings.
    /// </para>
    /// </summary>
    public static readonly Histogram<double> ModerationClassifyDuration =
        Instance.CreateHistogram<double>(
            name: "aat_moderation_classify_duration_seconds",
            unit: "s",
            description: "End-to-end duration of the moderation classify call.");

    /// <summary>
    /// Count of moderation fail-closed events — incremented exactly
    /// once per outer <c>OpenAIModerationAdapter.CheckContentAsync</c>
    /// call whose outcome is <c>IsSafe=false</c> with the
    /// <c>moderation_unavailable</c> category. Genuine content flags
    /// (Sexual / Violence / SelfHarm / Hate / Harassment) exit the
    /// happy path and do NOT increment this counter.
    /// <para>
    /// Tag <c>reason</c> is one of the seven values defined on
    /// <see cref="ModerationFailClosedReason"/>:
    /// <c>rate_limited_retry_failed</c>, <c>auth_error</c>,
    /// <c>server_error</c>, <c>timeout</c>, <c>network_error</c>,
    /// <c>parse_error</c>, <c>unknown</c>. Bounded enum — safe under
    /// the no-high-cardinality invariant. The retry path is collapsed
    /// to a single increment with reason <c>rate_limited_retry_failed</c>
    /// (the initial 429 is NOT counted separately — the adapter is
    /// designed to swallow a transient 429 silently and the metric
    /// reflects the FINAL outcome, not internal retry mechanics).
    /// </para>
    /// <para>
    /// Complements — does NOT replace — the existing
    /// <c>FailClosed.LogError</c> structured-log line which still
    /// carries the exception, HTTP status, and latency. The counter
    /// gives Prometheus an aggregate signal; the log carries the
    /// per-incident detail.
    /// </para>
    /// </summary>
    public static readonly Counter<long> ModerationFailClosed =
        Instance.CreateCounter<long>(
            name: "aat_moderation_failclosed_total",
            description: "Count of moderation fail-closed events by classified reason.");

    /// <summary>
    /// Count of per-device daily OpenAI cost-cap trips
    /// (<c>OpenAICostMeter.IsOverCap</c> returned true ahead of a chat
    /// or audio-chat request). Tag <c>kind</c> is one of <c>chat</c>,
    /// <c>audio</c>, or <c>story_qa</c> (the in-story voice Q&amp;A path).
    /// Bounded enumeration — safe under the no-high-cardinality invariant.
    /// <para>
    /// Trips fire once per request, so a buggy client that hits the
    /// cap many times in one day produces many counter increments
    /// (good — operators can alert on the rate) but exactly ONE
    /// structured-warning log line (see
    /// <c>OpenAICostMeter.ShouldLogCapTrip</c> for the flood-control
    /// gate). Do NOT add a per-device tag here — that is what the
    /// audit/log stream is for.
    /// </para>
    /// </summary>
    public static readonly Counter<long> OpenAICostCapTrip =
        Instance.CreateCounter<long>(
            name: "aat_openai_cost_cap_trip_total",
            description: "Count of per-device daily OpenAI cost-cap trips, by kind.");

    /// <summary>
    /// Count of in-story voice Q&amp;A turns handled by
    /// <c>StoryQaController.Ask</c> (one increment per turn that reaches
    /// transcription — input-validation rejections like unknown-story 404
    /// or empty-body 400 are NOT counted). Tag <c>outcome</c> is one of a
    /// bounded five-value set:
    /// <list type="bullet">
    ///   <item><c>answered</c> — GPT answered and passed the answer filter.</item>
    ///   <item><c>answer_fallback</c> — the bounded Q&amp;A returned its safe
    ///     fallback (model/filter rejected; not a safety block).</item>
    ///   <item><c>moderation_blocked</c> — input moderation blocked the
    ///     transcript; no GPT call.</item>
    ///   <item><c>answer_blocked</c> — the generated answer failed OUTPUT
    ///     moderation and was replaced by the safe fallback before TTS.</item>
    ///   <item><c>empty_transcript</c> — Whisper returned nothing.</item>
    ///   <item><c>unclear</c> — the transcript looked garbled (likely STT
    ///     mistranscription); the child was asked to repeat, no GPT call.</item>
    ///   <item><c>transient_failure</c> — STT / answer / TTS threw and the
    ///     child was served the spoken safe-fallback (the no-silence path).</item>
    /// </list>
    /// Bounded enum — safe under the no-high-cardinality invariant. Do NOT
    /// add story_id / device_id here.
    /// </summary>
    public static readonly Counter<long> StoryQaTurn =
        Instance.CreateCounter<long>(
            name: "aat_story_qa_turn_total",
            description: "Count of in-story voice Q&A turns by outcome.");

    /// <summary>
    /// Welcome-flow — one increment per <c>POST /api/devices/voice-intent</c>,
    /// tagged with the intent actually returned. The tag's value space is
    /// exactly <see cref="Helpers.VoiceIntents.All"/> (8 values), so it is
    /// safe under the no-high-cardinality invariant. Do NOT add device_id or
    /// the transcript here.
    /// <para>
    /// The ratio of <c>unknown</c> to everything else is the honest measure of
    /// whether voice-only selection actually works for small children — the
    /// one number worth watching after the first families.
    /// </para>
    /// </summary>
    public static readonly Counter<long> VoiceIntentTurn =
        Instance.CreateCounter<long>(
            name: "aat_voice_intent_turn_total",
            description: "Count of welcome-flow spoken menu answers by resolved intent.");

    /// <summary>
    /// End-to-end duration (unit = <b>seconds</b>) of a voice Q&amp;A turn
    /// in <c>StoryQaController.Ask</c> — from just before Whisper STT
    /// through GPT, TTS, and bridge/recap composition. This is the metric
    /// for the perceived "dead air" after a barge-in. Recorded for every
    /// metered turn regardless of outcome (incl. the spoken-fallback
    /// failure path). Untagged; bucket boundaries are wired via an OTel
    /// view in <c>Program.cs</c> (shared with the OpenAI latency
    /// histograms). Same no-high-cardinality invariant — no story_id /
    /// device_id / model_name tags.
    /// </summary>
    public static readonly Histogram<double> StoryQaDuration =
        Instance.CreateHistogram<double>(
            name: "aat_story_qa_duration_seconds",
            unit: "s",
            description: "End-to-end duration of an in-story voice Q&A turn.");

    /// <summary>
    /// Count of whole-story narration TTS renders in
    /// <c>StoryAudioController</c> — the expensive, cost-bearing cold-cache
    /// event (cached serves do NOT increment this). Tag <c>result</c> is
    /// one of <c>success</c> or <c>failure</c>. Bounded two-value
    /// enumeration. A high render rate signals cache churn / a re-render
    /// abuse attempt; pair with the <c>story_audio</c> rate-limit-rejection
    /// signal. Do NOT add story_id here.
    /// </summary>
    public static readonly Counter<long> StoryAudioRender =
        Instance.CreateCounter<long>(
            name: "aat_story_audio_render_total",
            description: "Count of whole-story narration TTS renders, by result.");
}
