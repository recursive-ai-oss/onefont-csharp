namespace OneFont;

/// <summary>One glyph of a shaped cluster. Advances and offsets are in font design units (see <see cref="FontFace.UnitsPerEm"/>).</summary>
/// <param name="GlyphId">The glyph id in the face that shaped the run.</param>
/// <param name="XAdvance">The horizontal advance, including pair kerning and other GPOS adjustments.</param>
/// <param name="XOffset">How far the glyph is drawn right of the pen position (marks, cursive attachment).</param>
/// <param name="YOffset">How far the glyph is drawn above the baseline.</param>
public readonly record struct ShapedGlyph(int GlyphId, int XAdvance, int XOffset, int YOffset);

/// <summary>
/// One shaped cluster of a run: the glyphs that stand for one or more characters and cannot be positioned apart (a letter with its
/// combining marks, a ligature, a syllable). A caller that lays text out by itself places each cluster separately; the clusters of a run
/// come in logical order and cover the text without gaps.
/// </summary>
public sealed class ShapedCluster
{
    private readonly ShapedGlyph[] _glyphs;

    internal ShapedCluster(int start, int length, int faceIndex, int advance, bool missing, ShapedGlyph[] glyphs)
    {
        Start = start;
        Length = length;
        FaceIndex = faceIndex;
        Advance = advance;
        HasMissingGlyph = missing;
        _glyphs = glyphs;
    }

    /// <summary>Gets the offset of the cluster's first character in the shaped text, in UTF-16 code units.</summary>
    public int Start { get; }

    /// <summary>Gets how many UTF-16 code units of the text the cluster covers (at least 1).</summary>
    public int Length { get; }

    /// <summary>Gets the index in the <see cref="FontFallbackChain"/> of the face the cluster was shaped with; always 0 for a run shaped with one face.</summary>
    public int FaceIndex { get; }

    /// <summary>
    /// Gets the horizontal distance from the cluster's pen position to the next one, in font units: the sum of the glyphs' advances. It
    /// includes pair kerning with the cluster that follows in logical order.
    /// </summary>
    public int Advance { get; }

    /// <summary>Gets whether any glyph of the cluster is the face's missing-glyph box because the face lacks the character.</summary>
    public bool HasMissingGlyph { get; }

    /// <summary>Gets the cluster's glyphs in drawing order. Each glyph's offsets are relative to the pen position after the glyphs before it.</summary>
    public ReadOnlySpan<ShapedGlyph> Glyphs => _glyphs;
}
