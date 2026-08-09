namespace ArmenianAiToy.Application.Audio;

/// <summary>
/// Splits a spoken Armenian reply into whole-sentence chunks that can be
/// synthesized CONCURRENTLY and concatenated back in order.
///
/// <para>
/// Pure function, no dependencies — the risky half of the parallel-TTS
/// latency work is "where do we cut?", so it lives on its own and is
/// unit-tested without any provider.
/// </para>
///
/// <para><b>Armenian punctuation matters here.</b> The question mark «՞»
/// and the exclamation «՜» are placed INSIDE the stressed vowel of a word,
/// not at the end of the sentence, so they are NOT sentence boundaries.
/// The Armenian full stop «։» (U+0589, verjaket) is. ASCII <c>. ! ?</c>
/// and the ellipsis are honored too because story text and canned lines
/// mix both conventions.
/// </para>
///
/// <para>
/// Two rules keep quality ahead of speed:
/// a chunk NEVER splits inside a sentence (a single long sentence stays
/// whole even if it exceeds the target), and text below
/// <c>minTextChars</c> is returned as ONE chunk so short lines take the
/// exact single-call path they take today.
/// </para>
/// </summary>
public static class SpeechChunker
{
    /// <summary>Sentence-final marks. «՞» / «՜» are deliberately absent —
    /// they are intra-word marks in Armenian.</summary>
    private static readonly char[] Terminators = ['։', '.', '!', '?', '…'];

    /// <summary>
    /// Splits <paramref name="text"/> into at most <paramref name="maxChunks"/>
    /// sentence-aligned chunks of roughly <paramref name="maxChunkChars"/>
    /// characters. Returns a single-element list (the trimmed original) when
    /// the text is short, has no sentence boundary, or splitting would not
    /// produce more than one chunk — callers treat that as "pass through
    /// unchanged".
    /// </summary>
    public static IReadOnlyList<string> Split(
        string? text, int minTextChars, int maxChunkChars, int maxChunks)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return [trimmed];
        }
        if (maxChunks < 2 || maxChunkChars < 1 || trimmed.Length < minTextChars)
        {
            return [trimmed];
        }

        var sentences = SplitSentences(trimmed);
        if (sentences.Count < 2)
        {
            // One sentence (or none detected) — never cut inside it.
            return [trimmed];
        }

        var chunks = Group(sentences, maxChunkChars);
        if (chunks.Count > maxChunks)
        {
            // Too many pieces: re-group against a wider target so the cap
            // holds. Ceiling division keeps the last chunk from being a
            // sliver.
            var target = (trimmed.Length + maxChunks - 1) / maxChunks;
            chunks = Group(sentences, Math.Max(target, maxChunkChars));
            // A run of very long sentences can still exceed the cap (we never
            // cut inside a sentence); merge the tail rather than break that rule.
            while (chunks.Count > maxChunks)
            {
                chunks[^2] = chunks[^2] + " " + chunks[^1];
                chunks.RemoveAt(chunks.Count - 1);
            }
        }

        return chunks.Count < 2 ? [trimmed] : chunks;
    }

    /// <summary>Cuts after each sentence-final mark, keeping the mark (and
    /// any trailing closing quote) with the sentence it ends.</summary>
    private static List<string> SplitSentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (Array.IndexOf(Terminators, text[i]) < 0)
            {
                continue;
            }
            // Absorb repeated marks («...», «!?») and a closing quote/bracket.
            var end = i;
            while (end + 1 < text.Length
                   && (Array.IndexOf(Terminators, text[end + 1]) >= 0
                       || text[end + 1] is '»' or '"' or '\'' or ')' or ']'))
            {
                end++;
            }
            var piece = text[start..(end + 1)].Trim();
            if (piece.Length > 0)
            {
                sentences.Add(piece);
            }
            start = end + 1;
            i = end;
        }
        var tail = start < text.Length ? text[start..].Trim() : string.Empty;
        if (tail.Length > 0)
        {
            sentences.Add(tail);
        }
        return sentences;
    }

    /// <summary>Greedily packs whole sentences into chunks of at most
    /// <paramref name="maxChunkChars"/>; a sentence longer than that becomes
    /// its own chunk rather than being cut.</summary>
    private static List<string> Group(List<string> sentences, int maxChunkChars)
    {
        var chunks = new List<string>();
        var current = string.Empty;
        foreach (var sentence in sentences)
        {
            if (current.Length == 0)
            {
                current = sentence;
                continue;
            }
            if (current.Length + 1 + sentence.Length <= maxChunkChars)
            {
                current = current + " " + sentence;
                continue;
            }
            chunks.Add(current);
            current = sentence;
        }
        if (current.Length > 0)
        {
            chunks.Add(current);
        }
        return chunks;
    }
}
