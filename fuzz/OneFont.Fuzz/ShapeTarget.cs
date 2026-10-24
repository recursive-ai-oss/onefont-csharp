using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OneFont.Fuzz;

/// <summary>
/// Hostile fonts and text through the whole public surface: loading, shaping (with features, a language and a direction), outlines,
/// bidi levels and order, line breaks, graphemes and fallback.
///
/// Input layout: byte 0 flags (bit 0 right to left, bit 1 kerning off, bits 2-3 which features, bit 4 a language, bit 5 forced
/// direction, bit 7 the font follows the text; otherwise one of the committed test fonts is used), byte 1 picks the font, byte 2 is the
/// text length in UTF-16 code units, then the text as little-endian code units, then (bit 7) the font bytes.
/// Only <see cref="FontFormatException"/> may escape a library call; a broken invariant throws <see cref="InvalidOperationException"/>.
/// </summary>
internal static class ShapeTarget
{
    private static readonly string[] FeatureTags = ["smcp", "liga", "calt", "salt", "aalt", "ccmp", "locl", "kern", "mark", "mkmk", "curs", "init", "medi", "fina", "rphf", "half", "pres", "vatu"];

    private static readonly string?[] Languages = [null, "tr", "sr", "ro", "ENG", "ARA", "HIN", "zh-Hant"];

    private static readonly Lazy<(string Name, byte[] Data)[]> Fonts = new(LoadFonts);

