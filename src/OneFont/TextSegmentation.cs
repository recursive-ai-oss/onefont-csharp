using System.Buffers;
using OneFont.Unicode;

namespace OneFont;

/// <summary>Unicode text segmentation: where a line may break (UAX #14) and where a user-perceived character, a word or a sentence ends (UAX #29).</summary>
public static class TextSegmentation
{
    /// <summary>
    /// Finds the line break opportunities of a text with the default rules of the Unicode line breaking algorithm (UAX #14). The small kana
    /// of class CJ are treated as NS, the algorithm's strict reading; there is no other tailoring.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>
    /// <c>text.Length + 1</c> entries: entry <c>i</c> says whether a line may end before UTF-16 index <c>i</c>. Entry 0 is always
    /// <see cref="LineBreakOpportunity.None"/>; the last entry is <see cref="LineBreakOpportunity.Mandatory"/> when the text ends with a hard
    /// break and <see cref="LineBreakOpportunity.Allowed"/> otherwise (empty text gives a single <see cref="LineBreakOpportunity.Allowed"/>).
    /// Positions inside a surrogate pair or a combining sequence are <see cref="LineBreakOpportunity.None"/>.
    /// </returns>
    public static LineBreakOpportunity[] GetLineBreakOpportunities(ReadOnlySpan<char> text)
    {
        var result = new LineBreakOpportunity[text.Length + 1];
        GetLineBreakOpportunities(text, result);
        return result;
    }

    /// <summary>
    /// Finds the line break opportunities like <see cref="GetLineBreakOpportunities(ReadOnlySpan{char})"/> into a buffer the caller owns,
    /// without allocating.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="opportunities">Receives <c>text.Length + 1</c> entries, laid out as the array form returns them; it holds at least that many.</param>
    /// <exception cref="ArgumentException"><paramref name="opportunities"/> is shorter than <c>text.Length + 1</c>.</exception>
    public static void GetLineBreakOpportunities(ReadOnlySpan<char> text, Span<LineBreakOpportunity> opportunities)
    {
        if (opportunities.Length < text.Length + 1)
        {
            throw new ArgumentException("The opportunities buffer is shorter than the text plus one.", nameof(opportunities));
        }

        byte[] raw = ArrayPool<byte>.Shared.Rent(text.Length + 1);
        try
        {
            LineBreaker.Fill(text, raw);
            for (int i = 0; i <= text.Length; i++)
            {
                opportunities[i] = (LineBreakOpportunity)raw[i];
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(raw);
        }
    }

    /// <summary>
    /// Finds the extended grapheme cluster boundaries of a text (UAX #29 section 3, including the Indic conjunct rule): a base with its
    /// combining marks, an emoji sequence, a Hangul syllable block or a regional indicator pair is one cluster.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The UTF-16 index where each cluster starts, ascending, followed by <c>text.Length</c>. Empty text gives <c>[0]</c>.</returns>
    public static int[] GetGraphemeBoundaries(ReadOnlySpan<char> text)
    {
        var result = new List<int>();
        int i = 0;
        while (i < text.Length)
        {
            result.Add(i);
            i += Graphemes.NextLength(text[i..]);
        }

        result.Add(text.Length);
        return result.ToArray();
    }

    /// <summary>Gives the length in UTF-16 code units of the grapheme cluster at the start of a text.</summary>
    /// <param name="text">Text that starts at a cluster boundary.</param>
    /// <returns>The length, at least 1 (an unpaired surrogate is its own cluster); 0 for empty text.</returns>
    public static int GetGraphemeLength(ReadOnlySpan<char> text) => Graphemes.NextLength(text);

    /// <summary>Gets whether a code point has East_Asian_Width Fullwidth, Wide or Halfwidth (ideographs, kana, Hangul syllables, fullwidth forms and the like).</summary>
    /// <param name="codePoint">The code point; values outside Unicode give <see langword="false"/>.</param>
    /// <returns><see langword="true"/> for such a character.</returns>
    public static bool IsEastAsianWide(int codePoint) => (uint)codePoint <= 0x10FFFF && LineBreaker.IsEastAsianWide(codePoint);

    /// <summary>
    /// Fills line break opportunities like <see cref="GetLineBreakOpportunities(ReadOnlySpan{char}, Span{LineBreakOpportunity})"/>, as plain
    /// bytes: 0 none, 1 allowed, 2 mandatory (the values of <see cref="LineBreakOpportunity"/>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="opportunities">Receives <c>text.Length + 1</c> entries, one per UTF-16 offset; it holds at least that many. Further entries are not touched.</param>
    /// <exception cref="ArgumentException"><paramref name="opportunities"/> is shorter than <c>text.Length + 1</c>.</exception>
    public static void FillLineBreakOpportunities(ReadOnlySpan<char> text, Span<byte> opportunities)
    {
        if (opportunities.Length < text.Length + 1)
        {
            throw new ArgumentException("The opportunities buffer is shorter than the text plus one.", nameof(opportunities));
        }

        LineBreaker.Fill(text, opportunities);
    }

    /// <summary>Marks where grapheme clusters start, as plain bytes, one per UTF-16 offset.</summary>
    /// <param name="text">The text.</param>
    /// <param name="starts">Receives <c>text.Length + 1</c> entries: 1 at an offset where a cluster starts (and always at <c>text.Length</c>), 0 inside a cluster; it holds at least that many. Further entries are not touched.</param>
    /// <exception cref="ArgumentException"><paramref name="starts"/> is shorter than <c>text.Length + 1</c>.</exception>
    public static void FillGraphemeStarts(ReadOnlySpan<char> text, Span<byte> starts)
    {
        if (starts.Length < text.Length + 1)
        {
            throw new ArgumentException("The starts buffer is shorter than the text plus one.", nameof(starts));
        }

        Graphemes.Fill(text, starts);
    }

    /// <summary>
    /// Finds the word boundaries of a text (UAX #29 section 4, default rules WB1 to WB999, no tailoring): letters, digits and the
    /// punctuation that joins them (an apostrophe in "can't", a point in "3.14") stay together; white space and other symbols stand
    /// alone. The segments include punctuation and white space; keeping only those that hold a letter or digit is the caller's choice.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The UTF-16 index where each segment starts, ascending, followed by <c>text.Length</c>. Empty text gives <c>[0]</c>. Positions inside a surrogate pair are never boundaries; an unpaired surrogate is an ordinary character of no class.</returns>
    public static int[] GetWordBoundaries(ReadOnlySpan<char> text)
    {
        var result = new List<int>();
        int i = 0;
        while (i < text.Length)
        {
            result.Add(i);
            i = WordBreaker.Next(text, i);
        }

        result.Add(text.Length);
        return result.ToArray();
    }

    /// <summary>Marks where word segments start (UAX #29 section 4), as plain bytes, one per UTF-16 offset.</summary>
    /// <param name="text">The text.</param>
    /// <param name="starts">Receives <c>text.Length + 1</c> entries: 1 at an offset where a segment starts (and always at <c>text.Length</c>), 0 inside a segment; it holds at least that many. Further entries are not touched.</param>
    /// <exception cref="ArgumentException"><paramref name="starts"/> is shorter than <c>text.Length + 1</c>.</exception>
    public static void FillWordStarts(ReadOnlySpan<char> text, Span<byte> starts)
    {
        if (starts.Length < text.Length + 1)
        {
            throw new ArgumentException("The starts buffer is shorter than the text plus one.", nameof(starts));
        }

        starts[..(text.Length + 1)].Clear();
        int i = 0;
        while (i < text.Length)
        {
            starts[i] = 1;
            i = WordBreaker.Next(text, i);
        }

        starts[text.Length] = 1;
    }

    /// <summary>
    /// Finds the end of the word segment that starts at an index (UAX #29 section 4), so a caller can walk a text one segment at a
    /// time without building the whole boundary list. The cost is proportional to the segment, not the text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">A boundary: 0 or a value this method or <see cref="GetWordBoundaries(ReadOnlySpan{char})"/> returned earlier. From any other index the text is read as if a segment started there.</param>
    /// <returns>The UTF-16 index just past the segment; <c>text.Length</c> for the last segment and when <paramref name="index"/> equals <c>text.Length</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or greater than <c>text.Length</c>.</exception>
    public static int GetNextWordBoundary(ReadOnlySpan<char> text, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, text.Length);
        return WordBreaker.Next(text, index);
    }

