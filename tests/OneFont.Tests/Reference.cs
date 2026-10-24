using System.Numerics;
using SixLabors.Fonts;
using RefFont = SixLabors.Fonts.Font;
using RefFontCollection = SixLabors.Fonts.FontCollection;

namespace OneFont.Tests;

/// <summary>SixLabors.Fonts 2.1.3, used only through its public API as a black-box reference.</summary>
internal static class Reference
{
    private static readonly Dictionary<string, RefFontCollection> Collections = [];

    public static RefFont Font(string file, float size, int face = 0, string? family = null)
    {
        string key = file;
        lock (Collections)
        {
            if (!Collections.TryGetValue(key, out RefFontCollection? c))
            {
                c = new RefFontCollection();
                if (file.EndsWith(".ttc", StringComparison.Ordinal))
                {
                    c.AddCollection(TestFonts.PathOf(file));
                }
                else
                {
                    c.Add(TestFonts.PathOf(file));
                }

                Collections[key] = c;
            }

            SixLabors.Fonts.FontFamily[] fams = [.. c.Families];
            SixLabors.Fonts.FontFamily fam = family is null ? fams[0] : c.Get(family);
            SixLabors.Fonts.FontStyle style = face == 1 ? SixLabors.Fonts.FontStyle.Bold : SixLabors.Fonts.FontStyle.Regular;
            return fam.CreateFont(size, style);
        }
    }
}
