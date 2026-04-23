namespace ArmenianAiToy.Application.Audio;

/// <summary>
/// Persists per-message audio blobs — one for the child's uploaded
/// audio, one for Areg's synthesized response. The returned path
/// string is stored verbatim in <c>Message.AudioBlobPath</c>; the
/// store alone owns the scheme (local disk, S3, etc.). Text remains
/// canonical in <c>Message.Content</c>; blobs are attachments.
/// <para>
/// <b>Deterministic paths.</b> A given (conversationId, messageId)
/// pair must always resolve to the same stored location so a
/// retry or late read can locate the blob without extra state.
/// </para>
/// <para>
/// <b>No delete in C1.</b> Cascade-on-conversation-delete and
/// cascade-on-device-delete are deferred to C2. This interface
/// deliberately does not expose a <c>DeleteAsync</c> method to
/// avoid tempting a partial cleanup implementation in this slice.
/// </para>
/// </summary>
public interface IAudioBlobStore
{
    /// <summary>
    /// Write the blob and return the deterministic path string to
    /// persist on <c>Message.AudioBlobPath</c>. Exceptions bubble up;
    /// the caller treats a write failure as non-fatal (still returns
    /// audio to the device; leaves <c>AudioBlobPath</c> null) so a
    /// flaky disk does not break the child-facing loop.
    /// </summary>
    Task<string> WriteAsync(
        Guid conversationId,
        Guid messageId,
        byte[] content,
        string mimeType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read a previously-written blob. Returns <c>null</c> if the
    /// blob does not exist. Used only by future read surfaces
    /// (C2's parent-dashboard "listen" button); no caller in C1.
    /// </summary>
    Task<(Stream Content, string MimeType)?> ReadAsync(
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken = default);
}
