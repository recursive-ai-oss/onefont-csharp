namespace OneFont.Fonts;

/// <summary>
/// Reads the container around the tables: a plain sfnt (TrueType or OpenType), or a TrueType Collection. 
/// The result is one table map per face; nothing here interprets the tables.
/// </summary>
internal static class FontFile
{
    public const int MaxFacesPerCollection = 256;
    public const int MaxTablesPerFace = 256;
    public const int MaxTableBytes = 128 << 20;

    private const uint TagTtcf = 0x74746366;
    private const uint TagOtto = 0x4F54544F;
    private const uint TagTrue = 0x74727565;
    private const uint TagTyp1 = 0x74797031;

    /// <summary>Gives the number of faces in the file: 1 for a plain sfnt, the header count for a collection.</summary>
    public static int CountFaces(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 12)
        {
            throw new FontFormatException("The data is too short to be a font.");
        }

        if (Be.U32(data, 0) != TagTtcf)
        {
            return 1;
        }

        // TTC header: tag, version, numFonts, offsets[numFonts].
        uint count = Be.U32(data, 8);
        if (count == 0 || count > MaxFacesPerCollection || 12L + (4L * count) > data.Length)
        {
            throw new FontFormatException("The font collection header is invalid.");
        }

        return (int)count;
    }

    /// <summary>Parses one face of the file; <paramref name="index"/> must be below <see cref="CountFaces"/>.</summary>
    public static FontData ParseFace(byte[] data, int index)
    {
        int count = CountFaces(data);
        if ((uint)index >= (uint)count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "The face index is outside the font file.");
        }

        if (Be.U32(data, 0) != TagTtcf)
        {
            return FontData.Create(ReadSfntDirectory(data, 0));
        }

        return FontData.Create(ReadSfntDirectory(data, Be.U32(data, 12 + (4 * index))));
    }

    /// <summary>Parses every face in the file.</summary>
    public static List<FontData> Parse(byte[] data)
    {
        int count = CountFaces(data);
        var faces = new List<FontData>(count);
        for (int i = 0; i < count; i++)
        {
            faces.Add(ParseFace(data, i));
        }

        return faces;
    }

    private static Dictionary<uint, ReadOnlyMemory<byte>> ReadSfntDirectory(byte[] data, uint offset)
    {
        if (offset > data.Length - 12L)
        {
            throw Be.Truncated();
        }

        int o = (int)offset;
        uint version = Be.U32(data, o);
        if (version != 0x00010000 && version != TagOtto && version != TagTrue && version != TagTyp1)
        {
            throw new FontFormatException("Not an sfnt font: unknown version tag.");
        }

        int numTables = Be.U16(data, o + 4);
        if (numTables == 0 || numTables > MaxTablesPerFace || o + 12L + (16L * numTables) > data.Length)
        {
            throw new FontFormatException("The table directory is invalid.");
        }

        var tables = new Dictionary<uint, ReadOnlyMemory<byte>>(numTables);
        for (int i = 0; i < numTables; i++)
        {
            int e = o + 12 + (16 * i);
            uint tag = Be.U32(data, e);
            uint tOff = Be.U32(data, e + 8);
            uint tLen = Be.U32(data, e + 12);
            if (tOff > data.Length || tLen > data.Length - tOff)
            {
                // A table running past the end of the file: skip it rather than fail the whole font; a required one is reported later.
                continue;
            }

            tables.TryAdd(tag, new ReadOnlyMemory<byte>(data, (int)tOff, (int)tLen));
        }

        return tables;
    }
}
