using System.Buffers;
using OneFont.Shaping;
using OneFont.Unicode;

namespace OneFont;

/// <summary>
/// One glyph of a run shaped from code points with <c>TextShaping.ShapeCodePoints</c>, in the order the shaper produced them
/// (no reordering into logical cluster order, unlike <see cref="ShapedRun"/>). Distances are in font units; the advance already includes
/// pair adjustments.
/// </summary>
/// <param name="GlyphId">The glyph id in the face that shaped the run.</param>
/// <param name="Cluster">The cluster value the caller gave for the first character the glyph stands for (several glyphs of one character share it).</param>
/// <param name="CodePoint">The first code point the glyph stands for. A glyph the shaper inserted takes the code point of the character it was inserted for.</param>
/// <param name="XAdvance">The horizontal advance, including pair kerning and other GPOS adjustments.</param>
/// <param name="XOffset">How far the glyph is drawn right of the pen position. In a right-to-left run offsets are meant for display order, which the caller gets by reversing the glyphs.</param>
/// <param name="YOffset">How far the glyph is drawn above the baseline.</param>
public readonly record struct CodePointGlyph(int GlyphId, int Cluster, int CodePoint, int XAdvance, int XOffset, int YOffset);

/// <summary>
/// The glyphs of a run shaped with <see cref="TextShaping.ShapeCodePoints(FontFace, ReadOnlySpan{int}, ReadOnlySpan{int}, CodePointRun, bool, ShapingOptions?, ref uint)"/>,
/// kept in one array that grows as needed and is reused by the next call, so a caller that shapes many runs allocates nothing per run.
/// Read <see cref="Glyphs"/> before the next call. A <see cref="CodePointRun"/> is not thread-safe; use one per thread.
/// </summary>
public sealed class CodePointRun
{
    private CodePointGlyph[] _glyphs = [];

    /// <summary>Gets the glyphs of the last run shaped into this object, in the order the shaper produced them. Empty before the first call.</summary>
    public ReadOnlySpan<CodePointGlyph> Glyphs => _glyphs.AsSpan(0, Count);

    /// <summary>Gets the number of glyphs.</summary>
    public int Count { get; internal set; }

    /// <summary>Gets the glyph at <paramref name="index"/>.</summary>
    /// <param name="index">A position from 0 to <see cref="Count"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside that range.</exception>
    public CodePointGlyph this[int index] => (uint)index < (uint)Count ? _glyphs[index] : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Forgets the glyphs; the array is kept for reuse.</summary>
    public void Clear() => Count = 0;

    internal CodePointGlyph[] Reserve(int n)
    {
        if (_glyphs.Length < n)
        {
            _glyphs = new CodePointGlyph[Math.Max(n, _glyphs.Length * 2)];
        }

        return _glyphs;
    }
}

public static partial class TextShaping
{
    /// <summary>
    /// Shapes a run given as code points, for callers that split text into runs themselves (by direction, face or style) and keep their own
    /// cluster numbering. The glyphs come in the order the shaper produced them, each with the cluster value and code point it stands for.
    /// </summary>
    /// <param name="face">The face.</param>
    /// <param name="codePoints">The run in logical order. A value that is not a Unicode scalar value (negative, a surrogate or above U+10FFFF) is shaped as U+FFFD.</param>
    /// <param name="clusters">One value per code point, copied to the glyphs the code point produces; typically the UTF-16 offset of the code point's grapheme cluster in the caller's text. Must have as many entries as <paramref name="codePoints"/>.</param>
    /// <param name="output">Receives the glyphs, appended after any it already holds.</param>
    /// <param name="rightToLeft">Whether the run is right to left (all of it at one odd level): mirrored characters get their mirror image and offsets are given for display order.</param>
    /// <param name="options">Language, kerning and features; the defaults when <see langword="null"/>.</param>
    /// <param name="script">
    /// The script of the text before the run, as an OpenType script tag (0 at the start of a paragraph). Characters with no script of their
    /// own (spaces, digits, punctuation) take it, so digits after Arabic are shaped as Arabic. On return it holds the script the run ended
    /// in; pass the same variable to the next run of the paragraph.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="face"/> or <paramref name="output"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The spans differ in length or are longer than <see cref="MaxRunLength"/>.</exception>
    /// <exception cref="FontFormatException">The font data is malformed. <paramref name="output"/> is then as it was before the call.</exception>
    public static void ShapeCodePoints(FontFace face, ReadOnlySpan<int> codePoints, ReadOnlySpan<int> clusters, List<CodePointGlyph> output, bool rightToLeft, ShapingOptions? options, ref uint script)
    {
        ArgumentNullException.ThrowIfNull(output);
        ShapeCodePointsCore(face, codePoints, clusters, output, null, rightToLeft, options, ref script);
    }

