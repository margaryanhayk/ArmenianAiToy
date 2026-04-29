using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Infrastructure.Audio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// C2.2a — direct on-disk tests for
/// <see cref="LocalDiskAudioBlobStore.DeleteConversationAudioAsync"/>.
/// Each test gets its own temp root so concurrent runs don't trample
/// each other; the temp dir is removed in <see cref="IDisposable"/>.
///
/// Pins the documented contract:
///  1. A directory full of .wav + .mp3 files is removed and the
///     count returned matches what hit disk.
///  2. A missing directory is idempotent success
///     (DirectoryMissing=true, FilesDeleted=0, Failed=false).
///  3. Sibling conversation directories under the same root are
///     never touched.
///  4. An empty directory is removed cleanly.
///  5. Cooperative cancellation is honored.
/// </summary>
public class LocalDiskAudioBlobStoreTests : IDisposable
{
    private readonly string _root;
    private readonly LocalDiskAudioBlobStore _store;

    public LocalDiskAudioBlobStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(),
            "aat-c22a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var config = Substitute.For<IConfiguration>();
        config["Audio:BlobStoreRoot"].Returns(_root);
        var logger = Substitute.For<ILogger<LocalDiskAudioBlobStore>>();
        _store = new LocalDiskAudioBlobStore(config, logger);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static readonly byte[] WavBytes = { 0x52, 0x49, 0x46, 0x46 }; // RIFF
    private static readonly byte[] Mp3Bytes = { 0xFF, 0xF3, 0xE4, 0xC4 }; // MP3 frame sync

    private async Task<(Guid ConvId, Guid UserMsgId, Guid AsstMsgId)> SeedConversationAsync()
    {
        var convId = Guid.NewGuid();
        var userMsgId = Guid.NewGuid();
        var asstMsgId = Guid.NewGuid();
        await _store.WriteAsync(convId, userMsgId, WavBytes, "audio/wav");
        await _store.WriteAsync(convId, asstMsgId, Mp3Bytes, "audio/mpeg");
        return (convId, userMsgId, asstMsgId);
    }

    [Fact]
    public async Task DeleteConversationAudioAsync_DirectoryWithMixedFiles_RemovesAllAndReturnsCount()
    {
        var (convId, _, _) = await SeedConversationAsync();
        var dir = Path.Combine(_root, convId.ToString("N"));
        Assert.True(Directory.Exists(dir));
        Assert.Equal(2, Directory.GetFiles(dir).Length);

        var result = await _store.DeleteConversationAudioAsync(convId);

        Assert.Equal(2, result.FilesDeleted);
        Assert.False(result.DirectoryMissing);
        Assert.False(result.Failed);
        Assert.Null(result.ErrorMessage);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task DeleteConversationAudioAsync_MissingDirectory_ReturnsIdempotentSuccess()
    {
        // Idempotent miss: a never-written conversation, or a second
        // call after an earlier successful delete. Not a failure.
        var result = await _store.DeleteConversationAudioAsync(Guid.NewGuid());

        Assert.Equal(0, result.FilesDeleted);
        Assert.True(result.DirectoryMissing);
        Assert.False(result.Failed);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteConversationAudioAsync_OnlyTouchesGivenConversationDirectory_PreservesSiblings()
    {
        // Anti-leak across conversations: a delete keyed on conv A
        // must never reach into conv B's directory. Pinning this
        // guards against a future "recursive" bug where the root
        // itself gets nuked.
        var seedA = await SeedConversationAsync();
        var seedB = await SeedConversationAsync();

        var result = await _store.DeleteConversationAudioAsync(seedA.ConvId);

        Assert.Equal(2, result.FilesDeleted);
        Assert.False(Directory.Exists(Path.Combine(_root, seedA.ConvId.ToString("N"))));
        // Sibling directory and its contents survive untouched.
        var dirB = Path.Combine(_root, seedB.ConvId.ToString("N"));
        Assert.True(Directory.Exists(dirB));
        Assert.Equal(2, Directory.GetFiles(dirB).Length);
    }

    [Fact]
    public async Task DeleteConversationAudioAsync_EmptyDirectory_RemovesItAndReturnsZero()
    {
        // Half-failed-earlier-write scenario: a conversation directory
        // exists but is empty. The cleanup should drain the directory
        // shell too rather than leaving an empty shell behind.
        var convId = Guid.NewGuid();
        var dir = Path.Combine(_root, convId.ToString("N"));
        Directory.CreateDirectory(dir);

        var result = await _store.DeleteConversationAudioAsync(convId);

        Assert.Equal(0, result.FilesDeleted);
        Assert.False(result.DirectoryMissing);
        Assert.False(result.Failed);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task DeleteConversationAudioAsync_HonorsCancellation()
    {
        // OperationCanceledException is the one exception the contract
        // allows to propagate — cooperative cancellation must remain
        // visible even though IO failures are swallowed.
        var (convId, _, _) = await SeedConversationAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.DeleteConversationAudioAsync(convId, cts.Token));
    }

    [Fact]
    public async Task DeleteConversationAudioAsync_LockedFile_LeavesPartialResultAndDoesNotThrow()
    {
        // Hold an exclusive handle on one of the seeded files so the
        // store's File.Delete call hits an IOException for that file.
        // Contract: log + count + continue, returning Failed=true with
        // a partial FilesDeleted count. Never throws.
        //
        // Windows-specific lock semantic — the test deliberately runs
        // on the same OS the dev/CI hosts use.
        var (convId, userMsgId, asstMsgId) = await SeedConversationAsync();
        var dir = Path.Combine(_root, convId.ToString("N"));
        var lockedPath = Path.Combine(dir, userMsgId.ToString("N") + ".wav");

        await using var hold = new FileStream(
            lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await _store.DeleteConversationAudioAsync(convId);

        Assert.True(result.Failed);
        Assert.NotNull(result.ErrorMessage);
        // The unlocked file (the assistant MP3) was deleted; the
        // locked file is still present.
        Assert.Equal(1, result.FilesDeleted);
        Assert.True(File.Exists(lockedPath));
        Assert.False(File.Exists(Path.Combine(dir, asstMsgId.ToString("N") + ".mp3")));
    }
}
