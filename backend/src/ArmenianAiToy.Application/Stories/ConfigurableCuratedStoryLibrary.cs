namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// Decorator over an inner <see cref="ICuratedStoryLibrary"/> that adds
/// two BENCH/LOCAL-only capabilities, both driven by config and both
/// inert when unconfigured:
/// <list type="number">
/// <item>side-loading extra story files from disk (parsed with
/// <c>requireApproved:false</c>, so an un-promoted DRAFT can be served
/// for a local bench WITHOUT changing its text, its review status, or
/// its review dates, and without embedding/promoting it); and</item>
/// <item>overriding which story <see cref="SelectDefault"/> returns via
/// a configured default id.</item>
/// </list>
/// <para>
/// With no extra files and no default id this decorator is transparent:
/// it returns exactly the inner library's stories and default, so the
/// shipped/production path (no <c>Story:ExtraStoryFiles</c>, no
/// <c>Story:DefaultStoryId</c>) is byte-for-byte unchanged. The
/// approved-only runtime guarantee is preserved for the embedded
/// library; the side-load is an explicit, operator-supplied local
/// override, never a promotion.
/// </para>
/// </summary>
public sealed class ConfigurableCuratedStoryLibrary : ICuratedStoryLibrary
{
    private readonly ICuratedStoryLibrary _inner;
    private readonly IReadOnlyList<CuratedStory> _all;
    private readonly string? _defaultId;

    private ConfigurableCuratedStoryLibrary(
        ICuratedStoryLibrary inner, IReadOnlyList<CuratedStory> all, string? defaultId)
    {
        _inner = inner;
        _all = all;
        _defaultId = defaultId;
    }

    /// <summary>
    /// Builds the decorator. <paramref name="extraStoryFilePaths"/> are
    /// loaded with <c>requireApproved:false</c>; missing files, parse
    /// failures, and id collisions with the inner library are skipped
    /// (reported via <paramref name="warn"/>) rather than throwing, so
    /// a misconfigured bench path can never stop the host from starting.
    /// <paramref name="defaultStoryId"/> (when non-blank) becomes the
    /// <see cref="SelectDefault"/> target if it resolves to a known
    /// story; otherwise selection falls back to the inner default.
    /// </summary>
    public static ConfigurableCuratedStoryLibrary Create(
        ICuratedStoryLibrary inner,
        IEnumerable<string> extraStoryFilePaths,
        string? defaultStoryId,
        Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(extraStoryFilePaths);

        var list = new List<CuratedStory>(inner.ListAvailable());
        foreach (var path in extraStoryFilePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }
            try
            {
                if (!File.Exists(path))
                {
                    warn?.Invoke($"extra story file not found: {path}");
                    continue;
                }
                // requireApproved:false — a local bench may serve a draft
                // (e.g. anban-huri) verbatim. Text/status/dates untouched.
                var story = StoryFileParser.Parse(
                    File.ReadAllText(path), path, requireApproved: false);
                if (list.Any(s => s.Id == story.Id))
                {
                    warn?.Invoke($"extra story id '{story.Id}' already present; skipping {path}");
                    continue;
                }
                list.Add(story);
                warn?.Invoke($"side-loaded local story '{story.Id}' from {path}");
            }
            catch (Exception ex)
            {
                warn?.Invoke($"failed to load extra story '{path}': {ex.Message}");
            }
        }

        var defaultId = string.IsNullOrWhiteSpace(defaultStoryId) ? null : defaultStoryId.Trim();
        return new ConfigurableCuratedStoryLibrary(inner, list, defaultId);
    }

    public CuratedStory? GetById(string id) =>
        _all.FirstOrDefault(s => s.Id == id);

    /// <summary>Configured default id when it resolves; otherwise the
    /// inner library's deterministic default.</summary>
    public CuratedStory SelectDefault() =>
        (_defaultId is not null ? GetById(_defaultId) : null) ?? _inner.SelectDefault();

    public IReadOnlyList<CuratedStory> ListAvailable() => _all;
}
