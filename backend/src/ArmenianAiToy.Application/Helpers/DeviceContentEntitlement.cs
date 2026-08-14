namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Per-toy content entitlement: the ONE function that turns the fleet
/// catalogue plus a device's override rows into that device's catalogue.
///
/// <para>
/// <b>One precedence rule, stated once:</b> a device override beats the
/// catalogue default. A <c>deny</c> row removes the item for that toy; an
/// <c>allow</c> row admits an item that is fleet-dark. Nothing is fleet-dark
/// today (every configured <c>ContentSync</c> item is offered to every toy),
/// so an <c>allow</c> row is currently inert — it is implemented, tested and
/// storable so the upload slice, which introduces fleet-dark catalogue rows,
/// changes this function's shape not at all.
/// </para>
///
/// <para>
/// <b>No overrides means the SAME OBJECT back.</b> <see cref="Apply"/>
/// returns the baseline reference when a device has nothing that changes its
/// catalogue, so "a toy with no override rows gets today's manifest exactly"
/// is true by construction rather than by careful copying — and the list
/// surfaces (the parent's devices, the console's device table) pay nothing
/// for a feature almost no toy uses.
/// </para>
///
/// <para>
/// Pure: no DB, no IO, no config reads, no clock. Callers hand it rows they
/// have already loaded, which is what lets the two list surfaces resolve a
/// whole page of devices from a single query instead of one per device.
/// </para>
/// </summary>
public static class DeviceContentEntitlement
{
    public const string KindStory = "story";
    public const string KindGame = "game";
    public const string KindVoice = "voice";
    public const string KindMusic = "music";

    public const string ModeAllow = "allow";
    public const string ModeDeny = "deny";

    /// <summary>The four namespaces, matching the four <c>ContentSync</c>
    /// lists exactly. Bounded on purpose — an unknown kind is refused at the
    /// write endpoint rather than stored and silently ignored forever.</summary>
    public static readonly IReadOnlyList<string> Kinds =
        [KindStory, KindGame, KindVoice, KindMusic];

    public static readonly IReadOnlyList<string> Modes = [ModeAllow, ModeDeny];

    /// <summary>Longest key we will store. A game key is a pair
    /// (<c>48 + '/' + 48</c>); every other kind is a single id bounded at 48.
    /// Bounding it here keeps an operator typo from becoming an unbounded
    /// column value.</summary>
    public const int MaxItemKeyLength = 97;

