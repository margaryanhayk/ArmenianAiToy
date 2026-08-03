namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// A parent's custom-story request (owner batch 2026-08-03, slice F): the
/// parent describes a story idea — or photographs a book page — and the
/// OWNER fulfils it by hand (write/adapt, rights-check, voice, deliver).
/// This entity is the work queue, not an automated pipeline.
///
/// <para>
/// Deliberately FK-FREE (AuditEvent's idiom): a request describes work in
/// the owner's queue and must survive the requesting account's deletion —
/// the owner may be mid-production when the parent leaves.
/// <see cref="ParentId"/> is a plain column for scoping reads.
/// </para>
/// </summary>
public class StoryRequest
{
    public Guid Id { get; set; }

    /// <summary>The requesting parent at submission time (no FK — see
    /// class remarks). Parent-facing reads filter on this.</summary>
    public Guid ParentId { get; set; }

    /// <summary>Bounded: <c>idea</c> (free-text story idea) or
    /// <c>book_photo</c> (a photographed book page rides along).
    /// Validated at the service boundary.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The parent's description / wish (≤ 2000 chars, enforced at
    /// the boundary). May be empty for a pure photo submission.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Server-internal relative path of the uploaded photo, or
    /// null for text-only requests. Never exposed on a parent-facing wire
    /// shape; streamed to operators via the internal console only.</summary>
    public string? PhotoPath { get; set; }

    /// <summary>Bounded lifecycle: <c>new</c> → <c>in_review</c> →
    /// <c>delivered</c> | <c>declined</c>. Moved by the internal console
    /// (operator action); parents only read it.</summary>
    public string Status { get; set; } = "new";

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
