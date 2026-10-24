namespace OneFont.Tests;

/// <summary>The public shaping, bidi and fallback API that callers with their own layout build on.</summary>
public class TextShapingApiTests
{
    [Fact]
    public void BidiLevelsMarkRightToLeftRunsAndTheParagraphLevel()
    {
        byte[] levels = TextShaping.GetBidiLevels("ab \u05D0\u05D1 cd", TextDirection.LeftToRight, out int para);
        Assert.Equal(0, para);
        Assert.Equal(new byte[] { 0, 0, 0, 1, 1, 0, 0, 0 }, levels);
        byte[] rtl = TextShaping.GetBidiLevels("\u05D0\u05D1", TextDirection.RightToLeft, out int rpara);
        Assert.Equal(1, rpara);
        Assert.All(rtl, l => Assert.Equal(1, l));
        Assert.Empty(TextShaping.GetBidiLevels("", TextDirection.Auto, out _));
    }

    [Fact]
    public void VisualOrderReversesRightToLeftRuns()
    {
        ReadOnlySpan<byte> ltr = [0, 0, 0];
        ReadOnlySpan<byte> mixed = [0, 1, 1, 0];
        Assert.Equal([0, 1, 2], TextShaping.GetVisualOrder(ltr));
        Assert.Equal([0, 2, 1, 3], TextShaping.GetVisualOrder(mixed));
        Assert.Empty(TextShaping.GetVisualOrder([]));
    }

    [Fact]
    public void ShapeRunReturnsOneClusterPerLetterWithAdvancesAndGlyphs()
    {
        FontFace face = TestFonts.Noto;
        IReadOnlyList<ShapedCluster> clusters = TextShaping.ShapeRun("Hi!", face);
        Assert.Equal(3, clusters.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(i, clusters[i].Start);
            Assert.Equal(1, clusters[i].Length);
            Assert.Equal(0, clusters[i].FaceIndex);
            Assert.True(clusters[i].Advance > 0);
            Assert.False(clusters[i].HasMissingGlyph);
            Assert.Equal(1, clusters[i].Glyphs.Length);
            Assert.Equal(clusters[i].Advance, clusters[i].Glyphs[0].XAdvance);
        }

        Assert.Equal(face.GetGlyphIndex('H'), clusters[0].Glyphs[0].GlyphId);
        GlyphOutline h = face.GetOutline(clusters[0].Glyphs[0].GlyphId);
        (float x0, float y0, float x1, float y1) = h.TightBox();
        Assert.True(x1 - x0 > 250 && y1 - y0 > 500, "H has a body");
        Assert.Equal(0f, y0, 1);
        Assert.InRange(clusters.Sum(c => c.Advance), 1000, 2000);
    }

    [Fact]
    public void KerningCanBeSwitchedOff()
    {
        FontFace face = TestFonts.Noto;
        int kerned = TextShaping.ShapeRun("AV", face)[0].Advance;
        int plain = TextShaping.ShapeRun("AV", face, options: new ShapingOptions { Kerning = KerningMode.None })[0].Advance;
        Assert.True(kerned < plain);
        Assert.Equal(kerned - plain, face.GetKerning(face.GetGlyphIndex('A'), face.GetGlyphIndex('V')));
    }

    [Fact]
    public void FeaturesAndLanguageReachTheShaper()
    {
        FontFace face = TestFonts.Public(TestFonts.Shaping);
        var smcp = new ShapingOptions();
        smcp.Features.Add(FontFeature.On("smcp"));
        int plain = TextShaping.ShapeRun("abc", face)[0].Glyphs[0].GlyphId;
        int small = TextShaping.ShapeRun("abc", face, options: smcp)[0].Glyphs[0].GlyphId;
        Assert.NotEqual(plain, small);
        _ = TextShaping.ShapeRun("abc", face, options: new ShapingOptions { Language = "tr" });
    }

    [Fact]
    public void LigaturesAndMarksFormClusters()
    {
        FontFace face = TestFonts.Public(TestFonts.Shaping);
        IReadOnlyList<ShapedCluster> fi = TextShaping.ShapeRun("office", face);
        Assert.True(fi.Count < 6, "ffi/fi ligature");
        Assert.Equal(6, fi.Sum(c => c.Length));
        IReadOnlyList<ShapedCluster> marked = TextShaping.ShapeRun("e\u0301x", face);
        Assert.Equal(2, marked.Count);
        Assert.Equal(2, marked[0].Length);
        Assert.Equal(0, marked[0].Start);
        Assert.Equal(2, marked[1].Start);
    }