    public static bool IsKnownKind(string? kind)
        => kind is not null && Kinds.Contains(kind.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsKnownMode(string? mode)
        => mode is not null && Modes.Contains(mode.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Normalises a kind/mode/key for storage and comparison. Kinds
    /// and modes are lowercased; keys keep their case but are trimmed, and
    /// every comparison in this file is case-insensitive, matching the
    /// content-sync contract where one backend id can never become two
    /// files.</summary>
    public static string NormalizeKind(string? kind) => (kind ?? string.Empty).Trim().ToLowerInvariant();

    public static string NormalizeMode(string? mode) => (mode ?? string.Empty).Trim().ToLowerInvariant();

    public static string NormalizeKey(string? key) => (key ?? string.Empty).Trim();

    /// <summary>
    /// The identity of one game clip. The PAIR is the key — four of the five
    /// games each define a clip called <c>intro</c> — and this is the single
    /// place it is built, so an entitlement row can never address a clip by a
    /// different name than the manifest and the content-file endpoint do.
    /// </summary>
    public static string GameItemKey(string? gameKey, string? clipId)
        => $"{NormalizeKey(gameKey)}/{NormalizeKey(clipId)}";

    /// <summary>
    /// Whether a key is well-formed for its namespace: the SAME id allowlist
    /// the manifest holds every content id to (lowercase ASCII, digits,
    /// <c>-</c>, <c>_</c>, bounded at 48), and for a game the pair
    /// <c>gameKey/clipId</c> — each half held to the same rule.
    ///
    /// <para>
    /// Two reasons this is enforced rather than assumed. A key outside the
    /// allowlist can never match a catalogue item, so storing one records an
    /// operator decision that will silently do nothing forever. And the key
    /// travels back out to the console, where it lands in an inline handler
    /// — an id that cannot contain a quote or an angle bracket cannot become
    /// markup, which is a stronger guarantee than remembering to escape it at
    /// every render site.
    /// </para>
    /// </summary>
    public static bool IsValidItemKey(string? kind, string? key)
    {
        var value = NormalizeKey(key);
        if (value.Length == 0 || value.Length > MaxItemKeyLength)
        {
            return false;
        }
        if (string.Equals(NormalizeKind(kind), KindGame, StringComparison.Ordinal))
        {
            var halves = value.Split('/');
            return halves.Length == 2 && halves.All(IsValidId);
        }
        return IsValidId(value);
    }

    /// <summary>The firmware's <c>cs_is_valid_story_id</c> allowlist, kept in
    /// step here exactly as <c>ContentManifestService</c> keeps it.</summary>
    private static bool IsValidId(string value)
    {
        if (value.Length == 0 || value.Length > 48)
        {
            return false;
        }
        foreach (var c in value)
        {
            if (c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// This device's catalogue: the fleet <paramref name="baseline"/> with
    /// its <c>deny</c> rows removed.
    /// </summary>
    /// <param name="baseline">The fleet catalogue (the DI singleton).</param>
    /// <param name="overrides">This device's rows. Empty / null is the
    /// overwhelmingly common case and returns <paramref name="baseline"/>
    /// itself.</param>
    public static ContentSyncOptions Apply(
        ContentSyncOptions baseline,
        IEnumerable<(string Kind, string Key, string Mode)>? overrides)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (overrides is null)
        {
            return baseline;
        }

        var denied = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, key, mode) in overrides)
        {
            // Only deny rows change anything today. An allow row is recorded
            // (it is the upload slice's "send this to one toy") but for a
            // catalogue where every item is already fleet-wide it can only
            // re-state the default, so it must NOT be read as a whitelist —
            // doing that would hide every item the operator has not named.
            if (!string.Equals(NormalizeMode(mode), ModeDeny, StringComparison.Ordinal))
            {
                continue;
            }
            var normalizedKey = NormalizeKey(key);
            if (normalizedKey.Length == 0)
            {
                continue;
            }
            var normalizedKind = NormalizeKind(kind);
            if (!denied.TryGetValue(normalizedKind, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                denied[normalizedKind] = set;
            }
            set.Add(normalizedKey);
        }

        if (denied.Count == 0)
        {
            return baseline;
        }

        var deniedStories = Set(denied, KindStory);
        var deniedGames = Set(denied, KindGame);
        var deniedVoice = Set(denied, KindVoice);
        var deniedMusic = Set(denied, KindMusic);

        // Filtering runs over the RESOLVED lists, not the raw config, for two
        // reasons. AudioRoot has already been applied, so the result carries
        // absolute paths and needs no root of its own. And the legacy
        // single-item shape has already been projected into a one-element
        // list — without that, denying the only story on a legacy-configured
        // deployment would empty Stories and hand ResolveStories() straight
        // back to the legacy scalars, re-serving the story that was just
        // withheld.
        return new ContentSyncOptions
        {
            Enabled = baseline.Enabled,
            // Deliberately empty: paths below are already resolved, and
            // ResolveAudioPath is a no-op on them either way.
            AudioRoot = string.Empty,
            Stories = baseline.ResolveStories()
                .Where(s => !deniedStories.Contains(NormalizeKey(s.StoryId)))
                .ToList(),
            Games = baseline.ResolveGames()
                .Where(g => !deniedGames.Contains(GameItemKey(g.GameKey, g.ClipId)))
                .ToList(),
            Voice = baseline.ResolveVoice()
                .Where(v => !deniedVoice.Contains(NormalizeKey(v.VoiceId)))
                .ToList(),
            Music = baseline.ResolveMusic()
                .Where(m => !deniedMusic.Contains(NormalizeKey(m.TrackId)))
                .ToList(),
            // The legacy scalars are left at their defaults on purpose. The
            // Stories list above is authoritative now, and a default-shaped
            // legacy item (empty id, zero size) is dropped by the manifest
            // validator — so denying every story yields an empty manifest
            // rather than falling back to a story nobody asked for.
        };
    }

    private static HashSet<string> Set(Dictionary<string, HashSet<string>> denied, string kind)
        => denied.TryGetValue(kind, out var set)
            ? set
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