    private static (string, byte[])[] LoadFonts()
    {
        string dir = Environment.GetEnvironmentVariable("ONEFONT_FUZZ_FONTS") ?? Path.Combine(AppContext.BaseDirectory, "fonts");
        return [.. Directory.EnumerateFiles(dir, "*.ttf").Concat(Directory.EnumerateFiles(dir, "*.otf")).Concat(Directory.EnumerateFiles(dir, "*.ttc")).OrderBy(f => f, StringComparer.Ordinal).Select(f => (Path.GetFileName(f), File.ReadAllBytes(f)))];
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
        {
            return;
        }

        byte flags = data[0];
        int textUnits = Math.Min(data[2], (data.Length - 3) / 2);
        char[] text = new char[textUnits];
        for (int i = 0; i < textUnits; i++)
        {
            text[i] = (char)(data[3 + (2 * i)] | (data[4 + (2 * i)] << 8));
        }

        (string Name, byte[] Data)[] fonts = Fonts.Value;
        byte[] primary;
        if ((flags & 0x80) != 0)
        {
            primary = data[(3 + (2 * textUnits))..].ToArray();
        }
        else
        {
            primary = fonts[data[1] % fonts.Length].Data;
        }

        FontFace face;
        try
        {
            face = FontFace.Load(primary, (flags & 0x80) != 0 ? data[1] % 3 : 0);
        }
        catch (FontFormatException)
        {
            return;
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        try
        {
            Exercise(face, fonts, flags, data[1], new string(text));
        }
        catch (FontFormatException)
        {
            // A damaged glyph is reported as a font error when its outline is read.
        }
    }

    private static void Exercise(FontFace face, (string Name, byte[] Data)[] fonts, byte flags, byte pick, string text)
    {
        var options = new ShapingOptions { Language = (flags & 0x10) == 0 ? null : Languages[1 + (pick % (Languages.Length - 1))] };
        if ((flags & 2) != 0)
        {
            options.Kerning = KerningMode.None;
        }

        for (int k = 0; k < 2; k++)
        {
            options.Features.Add(new FontFeature(FeatureTags[(pick + (((flags >> 2) & 3) * 5) + k) % FeatureTags.Length], k));
        }

        bool rtl = (flags & 1) != 0;
        IReadOnlyList<ShapedCluster> clusters = TextShaping.ShapeRun(text, face, rtl, options);
        CheckClusters(clusters, text.Length);

        // The reusable result must hold exactly what the list holds. It is shaped twice into one object, so a stale entry would show.
        var run = new ShapedRun();
        TextShaping.ShapeRun("stale", face, run);
        TextShaping.ShapeRun(text, face, run, rtl, options);
        CheckSame(clusters, run);
        foreach (ShapedCluster c in clusters.Take(64))
        {
            foreach (ShapedGlyph g in c.Glyphs)
            {
                try
                {
                    face.GetOutline(g.GlyphId).Write(new NullSink());
                }
                catch (FontFormatException)
                {
                    // Reported for the one glyph; the others are still read.
                }
            }
        }

        TextDirection dir = (flags & 0x20) != 0 ? (rtl ? TextDirection.RightToLeft : TextDirection.LeftToRight) : TextDirection.Auto;
        byte[] levels = TextShaping.GetBidiLevels(text, dir, out int para);
        if (levels.Length != text.Length || para is not (0 or 1))
        {
            throw new InvalidOperationException("bidi levels do not cover the text");
        }

        int[] order = TextShaping.GetVisualOrder(levels);
        if (order.Length > levels.Length || order.Distinct().Count() != order.Length)
        {
            throw new InvalidOperationException("visual order repeats an index");
        }

        LineBreakOpportunity[] breaks = TextSegmentation.GetLineBreakOpportunities(text);
        if (breaks.Length != text.Length + 1)
        {
            throw new InvalidOperationException("line break array has the wrong length");
        }

        int[] graphemes = TextSegmentation.GetGraphemeBoundaries(text);
        if (graphemes[0] != 0 || graphemes[^1] != text.Length)
        {
            throw new InvalidOperationException("grapheme boundaries do not cover the text");
        }

        ExerciseCodePoints(face, text, run, rtl, dir, options, pick, flags);

        FontFace other;
        try
        {
            other = FontFace.Load(fonts[(pick + 1) % fonts.Length].Data);
        }
        catch (FontFormatException)
        {
            return;
        }

        var chain = new FontFallbackChain([face, other]);
        IReadOnlyList<ShapedCluster> chained = chain.ShapeRun(text, rtl, options);
        CheckClusters(chained, text.Length);
        chain.ShapeRun(text, run, rtl, options);
        CheckSame(chained, run);

        // The span forms of bidi and line breaking match the array forms.
        byte[] into = new byte[text.Length];
        TextShaping.GetBidiLevels(text, dir, into, out int para2);
        if (para2 != para || !into.AsSpan().SequenceEqual(levels))
        {
            throw new InvalidOperationException("bidi levels into a buffer differ");
        }

        var breaksInto = new LineBreakOpportunity[text.Length + 1];
        TextSegmentation.GetLineBreakOpportunities(text, breaksInto);
        if (!breaksInto.AsSpan().SequenceEqual(breaks))
        {
            throw new InvalidOperationException("line breaks into a buffer differ");
        }
    }

    private static readonly CodePointRun SharedRun = new();

    // The code point entry points: shaping with a carried script, glyph origins, bidi with the removed marker, byte fills, properties and tables.
    private static void ExerciseCodePoints(FontFace face, string text, ShapedRun run, bool rtl, TextDirection dir, ShapingOptions options, byte pick, byte flags)
    {
        // Code points from the text; a lone surrogate or, with bit 6, a wild value from the code units stays as it is.
        var cpl = new List<int>();
        var offsets = new List<int>();
        for (int i = 0; i < text.Length;)
        {
            offsets.Add(i);
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cpl.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                i += 2;
            }
            else
            {
                cpl.Add((flags & 0x40) != 0 ? (text[i] << 16) | text[i] ^ (pick << 8) : text[i]);
                i++;
            }
        }

        int[] cps = [.. cpl];
        int[] clusters = [.. offsets];
        int cut = cps.Length / 2;
        uint script = 0;
        var glyphs = new List<CodePointGlyph>();
        TextShaping.ShapeCodePoints(face, cps.AsSpan(0, cut), clusters.AsSpan(0, cut), glyphs, rtl, options, ref script);
        int first = glyphs.Count;
        TextShaping.ShapeCodePoints(face, cps.AsSpan(cut), clusters.AsSpan(cut), glyphs, !rtl, options, ref script);

        // The reusable-buffer overload must give what the list overload gave, whatever the buffer held before.
        uint bufferScript = 0;
        CodePointRun buffer = SharedRun;
        TextShaping.ShapeCodePoints(face, cps, clusters, buffer, !rtl, null, ref bufferScript);
        bufferScript = 0;
        TextShaping.ShapeCodePoints(face, cps.AsSpan(0, cut), clusters.AsSpan(0, cut), buffer, rtl, options, ref bufferScript);
        var viaList = new List<CodePointGlyph>();
        uint listScript = 0;
        TextShaping.ShapeCodePoints(face, cps.AsSpan(0, cut), clusters.AsSpan(0, cut), viaList, rtl, options, ref listScript);
        if (listScript != bufferScript || viaList.Count != buffer.Count)
        {
            throw new InvalidOperationException("code point buffer overload differs in count or script");
        }

        for (int i = 0; i < viaList.Count; i++)
        {
            if (viaList[i] != buffer[i])
            {
                throw new InvalidOperationException("code point buffer overload differs in a glyph");
            }
        }
        foreach (CodePointGlyph g in glyphs)
        {
            if (g.GlyphId is < 0 or > 0xFFFF || g.Cluster < 0 || g.Cluster > text.Length)
            {
                throw new InvalidOperationException("code point glyph out of range");
            }
        }

        if (first > glyphs.Count)
        {
            throw new InvalidOperationException("glyphs were removed");
        }

        // Origins: one per glyph and a permutation of the shaper's order.
        if (run.Origins.Length != run.GlyphCount)
        {
            throw new InvalidOperationException("origins and glyphs differ in count");
        }

        var seen = new bool[run.GlyphCount];
        foreach (ShapedGlyphOrigin o in run.Origins)
        {
            if ((uint)o.ShaperIndex >= (uint)seen.Length || seen[o.ShaperIndex])
            {
                throw new InvalidOperationException("shaper order is not a permutation");
            }

            seen[o.ShaperIndex] = true;
        }

        // Bidi on code points.
        var levels = new byte[cps.Length];
        int para = TextShaping.ResolveBidiLevels(cps, dir, levels);
        if (para is not (0 or 1))
        {
            throw new InvalidOperationException("paragraph level");
        }

        byte[] reset = [.. levels];
        TextShaping.ResetTrailingWhitespace(cps, reset, para);
        for (int i = 0; i < levels.Length; i++)
        {
            if ((levels[i] == TextShaping.BidiRemoved) != (reset[i] == TextShaping.BidiRemoved))
            {
                throw new InvalidOperationException("L1 changed a removed marker");
            }
        }

        var order = new int[levels.Length];
        int shown = TextShaping.GetVisualOrder(reset, order);
        if (shown > levels.Length || order.Take(shown).Distinct().Count() != shown || order.Take(shown).Any(i => (uint)i >= (uint)levels.Length || reset[i] == TextShaping.BidiRemoved))
        {
            throw new InvalidOperationException("visual order with removed characters");
        }

        // Byte fills agree with the typed forms.
        var typed = new LineBreakOpportunity[text.Length + 1];
        TextSegmentation.GetLineBreakOpportunities(text, typed);
        var bytes = new byte[text.Length + 1];
        TextSegmentation.FillLineBreakOpportunities(text, bytes);
        var starts = new byte[text.Length + 1];
        TextSegmentation.FillGraphemeStarts(text, starts);
        int[] boundaries = TextSegmentation.GetGraphemeBoundaries(text);
        int b = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool start = b < boundaries.Length && boundaries[b] == i;
            if (start)
            {
                b++;
            }

            if ((byte)typed[i] != bytes[i] || start != (starts[i] != 0))
            {
                throw new InvalidOperationException("byte fills differ from the typed forms");
            }
        }

