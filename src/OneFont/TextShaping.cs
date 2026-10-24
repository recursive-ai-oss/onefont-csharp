using System.Buffers;
using OneFont.Fonts;
using OneFont.Shaping;
using OneFont.Unicode;

namespace OneFont;

/// <summary>
/// Shaping and bidirectional ordering for callers that do their own line layout: the Unicode bidirectional algorithm's levels and display
/// order, and the shaping of one run of text in one face and one direction into clusters.
/// </summary>
public static class TextShaping
{
    /// <summary>The longest text the bidi and shaping helpers take, in UTF-16 code units.</summary>
    public const int MaxRunLength = 1 << 24;

    /// <summary>
    /// Resolves the embedding level of every UTF-16 code unit of <paramref name="text"/> as one paragraph (Unicode Bidirectional Algorithm,
    /// rules P2 to I2). Level 0 is left-to-right, 1 right-to-left, higher levels are nested embeddings; the second half of a surrogate pair
    /// has its first half's level, and a character the algorithm removes (an embedding control) takes the level of the one before it.
    /// </summary>
    /// <param name="text">The paragraph.</param>
    /// <param name="direction">The paragraph direction; <see cref="TextDirection.Auto"/> takes it from the first strong character.</param>
    /// <param name="paragraphLevel">Receives the paragraph's base level, 0 or 1.</param>
    /// <returns>One level per UTF-16 code unit. Empty text gives an empty array.</returns>
    /// <exception cref="ArgumentException">The text is longer than <see cref="MaxRunLength"/>.</exception>
    public static byte[] GetBidiLevels(ReadOnlySpan<char> text, TextDirection direction, out int paragraphLevel)
    {
        if (text.Length > MaxRunLength)
        {
            throw new ArgumentException("The text is too long.", nameof(text));
        }

        paragraphLevel = 0;
        int length = text.Length;
        var result = new byte[length];
        if (length == 0)
        {
            return result;
        }

        int[] cps = ArrayPool<int>.Shared.Rent(length);
        byte[] lev = ArrayPool<byte>.Shared.Rent(length);
        int[] at = ArrayPool<int>.Shared.Rent(length);
        try
        {
            int n = 0;
            for (int i = 0; i < length; n++)
            {
                at[n] = i;
                if (char.IsHighSurrogate(text[i]) && i + 1 < length && char.IsLowSurrogate(text[i + 1]))
                {
                    cps[n] = char.ConvertToUtf32(text[i], text[i + 1]);
                    i += 2;
                }
                else
                {
                    cps[n] = char.IsSurrogate(text[i]) ? 0xFFFD : text[i];
                    i++;
                }
            }

            int request = direction switch { TextDirection.LeftToRight => 0, TextDirection.RightToLeft => 1, _ => -1 };
            paragraphLevel = Bidi.ResolveLevels(cps.AsSpan(0, n), request, lev.AsSpan(0, n), lineEndRule: false);
            byte prev = (byte)paragraphLevel;
            for (int k = 0; k < n; k++)
            {
                byte l = lev[k] == Bidi.Removed ? prev : lev[k];
                prev = l;
                int to = k + 1 < n ? at[k + 1] : length;
                for (int j = at[k]; j < to; j++)
                {
                    result[j] = l;
                }
            }

            return result;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(cps);
            ArrayPool<byte>.Shared.Return(lev);
            ArrayPool<int>.Shared.Return(at);
        }
    }

    /// <summary>
    /// Gives the display order of items that have embedding levels (Unicode Bidirectional Algorithm, rule L2): from the highest level down to
    /// the lowest odd one, every sequence of items at that level or above is reversed. Pass one level per cluster to order clusters.
    /// </summary>
    /// <param name="levels">The levels in logical order, for example the level of each cluster's first character.</param>
    /// <returns>The logical indices in left-to-right display order; every index appears once.</returns>
    public static int[] GetVisualOrder(ReadOnlySpan<byte> levels)
    {
        var order = new int[levels.Length];
        int shown = Bidi.VisualOrder(levels, order);
        return shown == order.Length ? order : order.AsSpan(0, shown).ToArray();
    }

