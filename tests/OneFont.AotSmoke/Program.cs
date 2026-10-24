using System;
using System.Collections.Generic;
using System.IO;

namespace OneFont.AotSmoke;

/// <summary>Shapes an Arabic and a Devanagari run, orders bidi text, finds break opportunities and falls back across faces, under NativeAOT.</summary>
internal static class Program
{
    private static int failures;

    private static int Main()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "fonts");
        FontFace arabic = FontFace.Load(File.ReadAllBytes(Path.Combine(dir, "NotoSansArabic-Subset-Regular.ttf")));
        FontFace deva = FontFace.Load(File.ReadAllBytes(Path.Combine(dir, "NotoSansDevanagari-Subset-Regular.ttf")));
        FontFace latin = FontFace.Load(File.ReadAllBytes(Path.Combine(dir, "NotoSans-Subset-Regular.ttf")));

        // Arabic: three beh letters join into initial, medial and final forms, none of them the isolated glyph.
        IReadOnlyList<ShapedCluster> beh = TextShaping.ShapeRun("\u0628\u0628\u0628", arabic, rightToLeft: true);
        Check("arabic cluster count", beh.Count == 3);
        Check("arabic forms", beh.Count == 3 && beh[0].Glyphs[0].GlyphId != beh[1].Glyphs[0].GlyphId && beh[1].Glyphs[0].GlyphId != beh[2].Glyphs[0].GlyphId && beh[0].Glyphs[0].GlyphId != arabic.GetGlyphIndex(0x628));

        // Devanagari: ka + virama + ssa forms one conjunct cluster; the i matra moves in front of its consonant.
        IReadOnlyList<ShapedCluster> kssa = TextShaping.ShapeRun("\u0915\u094D\u0937", deva);
        Check("devanagari conjunct", kssa.Count == 1 && kssa[0].Length == 3);
        IReadOnlyList<ShapedCluster> ki = TextShaping.ShapeRun("\u0915\u093F", deva);
        Check("devanagari matra order", ki.Count == 1 && ki[0].Glyphs.Length == 2 && ki[0].Glyphs[1].GlyphId == deva.GetGlyphIndex(0x915));

        // Outlines come out through a sink.
        var sink = new CountingSink();
        arabic.GetOutline(beh[0].Glyphs[0].GlyphId).Write(sink);
        Check("outline segments", sink.Segments > 3);

        // Bidi, line breaking, graphemes.
        byte[] levels = TextShaping.GetBidiLevels("ab \u05D0\u05D1 cd", TextDirection.Auto, out int para);
        Check("bidi levels", para == 0 && levels[3] == 1 && levels[0] == 0);
        Check("visual order", ((ReadOnlySpan<int>)TextShaping.GetVisualOrder([0, 1, 1, 0])).SequenceEqual([0, 2, 1, 3]));
        LineBreakOpportunity[] breaks = TextSegmentation.GetLineBreakOpportunities("ab cd");
        Check("line breaks", breaks[3] == LineBreakOpportunity.Allowed && breaks[1] == LineBreakOpportunity.None);
        Check("graphemes", ((ReadOnlySpan<int>)TextSegmentation.GetGraphemeBoundaries("e\u0301a")).SequenceEqual([0, 2, 3]));

        // Fallback: the Devanagari letter comes from the second face.
        var chain = new FontFallbackChain([latin, deva]);
        IReadOnlyList<ShapedCluster> mixed = chain.ShapeRun("a\u0915");
        Check("fallback", mixed.Count == 2 && mixed[0].FaceIndex == 0 && mixed[1].FaceIndex == 1);

        // The reusable result: the same clusters and glyphs in flat arrays, twice through one object.
        var run = new ShapedRun();
        for (int i = 0; i < 2; i++)
        {
            TextShaping.ShapeRun("\u0628\u0628\u0628", arabic, run, rightToLeft: true);
            Check("run arabic", run.ClusterCount == 3 && run.GlyphCount == 3 && run.Glyphs[0].GlyphId == beh[0].Glyphs[0].GlyphId && run.Clusters[2].Start == 2);
            chain.ShapeRun("a\u0915", run);
            Check("run fallback", run.ClusterCount == 2 && run.Clusters[1].FaceIndex == 1 && run.GlyphsOf(run.Clusters[1]).Length == mixed[1].Glyphs.Length);
        }

        var spanLevels = new byte[8];
        TextShaping.GetBidiLevels("ab \u05D0\u05D1 cd", TextDirection.Auto, spanLevels, out _);
        Check("bidi levels into a buffer", spanLevels[3] == 1 && spanLevels[0] == 0);

        // Code point entry points: a carried script, origins, bidi with the removed marker, byte fills, properties, raw tables.
        var glyphs = new List<CodePointGlyph>();
        uint script = 0;
        TextShaping.ShapeCodePoints(arabic, [0x628, 0x628], [0, 1], glyphs, true, null, ref script);
        Check("code point shaping", glyphs.Count == 2 && script != 0 && glyphs[1].Cluster == 1 && glyphs[0].CodePoint == 0x628);
        var cpRun = new CodePointRun();
        uint script2 = 0;
        TextShaping.ShapeCodePoints(arabic, [0x628, 0x628], [0, 1], cpRun, true, null, ref script2);
        Check("code point shaping into a reusable run", cpRun.Count == glyphs.Count && script2 == script && cpRun[1] == glyphs[1]);
        int[] cps = [0x61, 0x202B, 0x5D0, 0x202C];
        var cpLevels = new byte[4];
        int cpPara = TextShaping.ResolveBidiLevels(cps, TextDirection.Auto, cpLevels);
        Check("code point bidi", cpPara == 0 && cpLevels[1] == TextShaping.BidiRemoved && cpLevels[2] == 1);
        TextShaping.ResetTrailingWhitespace(cps, cpLevels, cpPara);
        var fill = new byte[6];
        TextSegmentation.FillLineBreakOpportunities("ab cd", fill);
        Check("byte fills", fill[3] == 1 && fill[1] == 0 && TextSegmentation.IsEastAsianWide(0x65E5));
        Check("properties", UnicodeProperties.GetJoiningKind(0x628) == JoiningKind.Dual && UnicodeProperties.IsSyllableScript(0x915));
        TextShaping.ShapeRun("कि", deva, run);
        Check("origins", run.Origins.Length == run.GlyphCount && run.GlyphCount == 2);
        Check("tables", latin.HasTable("head") && latin.GetTable("head").Length == 54 && !latin.HasTable("COLR"));

        Console.WriteLine(failures == 0 ? "OneFont AOT smoke: ok" : $"OneFont AOT smoke: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    private static void Check(string what, bool ok)
    {
        if (!ok)
        {
            failures++;
            Console.Error.WriteLine("FAILED: " + what);
        }
    }

    private sealed class CountingSink : IOutlineSink
    {
        public int Segments { get; private set; }

        public void MoveTo(float x, float y) => Segments++;

        public void LineTo(float x, float y) => Segments++;

        public void QuadTo(float cx, float cy, float x, float y) => Segments++;

        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y) => Segments++;

        public void Close() => Segments++;
    }
}
