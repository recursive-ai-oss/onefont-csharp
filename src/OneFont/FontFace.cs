using OneFont.Fonts;

namespace OneFont;

/// <summary>
/// One face of a font file: TrueType (<c>glyf</c>) or OpenType with CFF outlines. A face gives metrics, the character map, advances and
/// outlines, and is what <see cref="TextShaping"/> shapes with. Faces are immutable and safe to use from several threads.
/// </summary>
/// <remarks>
/// A face reads the font's tables lazily from the byte array it was loaded from, so the array must not change while the face is in use.
/// Malformed data raises <see cref="FontFormatException"/> when it is read; layout tables (GSUB, GPOS, GDEF, <c>kern</c>) with an unusable
/// header are ignored and the font then shapes with plain advances. WOFF, WOFF2 and CFF2 fonts are not supported.
/// </remarks>
public sealed class FontFace
{
    private readonly FontData _data;
    private FontMetrics? _metrics;

    private FontFace(FontData data)
    {
        _data = data;
    }

    internal FontData Data => _data;

    /// <summary>Gets how many faces a font file holds: 1 for a plain TrueType or OpenType file, the collection's count for a TrueType Collection.</summary>
    /// <param name="fontData">The bytes of a <c>.ttf</c>, <c>.otf</c> or <c>.ttc</c> file.</param>
    /// <returns>The number of faces, at most 256.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fontData"/> is <see langword="null"/>.</exception>
    /// <exception cref="FontFormatException">The data is too short to be a font or the collection header is invalid.</exception>
    public static int GetFaceCount(byte[] fontData) => FontFile.CountFaces(fontData);

    /// <summary>Loads one face of a font file.</summary>
    /// <param name="fontData">The bytes of a <c>.ttf</c>, <c>.otf</c> or <c>.ttc</c> file. The face keeps a reference to the array.</param>
    /// <param name="faceIndex">The face to load; 0 for a file with one face.</param>
    /// <returns>The face.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fontData"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is not below <see cref="GetFaceCount"/>.</exception>
    /// <exception cref="FontFormatException">The file is malformed, truncated, has no Unicode character map, or uses a feature this library does not read.</exception>
    public static FontFace Load(byte[] fontData, int faceIndex = 0) => new(FontFile.ParseFace(fontData, faceIndex));

    /// <summary>Gets the font family name (the typographic family when the font has one, else the legacy family; <c>Unknown</c> when it has no name).</summary>
    public string FamilyName => _data.FamilyName;

    /// <summary>Gets the subfamily (style) name such as <c>Bold Italic</c>.</summary>
    public string SubfamilyName => _data.SubfamilyName;

    /// <summary>Gets the full font name, or <see langword="null"/> when the font has none.</summary>
    public string? FullName => _data.FullName;

    /// <summary>Gets the PostScript name, or <see langword="null"/> when the font has none.</summary>
    public string? PostScriptName => _data.PostScriptName;

    /// <summary>Gets whether the face is bold (OS/2 <c>fsSelection</c>, <c>head.macStyle</c> or a weight class of 600 or more).</summary>
    public bool IsBold => _data.IsBold;

    /// <summary>Gets whether the face is italic or oblique.</summary>
    public bool IsItalic => _data.IsItalic;

    /// <summary>Gets the weight class (OS/2 <c>usWeightClass</c>, 100 to 900; 400 or 700 when the font has no OS/2 table).</summary>
    public int Weight => _data.WeightClass;

    /// <summary>Gets whether the outlines are CFF (cubic) rather than TrueType (quadratic).</summary>
    public bool IsCff => _data.IsCff;

    /// <summary>Gets the design units per em (16 to 16384); every metric, advance and outline coordinate is in these units.</summary>
    public int UnitsPerEm => _data.UnitsPerEm;

    /// <summary>Gets the number of glyphs the face declares.</summary>
    public int GlyphCount => _data.NumGlyphs;

    /// <summary>Gets the face's line, underline, strikeout and height metrics.</summary>
    public FontMetrics Metrics => _metrics ??= new FontMetrics(_data);

    /// <summary>Gets how far the font recommends raising superscript text above the baseline (OS/2 <c>ySuperscriptYOffset</c>), in font units; 0 without an OS/2 table.</summary>
    public int SuperscriptYOffset => _data.SuperscriptYOffset;