    /// <summary>
    /// Finds the sentence boundaries of a text (UAX #29 section 5, default rules SB1 to SB998, no tailoring and no abbreviation list):
    /// a sentence ends after its terminator (. ? ! and their relatives), any closing quotes or brackets and the white space that
    /// follows, unless the rules read the full stop as part of a number, an initial or a lower-case continuation.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The UTF-16 index where each sentence starts, ascending, followed by <c>text.Length</c>. Empty text gives <c>[0]</c>. A sentence keeps its trailing white space.</returns>
    public static int[] GetSentenceBoundaries(ReadOnlySpan<char> text)
    {
        var result = new List<int>();
        int i = 0;
        while (i < text.Length)
        {
            result.Add(i);
            i = SentenceBreaker.Next(text, i);
        }

        result.Add(text.Length);
        return result.ToArray();
    }

    /// <summary>Marks where sentences start (UAX #29 section 5), as plain bytes, one per UTF-16 offset.</summary>
    /// <param name="text">The text.</param>
    /// <param name="starts">Receives <c>text.Length + 1</c> entries: 1 at an offset where a sentence starts (and always at <c>text.Length</c>), 0 inside a sentence; it holds at least that many. Further entries are not touched.</param>
    /// <exception cref="ArgumentException"><paramref name="starts"/> is shorter than <c>text.Length + 1</c>.</exception>
    public static void FillSentenceStarts(ReadOnlySpan<char> text, Span<byte> starts)
    {
        if (starts.Length < text.Length + 1)
        {
            throw new ArgumentException("The starts buffer is shorter than the text plus one.", nameof(starts));
        }

        starts[..(text.Length + 1)].Clear();
        int i = 0;
        while (i < text.Length)
        {
            starts[i] = 1;
            i = SentenceBreaker.Next(text, i);
        }

        starts[text.Length] = 1;
    }

    /// <summary>
    /// Finds the end of the sentence that starts at an index (UAX #29 section 5), so a caller can walk a text one sentence at a time.
    /// Rule SB8 may look ahead past the terminator, up to the next terminator or paragraph separator.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">A boundary: 0 or a value this method or <see cref="GetSentenceBoundaries(ReadOnlySpan{char})"/> returned earlier. From any other index the text is read as if a sentence started there.</param>
    /// <returns>The UTF-16 index just past the sentence; <c>text.Length</c> for the last sentence and when <paramref name="index"/> equals <c>text.Length</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or greater than <c>text.Length</c>.</exception>
    public static int GetNextSentenceBoundary(ReadOnlySpan<char> text, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, text.Length);
        return SentenceBreaker.Next(text, index);
    }
}
