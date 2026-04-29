using ArmenianAiToy.Application.Audio;

namespace ArmenianAiToy.Application.Tests.Helpers;

/// <summary>
/// Shared in-memory <see cref="IAudioBlobStore"/> test double used by
/// the C2.2a parent-driven cascade tests and the C2.2b retention /
/// dormancy cascade tests. Mirrors the on-disk
/// <c>LocalDiskAudioBlobStore</c> layout
/// (<c>{convId:N}/{msgId:N}.{ext}</c>) so prefix-based delete matches
/// the production semantic; tracks every
/// <see cref="DeleteConversationAudioAsync"/> call so a test can pin
/// "how many conversations were attempted."
/// <para>
/// Lifted into Helpers so RetentionPurgeServiceTests can register the
/// same shape on its harness's <c>ServiceCollection</c> without
/// copy-pasting the class. Keeping the type non-sealed lets a future
/// caller subclass to inject canned failures if a third scenario
/// needs them; today the always-failing variant is
/// <see cref="FailingBlobStore"/>.
/// </para>
/// </summary>
public class RecordingBlobStore : IAudioBlobStore
{
    public readonly Dictionary<string, byte[]> Files = new();
    public readonly List<Guid> DeleteCalls = new();

    public virtual Task<string> WriteAsync(Guid conversationId, Guid messageId,
        byte[] content, string mimeType, CancellationToken cancellationToken = default)
    {
        var ext = mimeType.StartsWith("audio/wav") ? ".wav"
                 : mimeType.StartsWith("audio/mpeg") ? ".mp3"
                 : ".bin";
        var path = $"{conversationId:N}/{messageId:N}{ext}";
        Files[path] = content;
        return Task.FromResult(path);
    }

    public virtual Task<(Stream Content, string MimeType)?> ReadAsync(
        Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
        => Task.FromResult<(Stream, string)?>(null);

    public virtual Task<AudioBlobDeleteResult> DeleteConversationAudioAsync(
        Guid conversationId, CancellationToken cancellationToken = default)
    {
        DeleteCalls.Add(conversationId);
        var prefix = conversationId.ToString("N") + "/";
        var matched = Files.Keys.Where(k => k.StartsWith(prefix)).ToList();
        if (matched.Count == 0)
        {
            return Task.FromResult(new AudioBlobDeleteResult(
                FilesDeleted: 0, DirectoryMissing: true, Failed: false, ErrorMessage: null));
        }
        foreach (var k in matched) Files.Remove(k);
        return Task.FromResult(new AudioBlobDeleteResult(
            FilesDeleted: matched.Count, DirectoryMissing: false, Failed: false, ErrorMessage: null));
    }

    /// <summary>
    /// Mirror an existing seeded path into the in-memory store. Used
    /// by tests that seed audio paths into <c>Message.AudioBlobPath</c>
    /// directly (without going through <see cref="WriteAsync"/>) and
    /// then need the in-memory store to reflect that those blobs
    /// "exist" so a delete actually finds something to remove.
    /// </summary>
    public void Seed(string relativePath, byte[] content) => Files[relativePath] = content;
}

/// <summary>
/// Always-failing companion to <see cref="RecordingBlobStore"/>. Pins
/// the "IO failure does not fail the parent / retention action"
/// contract: every call returns
/// <c>Failed=true, FilesDeleted=0</c>, and the destructive caller
/// should still commit its DB delete and surface success to its own
/// caller.
/// </summary>
public sealed class FailingBlobStore : IAudioBlobStore
{
    public int Calls;
    public Task<string> WriteAsync(Guid conversationId, Guid messageId,
        byte[] content, string mimeType, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<(Stream Content, string MimeType)?> ReadAsync(
        Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
        => Task.FromResult<(Stream, string)?>(null);
    public Task<AudioBlobDeleteResult> DeleteConversationAudioAsync(
        Guid conversationId, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new AudioBlobDeleteResult(
            FilesDeleted: 0, DirectoryMissing: false, Failed: true,
            ErrorMessage: "synthetic IO failure"));
    }
}
