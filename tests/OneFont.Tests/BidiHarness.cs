using System.Numerics;
using OneFont.Fonts;
using SixLabors.Fonts;
using RefTextOptions = SixLabors.Fonts.TextOptions;
using RefFont = SixLabors.Fonts.Font;

namespace OneFont.Tests;

/// <summary>Records the glyph id, font family and lowest-left outline point of every glyph the reference draws.</summary>
internal sealed class FamilyGlyphRecorder : IGlyphRenderer
{
    public List<(int GlyphId, string Family, Vector2 Min, bool HasOutline)> Glyphs { get; } = [];

    private int _glyphId;
    private string _family = "";
    private Vector2 _min;
    private bool _any;

    public void BeginText(in FontRectangle bounds)
    {
    }

    public bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
        _glyphId = parameters.GlyphId;
        _family = parameters.Font ?? "";
        _min = new Vector2(float.MaxValue, float.MaxValue);
        _any = false;
        return true;
    }

    private void Touch(Vector2 p)
    {
        _any = true;
        _min = Vector2.Min(_min, p);
    }

    public void BeginFigure()
    {
    }

    public void MoveTo(Vector2 point) => Touch(point);

    public void LineTo(Vector2 point) => Touch(point);

    public void QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
    {
        Touch(secondControlPoint);
        Touch(point);
    }

    public void CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
    {
        Touch(secondControlPoint);
        Touch(thirdControlPoint);
        Touch(point);
    }

    public void ArcTo(float radiusX, float radiusY, float rotationDegrees, bool largeArc, bool sweep, Vector2 point) => Touch(point);

    public void EndFigure()
    {
    }

    public void EndGlyph() => Glyphs.Add((_glyphId, _family, _min, _any));

    public void EndText()
    {
    }

    public SixLabors.Fonts.TextDecorations EnabledDecorations() => SixLabors.Fonts.TextDecorations.None;

    public void SetDecoration(SixLabors.Fonts.TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
    {
    }
}

/// <summary>Our layout of right-to-left and mixed text side by side with SixLabors.Fonts 2.1.3, used only through its public API.</summary>
internal static class BidiHarness
{
    public const string Merged = "NotoSans-Bidi-Subset.ttf";
    public const string Latin = "NotoSans-Shaping-Subset.ttf";
    public const string Arabic = "NotoSansArabic-Subset-Regular.ttf";
    public const string Hebrew = "NotoSansHebrew-Subset-Regular.ttf";

    public sealed record Glyph(int Id, string Family, float X, float Y, int Line = 0, bool LineHasRtl = true);

    /// <summary>
    /// Lays out <paramref name="text"/> as one line in one font that holds the Latin, Arabic and Hebrew subsets, in both libraries, and
    /// returns the glyphs that have outlines as origins in pixels (our pen position plus offset; the reference's outline corner minus
    /// ours). The line starts at x = 20 in both, at the top of the line box (the baseline is one ascender down).
    /// </summary>
    public static (List<Glyph> Ours, List<Glyph> Theirs) Run(string text, float size, TextDirection direction = TextDirection.Auto, KerningMode kerning = KerningMode.Normal, Action<RefTextOptions>? theirs = null)
    {
        FontData data = TestFonts.Face(Merged);
        var o = new ShapingOptions { Kerning = kerning };
        List<Placed> layout = MiniLayout.Run(text, data, size, direction, o);
        float baseline = MiniLayout.Baseline(data, size);
        bool hasRtl = layout.Any(g => (g.Level & 1) == 1);
        var ourList = new List<Glyph>();
        foreach (Placed g in layout)
        {
            if (g.IsSpace || data.GetOutline(g.GlyphId).IsEmpty)
            {
                continue;
            }

            ourList.Add(new Glyph(g.GlyphId, data.FamilyName, 20 + g.PenX + g.XOffset, baseline - g.YOffset, 0, hasRtl));
        }

        var rfont = Reference.Font(Merged, size);
        var ro = new RefTextOptions(rfont) { Origin = new Vector2(20, 0), KerningMode = kerning == KerningMode.None ? SixLabors.Fonts.KerningMode.None : SixLabors.Fonts.KerningMode.Auto };
        ro.TextDirection = direction switch { TextDirection.LeftToRight => SixLabors.Fonts.TextDirection.LeftToRight, TextDirection.RightToLeft => SixLabors.Fonts.TextDirection.RightToLeft, _ => SixLabors.Fonts.TextDirection.Auto };
        theirs?.Invoke(ro);
        var rec = new FamilyGlyphRecorder();
        TextRenderer.RenderTextTo(rec, text, ro);
        var theirList = new List<Glyph>();
        foreach ((int id, string fam, Vector2 min, bool has) in rec.Glyphs)
        {
            if (!has)
            {
                continue;
            }

            GlyphOutline outline = data.GetOutline(id);
            float minX = float.MaxValue, minY = float.MaxValue;
            for (int k = 0; k + 1 < outline.Coords.Length; k += 2)
            {
                minX = Math.Min(minX, outline.Coords[k]);
                minY = Math.Min(minY, -outline.Coords[k + 1]);
            }

            float scale = size / data.UnitsPerEm;
            theirList.Add(new Glyph(id, fam, min.X - (minX * scale), min.Y - (minY * scale), 0));
        }

        return (ourList, theirList);
    }

    public static string Describe(List<Glyph> l) => string.Join(" ", l.Select(g => $"{g.Id}@{g.X:0.##},{g.Y:0.##}"));
}
