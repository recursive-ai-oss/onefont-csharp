using System.Buffers;
using OneFont.Fonts;
using OneFont.Shaping;
using OneFont.Unicode;

namespace OneFont.Tests;

/// <summary>One glyph placed by <see cref="MiniLayout"/>. Advances and offsets are in pixels at the layout's size.</summary>
internal sealed record Placed(int GlyphId, int CodePoint, int Cluster, int Length, float Advance, float XOffset, float YOffset, bool IsSpace, byte Level)
{
    /// <summary>The pen position in display order.</summary>
    public float PenX { get; set; }
}

/// <summary>
/// A single-line layout for the tests: one paragraph, one face, no wrapping. It does what a paragraph engine does around the shaper: bidi
/// levels, runs of one direction shaped with the script carried along, joiners kept only where they matter, and the display order of
/// the glyphs (rule L2, trailing white space reset by L1).
/// </summary>
internal static class MiniLayout
{
    public static List<Placed> Run(string text, FontData face, float size, TextDirection direction = TextDirection.Auto, ShapingOptions? options = null, bool reorder = true)
    {
        uint language = Languages.ToTag(options?.Language);
        FeatureSet features = TextShaping.FeaturesOf(options);
        float sx = size / face.UnitsPerEm;
        int length = text.Length;
        var glyphs = new List<Placed>();
        if (length == 0)
        {
            return glyphs;
        }

        // Bidi levels per UTF-16 index (raw: with the characters rule X9 removes marked).
        int[] cps0 = new int[length];
        int[] at = new int[length];
        byte[] lev0 = new byte[length];
        int count = 0;
        for (int i = 0; i < length; count++)
        {
            at[count] = i;
            if (char.IsHighSurrogate(text[i]) && i + 1 < length && char.IsLowSurrogate(text[i + 1]))
            {
                cps0[count] = char.ConvertToUtf32(text[i], text[i + 1]);
                i += 2;
            }
            else
            {
                cps0[count] = char.IsSurrogate(text[i]) ? 0xFFFD : text[i];
                i++;
            }
        }

        int request = direction switch { TextDirection.LeftToRight => 0, TextDirection.RightToLeft => 1, _ => -1 };
        int paraLevel = Bidi.ResolveLevels(cps0.AsSpan(0, count), request, lev0.AsSpan(0, count), lineEndRule: false);
        byte[] levels = new byte[length];
        byte[] raw = new byte[length];
        byte prev = (byte)paraLevel;
        for (int k = 0; k < count; k++)
        {
            byte l = lev0[k] == Bidi.Removed ? prev : lev0[k];
            prev = l;
            int to = k + 1 < count ? at[k + 1] : length;
            for (int j = at[k]; j < to; j++)
            {
                levels[j] = l;
                raw[j] = lev0[k];
            }
        }

        // Shaping: a run is a stretch of one direction; zero width non-joiners, zero width spaces and soft hyphens end it.
        var cps = new List<int>();
        var clusters = new List<int>();
        bool runRtl = false;
        uint script = 0;
        var output = new List<ShapedRunGlyph>();

        void Flush()
        {
            if (cps.Count == 0)
            {
                return;
            }

            output.Clear();
            TextShaper.Shape(face, [.. cps], [.. clusters], language, features, output, runRtl, ref script);
            for (int k = 0; k < output.Count; k++)
            {
                ShapedRunGlyph r = output[k];
                int next = k + 1 < output.Count ? output[k + 1].Cluster : length;
                glyphs.Add(new Placed(r.GlyphId, r.CodePoint, r.Cluster, Math.Max(next - r.Cluster, 1), r.XAdvance * sx, r.XOffset * sx, r.YOffset * sx, IsBreakingSpace(r.CodePoint), levels[r.Cluster]));
            }

            cps.Clear();
            clusters.Clear();
        }

        for (int i = 0; i < length;)
        {
            int cp = char.IsHighSurrogate(text[i]) && i + 1 < length && char.IsLowSurrogate(text[i + 1]) ? char.ConvertToUtf32(text[i], text[i + 1]) : (char.IsSurrogate(text[i]) ? 0xFFFD : text[i]);
            int len = cp > 0xFFFF ? 2 : 1;
            bool indicJoiner = cp is 0x200C or 0x200D && SyllableJoinerMatters(text, i, 0, length);
            bool ignorable = (IsIgnorable(cp) && !indicJoiner && !(cp == 0x200D && JoinerMatters(text, i, 0, length))) || cp == '\t';
            bool rtl = (levels[i] & 1) == 1;
            if (rtl != runRtl)
            {
                Flush();
                runRtl = rtl;
            }

            if (cp == '\t')
            {
                Flush();
            }
            else if (cp is 0x200C or 0x200B or 0x00AD && !indicJoiner)
            {
                Flush();
            }
            else if (!ignorable)
            {
                cps.Add(cp);
                clusters.Add(i);
            }

            i += len;
        }

        Flush();
        if (reorder)
        {
            Reorder(glyphs, text, raw, paraLevel);
        }

        float x = 0;
        foreach (Placed g in glyphs)
        {
            g.PenX = x;
            x += g.Advance;
        }

        return glyphs;
    }

