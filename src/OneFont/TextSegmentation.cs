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
        byte[] raw = ArrayPool<byte>.Shared.Rent(text.Length + 1);
        try
        {
            LineBreaker.Fill(text, raw);
            var result = new LineBreakOpportunity[text.Length + 1];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = (LineBreakOpportunity)raw[i];
            }

            return result;
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
}