    /// <summary>Gets how far the font recommends lowering subscript text below the baseline (OS/2 <c>ySubscriptYOffset</c>), as a positive number in font units; 0 without an OS/2 table.</summary>
    public int SubscriptYOffset => _data.SubscriptYOffset;

    /// <summary>Gets the glyph id for a character (Unicode <c>cmap</c>); 0, the missing-glyph box, when the font lacks it.</summary>
    /// <param name="codePoint">The Unicode scalar value.</param>
    /// <returns>The glyph id.</returns>
    public int GetGlyphIndex(int codePoint) => _data.GetGlyphIndex(codePoint);

    /// <summary>Gets whether the font has a glyph for a character.</summary>
    /// <param name="codePoint">The Unicode scalar value.</param>
    /// <returns><see langword="true"/> when <see cref="GetGlyphIndex"/> is not 0.</returns>
    public bool HasGlyph(int codePoint) => _data.GetGlyphIndex(codePoint) != 0;

    /// <summary>Gets a glyph's horizontal advance in font units (<c>hmtx</c>); a glyph id out of range gives the last glyph's advance.</summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <returns>The advance.</returns>
    public int GetAdvance(int glyphId) => _data.GetAdvance(glyphId);

    /// <summary>Gets a glyph's left side bearing in font units (<c>hmtx</c>).</summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <returns>The bearing.</returns>
    public int GetLeftSideBearing(int glyphId) => _data.GetLeftSideBearing(glyphId);

    /// <summary>Gets a glyph's unhinted outline in font units, y up. Outlines are cached and shared; an out-of-range id or a glyph without ink gives an empty outline.</summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <returns>The outline.</returns>
    /// <exception cref="FontFormatException">The glyph data is malformed or exceeds the outline limits.</exception>
    public GlyphOutline GetOutline(int glyphId) => _data.GetOutline(glyphId);

    /// <summary>Gets the pair adjustment the font gives two adjacent glyphs (GPOS pair positioning for the Latin script, else the legacy <c>kern</c> table), in font units.</summary>
    /// <param name="leftGlyph">The left glyph id.</param>
    /// <param name="rightGlyph">The right glyph id.</param>
    /// <returns>The adjustment to the left glyph's advance; negative tightens.</returns>
    public int GetKerning(int leftGlyph, int rightGlyph) => _data.GetKerning(leftGlyph, rightGlyph);

    /// <summary>
    /// Gets the raw bytes of one of the font's tables, as stored in the file (for example <c>COLR</c>, <c>CPAL</c>, <c>vhea</c>, <c>vmtx</c>,
    /// <c>VORG</c> or the hinting tables <c>fpgm</c>, <c>prep</c> and <c>cvt </c>). The bytes are not interpreted or validated.
    /// </summary>
    /// <param name="tag">The four-character table tag, such as <c>"vhea"</c>; a tag shorter than four characters is padded with spaces, so <c>"cvt"</c> is <c>"cvt "</c>.</param>
    /// <returns>A view of the table inside the array the face was loaded from, valid as long as that array is unchanged; empty when the font has no such table (see <see cref="HasTable"/>).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is longer than four characters or has a character above U+00FF.</exception>
    public ReadOnlyMemory<byte> GetTable(string tag) => _data.TryGetTable(ParseTag(tag), out ReadOnlyMemory<byte> t) ? t : default;

    /// <summary>Gets whether the font has a table with the given tag (see <see cref="GetTable"/>), even an empty one.</summary>
    /// <param name="tag">The four-character table tag.</param>
    /// <returns><see langword="true"/> when the font directory lists the table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is longer than four characters or has a character above U+00FF.</exception>
    public bool HasTable(string tag) => _data.TryGetTable(ParseTag(tag), out _);

    private static uint ParseTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.Length == 0 || tag.Length > 4)
        {
            throw new ArgumentException("A table tag has one to four characters.", nameof(tag));
        }

        uint v = 0;
        for (int i = 0; i < 4; i++)
        {
            int c = i < tag.Length ? tag[i] : ' ';
            if (c > 0xFF)
            {
                throw new ArgumentException("A table tag has Latin-1 characters only.", nameof(tag));
            }

            v = (v << 8) | (uint)c;
        }

        return v;
    }
}
