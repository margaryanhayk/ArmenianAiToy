// EvidenceWriter — produces per-session MD + JSON evidence and a
// LATEST_SUMMARY.md roll-up at the end of every run.

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ArmenianAiToy.Tools.StoryInteractiveLoop;

internal static class EvidenceWriter
{
    public static async Task<string> WritePerSessionAsync(string outputDir, SessionRecord record)
    {
        var stem = $"story-loop-{record.RunStamp}-{record.SessionIndex:D2}";
        var jsonPath = Path.Combine(outputDir, stem + ".json");
        var mdPath = Path.Combine(outputDir, stem + ".md");

        await File.WriteAllTextAsync(jsonPath,
            JsonSerializer.Serialize(record, Program.JsonOpts));

        var md = BuildSessionMarkdown(record);
        await File.WriteAllTextAsync(mdPath, md);
        return mdPath;
    }

    public static async Task<string> WriteLatestSummaryAsync(
        string outputDir,
        IReadOnlyList<SessionRecord> sessions,
        GitSnapshot git,
        string runStamp,
        RunSettings settings)
    {
        var path = Path.Combine(outputDir, "LATEST_SUMMARY.md");
        var sb = new StringBuilder();
        sb.AppendLine("# StoryInteractiveLoop — Latest Run Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Run stamp**: `{runStamp}` (UTC)");
        sb.AppendLine($"- **Git SHA**: `{Short(git.Sha)}` (dirty={git.Dirty})");
        sb.AppendLine($"- **Branch**: `{git.Branch}`");
        sb.AppendLine($"- **Base URL**: `{settings.BaseUrl}`");
        sb.AppendLine($"- **Sessions**: {sessions.Count} (max={settings.MaxSessions}, turns={settings.MaxTurns}, strategy={settings.Strategy}, allowLarger={settings.AllowLarger})");
        sb.AppendLine();

        int pass = sessions.Count(s => s.Verdict.OverallVerdict == "PASS");
        int warn = sessions.Count(s => s.Verdict.OverallVerdict == "WARN");
        int fail = sessions.Count(s => s.Verdict.OverallVerdict == "FAIL");
        int turns = sessions.Sum(s => s.Turns.Count);

        sb.AppendLine("## Verdict roll-up");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Sessions PASS | {pass} |");
        sb.AppendLine($"| Sessions WARN | {warn} |");
        sb.AppendLine($"| Sessions FAIL | {fail} |");
        sb.AppendLine($"| Total turns   | {turns} |");
        sb.AppendLine($"| Avg Armenian   | {AvgScore(sessions, v => v.ArmenianQualityScore):F1} |");
        sb.AppendLine($"| Avg Story logic | {AvgScore(sessions, v => v.StoryLogicScore):F1} |");
        sb.AppendLine($"| Avg Suitability | {AvgScore(sessions, v => v.ChildSuitabilityScore):F1} |");
        sb.AppendLine($"| Avg Choice quality | {AvgScore(sessions, v => v.ChoiceQualityScore):F1} |");
        sb.AppendLine($"| Avg Continuation | {AvgScore(sessions, v => v.ContinuationCoherenceScore):F1} |");
        sb.AppendLine();

        // ---- recurring warning histogram ----
        var hist = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in sessions)
        foreach (var t in s.Turns)
        foreach (var w in t.Warnings)
        {
            var key = w.Split(':')[0];
            hist[key] = hist.GetValueOrDefault(key, 0) + 1;
        }
        if (hist.Count > 0)
        {
            sb.AppendLine("## Recurring warnings (turn-count)");
            sb.AppendLine();
            sb.AppendLine("| Code | Count |");
            sb.AppendLine("|------|-------|");
            foreach (var kv in hist.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"| `{kv.Key}` | {kv.Value} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Sessions");
        sb.AppendLine();
        sb.AppendLine("| # | Seed | Stop reason | Turns | Verdict | Arm | Logic | Suit | Choice | Cont |");
        sb.AppendLine("|---|------|-------------|-------|---------|-----|-------|------|--------|------|");
        foreach (var s in sessions)
        {
            sb.AppendLine(
                $"| {s.SessionIndex} | `{s.SeedId}` | {s.StopReason ?? "—"} | {s.Turns.Count} | " +
                $"**{s.Verdict.OverallVerdict}** | {s.Verdict.ArmenianQualityScore} | " +
                $"{s.Verdict.StoryLogicScore} | {s.Verdict.ChildSuitabilityScore} | " +
                $"{s.Verdict.ChoiceQualityScore} | {s.Verdict.ContinuationCoherenceScore} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Per-session evidence files");
        sb.AppendLine();
        foreach (var s in sessions)
        {
            var stem = $"story-loop-{s.RunStamp}-{s.SessionIndex:D2}";
            sb.AppendLine($"- session {s.SessionIndex}: `{stem}.md` / `{stem}.json`");
        }
        sb.AppendLine();

        sb.AppendLine("> This file is overwritten on every run. Per-session MD/JSON files persist.");

        await File.WriteAllTextAsync(path, sb.ToString());
        return path;
    }

    private static string BuildSessionMarkdown(SessionRecord r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Story loop session {r.SessionIndex} — {r.SeedId}");
        sb.AppendLine();
        sb.AppendLine($"- **Run stamp**: `{r.RunStamp}` UTC");
        sb.AppendLine($"- **Started**: {r.StartedAtUtc:O}");
        sb.AppendLine($"- **Ended**: {r.EndedAtUtc:O}");
        sb.AppendLine($"- **Git SHA**: `{Short(r.GitSha)}` (dirty={r.Dirty})");
        sb.AppendLine($"- **Branch**: `{r.Branch}`");
        sb.AppendLine($"- **Device id**: `{r.DeviceId}`");
        sb.AppendLine($"- **Seed prompt**: `{r.SeedPrompt}`");
        sb.AppendLine($"- **Start-choice strategy**: `{r.StartChoiceStrategy}`");
        sb.AppendLine($"- **Stop reason**: `{r.StopReason}`");
        if (!string.IsNullOrEmpty(r.FatalError))
            sb.AppendLine($"- **Fatal error**: `{r.FatalError}`");
        sb.AppendLine();

        sb.AppendLine("## Verdict");
        sb.AppendLine();
        sb.AppendLine($"**Overall: {r.Verdict.OverallVerdict}**");
        sb.AppendLine();
        sb.AppendLine($"| Dimension | Score |");
        sb.AppendLine($"|-----------|-------|");
        sb.AppendLine($"| Armenian quality        | {r.Verdict.ArmenianQualityScore} |");
        sb.AppendLine($"| Story logic             | {r.Verdict.StoryLogicScore} |");
        sb.AppendLine($"| Child suitability       | {r.Verdict.ChildSuitabilityScore} |");
        sb.AppendLine($"| Choice quality          | {r.Verdict.ChoiceQualityScore} |");
        sb.AppendLine($"| Continuation coherence  | {r.Verdict.ContinuationCoherenceScore} |");
        sb.AppendLine($"| Average                 | {r.Verdict.AverageScore:F1} |");
        sb.AppendLine();

        sb.AppendLine("## Turns");
        sb.AppendLine();
        foreach (var t in r.Turns)
        {
            sb.AppendLine($"### Turn {t.TurnIndex}");
            sb.AppendLine();
            sb.AppendLine($"- **Mode**: `{t.Mode}` | safetyFlag={t.SafetyFlag} | http={t.HttpStatus} | latencyMs={t.LatencyMs}");
            sb.AppendLine($"- **storySessionId**: in=`{t.StorySessionIdIn}` → out=`{t.StorySessionIdOut}`");
            if (t.SelectedChoice is not null)
                sb.AppendLine($"- **Selected previous choice**: `{t.SelectedChoice}` — `{t.SelectedChoiceLabel}`");
            sb.AppendLine();
            sb.AppendLine($"**User input**:");
            sb.AppendLine();
            sb.AppendLine($"> {Escape(t.UserInput)}");
            sb.AppendLine();
            sb.AppendLine($"**Assistant body** ({t.BodyLen} chars, Armenian ratio {t.ArmenianRatio:F2}):");
            sb.AppendLine();
            sb.AppendLine($"> {Escape(t.AssistantBody ?? "")}");
            sb.AppendLine();
            sb.AppendLine($"- **ChoiceA**: `{t.ChoiceA ?? "(none)"}`");
            sb.AppendLine($"- **ChoiceB**: `{t.ChoiceB ?? "(none)"}`");
            sb.AppendLine();
            if (t.Warnings.Count > 0)
            {
                sb.AppendLine($"**Warnings**:");
                foreach (var w in t.Warnings) sb.AppendLine($"- `{w}`");
                sb.AppendLine();
            }
            if (!string.IsNullOrEmpty(t.ErrorMessage))
            {
                sb.AppendLine($"**Error**: `{Escape(t.ErrorMessage)}`");
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Short(string sha) =>
        string.IsNullOrEmpty(sha) ? "(unknown)" : sha[..Math.Min(8, sha.Length)];

    private static double AvgScore(IReadOnlyList<SessionRecord> sessions, Func<SessionVerdict, int> sel)
    {
        if (sessions.Count == 0) return 0.0;
        return sessions.Average(s => (double)sel(s.Verdict));
    }

    // Markdown blockquote escape — keep this minimal. We only need to
    // make sure a child's message containing a leading '>' doesn't merge
    // into the surrounding quote block.
    private static string Escape(string s) =>
        s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
}