    /// <summary>
    /// Shapes a run given as code points into a reusable <see cref="CodePointRun"/>: the same glyphs, clusters, code points, advances,
    /// offsets and carried script as the <see cref="List{T}"/> overload, with no allocation per call once the run has grown to the size
    /// of the longest run shaped into it.
    /// </summary>
    /// <param name="face">The face.</param>
    /// <param name="codePoints">The run in logical order. A value that is not a Unicode scalar value (negative, a surrogate or above U+10FFFF) is shaped as U+FFFD.</param>
    /// <param name="clusters">One value per code point, copied to the glyphs the code point produces. Must have as many entries as <paramref name="codePoints"/>.</param>
    /// <param name="result">Receives the glyphs. Whatever it held is replaced, also when the run is empty; if the call throws it is left empty.</param>
    /// <param name="rightToLeft">Whether the run is right to left (all of it at one odd level).</param>
    /// <param name="options">Language, kerning and features; the defaults when <see langword="null"/>.</param>
    /// <param name="script">The script of the text before the run (0 at the start of a paragraph); on return the script the run ended in. See the list overload.</param>
    /// <exception cref="ArgumentNullException"><paramref name="face"/> or <paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The spans differ in length or are longer than <see cref="MaxRunLength"/>.</exception>
    /// <exception cref="FontFormatException">The font data is malformed.</exception>
    public static void ShapeCodePoints(FontFace face, ReadOnlySpan<int> codePoints, ReadOnlySpan<int> clusters, CodePointRun result, bool rightToLeft, ShapingOptions? options, ref uint script)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Clear();
        ShapeCodePointsCore(face, codePoints, clusters, null, result, rightToLeft, options, ref script);
    }

    private static void ShapeCodePointsCore(FontFace face, ReadOnlySpan<int> codePoints, ReadOnlySpan<int> clusters, List<CodePointGlyph>? list, CodePointRun? result, bool rightToLeft, ShapingOptions? options, ref uint script)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (codePoints.Length != clusters.Length)
        {
            throw new ArgumentException("There must be one cluster value per code point.", nameof(clusters));
        }

        if (codePoints.Length > MaxRunLength)
        {
            throw new ArgumentException("The run is too long.", nameof(codePoints));
        }

        if (codePoints.IsEmpty)
        {
            return;
        }

        int[]? clean = null;
        try
        {
            ReadOnlySpan<int> cps = codePoints;
            for (int i = 0; i < codePoints.Length; i++)
            {
                if (!IsScalar(codePoints[i]))
                {
                    clean = ArrayPool<int>.Shared.Rent(codePoints.Length);
                    codePoints.CopyTo(clean);
                    for (int k = i; k < codePoints.Length; k++)
                    {
                        if (!IsScalar(clean[k]))
                        {
                            clean[k] = 0xFFFD;
                        }
                    }

                    cps = clean.AsSpan(0, codePoints.Length);
                    break;
                }
            }

            ShapeScratch scratch = ShapeScratch.Current;
            List<ShapedRunGlyph> run = scratch.Run;
            run.Clear();
            uint state = script;
            try
            {
                TextShaper.Shape(face.Data, cps, clusters, Languages.ToTag(options?.Language), FeaturesOf(options), run, rightToLeft, ref state);
                if (list is not null)
                {
                    list.EnsureCapacity(list.Count + run.Count);
                    foreach (ShapedRunGlyph g in run)
                    {
                        list.Add(new CodePointGlyph(g.GlyphId, g.Cluster, g.CodePoint, g.XAdvance, g.XOffset, g.YOffset));
                    }
                }
                else
                {
                    CodePointGlyph[] dest = result!.Reserve(run.Count);
                    for (int i = 0; i < run.Count; i++)
                    {
                        ShapedRunGlyph g = run[i];
                        dest[i] = new CodePointGlyph(g.GlyphId, g.Cluster, g.CodePoint, g.XAdvance, g.XOffset, g.YOffset);
                    }

                    result.Count = run.Count;
                }

                script = state;
            }
            finally
            {
                run.Clear();
                scratch.Trim();
            }
        }
        finally
        {
            if (clean is not null)
            {
                ArrayPool<int>.Shared.Return(clean);
            }
        }
    }

    private static bool IsScalar(int c) => (uint)c <= 0x10FFFF && (c < 0xD800 || c > 0xDFFF);

    /// <summary>The level <see cref="ResolveBidiLevels"/> gives a character that rule X9 removes (the embedding and override controls LRE, RLE, LRO, RLO and PDF, and boundary neutrals).</summary>
    public const byte BidiRemoved = Bidi.Removed;

    /// <summary>
    /// Resolves the embedding level of every code point of one paragraph (Unicode Bidirectional Algorithm, rules P2 to I2) and keeps the
    /// rule X9 marker: a character the algorithm removes gets <see cref="BidiRemoved"/> instead of the level of the character before it,
    /// so the caller can leave it out of runs and of the display order. Rule L1 is not applied; see <see cref="ResetTrailingWhitespace"/>.
    /// </summary>
    /// <param name="codePoints">The paragraph; a value outside the Unicode range is treated as a left-to-right letter.</param>
    /// <param name="direction">The paragraph direction; <see cref="TextDirection.Auto"/> takes it from the first strong character.</param>
    /// <param name="levels">Receives one level per code point; it holds at least <c>codePoints.Length</c> entries.</param>
    /// <returns>The paragraph's base level, 0 or 1.</returns>
    /// <exception cref="ArgumentException">The paragraph is longer than <see cref="MaxRunLength"/>, or <paramref name="levels"/> is shorter than it.</exception>
    public static int ResolveBidiLevels(ReadOnlySpan<int> codePoints, TextDirection direction, Span<byte> levels)
    {
        if (codePoints.Length > MaxRunLength)
        {
            throw new ArgumentException("The paragraph is too long.", nameof(codePoints));
        }

        if (levels.Length < codePoints.Length)
        {
            throw new ArgumentException("The levels buffer is shorter than the paragraph.", nameof(levels));
        }

        if (codePoints.IsEmpty)
        {
            return direction == TextDirection.RightToLeft ? 1 : 0;
        }

        int request = direction switch { TextDirection.LeftToRight => 0, TextDirection.RightToLeft => 1, _ => -1 };
        return Bidi.ResolveLevels(codePoints, request, levels[..codePoints.Length], lineEndRule: false);
    }

    /// <summary>
    /// Applies rule L1 of the Unicode Bidirectional Algorithm to one line: white space and isolate controls at the end of the line, and
    /// before each segment or paragraph separator on it, return to the paragraph level. Entries that are <see cref="BidiRemoved"/> stay so.
    /// </summary>
    /// <param name="codePoints">The code points of the line.</param>
    /// <param name="levels">The line's levels from <see cref="ResolveBidiLevels"/>, changed in place; as many entries as code points.</param>
    /// <param name="paragraphLevel">The paragraph's base level (0 or 1).</param>
    /// <exception cref="ArgumentException">The two spans differ in length.</exception>
    public static void ResetTrailingWhitespace(ReadOnlySpan<int> codePoints, Span<byte> levels, int paragraphLevel)
    {
        if (codePoints.Length != levels.Length)
        {
            throw new ArgumentException("There must be one level per code point.", nameof(levels));
        }

        Bidi.ResetTrailingWhitespace(codePoints, levels, paragraphLevel & 1);
    }
}

