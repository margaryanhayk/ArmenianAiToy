using ArmenianAiToy.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.StoryRequests;

/// <summary>
/// Local-disk photo store for custom story requests (slice F). Layout is
/// flat — <c>{requestId:N}.{ext}</c> under <c>StoryRequests:PhotoRoot</c>
/// (default <c>story-requests</c>, resolved like the audio-blob root).
/// The stored path is the FILE NAME only; ReadAsync rejects anything
/// path-shaped so a tampered DB value can never traverse the filesystem.
/// </summary>
public sealed class LocalDiskStoryRequestPhotoStore : IStoryRequestPhotoStore
{
    public const string DefaultPhotoRoot = "story-requests";

    /// <summary>Bounded upload vocabulary — phone camera formats only.</summary>
    private static readonly Dictionary<string, string> ExtensionByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = "jpg",
            ["image/png"] = "png",
            ["image/heic"] = "heic",
        };

    private readonly string _root;
    private readonly ILogger<LocalDiskStoryRequestPhotoStore> _logger;

    public LocalDiskStoryRequestPhotoStore(
        IConfiguration config, ILogger<LocalDiskStoryRequestPhotoStore> logger)
    {
        var configured = config["StoryRequests:PhotoRoot"];
        _root = string.IsNullOrWhiteSpace(configured) ? DefaultPhotoRoot : configured;
        _logger = logger;
    }

    public async Task<string?> SaveAsync(
        Guid requestId, byte[] content, string contentType,
        CancellationToken cancellationToken = default)
    {
        if (!ExtensionByContentType.TryGetValue(contentType ?? string.Empty, out var ext))
        {
            return null;
        }
        Directory.CreateDirectory(_root);
        var fileName = $"{requestId:N}.{ext}";
        await File.WriteAllBytesAsync(
            Path.Combine(_root, fileName), content, cancellationToken);
        _logger.LogInformation(
            "Story-request photo written: request_id={RequestId} bytes={Bytes}",
            requestId, content.Length);
        return fileName;
    }

    public async Task<(byte[] Content, string ContentType)?> ReadAsync(
        string photoPath, CancellationToken cancellationToken = default)
    {
        // The stored value is a bare file name; anything path-shaped is
        // rejected outright (defense against a tampered DB value).
        if (string.IsNullOrWhiteSpace(photoPath)
            || photoPath.Contains('/') || photoPath.Contains('\\')
            || photoPath.Contains(".."))
        {
            return null;
        }
        var fullPath = Path.Combine(_root, photoPath);
        if (!File.Exists(fullPath))
        {
            return null;
        }
        var contentType = Path.GetExtension(photoPath).ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".png" => "image/png",
            ".heic" => "image/heic",
            _ => "application/octet-stream",
        };
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        return (bytes, contentType);
    }
}
