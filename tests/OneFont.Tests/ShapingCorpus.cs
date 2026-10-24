using System.Security.Cryptography;
using System.Text;

namespace OneFont.Tests;

/// <summary>
/// A fixed corpus of texts, faces and shaping settings, and a digest of what shaping makes of it. It uses only the 1.0 API so the same
/// file can be run against an older build to produce the golden digests in <c>testdata/shaping/golden.txt</c>.
/// </summary>
internal static class ShapingCorpus
{
    public static List<(string Name, FontFace Face)> Faces(string fontsDir)
    {
        var faces = new List<(string, FontFace)>();
        foreach (string f in Directory.EnumerateFiles(fontsDir).Where(f => f.EndsWith(".ttf") || f.EndsWith(".otf") || f.EndsWith(".ttc")).OrderBy(f => f, StringComparer.Ordinal))
        {
            byte[] bytes = File.ReadAllBytes(f);
            int count = FontFace.GetFaceCount(bytes);
            for (int i = 0; i < count; i++)
            {
                faces.Add((Path.GetFileName(f) + "#" + i, FontFace.Load(bytes, i)));
            }
        }

        return faces;
    }

    /// <summary>The test lines of the complex-script files (whole lines and their words) and seeded random text over several scripts.</summary>
    public static List<string> Texts(string complexDir, int randomCases)
    {
        var texts = new List<string>();
        foreach (string f in Directory.EnumerateFiles(complexDir, "*.txt").OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach (string line in File.ReadAllLines(f))
            {
                if (line.Length > 0 && line[0] != '#')
                {
                    texts.Add(line);
                    texts.AddRange(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }

        int[][] pools =
        [
            [.. Enumerable.Range('a', 26), .. Enumerable.Range('A', 26), .. Enumerable.Range('0', 10), ' ', ' ', ' ', '.', ',', '-', 0xA0, 0xAD, 0xFB01, 0xFB02, 0xE9, 0xF1, 0x300, 0x301, 0x308],
            [.. Enumerable.Range(0x620, 0x50), 0x640, 0x64B, 0x64C, 0x651, 0x652, 0x670, 0x200C, 0x200D, ' ', ' ', 0x644, 0x627, 0x644, 0x627],
            [.. Enumerable.Range(0x900, 0x80), 0x200C, 0x200D, ' ', 0x915, 0x94D, 0x937, 0x930, 0x94D],
            [.. Enumerable.Range(0x5D0, 0x20), .. Enumerable.Range(0x591, 0x20), ' '],
            [.. Enumerable.Range(0xE01, 0x5B), .. Enumerable.Range(0xE81, 0x5F), .. Enumerable.Range(0x1780, 0x60), .. Enumerable.Range(0x1000, 0x60)],
            [.. Enumerable.Range(0x980, 0x100), .. Enumerable.Range(0xB80, 0x80), .. Enumerable.Range(0xC00, 0x100), .. Enumerable.Range(0xD00, 0x80)],
            [0x4E00, 0x65E5, 0x672C, 0x8A9E, 0x1F600, 0x1F468, 0x200D, 0x1F469, 0x1F3FB, 0xD800, 0xDC00, 0xDFFF, 0x0, 0x1, 0x202E, 0x2066, 0x3042, 0xAC00, 0x1100, 0x1161],
        ];

        // A linear congruential generator, so the corpus is the same on every runtime.
        ulong state = 0x2545F4914F6CDD1DUL;
        int Next(int bound)
        {
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            return (int)((state >> 33) % (ulong)bound);
        }

        for (int i = 0; i < randomCases; i++)
        {
            int len = Next(90);
            var sb = new StringBuilder();
            int[] main = pools[Next(pools.Length)];
            int[] alt = pools[Next(pools.Length)];
            for (int k = 0; k < len; k++)
            {
                int[] src = Next(8) == 0 ? alt : main;
                int cp = src[Next(src.Length)];
                if (cp is >= 0xD800 and <= 0xDFFF)
                {
                    sb.Append((char)cp);
                }
                else
                {
                    sb.Append(char.ConvertFromUtf32(cp));
                }
            }

            texts.Add(sb.ToString());
        }

        return texts;
    }

    public static ShapingOptions? Options(int index) => (index & 3) switch
    {
        0 => null,
        1 => new ShapingOptions { Kerning = KerningMode.None },
        2 => Make("tr", ("smcp", 1), ("liga", 0)),
        _ => Make(null, ("salt", 2), ("aalt", 1), ("kern", 0)),
    };

    private static ShapingOptions Make(string? language, params (string, int)[] features)
    {
        var o = new ShapingOptions { Language = language };
        foreach ((string tag, int value) in features)
        {
            o.Features.Add(new FontFeature(tag, value));
        }

        return o;
    }

    /// <summary>Describes a list of clusters in one line of text.</summary>
    public static string Describe(IReadOnlyList<ShapedCluster> clusters)
    {
        var sb = new StringBuilder();
        foreach (ShapedCluster c in clusters)
        {
            sb.Append('[').Append(c.Start).Append(',').Append(c.Length).Append(',').Append(c.FaceIndex).Append(',').Append(c.Advance).Append(',').Append(c.HasMissingGlyph ? 1 : 0).Append(':');
            foreach (ShapedGlyph g in c.Glyphs)
            {
                sb.Append(g.GlyphId).Append('/').Append(g.XAdvance).Append('/').Append(g.XOffset).Append('/').Append(g.YOffset).Append(' ');
            }

            sb.Append(']');
        }

        return sb.ToString();
    }

    /// <summary>
    /// One line per face: the face name and a digest of the description of every text shaped alone (direction and settings cycling with the
    /// text's number), then one line for two fallback chains.
    /// </summary>
    public static IEnumerable<string> Digests(string fontsDir, string complexDir, Func<string, FontFace, bool, ShapingOptions?, string> shape, Func<string, FontFallbackChain, bool, ShapingOptions?, string> shapeChain)
    {
        List<(string Name, FontFace Face)> faces = Faces(fontsDir);
        List<string> texts = Texts(complexDir, 400);
        foreach ((string name, FontFace face) in faces)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int i = 0; i < texts.Count; i++)
            {
                string d;
                try
                {
                    d = shape(texts[i], face, (i & 1) == 1, Options(i));
                }
                catch (FontFormatException e)
                {
                    d = "FFE " + e.Message;
                }

                hash.AppendData(Encoding.UTF8.GetBytes(d + "\n"));
            }

            yield return name + " " + Convert.ToHexString(hash.GetHashAndReset())[..32];
        }

        FontFallbackChain[] chains = [new(faces.Take(4).Select(f => f.Face)), new(faces.Select(f => f.Face).Take(12))];
        for (int c = 0; c < chains.Length; c++)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int i = 0; i < texts.Count; i++)
            {
                string d;
                try
                {
                    d = shapeChain(texts[i], chains[c], ((i >> 1) & 1) == 1, Options(i + c));
                }
                catch (FontFormatException e)
                {
                    d = "FFE " + e.Message;
                }

                hash.AppendData(Encoding.UTF8.GetBytes(d + "\n"));
            }

            yield return "chain" + c + " " + Convert.ToHexString(hash.GetHashAndReset())[..32];
        }
    }
}
