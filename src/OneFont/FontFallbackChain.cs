using OneFont.Shaping;
using OneFont.Unicode;

namespace OneFont;

/// <summary>
/// An ordered list of faces that cover each other's gaps. Shaping a run through the chain picks, for every grapheme cluster, the first face
/// that has a glyph for the cluster's first character, and shapes each stretch of one face with that face's own tables; a character no
/// face covers stays with the first face, which draws its missing-glyph box.
/// </summary>
public sealed class FontFallbackChain
{
    /// <summary>The most faces a chain holds.</summary>
    public const int MaxFaces = 32;

    private readonly FontFace[] _faces;

    /// <summary>Creates a chain.</summary>
    /// <param name="faces">The faces in order of preference; at least one and at most <see cref="MaxFaces"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="faces"/> or one of its entries is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The list is empty or longer than <see cref="MaxFaces"/>.</exception>
    public FontFallbackChain(IEnumerable<FontFace> faces)
    {
        ArgumentNullException.ThrowIfNull(faces);
        _faces = faces.ToArray();
        if (_faces.Length is 0 or > MaxFaces)
        {
            throw new ArgumentException($"A chain holds 1 to {MaxFaces} faces.", nameof(faces));
        }

        foreach (FontFace f in _faces)
        {
            ArgumentNullException.ThrowIfNull(f, nameof(faces));
        }
    }

    /// <summary>Gets the faces in order of preference. <see cref="ShapedCluster.FaceIndex"/> indexes this list.</summary>
    public IReadOnlyList<FontFace> Faces => _faces;

    /// <summary>Gets the index of the first face with a glyph for a character, or 0 when none has one.</summary>
    /// <param name="codePoint">The Unicode scalar value.</param>
    /// <returns>The face index.</returns>
    public int SelectFace(int codePoint)
    {
        for (int k = 0; k < _faces.Length; k++)
        {
            if (_faces[k].Data.GetGlyphIndex(codePoint) != 0)
            {
                return k;
            }
        }

        return 0;
    }

    /// <summary>
    /// Shapes a run through the chain, in one direction, into clusters in logical order. Invisible characters (controls, zero width
    /// spaces and joiners, soft hyphens, bidi controls) take the face of the cluster before them. Each stretch of one face is shaped
    /// separately, so a cluster never spans two faces.
    /// </summary>
    /// <param name="text">The run. Lone surrogates are shaped as U+FFFD.</param>
    /// <param name="rightToLeft">Whether the run is right to left.</param>
    /// <param name="options">Language, kerning and features; the defaults when <see langword="null"/>. Advances are in each cluster's own face's units: scale by <c>size / Faces[cluster.FaceIndex].UnitsPerEm</c>.</param>
    /// <returns>The clusters in logical order, covering the text without gaps.</returns>
    /// <exception cref="ArgumentException">The text is longer than <see cref="TextShaping.MaxRunLength"/>.</exception>
    /// <exception cref="FontFormatException">A font's data is malformed.</exception>
    public IReadOnlyList<ShapedCluster> ShapeRun(ReadOnlySpan<char> text, bool rightToLeft = false, ShapingOptions? options = null)
    {
        if (text.Length > TextShaping.MaxRunLength)
        {
            throw new ArgumentException("The text is too long.", nameof(text));
        }

        var clusters = new List<ShapedCluster>();
        uint language = Languages.ToTag(options?.Language);
        FeatureSet features = TextShaping.FeaturesOf(options);
        uint script = 0;
        int segmentStart = 0;
        int segmentFace = 0;
        int i = 0;
        while (i < text.Length)
        {
            int len = Graphemes.NextLength(text[i..]);
            int cp = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                ? char.ConvertToUtf32(text[i], text[i + 1])
                : (char.IsSurrogate(text[i]) ? 0xFFFD : text[i]);
            int face = TextShaping.IsInvisible(cp) ? segmentFace : SelectFace(cp);
            if (face != segmentFace && i > segmentStart)
            {
                TextShaping.ShapeInto(clusters, text[segmentStart..i], segmentStart, _faces[segmentFace], segmentFace, rightToLeft, language, features, ref script);
                segmentStart = i;
            }

            segmentFace = face;
            i += len;
        }

        TextShaping.ShapeInto(clusters, text[segmentStart..], segmentStart, _faces[segmentFace], segmentFace, rightToLeft, language, features, ref script);
        return clusters;
    }
}
