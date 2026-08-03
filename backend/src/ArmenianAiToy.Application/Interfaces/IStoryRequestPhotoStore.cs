namespace ArmenianAiToy.Application.Interfaces;

/// <summary>
/// Storage seam for custom-story-request photos (slice F): a parent's
/// photographed book page, saved server-side for the OWNER's fulfilment
/// queue only. Never publicly served; the internal console streams it to
/// authenticated operators. Same local-disk idiom as
/// <c>IAudioBlobStore</c>.
/// </summary>
public interface IStoryRequestPhotoStore
{
    /// <summary>Persists the photo and returns the server-internal
    /// relative path stored on the request row, or null when the content
    /// type is outside the allowlist.</summary>
    Task<string?> SaveAsync(
        Guid requestId, byte[] content, string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a previously saved photo, or null when missing.
    /// <paramref name="photoPath"/> must be exactly the value SaveAsync
    /// returned (implementations reject anything path-shaped).</summary>
    Task<(byte[] Content, string ContentType)?> ReadAsync(
        string photoPath, CancellationToken cancellationToken = default);
}
