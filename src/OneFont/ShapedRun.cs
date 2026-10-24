namespace OneFont;

/// <summary>
/// Where one shaped cluster sits in a <see cref="ShapedRun"/>: the characters it covers, the face that shaped it, its total advance and
/// the range of glyphs that draw it. It is the value-type form of <see cref="ShapedCluster"/>.
/// </summary>
public readonly struct ShapedClusterRange
{
    internal ShapedClusterRange(int start, int length, int faceIndex, int advance, bool missing, int firstGlyph, int glyphCount)
    {
        Start = start;
        Length = length;
        FaceIndex = faceIndex;
        Advance = advance;
        HasMissingGlyph = missing;
        GlyphStart = firstGlyph;
        GlyphCount = glyphCount;
    }

    /// <summary>Gets the offset of the cluster's first character in the shaped text, in UTF-16 code units.</summary>
    public int Start { get; }

    /// <summary>Gets how many UTF-16 code units of the text the cluster covers (at least 1).</summary>
    public int Length { get; }

    /// <summary>Gets the index in the <see cref="FontFallbackChain"/> of the face the cluster was shaped with; always 0 for a run shaped with one face.</summary>
    public int FaceIndex { get; }

    /// <summary>Gets the horizontal distance from the cluster's pen position to the next one, in font units, including pair kerning with the cluster that follows in logical order.</summary>
    public int Advance { get; }

    /// <summary>Gets whether any glyph of the cluster is the face's missing-glyph box because the face lacks the character.</summary>
    public bool HasMissingGlyph { get; }

    /// <summary>Gets the index in <see cref="ShapedRun.Glyphs"/> of the cluster's first glyph.</summary>
    public int GlyphStart { get; }

    /// <summary>Gets how many glyphs the cluster has; 0 only when the shaper dropped every character of the run (the run is then this one cluster).</summary>
    public int GlyphCount { get; }

    internal ShapedClusterRange WithSpan(int start, int length, int firstGlyph) =>
        new(start, length, FaceIndex, Advance, HasMissingGlyph, firstGlyph, GlyphCount);
}

/// <summary>
/// The result of shaping a run, kept in two flat arrays so a caller that shapes many runs allocates nothing per run: the glyphs of all
/// clusters in logical order and one <see cref="ShapedClusterRange"/> per cluster. Create one, pass it to
/// <see cref="TextShaping.ShapeRun(ReadOnlySpan{char}, FontFace, ShapedRun, bool, ShapingOptions?)"/> again and again, and read the spans
/// before the next call. The arrays grow as needed and are kept.
/// </summary>
/// <remarks>
/// The values are exactly those the <see cref="ShapedCluster"/> list of the same call has: cluster <c>i</c> has the same start, length,
/// face, advance and glyphs. A <see cref="ShapedRun"/> is not thread-safe; use one per thread.
/// </remarks>
public sealed class ShapedRun
{
    private ShapedGlyph[] _glyphs = [];
    private ShapedClusterRange[] _clusters = [];
    private int _glyphCount;
    private int _clusterCount;

    /// <summary>Gets the glyphs of all clusters, cluster after cluster in logical order, each cluster's glyphs in drawing order.</summary>
    public ReadOnlySpan<ShapedGlyph> Glyphs => new(_glyphs, 0, _glyphCount);

    /// <summary>Gets the clusters in logical order; they start at the run's first character and follow each other to its end without gaps.</summary>
    public ReadOnlySpan<ShapedClusterRange> Clusters => new(_clusters, 0, _clusterCount);

    /// <summary>Gets the number of glyphs.</summary>
    public int GlyphCount => _glyphCount;

    /// <summary>Gets the number of clusters.</summary>
    public int ClusterCount => _clusterCount;

    /// <summary>Gets the glyphs of one cluster of this run.</summary>
    /// <param name="cluster">A cluster from <see cref="Clusters"/>.</param>
    /// <returns>The cluster's glyphs; empty for a cluster that is not from this run's current contents.</returns>
    public ReadOnlySpan<ShapedGlyph> GlyphsOf(in ShapedClusterRange cluster) =>
        cluster.GlyphStart >= 0 && cluster.GlyphCount >= 0 && cluster.GlyphStart <= _glyphCount && cluster.GlyphCount <= _glyphCount - cluster.GlyphStart
            ? new ReadOnlySpan<ShapedGlyph>(_glyphs, cluster.GlyphStart, cluster.GlyphCount)
            : default;

    /// <summary>Empties the run, keeping its arrays.</summary>
    public void Clear()
    {
        _glyphCount = 0;
        _clusterCount = 0;
    }

    // Arrays above this size are dropped by Trim so one huge run does not pin memory for the life of a reused object.
    internal const int RetainedGlyphs = 1 << 16;

    internal ShapedGlyph[] GlyphArray => _glyphs;

    internal ShapedClusterRange[] ClusterArray => _clusters;

    internal void SetCounts(int glyphs, int clusters)
    {
        _glyphCount = glyphs;
        _clusterCount = clusters;
    }

    internal void EnsureGlyphs(int total)
    {
        if (total > _glyphs.Length)
        {
            Array.Resize(ref _glyphs, Math.Max(total, Math.Min(Math.Max(_glyphs.Length * 2, 16), int.MaxValue / 2)));
        }
    }

    internal void EnsureClusters(int total)
    {
        if (total > _clusters.Length)
        {
            Array.Resize(ref _clusters, Math.Max(total, Math.Min(Math.Max(_clusters.Length * 2, 16), int.MaxValue / 2)));
        }
    }

    internal void Trim()
    {
        if (_glyphs.Length > RetainedGlyphs)
        {
            _glyphs = [];
        }

        if (_clusters.Length > RetainedGlyphs)
        {
            _clusters = [];
        }
    }
}
