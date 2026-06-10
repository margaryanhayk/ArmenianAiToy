using System.Reflection;
using System.Text;

namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// Curated story library backed by <c>*.story.json</c> EMBEDDED
/// RESOURCES. Slice 1: the mechanism only — no real story content is
/// embedded in the Application assembly yet, nothing constructs this
/// class at runtime, and there is no DI registration.
/// <see cref="InMemoryCuratedStoryLibrary"/> remains the canonical
/// library until the migration slice.
/// <para>
/// Why embedded resources: approval becomes a reviewed git commit
/// (moving a file into the embedded folder), the served text is exactly
/// what was reviewed and compiled (no on-host file edits, no missing-
/// file runtime IO), and draft files living outside backend/src are
/// structurally incapable of being loaded.
/// </para>
/// </summary>
public sealed class EmbeddedCuratedStoryLibrary : ICuratedStoryLibrary
{
    private readonly IReadOnlyList<CuratedStory> _stories;

    /// <summary>
    /// Loads and strictly validates every <c>*.story.json</c> resource
    /// under <paramref name="resourcePrefix"/> in
    /// <paramref name="assembly"/>. Throws on zero resources, on any
    /// schema violation, on a non-approved story, on a duplicate id, or
    /// on an id that does not match its file name — a misconfigured
    /// library must fail loudly at construction, never serve partially.
    /// </summary>
    public EmbeddedCuratedStoryLibrary(Assembly assembly, string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);

        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name =>
                name.StartsWith(resourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".story.json", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (resourceNames.Count == 0)
        {
            throw new InvalidOperationException(
                $"No *.story.json embedded resources found under prefix '{resourcePrefix}' " +
                $"in assembly '{assembly.GetName().Name}'. A story library with zero stories " +
                "is a misconfiguration, not an empty catalog.");
        }

        var stories = new List<CuratedStory>(resourceNames.Count);
        foreach (var resourceName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Resource '{resourceName}' could not be opened.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var story = StoryFileParser.Parse(reader.ReadToEnd(), resourceName);

            // Id must match the file name stem: <id>.story.json. Keeps
            // file listings honest and prevents copy-paste id drift.
            if (!resourceName.EndsWith($".{story.Id}.story.json", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{resourceName}: story id '{story.Id}' does not match the file name " +
                    $"(expected '...{story.Id}.story.json').");
            }
            stories.Add(story);
        }

        var duplicate = stories.GroupBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Duplicate story id '{duplicate.Key}' across embedded resources.");
        }

        // Ordinal id order makes ListAvailable / SelectDefault
        // deterministic regardless of resource enumeration order.
        _stories = stories.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
    }

    public CuratedStory? GetById(string id) =>
        _stories.FirstOrDefault(s => s.Id == id);

    /// <summary>Deterministic: first story by ordinal id order. A later
    /// selection slice layers no-repeat / BedtimeSafe / tag policy on
    /// top of ListAvailable.</summary>
    public CuratedStory SelectDefault() => _stories[0];

    public IReadOnlyList<CuratedStory> ListAvailable() => _stories;
}
