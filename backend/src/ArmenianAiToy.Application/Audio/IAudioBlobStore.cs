namespace ArmenianAiToy.Application.Audio;

/// <summary>
/// Per-conversation result of <see cref="IAudioBlobStore.DeleteConversationAudioAsync"/>.
/// Compact, count-based — a parent-driven destructive action sums
/// across many of these into a single audit-row metadata blob with
/// <c>audio_conversations_attempted</c>, <c>audio_files_deleted</c>,
/// and <c>audio_delete_failures</c>. Carrying paths or filenames here
/// would make the result PII-adjacent; we deliberately keep counts
/// + a flat error string only.
/// </summary>
/// <param name="FilesDeleted">
/// Number of blob FILES actually removed from disk. Reflects what
/// hit disk, not what was attempted. <c>0</c> on a missing-directory
/// idempotent success.
/// </param>
/// <param name="DirectoryMissing">
/// True when no directory existed for the conversation — the call
/// was a no-op idempotent success. Lets retention / parent paths
/// distinguish "nothing to clean" from "cleaned up cleanly."
/// </param>
/// <param name="Failed">
/// True when the implementation could not complete the cleanup
/// (one or more files left behind, or the directory could not be
/// removed). Auditing aggregates these into
/// <c>audio_delete_failures</c>; the parent-facing API does NOT
/// surface a failure on this signal — DB delete is the source of
/// truth, and a leftover blob is a future C2.3 orphan-sweeper
/// concern.
/// </param>
/// <param name="ErrorMessage">
/// Optional human-readable summary of why the delete failed. Used
/// by structured logs only; never returned on the wire.
/// </param>
public readonly record struct AudioBlobDeleteResult(
    int FilesDeleted,
    bool DirectoryMissing,
    bool Failed,
    string? ErrorMessage);

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
/// <b>Conversation-level delete (C2.2a).</b> Local layout groups
/// every blob for one conversation under a single directory
/// (<c>{conversationId:N}/{messageId:N}.{ext}</c>), so cascade
/// cleanup is one directory delete per conversation regardless of
/// turn count. <see cref="DeleteConversationAudioAsync"/> is
/// idempotent on a missing directory and never throws on per-file
/// IO failure — the destructive DB action is the source of truth,
/// orphaned blob residue is a C2.3 sweeper concern.
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
    /// blob does not exist. Used by the C2.1 parent-dashboard
    /// assistant audio replay endpoint.
    /// </summary>
    Task<(Stream Content, string MimeType)?> ReadAsync(
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// C2.2a — delete every blob for the given conversation. The
    /// local layout groups all of a conversation's audio under one
    /// directory keyed by <c>conversationId:N</c>, so this is one
    /// recursive directory delete in practice.
    /// <para>
    /// <b>Idempotent on a missing directory.</b> Returns
    /// <c>DirectoryMissing = true</c> with <c>FilesDeleted = 0</c>;
    /// not a failure. A second call after a successful first call
    /// is therefore safe.
    /// </para>
    /// <para>
    /// <b>Never throws on per-file IO failure.</b> The destructive
    /// DB action that triggers this call has already committed by
    /// the time this method runs, so a mid-cleanup IO error must
    /// not surface to the parent. Implementations log + count the
    /// failure into <see cref="AudioBlobDeleteResult.Failed"/> and
    /// return; orphaned files are picked up by a future C2.3
    /// sweeper. <c>OperationCanceledException</c> is the one
    /// exception that does propagate — cooperative cancellation
    /// must remain visible.
    /// </para>
    /// </summary>
    Task<AudioBlobDeleteResult> DeleteConversationAudioAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}
