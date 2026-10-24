using OneFont.Fonts;

namespace OneFont.Tests;

public class LoadingTests
{
    [Fact]
    public void TrueTypeFontReportsFamilyStyleAndMetrics()
    {
        FontFace f = TestFonts.Noto;
        Assert.Equal("Noto Sans", f.FamilyName);
        Assert.False(f.IsBold);
        Assert.False(f.IsItalic);
        Assert.False(f.IsCff);
        Assert.Equal(1000, f.UnitsPerEm);
        Assert.Equal(1000, f.Metrics.UnitsPerEm);
        Assert.Equal(1069, f.Metrics.Ascender);
        Assert.Equal(-293, f.Metrics.Descender);
        Assert.Equal(1362, f.Metrics.LineHeight);
        Assert.True(f.HasGlyph('A'));
        Assert.True(f.HasGlyph(0x416)); // Cyrillic
        Assert.False(f.HasGlyph(0x4E00));
        Assert.Equal(639, f.GetAdvance(f.GetGlyphIndex('A')));
    }

    [Fact]
    public void CffFontLoadsWithCidKeyedOutlines()
    {
        FontFace face = TestFonts.NotoCjk;
        Assert.True(face.IsCff);
        Assert.Equal(1000, face.UnitsPerEm);
        int ideograph = face.GetGlyphIndex(0x65E5);
        Assert.NotEqual(0, ideograph);
        GlyphOutline o = face.GetOutline(ideograph);
        Assert.False(o.IsEmpty);
        (float x0, float y0, float x1, float y1) = o.TightBox();
        Assert.InRange(x1 - x0, 400, 1000);
        Assert.InRange(y1 - y0, 400, 1000);
        Assert.Equal("Noto Sans CJK JP", face.FamilyName);
    }

    [Fact]
    public void CollectionHoldsTheFacesAndTheIndexPicksOne()
    {
        byte[] ttc = TestFonts.Bytes(TestFonts.Collection);
        Assert.Equal(2, FontFace.GetFaceCount(ttc));
        Assert.Equal(1, FontFace.GetFaceCount(TestFonts.Bytes(TestFonts.Regular)));
        FontFace regular = FontFace.Load(ttc, 0);
        FontFace bold = FontFace.Load(ttc, 1);
        Assert.Equal(regular.FamilyName, bold.FamilyName);
        Assert.False(regular.IsBold);
        Assert.True(bold.IsBold);
        Assert.True(bold.Weight >= 700);
        Assert.Contains("Bold", bold.FullName);

        // Face 1 is the bold face with the legacy kern table; face 0 has GPOS kerning only.
        Assert.NotEqual(regular.GetAdvance(regular.GetGlyphIndex('H')), bold.GetAdvance(bold.GetGlyphIndex('H')));
        Assert.Throws<ArgumentOutOfRangeException>(() => FontFace.Load(ttc, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => FontFace.Load(ttc, -1));
    }

    [Fact]
    public void WoffAndWoff2AreRefusedAsFontErrors()
    {
        byte[] data = new byte[64];
        "wOF2"u8.CopyTo(data);
        Assert.Throws<FontFormatException>(() => FontFace.Load(data));
        "wOFF"u8.CopyTo(data);
        Assert.Throws<FontFormatException>(() => FontFace.Load(data));
        Assert.Throws<FontFormatException>(() => FontFace.Load(new byte[4]));
        Assert.Throws<ArgumentNullException>(() => FontFace.Load(null!));
    }

    [Fact]
    public void NamesWeightAndSlantAreReported()
    {
        Assert.Equal(400, TestFonts.Noto.Weight);
        Assert.True(TestFonts.Public(TestFonts.BoldKern).Weight >= 700);
        Assert.False(TestFonts.Noto.IsItalic);
        Assert.True(TestFonts.Noto.SuperscriptYOffset >= 0);
        Assert.True(TestFonts.Noto.SubscriptYOffset >= 0);
        Assert.NotNull(TestFonts.Noto.PostScriptName);
    }

    [Fact]
    public void OutlinesReplayThroughASinkWithATransform()
    {
        FontFace face = TestFonts.Noto;
        GlyphOutline o = face.GetOutline(face.GetGlyphIndex('A'));
        var plain = new Recorder();
        o.Write(plain);
        var scaled = new Recorder();
        o.Write(scaled, new System.Numerics.Matrix3x2(0.5f, 0, 0, -0.5f, 10, 20));
        Assert.Equal(plain.Points.Count, scaled.Points.Count);
        Assert.Equal(plain.Verbs, scaled.Verbs);
        for (int i = 0; i < plain.Points.Count; i++)
        {
            Assert.Equal((plain.Points[i].X * 0.5f) + 10, scaled.Points[i].X, 3);
            Assert.Equal((plain.Points[i].Y * -0.5f) + 20, scaled.Points[i].Y, 3);
        }

        Assert.Same(o, face.GetOutline(face.GetGlyphIndex('A')));
        Assert.True(face.GetOutline(face.GetGlyphIndex(' ')).IsEmpty);
    }

    private sealed class Recorder : IOutlineSink
    {
        public List<char> Verbs { get; } = [];

        public List<(float X, float Y)> Points { get; } = [];

        public void MoveTo(float x, float y) => Add('M', (x, y));

        public void LineTo(float x, float y) => Add('L', (x, y));

        public void QuadTo(float cx, float cy, float x, float y) => Add('Q', (cx, cy), (x, y));

        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y) => Add('C', (c1x, c1y), (c2x, c2y), (x, y));

        public void Close() => Verbs.Add('Z');

        private void Add(char verb, params (float, float)[] points)
        {
            Verbs.Add(verb);
            Points.AddRange(points);
        }
    }

    [Fact]
    public void TruncatedAndCorruptedFontsOnlyThrowFontErrors()
    {
        var rng = new Random(7);
        foreach (string file in new[] { TestFonts.Regular, TestFonts.Cff, TestFonts.Collection, TestFonts.BoldKern })
        {
            byte[] good = TestFonts.Bytes(file);
            for (int i = 0; i < 400; i++)
            {
                byte[] bad = i % 2 == 0 ? good[..rng.Next(0, good.Length)] : (byte[])good.Clone();
                if (i % 2 == 1)
                {
                    for (int k = rng.Next(1, 12); k > 0; k--)
                    {
                        bad[rng.Next(bad.Length)] = (byte)rng.Next(256);
                    }
                }

                Exercise(bad);
            }
        }
    }

    internal static void Exercise(byte[] data)
    {
        try
        {
            foreach (FontData face in FontFile.Parse(data))
            {
                _ = new FontMetrics(face).LineHeight;
                int prev = 0;
                foreach (char c in "AVa gHé日Ж")
                {
                    int g = face.GetGlyphIndex(c);
                    _ = face.GetAdvance(g);
                    _ = face.GetOutline(g).TightBox();
                    _ = face.GetKerning(prev, g);
                    prev = g;
                }

                for (int g = 0; g < Math.Min(face.NumGlyphs, 64); g++)
                {
                    _ = face.GetOutline(g);
                }
            }
        }
        catch (FontFormatException)
        {
        }
    }
}