        foreach (int cp in cps)
        {
            _ = UnicodeProperties.GetJoiningKind(cp);
            _ = UnicodeProperties.IsSyllableScript(cp);
            _ = TextSegmentation.IsEastAsianWide(cp);
        }

        // Raw tables: any tag the caller can build is answered or rejected with an argument exception.
        string[] tags = ["head", "GSUB", "COLR", "cvt", "cmap", "x"];
        string tag = tags[pick % tags.Length];
        if (face.HasTable(tag) != !face.GetTable(tag).IsEmpty && face.GetTable(tag).Length != 0)
        {
            throw new InvalidOperationException("table presence differs");
        }
    }

    private static void CheckSame(IReadOnlyList<ShapedCluster> clusters, ShapedRun run)
    {
        if (clusters.Count != run.ClusterCount)
        {
            throw new InvalidOperationException("cluster counts differ");
        }

        int glyphs = 0;
        for (int i = 0; i < clusters.Count; i++)
        {
            ShapedCluster c = clusters[i];
            ShapedClusterRange r = run.Clusters[i];
            if (c.Start != r.Start || c.Length != r.Length || c.FaceIndex != r.FaceIndex || c.Advance != r.Advance || c.HasMissingGlyph != r.HasMissingGlyph || r.GlyphStart != glyphs
                || !c.Glyphs.SequenceEqual(run.GlyphsOf(r)))
            {
                throw new InvalidOperationException($"cluster {i} differs between the list and the reusable result");
            }

            glyphs += r.GlyphCount;
        }

        if (glyphs != run.GlyphCount)
        {
            throw new InvalidOperationException("glyph counts differ");
        }
    }

    // The clusters of a run follow each other without gaps and cover the text; every glyph id fits 16 bits (a damaged font can substitute to an id it has no glyph for).
    private static void CheckClusters(IReadOnlyList<ShapedCluster> clusters, int length)
    {
        int at = 0;
        foreach (ShapedCluster c in clusters)
        {
            if (c.Start != at || c.Length < 1 || (c.Glyphs.Length == 0 && c.Advance != 0))
            {
                throw new InvalidOperationException($"cluster at {c.Start} (length {c.Length}) does not follow offset {at}");
            }

            at += c.Length;
            foreach (ShapedGlyph g in c.Glyphs)
            {
                if (g.GlyphId is < 0 or > 0xFFFF)
                {
                    throw new InvalidOperationException($"glyph id {g.GlyphId} is not a 16-bit glyph id");
                }
            }
        }

        if (at != length)
        {
            throw new InvalidOperationException($"clusters cover {at} of {length} units");
        }
    }

    private sealed class NullSink : IOutlineSink
    {
        public void MoveTo(float x, float y) => Check(x, y);

        public void LineTo(float x, float y) => Check(x, y);

        public void QuadTo(float cx, float cy, float x, float y) => Check(cx + x, cy + y);

        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y) => Check(c1x + c2x + x, c1y + c2y + y);

        public void Close()
        {
        }

        private static void Check(float x, float y)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y))
            {
                throw new InvalidOperationException("outline coordinate is not finite");
            }
        }
    }

    // ---- seeds ----

    private static readonly string[] SampleTexts =
    [
        "office AVATAR fi",
        "e\u0301 a\u0308 o\u0302\u0301 \u03B1\u0301",
        "\u0627\u0644\u0633\u0644\u0627\u0645 \u0639\u0644\u064A\u0643\u0645 \u0644\u0627 \u0628\u0651\u064E",
        "abc \u05E9\u05DC\u05D5\u05DD 123 \u202B\u0627\u0628\u202C (x)",
        "\u0915\u094D\u0937\u093F \u0930\u094D\u0915\u0940 \u0928\u092E\u0938\u094D\u0924\u0947 \u0915\u094D\u200D\u0937",
        "\u0995\u09CD\u0995 \u09B0\u09CD\u0995 \u0995\u09CB \u0A15\u0A4D\u0A15 \u0AB0\u0ACD\u0A95 \u0B15\u0B4D\u0B15",
        "\u0B95\u0BCA \u0C15\u0C4D\u0C15 \u0C95\u0CCD\u0C95 \u0D15\u0D4D\u0D15 \u0D9A\u0DCA\u200D\u0DBB\u0DD9",
        "\u1780\u17D2\u179A\u17C1 \u1000\u103C\u1031 \u1004\u103A\u1039\u1000 \u0F40\u0F73",
        "\u0E19\u0E49\u0E33 \u0EAB\u0EB3 \u0E01\u0E47 \u1B05\u1B36 \uA98F\uA9BA",
        "\u65E5\u672C\u8A9E \uD83D\uDE00 \uD83C\uDDE9\uD83C\uDDEA \u1100\u1161\u11A8",
    ];

    public static int WriteSeeds(string outDir, string fontDir)
    {
        Directory.CreateDirectory(outDir);
        Environment.SetEnvironmentVariable("ONEFONT_FUZZ_FONTS", fontDir);
        (string Name, byte[] Data)[] fonts = Fonts.Value;
        int n = 0;
        for (int f = 0; f < fonts.Length; f++)
        {
            for (int t = 0; t < SampleTexts.Length; t++)
            {
                // Committed font, text only.
                File.WriteAllBytes(Path.Combine(outDir, $"seed-text-{f:D2}-{t:D2}"), Build((byte)(t % 2 == 0 ? 0 : 1), (byte)f, SampleTexts[t], null));
                n++;
            }
        }

        // Font follows the text: fonts small enough to be mutated usefully.
        for (int f = 0; f < fonts.Length; f++)
        {
            if (fonts[f].Data.Length < 120_000)
            {
                File.WriteAllBytes(Path.Combine(outDir, $"seed-font-{f:D2}"), Build(0x80, 0, SampleTexts[f % SampleTexts.Length], fonts[f].Data));
                n++;
            }
        }

        Console.WriteLine($"{n} seeds");
        return 0;
    }

    private static byte[] Build(byte flags, byte pick, string text, byte[]? font)
    {
        text = text.Length > 255 ? text[..255] : text;
        var bytes = new List<byte> { flags, pick, (byte)text.Length };
        foreach (char c in text)
        {
            bytes.Add((byte)(c & 0xFF));
            bytes.Add((byte)(c >> 8));
        }

        if (font is not null)
        {
            bytes.AddRange(font);
        }

        return [.. bytes];
    }
}
