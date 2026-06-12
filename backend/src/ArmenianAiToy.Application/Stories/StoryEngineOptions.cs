using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// Deterministic Story-engine flag (MODES.md §1A: "resolved by the
/// deterministic Story:Engine config flag — never by a heuristic").
/// Shipped default is <see cref="LegacyEngine"/>; a missing or
/// unrecognized config value also resolves to legacy, so the library
/// engine can only be enabled by an explicit, well-formed opt-in.
/// Flipping the flag to "library" is a HUMAN release act (the
/// ≥15-reviewed-stories criterion in MODES.md) — no code may switch
/// engines at runtime based on library size or any other signal.
/// </summary>
public sealed class StoryEngineOptions
{
    public const string SectionKey = "Story:Engine";
    public const string LegacyEngine = "legacy";
    public const string LibraryEngine = "library";

    /// <summary>Raw configured engine name. Defaults to legacy.</summary>
    public string Engine { get; init; } = LegacyEngine;

    /// <summary>True only for an explicit, exact (case-insensitive)
    /// "library" value. Anything else — missing, empty, typo'd — is
    /// legacy. Fail-safe direction: a misconfigured deployment keeps
    /// the long-established legacy behavior.</summary>
    public bool UseLibraryEngine =>
        string.Equals(Engine?.Trim(), LibraryEngine, StringComparison.OrdinalIgnoreCase);

    /// <summary>Manual read of <c>Story:Engine</c> — same narrow
    /// string-indexer convention the rest of Infrastructure DI uses
    /// (no Configuration.Binder dependency).</summary>
    public static StoryEngineOptions FromConfiguration(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var engine = config[SectionKey];
        return string.IsNullOrWhiteSpace(engine)
            ? new StoryEngineOptions()
            : new StoryEngineOptions { Engine = engine };
    }
}
