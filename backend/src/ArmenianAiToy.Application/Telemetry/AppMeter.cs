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
/// This is the first observability slice — metrics only; custom trace
/// spans and latency histograms are explicit later scope.
///
/// <para><b>Invariant — no high-cardinality tags.</b> Tag values here
/// are drawn from small, bounded enumerations (<c>paused|bedtime|
/// mode_disabled</c>, <c>ok|unhealthy</c>, the finite
/// <see cref="ArmenianAiToy.Domain.Enums.AuditEventType"/> name set).
/// Do NOT add tags whose value space is device-scoped, parent-scoped,
/// or child-scoped — no <c>device_id</c>, <c>parent_id</c>,
/// <c>child_id</c>, <c>mac_address</c>, or free-form strings. A new
/// counter that wants such a tag belongs in a different tier
/// (the <c>AuditEvents</c> table, the structured log stream) rather
/// than in Prometheus-scrapable metrics.</para>
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
    /// Count of Path-5 OpenAI upstream failures caught in
    /// <c>ChatController.Chat</c>. No tags.
    /// </summary>
    public static readonly Counter<long> ChatOpenAIFailure =
        Instance.CreateCounter<long>(
            name: "aat_chat_openai_failure_total",
            description: "Count of OpenAI upstream failures caught at the Path-5 branch.");

    /// <summary>
    /// Count of per-device rate-limit rejections. No tags —
    /// <c>device_id</c> as a tag would blow up cardinality.
    /// </summary>
    public static readonly Counter<long> RateLimitRejected =
        Instance.CreateCounter<long>(
            name: "aat_rate_limit_rejected_total",
            description: "Count of rate-limit rejections in the chat pipeline.");

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
}