    /// <summary>The baseline below the top of a line box one em tall: the ascent, with the font's line height centered in the em.</summary>
    public static float Baseline(FontData face, float size)
    {
        var m = new FontMetrics(face);
        float scale = size / face.UnitsPerEm;
        return (m.Ascender * scale) - (((m.LineHeight * scale) - size) / 2);
    }

    // Display order: by the visual rank of the character a glyph starts at, and in a right-to-left run the glyphs of one character reversed.
    private static void Reorder(List<Placed> glyphs, string text, byte[] raw, int paraLevel)
    {
        int length = text.Length;
        int n = glyphs.Count;
        int[] cps = new int[length];
        byte[] lev = new byte[length];
        int[] rankAt = new int[length];
        int m = 0;
        for (int i = 0; i < length; m++)
        {
            int cp = char.IsHighSurrogate(text[i]) && i + 1 < length && char.IsLowSurrogate(text[i + 1]) ? char.ConvertToUtf32(text[i], text[i + 1]) : (char.IsSurrogate(text[i]) ? 0xFFFD : text[i]);
            int len = cp > 0xFFFF ? 2 : 1;
            cps[m] = cp;
            lev[m] = raw[i];
            rankAt[i] = m;
            if (len == 2)
            {
                rankAt[i + 1] = m;
            }

            i += len;
        }

        int[] order = new int[m];
        Bidi.ResetTrailingWhitespace(cps.AsSpan(0, m), lev.AsSpan(0, m), paraLevel);
        int shown = Bidi.VisualOrder(lev.AsSpan(0, m), order);
        int[] rankByIndex = new int[m];
        for (int r = 0; r < shown; r++)
        {
            rankByIndex[order[r]] = r;
        }

        int last = 0;
        for (int c = 0; c < m; c++)
        {
            if (lev[c] == Bidi.Removed)
            {
                rankByIndex[c] = last;
            }
            else
            {
                last = rankByIndex[c];
            }
        }

        var keys = new long[n];
        var keyed = new int[n];
        for (int j = 0; j < n; j++)
        {
            Placed g = glyphs[j];
            int rank = rankByIndex[rankAt[Math.Clamp(g.Cluster, 0, length - 1)]];
            int within = (g.Level & 1) == 1 ? n - j : j;
            keys[j] = ((long)rank << 32) | (uint)within;
            keyed[j] = j;
        }

        Array.Sort(keys, keyed, 0, n);
        Placed[] copy = [.. glyphs];
        for (int v = 0; v < n; v++)
        {
            glyphs[v] = copy[keyed[v]];
        }
    }

    private static bool IsIgnorable(int cp) =>
        cp is >= 0x200B and <= 0x200F or 0x2060 or 0xFEFF or 0x00AD or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);

    private static bool IsBreakingSpace(int cp) =>
        cp is 0x20 or 0x09 or 0x1680 or (>= 0x2000 and <= 0x2006) or (>= 0x2008 and <= 0x200A) or 0x205F or 0x3000;

    // A zero width joiner is kept for the shaper only where it sits next to a letter that can join through it, and in the Indic and
    // Universal Shaping Engine scripts where it steers conjunct and reph formation.
    private static bool SyllableJoinerMatters(string text, int i, int start, int end)
    {
        if (i > start && IsSyllableScript(text[i - 1]))
        {
            return true;
        }

        return i + 1 < end && IsSyllableScript(text[i + 1]);
    }

    private static bool IsSyllableScript(char c)
    {
        if (c < 0x900)
        {
            return false;
        }

        ShapedScript s = IndicProps.ScriptOf(c);
        return s != ShapedScript.None && s != ShapedScript.Thai && s != ShapedScript.Lao;
    }

    private static bool JoinerMatters(string text, int i, int start, int end)
    {
        int before = 0;
        if (i > start)
        {
            before = text[i - 1];
            if (char.IsLowSurrogate(text[i - 1]) && i - 2 >= start && char.IsHighSurrogate(text[i - 2]))
            {
                before = char.ConvertToUtf32(text[i - 2], text[i - 1]);
            }
        }

        int after = 0;
        if (i + 1 < end)
        {
            after = text[i + 1];
            if (char.IsHighSurrogate(text[i + 1]) && i + 2 < end && char.IsLowSurrogate(text[i + 2]))
            {
                after = char.ConvertToUtf32(text[i + 1], text[i + 2]);
            }
        }

        return (before >= 0x600 && UnicodeBidi.GetJoiningType(before) is JoiningType.Dual or JoiningType.Left)
            || (after >= 0x600 && UnicodeBidi.GetJoiningType(after) is JoiningType.Dual or JoiningType.Right);
    }
}