    [Fact]
    public void AnUnknownCharacterIsReportedAsMissing()
    {
        IReadOnlyList<ShapedCluster> clusters = TextShaping.ShapeRun("\u4E2D", TestFonts.Noto);
        Assert.Single(clusters);
        Assert.True(clusters[0].HasMissingGlyph);
        Assert.Equal(0, clusters[0].Glyphs[0].GlyphId);
        Assert.Empty(TextShaping.ShapeRun("", TestFonts.Noto));
    }

    [Fact]
    public void ArabicAndDevanagariShapeIntoJoinedFormsAndConjuncts()
    {
        FontFace arabic = TestFonts.Public(BidiHarness.Arabic);
        IReadOnlyList<ShapedCluster> beh = TextShaping.ShapeRun("\u0628\u0628\u0628", arabic, rightToLeft: true);
        int[] ids = [.. beh.Select(c => c.Glyphs[0].GlyphId)];
        Assert.Equal(3, ids.Length);
        Assert.NotEqual(ids[0], ids[1]);   // initial and medial forms differ
        Assert.NotEqual(ids[1], ids[2]);   // medial and final forms differ
        Assert.NotEqual(arabic.GetGlyphIndex(0x628), ids[0]);

        FontFace deva = TestFonts.Public("NotoSansDevanagari-Subset-Regular.ttf");
        IReadOnlyList<ShapedCluster> kssa = TextShaping.ShapeRun("\u0915\u094D\u0937", deva);
        Assert.Single(kssa);
        Assert.Equal(3, kssa[0].Length);
    }

    [Fact]
    public void ALongRunIsRefusedBeforeAnyWork()
    {
        Assert.Throws<ArgumentNullException>(() => TextShaping.ShapeRun("a", null!));
    }

    [Fact]
    public void FallbackPicksTheFirstFaceThatHasTheCharacter()
    {
        FontFace latin = TestFonts.Noto;
        FontFace cjk = TestFonts.NotoCjk;
        var chain = new FontFallbackChain([latin, cjk]);
        Assert.Equal(2, chain.Faces.Count);
        Assert.Equal(0, chain.SelectFace('a'));
        Assert.Equal(1, chain.SelectFace(0x65E5));
        Assert.Equal(0, chain.SelectFace(0x1F600)); // nobody has it: the first face draws its missing glyph

        string text = "ab\u65E5\u672Cc \u200B\U0001F600";
        IReadOnlyList<ShapedCluster> clusters = chain.ShapeRun(text);
        Assert.Equal(text.Length, clusters.Sum(c => c.Length));
        int at = 0;
        foreach (ShapedCluster c in clusters)
        {
            Assert.Equal(at, c.Start);
            at += c.Length;
        }

        Assert.Equal([0, 0, 1, 1, 0, 0, 0, 0], clusters.Select(c => c.FaceIndex).Take(8));
        Assert.False(clusters[2].HasMissingGlyph);
        Assert.Equal(cjk.GetGlyphIndex(0x65E5), clusters[2].Glyphs[0].GlyphId);
        Assert.Equal(cjk.GetAdvance(cjk.GetGlyphIndex(0x65E5)), clusters[2].Advance);
        Assert.True(clusters[^1].HasMissingGlyph);
    }

    [Fact]
    public void FallbackChainValidatesItsFaces()
    {
        Assert.Throws<ArgumentException>(() => new FontFallbackChain([]));
        Assert.Throws<ArgumentNullException>(() => new FontFallbackChain([null!]));
        Assert.Throws<ArgumentException>(() => new FontFallbackChain(Enumerable.Repeat(TestFonts.Noto, FontFallbackChain.MaxFaces + 1)));
        Assert.Equal(new FontFallbackChain([TestFonts.Noto]).ShapeRun("Hi!").Select(c => c.Advance), TextShaping.ShapeRun("Hi!", TestFonts.Noto).Select(c => c.Advance));
    }

    [Theory]
    [InlineData("‌क्ष")]
    [InlineData("‍क")]
    [InlineData("‌")]
    [InlineData("‌‍‌")]
    [InlineData("क‌‌")]
    public void ClustersCoverTheTextEvenWhenTheShaperDropsCharacters(string text)
    {
        // Joiners next to Indic letters are consumed by the shaper; the clusters must still start at 0 and follow each other to the end.
        FontFace deva = TestFonts.Public("NotoSansDevanagari-Subset-Regular.ttf");
        IReadOnlyList<ShapedCluster> clusters = TextShaping.ShapeRun(text, deva);
        Assert.NotEmpty(clusters);
        int at = 0;
        foreach (ShapedCluster c in clusters)
        {
            Assert.Equal(at, c.Start);
            Assert.True(c.Length >= 1);
            at += c.Length;
        }

        Assert.Equal(text.Length, at);
    }
}
