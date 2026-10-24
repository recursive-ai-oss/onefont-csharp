using System.Buffers;
using OneFont.Fonts;
using OneFont.Shaping;
using OneFont.Unicode;

namespace OneFont;

/// <summary>
/// Shaping and bidirectional ordering for callers that do their own line layout: the Unicode bidirectional algorithm's levels and display
/// order, and the shaping of one run of text in one face and one direction into clusters.
/// </summary>
public static partial class TextShaping
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
        ThrowIfTooLong(text);
        var result = new byte[text.Length];
        GetBidiLevels(text, direction, result, out paragraphLevel);
        return result;
    }

    /// <summary>
    /// Resolves embedding levels like <see cref="GetBidiLevels(ReadOnlySpan{char}, TextDirection, out int)"/> into a buffer the caller owns,
    /// without allocating.
    /// </summary>
    /// <param name="text">The paragraph.</param>
    /// <param name="direction">The paragraph direction; <see cref="TextDirection.Auto"/> takes it from the first strong character.</param>
    /// <param name="levels">Receives one level per UTF-16 code unit of <paramref name="text"/>; it holds at least that many entries.</param>
    /// <param name="paragraphLevel">Receives the paragraph's base level, 0 or 1.</param>
    /// <returns><see langword="true"/> when every level is 0 (the text is plain left to right; also for empty text).</returns>
    /// <exception cref="ArgumentException">The text is longer than <see cref="MaxRunLength"/>, or <paramref name="levels"/> is shorter than the text.</exception>
    public static bool GetBidiLevels(ReadOnlySpan<char> text, TextDirection direction, Span<byte> levels, out int paragraphLevel)
    {
        ThrowIfTooLong(text);
        if (levels.Length < text.Length)
        {
            throw new ArgumentException("The levels buffer is shorter than the text.", nameof(levels));
        }

        Span<byte> result = levels[..text.Length];
        paragraphLevel = 0;
        int length = text.Length;
        if (length == 0)
        {
            return true;
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
            byte any = 0;
            for (int k = 0; k < n; k++)
            {
                byte l = lev[k] == Bidi.Removed ? prev : lev[k];
                prev = l;
                any |= l;
                int to = k + 1 < n ? at[k + 1] : length;
                for (int j = at[k]; j < to; j++)
                {
                    result[j] = l;
                }
            }

            return any == 0;
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

    /// <summary>Gives the display order like <see cref="GetVisualOrder(ReadOnlySpan{byte})"/> into a buffer the caller owns, without allocating.</summary>
    /// <param name="levels">The levels in logical order.</param>
    /// <param name="order">Receives the logical indices in left-to-right display order; it holds at least <c>levels.Length</c> entries.</param>
    /// <returns>How many entries of <paramref name="order"/> were written (every index appears once).</returns>
    /// <exception cref="ArgumentException"><paramref name="order"/> is shorter than <paramref name="levels"/>.</exception>
    public static int GetVisualOrder(ReadOnlySpan<byte> levels, Span<int> order)
    {
        if (order.Length < levels.Length)
        {
            throw new ArgumentException("The order buffer is shorter than the levels.", nameof(order));
        }

        return Bidi.VisualOrder(levels, order);
    }

    /// <summary>
    /// Shapes one run of text, set in one face in one direction, into clusters in logical order. The run is split into stretches of one
    /// script and each is shaped with that script's rules (Arabic joining, Indic and Southeast Asian syllables, ligatures, kerning, marks).
    /// Characters the face lacks give clusters with <see cref="ShapedCluster.HasMissingGlyph"/>; to choose another face per cluster use
    /// <see cref="FontFallbackChain"/>. To shape many runs without allocating per run, use the overload that fills a reusable <see cref="ShapedRun"/>.
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
        ThrowIfTooLong(text);
        ShapeScratch scratch = ShapeScratch.Current;
        ShapedRun temp = scratch.Temp;
        temp.Clear();
        uint script = 0;
        ShapeInto(temp, text, 0, face, 0, rightToLeft, Languages.ToTag(options?.Language), FeaturesOf(options), ref script);
        return ToClusters(temp, scratch);
    }

    /// <summary>
    /// Shapes a run like <see cref="ShapeRun(ReadOnlySpan{char}, FontFace, bool, ShapingOptions?)"/> into a reusable <see cref="ShapedRun"/>: the
    /// same clusters and glyphs in flat arrays, with no allocation per call once the result and the calling thread have warmed up.
    /// </summary>
    /// <param name="text">The run. Lone surrogates are shaped as U+FFFD.</param>
    /// <param name="face">The face.</param>
    /// <param name="result">Receives the clusters and glyphs; its previous contents are replaced. Its arrays are kept for the next call.</param>
    /// <param name="rightToLeft">Whether the run is right to left (all of it at one odd level).</param>
    /// <param name="options">Language, kerning and features; the defaults when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="face"/> or <paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The text is longer than <see cref="MaxRunLength"/>.</exception>
    /// <exception cref="FontFormatException">The font data is malformed. <paramref name="result"/> is then empty.</exception>
    public static void ShapeRun(ReadOnlySpan<char> text, FontFace face, ShapedRun result, bool rightToLeft = false, ShapingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(result);
        ThrowIfTooLong(text);
        result.Clear();
        uint script = 0;
        try
        {
            ShapeInto(result, text, 0, face, 0, rightToLeft, Languages.ToTag(options?.Language), FeaturesOf(options), ref script);
        }
        catch (FontFormatException)
        {
            result.Clear();
            throw;
        }
    }

    internal static void ThrowIfTooLong(ReadOnlySpan<char> text)
    {
        if (text.Length > MaxRunLength)
        {
            throw new ArgumentException("The text is too long.", nameof(text));
        }
    }

    // The 1.0 result: cluster objects over one copy of the glyphs.
    internal static List<ShapedCluster> ToClusters(ShapedRun run, ShapeScratch scratch)
    {
        ReadOnlySpan<ShapedClusterRange> ranges = run.Clusters;
        var clusters = new List<ShapedCluster>(ranges.Length);
        if (ranges.Length > 0)
        {
            ShapedGlyph[] glyphs = run.Glyphs.ToArray();
            foreach (ref readonly ShapedClusterRange r in ranges)
            {
                clusters.Add(new ShapedCluster(r.Start, r.Length, r.FaceIndex, r.Advance, r.HasMissingGlyph, glyphs, r.GlyphStart, r.GlyphCount));
            }
        }

        scratch.Trim();
        return clusters;
    }

    internal static FeatureSet FeaturesOf(ShapingOptions? options) => options is null ? FeatureSet.Empty : options.GetFeatureSet();

    // Shapes text (a slice of the caller's run starting at baseOffset) with one face and appends its clusters, in logical order.
    internal static void ShapeInto(ShapedRun result, ReadOnlySpan<char> text, int baseOffset, FontFace face, int faceIndex, bool rightToLeft, uint language, FeatureSet features, ref uint script)
    {
        int length = text.Length;
        if (length == 0)
        {
            return;
        }

        FontData data = face.Data;
        ShapeScratch scratch = ShapeScratch.Current;

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

            List<ShapedRunGlyph> run = scratch.Run;
            run.Clear();
            TextShaper.Shape(data, cps.AsSpan(0, n), cl.AsSpan(0, n), language, features, run, rightToLeft, ref script);
            AppendClusters(result, scratch, run, baseOffset, length, faceIndex);
        }
        finally
        {
            scratch.Run.Clear();
            ArrayPool<byte>.Shared.Return(starts);
            ArrayPool<int>.Shared.Return(cps);
            ArrayPool<int>.Shared.Return(cl);
        }
    }

    // Groups the shaped glyphs by cluster, puts the clusters in logical order and appends them (and their glyphs) to the result.
    internal static void AppendClusters(ShapedRun result, ShapeScratch scratch, List<ShapedRunGlyph> run, int baseOffset, int length, int faceIndex)
    {
        int count = run.Count;
        int glyphBase = result.GlyphCount;
        int clusterBase = result.ClusterCount;
        if (count == 0)
        {
            // Every character was dropped by the shaper (for example a lone joiner next to an Indic letter): one empty cluster keeps the
            // clusters covering the text.
            result.EnsureClusters(clusterBase + 1);
            result.ClusterArray[clusterBase] = new ShapedClusterRange(baseOffset, length, faceIndex, 0, false, glyphBase, 0);
            result.SetCounts(glyphBase, clusterBase + 1);
            return;
        }

        result.EnsureGlyphs(glyphBase + count);
        ShapedGlyph[] glyphs = result.GlyphArray;
        ShapedGlyphOrigin[] origins = result.OriginArray;
        int m = 0;
        bool sorted = true;
        int lastStart = -1;

        // Consecutive glyphs of one cluster id form a cluster; ranges hold the cluster id (offset in this stretch) in Start for now.
        // At most one cluster per glyph, so room for count more is enough.
        result.EnsureClusters(clusterBase + count);
        ShapedClusterRange[] ranges = result.ClusterArray;
        int g = 0;
        while (g < count)
        {
            int c = run[g].Cluster;
            int e = g;
            int advance = 0;
            bool missing = false;
            while (e < count && run[e].Cluster == c)
            {
                ShapedRunGlyph r = run[e];
                glyphs[glyphBase + e] = new ShapedGlyph(r.GlyphId, r.XAdvance, r.XOffset, r.YOffset);
                origins[glyphBase + e] = new ShapedGlyphOrigin(r.CodePoint, baseOffset + r.Cluster, glyphBase + e);
                advance += r.XAdvance;
                missing |= r.GlyphId == 0 && !IsInvisible(r.CodePoint);
                e++;
            }

            ranges[clusterBase + m++] = new ShapedClusterRange(c, 0, faceIndex, advance, missing, glyphBase + g, e - g);
            sorted &= c >= lastStart;
            lastStart = c;
            g = e;
        }

        if (!sorted)
        {
            SortClusters(result, scratch, clusterBase, m, glyphBase, count);
            ranges = result.ClusterArray;
        }

        // Lengths run to the next cluster's start (the last to the end of the text).
        for (int k = 0; k < m; k++)
        {
            ShapedClusterRange r = ranges[clusterBase + k];
            int end = k + 1 < m ? ranges[clusterBase + k + 1].Start : length;

            // Characters the shaper dropped at the start of the run (a joiner before an Indic letter) belong to the first cluster.
            int first = k == 0 ? 0 : r.Start;
            ranges[clusterBase + k] = r.WithSpan(baseOffset + first, Math.Max(end - first, 1), r.GlyphStart);
        }

        result.SetCounts(glyphBase + count, clusterBase + m);
    }

    // Puts the clusters of the stretch in ascending order of start, keeping the order of the shaper among equal starts, and lays their
    // glyphs out in the same order. Reordering shapers (Indic) are the only ones that get here.
    private static void SortClusters(ShapedRun result, ShapeScratch scratch, int clusterBase, int m, int glyphBase, int glyphCount)
    {
        if (scratch.SortKeys.Length < m)
        {
            scratch.SortKeys = new long[Math.Max(m, 64)];
            scratch.SortItems = new ShapedClusterRange[scratch.SortKeys.Length];
        }

        if (scratch.GlyphCopy.Length < glyphCount)
        {
            scratch.GlyphCopy = new ShapedGlyph[Math.Max(glyphCount, 64)];
            scratch.OriginCopy = new ShapedGlyphOrigin[scratch.GlyphCopy.Length];
        }

        ShapedClusterRange[] ranges = result.ClusterArray;
        ShapedGlyph[] glyphs = result.GlyphArray;
        ShapedGlyphOrigin[] origins = result.OriginArray;
        long[] keys = scratch.SortKeys;
        ShapedClusterRange[] items = scratch.SortItems;
        for (int k = 0; k < m; k++)
        {
            ShapedClusterRange r = ranges[clusterBase + k];
            keys[k] = ((long)r.Start << 32) | (uint)k;
            items[k] = r;
        }

        Array.Sort(keys, items, 0, m);
        Array.Copy(glyphs, glyphBase, scratch.GlyphCopy, 0, glyphCount);
        Array.Copy(origins, glyphBase, scratch.OriginCopy, 0, glyphCount);
        int at = glyphBase;
        for (int k = 0; k < m; k++)
        {
            ShapedClusterRange r = items[k];
            Array.Copy(scratch.GlyphCopy, r.GlyphStart - glyphBase, glyphs, at, r.GlyphCount);
            Array.Copy(scratch.OriginCopy, r.GlyphStart - glyphBase, origins, at, r.GlyphCount);
            ranges[clusterBase + k] = r.WithSpan(r.Start, 0, at);
            at += r.GlyphCount;
        }
    }

    internal static bool IsInvisible(int cp) =>
        cp is >= 0x200B and <= 0x200F or 0x2060 or 0xFEFF or 0x00AD or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or < 0x20;
}