/// <summary>Unicode character properties some callers need next to shaping.</summary>
public static class UnicodeProperties
{
    /// <summary>Gets the Joining_Type of a code point (ArabicShaping.txt).</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The joining type; <see cref="JoiningKind.None"/> for anything outside the Arabic-style scripts and for invalid values.</returns>
    public static JoiningKind GetJoiningKind(int codePoint) => (uint)codePoint > 0x10FFFF ? JoiningKind.None : (JoiningKind)UnicodeBidi.GetJoiningType(codePoint);

    /// <summary>
    /// Gets whether a code point belongs to a script shaped in syllables with reordering (the Indic scripts, Khmer, Myanmar, Tibetan,
    /// Balinese and the other scripts of the Universal Shaping Engine), as opposed to Thai and Lao, which only reorder prefix vowels, and
    /// all other scripts.
    /// </summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> for a character of such a script (letters, vowel signs, viramas and the like).</returns>
    public static bool IsSyllableScript(int codePoint)
    {
        if (codePoint < 0x900 || codePoint > 0x10FFFF)
        {
            return false;
        }

        ShapedScript s = IndicProps.ScriptOf(codePoint);
        return s != ShapedScript.None && s != ShapedScript.Thai && s != ShapedScript.Lao;
    }
}

/// <summary>The Joining_Type of a character (Unicode ArabicShaping.txt): how it connects to its neighbours in cursive scripts.</summary>
public enum JoiningKind
{
    /// <summary>Non_Joining (U): never connects.</summary>
    None = 0,

    /// <summary>Left_Joining (L): connects to the next character only.</summary>
    Left = 1,

    /// <summary>Right_Joining (R): connects to the previous character only.</summary>
    Right = 2,

    /// <summary>Dual_Joining (D): connects on both sides.</summary>
    Dual = 3,

    /// <summary>Join_Causing (C): joins its neighbours (tatweel, ZWJ).</summary>
    Causing = 4,

    /// <summary>Transparent (T): marks that do not interrupt joining.</summary>
    Transparent = 5,
}
