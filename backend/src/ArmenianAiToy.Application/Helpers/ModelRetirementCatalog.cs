namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Provider models with an announced removal date, checked against the
/// models this instance is actually configured to call so a retirement is a
/// loud boot-time warning months ahead instead of a dead voice path on the
/// day. Pure — the caller supplies the effective (config key, model) pairs
/// and today's date — so it is unit-testable.
///
/// Only removals corroborated by more than one source are listed; see
/// <c>docs/ai-landscape-2026-09.md</c> § R1. Add a row when a provider
/// announces one; never remove a row until the configured default has moved.
/// </summary>
public static class ModelRetirementCatalog
{
    public sealed record Retirement(string Model, DateOnly RemovalDate, string Replacement);

    public sealed record Warning(string ConfigKey, string Model, DateOnly RemovalDate,
        string Replacement, bool AlreadyRemoved);

    /// <summary>
    /// OpenAI notice of 2026-08-26: these four transcription models are
    /// removed from the API on 2027-02-26.
    /// </summary>
    public static readonly IReadOnlyList<Retirement> Known = new[]
    {
        new Retirement("whisper-1", new DateOnly(2027, 2, 26), "gpt-transcribe"),
        new Retirement("gpt-4o-transcribe", new DateOnly(2027, 2, 26), "gpt-transcribe"),
        new Retirement("gpt-4o-mini-transcribe", new DateOnly(2027, 2, 26), "gpt-transcribe"),
        new Retirement("gpt-4o-transcribe-diarize", new DateOnly(2027, 2, 26), "gpt-transcribe"),
    };

    /// <summary>
    /// Returns one warning per configured model that matches a known
    /// retirement — the bare name or a dated snapshot of it
    /// (<c>gpt-4o-mini-transcribe-2025-12-15</c>). Case-insensitive; blank
    /// models are ignored.
    /// </summary>
    public static IReadOnlyList<Warning> Evaluate(
        IEnumerable<(string ConfigKey, string? Model)> configured, DateOnly today)
    {
        var warnings = new List<Warning>();
        foreach (var (key, raw) in configured)
        {
            var model = raw?.Trim();
            if (string.IsNullOrEmpty(model))
                continue;

            var hit = Known.FirstOrDefault(r => Matches(model, r.Model));
            if (hit is null)
                continue;

            warnings.Add(new Warning(key, model, hit.RemovalDate, hit.Replacement,
                AlreadyRemoved: today >= hit.RemovalDate));
        }
        return warnings;
    }

    private static bool Matches(string model, string retired)
        => string.Equals(model, retired, StringComparison.OrdinalIgnoreCase)
           || (model.StartsWith(retired + "-20", StringComparison.OrdinalIgnoreCase)
               && model.Length == retired.Length + "-2025-12-15".Length);
}
