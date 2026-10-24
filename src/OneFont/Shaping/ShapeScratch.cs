namespace OneFont.Shaping;

/// <summary>
/// Working storage one thread reuses from run to run so shaping allocates nothing per run once it has warmed up: the glyph list a run is
/// shaped into, the lookup engines and the sort space for cluster order. Each thread has its own, so concurrent callers never share it;
/// a call uses it from start to finish and does not call out, so it is never entered twice.
/// </summary>
internal sealed class ShapeScratch
{
    // What an idle thread keeps; a longer run grows the space for that call only.
    private const int RetainedGlyphs = 1 << 14;

    [ThreadStatic]
    private static ShapeScratch? t_current;

    /// <summary>The shaped glyphs of the stretch being shaped, in logical order.</summary>
    public readonly List<ShapedRunGlyph> Run = [];

    /// <summary>The result the 1.0 list API builds its clusters from.</summary>
    public readonly ShapedRun Temp = new();

    public ShapeContext? Context;
    public Engine? GsubEngine;
    public Engine? GposEngine;

    // The decomposed characters a syllable shaper (Indic, Universal Shaping Engine) classifies before it builds glyph records.
    public int[] RunCodePoints = [];
    public int[] RunClusters = [];
    public byte[] RunCategories = [];
    public byte[] RunMatras = [];
    public bool[] RunAbove = [];

    public long[] SortKeys = [];
    public ShapedClusterRange[] SortItems = [];
    public ShapedGlyph[] GlyphCopy = [];
    public ShapedGlyphOrigin[] OriginCopy = [];

    public static ShapeScratch Current => t_current ??= new ShapeScratch();

    /// <summary>Lets go of anything a long run made large.</summary>
    public void Trim()
    {
        if (Run.Capacity > RetainedGlyphs)
        {
            Run.Clear();
            Run.Capacity = 0;
        }

        if (SortKeys.Length > RetainedGlyphs)
        {
            SortKeys = [];
            SortItems = [];
        }

        if (RunCodePoints.Length > RetainedGlyphs)
        {
            RunCodePoints = [];
            RunClusters = [];
            RunCategories = [];
            RunMatras = [];
            RunAbove = [];
        }

        if (GlyphCopy.Length > RetainedGlyphs)
        {
            GlyphCopy = [];
            OriginCopy = [];
        }

        Temp.Trim();
    }

    /// <summary>Makes the syllable shapers' character arrays at least <paramref name="size"/> long, keeping what they hold.</summary>
    public void EnsureRun(int size)
    {
        if (RunCodePoints.Length < size)
        {
            int n = Math.Max(size, 64);
            Array.Resize(ref RunCodePoints, n);
            Array.Resize(ref RunClusters, n);
            Array.Resize(ref RunCategories, n);
            Array.Resize(ref RunMatras, n);
            Array.Resize(ref RunAbove, n);
        }
    }

    public Engine GetEngine(bool gpos, LayoutTable table, Gdef? gdef, GlyphBuffer buffer, int numGlyphs, int textLength, bool rtl)
    {
        Engine? e = gpos ? GposEngine : GsubEngine;
        if (e is null)
        {
            e = new Engine(table, gdef, buffer, numGlyphs, textLength, rtl);
            if (gpos)
            {
                GposEngine = e;
            }
            else
            {
                GsubEngine = e;
            }
        }
        else
        {
            e.Reset(table, gdef, buffer, numGlyphs, textLength, rtl);
        }

        return e;
    }
}
