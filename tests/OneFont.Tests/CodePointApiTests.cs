namespace OneFont.Tests;

/// <summary>The code point level API: shaping with a carried script, glyph origins, bidi with the removed marker, segmentation fills, character properties and raw tables.</summary>
public class CodePointApiTests
{
    private const string BidiFont = "NotoSans-Bidi-Subset.ttf";
    private const string Deva = "NotoSansDevanagari-Subset-Regular.ttf";

    private static int[] Cps(string s)
    {
        var list = new List<int>();
        for (int i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
        {
            list.Add(char.ConvertToUtf32(s, i));
        }

        return [.. list];
    }

    private static int[] Offsets(string s)
    {
        var list = new List<int>();
        for (int i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
        {
            list.Add(i);
        }

        return [.. list];
    }

    private static List<CodePointGlyph> Shape(FontFace face, string text, bool rtl, ref uint script)
    {
        var output = new List<CodePointGlyph>();
        TextShaping.ShapeCodePoints(face, Cps(text), Offsets(text), output, rtl, null, ref script);
        return output;
    }

    [Fact]
    public void DigitsAfterArabicTakeTheArabicScriptWhenTheScriptIsCarried()
    {
        FontFace face = TestFonts.Public(BidiFont);
        uint script = 0;
        Shape(face, "\u0627\u0644\u0633\u0644\u0627\u0645", true, ref script);
        Assert.NotEqual(0u, script);
        uint carried = script;
        Assert.Equal([1853, 2232, 2215], Shape(face, "123", false, ref carried).Select(g => g.GlyphId));

        uint fresh = 0;
        Assert.Equal([18, 19, 20], Shape(face, "123", false, ref fresh).Select(g => g.GlyphId));
        Assert.Equal([18, 19, 20], TextShaping.ShapeRun("123", face).SelectMany(c => c.Glyphs.ToArray()).Select(g => g.GlyphId));
    }

    [Fact]
    public void GlyphsCarryTheCallersClusterAndTheCodePoint()
    {
        FontFace face = TestFonts.Noto;
        int[] cps = [0x41, 0x1F600, 0x42];
        int[] clusters = [10, 20, 30];
        var output = new List<CodePointGlyph> { new(7, 7, 7, 7, 7, 7) };
        uint script = 0;
        TextShaping.ShapeCodePoints(face, cps, clusters, output, false, null, ref script);
        Assert.Equal(4, output.Count);
        Assert.Equal(7, output[0].GlyphId);
        Assert.Equal([0x41, 0x1F600, 0x42], output.Skip(1).Select(g => g.CodePoint));
        Assert.Equal([10, 20, 30], output.Skip(1).Select(g => g.Cluster));
        Assert.Equal(face.GetGlyphIndex('A'), output[1].GlyphId);
    }

    [Fact]
    public void ScalarCheckAndLengthChecks()
    {
        FontFace face = TestFonts.Noto;
        var output = new List<CodePointGlyph>();
        uint script = 0;
        TextShaping.ShapeCodePoints(face, [0x41, -5, 0xD800, 0x110000], [0, 1, 2, 3], output, false, null, ref script);
        Assert.Equal(4, output.Count);
        Assert.Equal([0x41, 0xFFFD, 0xFFFD, 0xFFFD], output.Select(g => g.CodePoint));
        uint s2 = 0;
        Assert.Throws<ArgumentException>(() =>
        {
            uint s = 0;
            TextShaping.ShapeCodePoints(face, [1, 2], [1], output, false, null, ref s);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            uint s = 0;
            TextShaping.ShapeCodePoints(null!, [1], [1], output, false, null, ref s);
        });
        TextShaping.ShapeCodePoints(face, [], [], output, false, null, ref s2);
        Assert.Equal(0u, s2);
    }

    [Fact]
    public void OptionsReachTheCodePointShaper()
    {
        FontFace face = TestFonts.Public(TestFonts.BoldKern);
        var plain = new ShapingOptions { Kerning = KerningMode.None };
        uint s1 = 0, s2 = 0;
        var kerned = new List<CodePointGlyph>();
        var flat = new List<CodePointGlyph>();
        TextShaping.ShapeCodePoints(face, Cps("AVAV"), Offsets("AVAV"), kerned, false, null, ref s1);
        TextShaping.ShapeCodePoints(face, Cps("AVAV"), Offsets("AVAV"), flat, false, plain, ref s2);
        Assert.NotEqual(kerned.Sum(g => g.XAdvance), flat.Sum(g => g.XAdvance));
    }

    [Fact]
    public void ShapedRunOriginsKeepTheCodePointAndTheShaperOrder()
    {
        FontFace face = TestFonts.Public(Deva);
        const string text = "\u0915\u093F"; // KA + short i: the vowel sign is drawn before the consonant
        var run = new ShapedRun();
        TextShaping.ShapeRun(text, face, run);
        Assert.Equal(run.GlyphCount, run.Origins.Length);
        for (int i = 0; i < run.Origins.Length; i++)
        {
            Assert.Equal(text.Length > 0 ? run.Origins[i].CodePoint : 0, run.Origins[i].CodePoint);
            Assert.InRange(run.Origins[i].ShaperIndex, 0, run.GlyphCount - 1);
        }

        // The origins are a permutation of the shaper's order and agree with the code point entry point.
        var raw = new List<CodePointGlyph>();
        uint script = 0;
        TextShaping.ShapeCodePoints(face, Cps(text), Offsets(text), raw, false, null, ref script);
        Assert.Equal(raw.Count, run.GlyphCount);
        for (int i = 0; i < run.GlyphCount; i++)
        {
            int k = run.Origins[i].ShaperIndex;
            Assert.Equal(raw[k].GlyphId, run.Glyphs[i].GlyphId);
            Assert.Equal(raw[k].CodePoint, run.Origins[i].CodePoint);
            Assert.Equal(raw[k].Cluster, run.Origins[i].Cluster);
        }
    }

    [Fact]
    public void ShapedRunOriginsReflectReorderedIndicClusters()
    {
        FontFace face = TestFonts.Public(Deva);
        string[] texts = ["\u0915\u093F", "\u0915\u094D\u0937\u093F \u0928\u092E\u0938\u094D\u0924\u0947", "\u0930\u094D\u0915\u0947 \u0915\u093F\u0924\u093E\u092C"];
        var run = new ShapedRun();
        foreach (string text in texts)
        {
            TextShaping.ShapeRun(text, face, run);
            var seen = new bool[run.GlyphCount];
            foreach (ShapedGlyphOrigin o in run.Origins)
            {
                Assert.False(seen[o.ShaperIndex]);
                seen[o.ShaperIndex] = true;
            }
        }
    }

    [Fact]
    public void BidiKeepsTheRemovedMarkerAndLineEndsReset()
    {
        int[] cps = Cps("ab \u202B\u05D0\u05D1\u202C cd  ");
        var levels = new byte[cps.Length];
        int para = TextShaping.ResolveBidiLevels(cps, TextDirection.LeftToRight, levels);
        Assert.Equal(0, para);
        Assert.Equal(TextShaping.BidiRemoved, levels[3]);
        Assert.Equal(TextShaping.BidiRemoved, levels[6]);
        Assert.Equal(1, levels[4] & 1);

        // The UTF-16 API fills the removed characters instead.
        byte[] filled = TextShaping.GetBidiLevels("ab \u202B\u05D0\u05D1\u202C cd  ", TextDirection.LeftToRight, out _);
        Assert.NotEqual(TextShaping.BidiRemoved, filled[3]);

        // L1: trailing white space goes back to the paragraph level.
        int[] rtl = Cps("\u05D0\u05D1 x  ");
        var l2 = new byte[rtl.Length];
        int p2 = TextShaping.ResolveBidiLevels(rtl, TextDirection.RightToLeft, l2);
        Assert.Equal(1, p2);
        TextShaping.ResetTrailingWhitespace(rtl, l2, p2);
        Assert.Equal(1, l2[^1]);
        int[] ltr = Cps("abc \u05D0  ");
        var l3 = new byte[ltr.Length];
        TextShaping.ResolveBidiLevels(ltr, TextDirection.LeftToRight, l3);
        TextShaping.ResetTrailingWhitespace(ltr, l3, 0);
        Assert.Equal(0, l3[^1]);
        Assert.Equal(0, l3[^2]);

        // Visual order counts only shown characters.
        var order = new int[cps.Length];
        int shown = TextShaping.GetVisualOrder(levels, order);
        Assert.Equal(cps.Length - 2, shown);
        Assert.DoesNotContain(3, order.Take(shown));
        Assert.DoesNotContain(6, order.Take(shown));
    }

    [Fact]
    public void BidiRejectsShortBuffersAndHandlesEmptyText()
    {
        Assert.Throws<ArgumentException>(() => TextShaping.ResolveBidiLevels([1, 2], TextDirection.Auto, new byte[1]));
        Assert.Equal(0, TextShaping.ResolveBidiLevels([], TextDirection.Auto, []));
        Assert.Equal(1, TextShaping.ResolveBidiLevels([], TextDirection.RightToLeft, []));
        Assert.Throws<ArgumentException>(() => TextShaping.ResetTrailingWhitespace([1], new byte[2], 0));
    }

    [Fact]
    public void ByteFillsMatchTheTypedApis()
    {
        string text = "Hello, w\u00F6rld \u65E5\u672C\u8A9E e\u0301 \U0001F600\nx-y";
        var typed = new LineBreakOpportunity[text.Length + 1];
        TextSegmentation.GetLineBreakOpportunities(text, typed);
        var bytes = new byte[text.Length + 3];
        TextSegmentation.FillLineBreakOpportunities(text, bytes);
        for (int i = 0; i <= text.Length; i++)
        {
            Assert.Equal((byte)typed[i], bytes[i]);
        }

        var starts = new byte[text.Length + 1];
        TextSegmentation.FillGraphemeStarts(text, starts);
        int[] boundaries = TextSegmentation.GetGraphemeBoundaries(text);
        for (int i = 0; i <= text.Length; i++)
        {
            Assert.Equal(boundaries.Contains(i), starts[i] != 0);
        }

        Assert.Throws<ArgumentException>(() => TextSegmentation.FillLineBreakOpportunities(text, new byte[text.Length]));
        Assert.Throws<ArgumentException>(() => TextSegmentation.FillGraphemeStarts(text, new byte[text.Length]));
    }

    [Fact]
    public void EastAsianWidth()
    {
        Assert.True(TextSegmentation.IsEastAsianWide(0x65E5));
        Assert.True(TextSegmentation.IsEastAsianWide(0x3042));
        Assert.True(TextSegmentation.IsEastAsianWide(0xFF21));
        Assert.True(TextSegmentation.IsEastAsianWide(0xAC00));
        Assert.False(TextSegmentation.IsEastAsianWide('A'));
        Assert.False(TextSegmentation.IsEastAsianWide(0x05D0));
        Assert.False(TextSegmentation.IsEastAsianWide(-1));
        Assert.False(TextSegmentation.IsEastAsianWide(0x110000));
    }

    [Fact]
    public void JoiningKindsAndSyllableScripts()
    {
        Assert.Equal(JoiningKind.Dual, UnicodeProperties.GetJoiningKind(0x0628));
        Assert.Equal(JoiningKind.Right, UnicodeProperties.GetJoiningKind(0x0627));
        Assert.Equal(JoiningKind.Causing, UnicodeProperties.GetJoiningKind(0x0640));
        Assert.Equal(JoiningKind.Transparent, UnicodeProperties.GetJoiningKind(0x064E));
        Assert.Equal(JoiningKind.None, UnicodeProperties.GetJoiningKind('a'));
        Assert.Equal(JoiningKind.None, UnicodeProperties.GetJoiningKind(-1));
        Assert.Equal(JoiningKind.None, UnicodeProperties.GetJoiningKind(0x7FFFFFFF));

        Assert.True(UnicodeProperties.IsSyllableScript(0x0915));
        Assert.True(UnicodeProperties.IsSyllableScript(0x1780));
        Assert.True(UnicodeProperties.IsSyllableScript(0x1000));
        Assert.False(UnicodeProperties.IsSyllableScript(0x0E01)); // Thai
        Assert.False(UnicodeProperties.IsSyllableScript(0x0E81)); // Lao
        Assert.False(UnicodeProperties.IsSyllableScript('a'));
        Assert.False(UnicodeProperties.IsSyllableScript(0x0627));
        Assert.False(UnicodeProperties.IsSyllableScript(-3));
    }

    [Fact]
    public void RawTablesAreReturnedAsStored()
    {
        byte[] data = TestFonts.Bytes(TestFonts.Regular);
        FontFace face = FontFace.Load(data);
        ReadOnlyMemory<byte> head = face.GetTable("head");
        Assert.Equal(54, head.Length);
        Assert.True(face.HasTable("head"));
        Assert.Equal(0x5F0F3CF5u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(head.Span[12..]));
        Assert.True(face.HasTable("glyf"));
        Assert.False(face.HasTable("COLR"));
        Assert.True(face.GetTable("COLR").IsEmpty);
        Assert.Equal(face.HasTable("cvt "), face.HasTable("cvt"));
        Assert.Throws<ArgumentNullException>(() => face.GetTable(null!));
        Assert.Throws<ArgumentException>(() => face.GetTable("headx"));
        Assert.Throws<ArgumentException>(() => face.GetTable(""));
        Assert.Throws<ArgumentException>(() => face.GetTable("h\u0100ad"));
    }
}