    /// <summary>
    /// Shapes one run of text, set in one face in one direction, into clusters in logical order. The run is split into stretches of one
    /// script and each is shaped with that script's rules (Arabic joining, Indic and Southeast Asian syllables, ligatures, kerning, marks).
    /// Characters the face lacks give clusters with <see cref="ShapedCluster.HasMissingGlyph"/>; to choose another face per cluster use
    /// <see cref="FontFallbackChain"/>.
    /// </summary>
    /// <param name="text">The run. Lone surrogates are shaped as U+FFFD.</param>
    /// <param name="face">The face.</param>
    /// <param name="rightToLeft">Whether the run is right to left (all of it at one odd level).</param>
    /// <param name="options">Language, kerning and features; the defaults when <see langword="null"/>.</param>
    /// <returns>The clusters in logical order, covering the text without gaps. Empty text gives an empty list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="face"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The text is longer than <see cref="MaxRunLength"/>.</exception>
    /// <exception cref="FontFormatException">The font data is malformed.</exception>
    public static IReadOnlyList<ShapedCluster> ShapeRun(ReadOnlySpan<char> text, FontFace face, bool rightToLeft = false, ShapingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (text.Length > MaxRunLength)
        {
            throw new ArgumentException("The text is too long.", nameof(text));
        }

        var clusters = new List<ShapedCluster>();
        uint script = 0;
        ShapeInto(clusters, text, 0, face, 0, rightToLeft, Languages.ToTag(options?.Language), FeaturesOf(options), ref script);
        return clusters;
    }

    internal static FeatureSet FeaturesOf(ShapingOptions? options)
    {
        var settings = new List<(uint, int)>();
        if (options is not null)
        {
            if (options.Kerning != KerningMode.Normal)
            {
                settings.Add((Tag.Kern, 0));
            }

            foreach (FontFeature f in options.Features)
            {
                if (f.Tag is not null)
                {
                    settings.Add((Be.Tag(f.Tag), f.Value));
                }
            }
        }

        return FeatureSet.Create(settings);
    }

    // Shapes text (a slice of the caller's run starting at baseOffset) with one face and appends its clusters, in logical order.
    internal static void ShapeInto(List<ShapedCluster> clusters, ReadOnlySpan<char> text, int baseOffset, FontFace face, int faceIndex, bool rightToLeft, uint language, FeatureSet features, ref uint script)
    {
        int length = text.Length;
        if (length == 0)
        {
            return;
        }

        FontData data = face.Data;

        // Code points with the UTF-16 offset of the grapheme cluster each belongs to: marks and the like shape with their base.
        byte[] starts = ArrayPool<byte>.Shared.Rent(length + 1);
        int[] cps = ArrayPool<int>.Shared.Rent(length);
        int[] cl = ArrayPool<int>.Shared.Rent(length);
        try
        {
            Graphemes.Fill(text, starts);
            int n = 0;
            int cluster = 0;
            for (int i = 0; i < length; n++)
            {
                if (starts[i] != 0)
                {
                    cluster = i;
                }

                int cp;
                if (char.IsHighSurrogate(text[i]) && i + 1 < length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i += 2;
                }
                else
                {
                    cp = char.IsSurrogate(text[i]) ? 0xFFFD : text[i];
                    i++;
                }

                cps[n] = cp;
                cl[n] = cluster;
            }

            var run = new List<ShapedRunGlyph>(n + 4);
            var pending = new List<(int Start, int Advance, bool Missing, ShapedGlyph[] Glyphs)>();
            TextShaper.Shape(data, cps.AsSpan(0, n), cl.AsSpan(0, n), language, features, run, rightToLeft, ref script);

            int g = 0;
            while (g < run.Count)
            {
                int c = run[g].Cluster;
                int e = g;
                while (e < run.Count && run[e].Cluster == c)
                {
                    e++;
                }

                var glyphs = new ShapedGlyph[e - g];
                int advance = 0;
                bool missing = false;
                for (int k = g; k < e; k++)
                {
                    ShapedRunGlyph r = run[k];
                    glyphs[k - g] = new ShapedGlyph(r.GlyphId, r.XAdvance, r.XOffset, r.YOffset);
                    advance += r.XAdvance;
                    missing |= r.GlyphId == 0 && !IsInvisible(r.CodePoint);
                }

                pending.Add((c, advance, missing, glyphs));
                g = e;
            }

            // Lengths run to the next cluster's start (the last to the end of the text); clusters come out in logical order.
            pending.Sort(static (x, y) => x.Start.CompareTo(y.Start));
            if (pending.Count == 0)
            {
                // Every character was dropped by the shaper (for example a lone joiner next to an Indic letter): one empty cluster keeps the
                // clusters covering the text.
                clusters.Add(new ShapedCluster(baseOffset, length, faceIndex, 0, false, []));
            }

            for (int k = 0; k < pending.Count; k++)
            {
                int end = k + 1 < pending.Count ? pending[k + 1].Start : length;

                // Characters the shaper dropped at the start of the run (a joiner before an Indic letter) belong to the first cluster.
                int first = k == 0 ? 0 : pending[k].Start;
                clusters.Add(new ShapedCluster(baseOffset + first, Math.Max(end - first, 1), faceIndex, pending[k].Advance, pending[k].Missing, pending[k].Glyphs));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(starts);
            ArrayPool<int>.Shared.Return(cps);
            ArrayPool<int>.Shared.Return(cl);
        }
    }

    internal static bool IsInvisible(int cp) =>
        cp is >= 0x200B and <= 0x200F or 0x2060 or 0xFEFF or 0x00AD or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or < 0x20;
}
