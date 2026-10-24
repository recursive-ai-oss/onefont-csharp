using System.Buffers;
using OneFont.Unicode;

namespace OneFont;

/// <summary>Unicode text segmentation: where a line may break (UAX #14) and where a user-perceived character ends (UAX #29).</summary>
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
}
