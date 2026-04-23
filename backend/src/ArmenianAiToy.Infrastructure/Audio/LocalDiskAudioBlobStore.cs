using ArmenianAiToy.Application.Audio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.Audio;

/// <summary>
/// Production <see cref="IAudioBlobStore"/> for C1 — writes blobs to
/// the local filesystem under a configured root. The stored path is
/// a relative path string (<c>{convId:N}/{messageId:N}.{ext}</c>) so
/// the DB column does not contain absolute filesystem paths that
/// would break on a different host. Reads resolve the relative path
/// against the same root at runtime.
/// <para>
/// <b>Retention is NOT in C1.</b> There is no delete method on the
/// interface, no cascade-on-conversation-delete, no orphan sweeper.
/// Blobs accumulate on disk until a C2 slice lands the cleanup
/// cascade. Bench-scale usage (<10,000 files per session) is fine;
/// operators running longer bench sessions can <c>rm -rf</c> the
/// configured root between runs.
/// </para>
/// </summary>
public sealed class LocalDiskAudioBlobStore : IAudioBlobStore
{
    public const string DefaultBlobStoreRoot = "audio-blobs";

    private readonly string _root;
    private readonly ILogger<LocalDiskAudioBlobStore> _logger;

    public LocalDiskAudioBlobStore(
        IConfiguration config, ILogger<LocalDiskAudioBlobStore> logger)
    {
        var configured = config["Audio:BlobStoreRoot"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? DefaultBlobStoreRoot
            : configured;
        _logger = logger;
    }

    public async Task<string> WriteAsync(
        Guid conversationId,
        Guid messageId,
        byte[] content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var relativePath = BuildRelativePath(conversationId, messageId, mimeType);
        var absolutePath = Path.Combine(_root, relativePath);
        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(absolutePath, content, cancellationToken);
        _logger.LogInformation(
            "Audio blob written: conversation_id={ConversationId} message_id={MessageId} bytes={Bytes} path={RelativePath}",
            conversationId, messageId, content.Length, relativePath);
        return relativePath;
    }

    public Task<(Stream Content, string MimeType)?> ReadAsync(
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        // Probe both extensions we might have written (wav for child,
        // mp3 for assistant). The caller identifies the message by id
        // but does not carry the MIME type into the read.
        var wavPath = Path.Combine(_root, BuildRelativePath(conversationId, messageId, "audio/wav"));
        var mp3Path = Path.Combine(_root, BuildRelativePath(conversationId, messageId, "audio/mpeg"));
        if (File.Exists(wavPath))
        {
            return Task.FromResult<(Stream, string)?>(
                (File.OpenRead(wavPath), "audio/wav"));
        }
        if (File.Exists(mp3Path))
        {
            return Task.FromResult<(Stream, string)?>(
                (File.OpenRead(mp3Path), "audio/mpeg"));
        }
        return Task.FromResult<(Stream, string)?>(null);
    }

    private static string BuildRelativePath(Guid conversationId, Guid messageId, string mimeType)
    {
        var ext = ExtensionFor(mimeType);
        return Path.Combine(conversationId.ToString("N"), messageId.ToString("N") + ext);
    }

    private static string ExtensionFor(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return ".bin";
        var lower = mimeType.Trim().ToLowerInvariant();
        if (lower.StartsWith("audio/wav") || lower.StartsWith("audio/x-wav"))
            return ".wav";
        if (lower.StartsWith("audio/mpeg") || lower.StartsWith("audio/mp3"))
            return ".mp3";
        if (lower.StartsWith("audio/ogg"))
            return ".ogg";
        if (lower.StartsWith("audio/opus"))
            return ".opus";
        if (lower.StartsWith("audio/webm"))
            return ".webm";
        if (lower.StartsWith("audio/m4a") || lower.StartsWith("audio/mp4"))
            return ".m4a";
        if (lower.StartsWith("audio/flac"))
            return ".flac";
        return ".bin";
    }
}
