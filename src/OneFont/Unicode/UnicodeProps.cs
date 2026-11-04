using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace OneFont.Unicode;

/// <summary>Lookups into the generated property tables.</summary>
internal static class UnicodeProps
{
    // The tables cover U+0000..U+3FFFF and the U+E0000 block; every other code point has the default value.
    private const int LowBlocks = 0x40000 >> UnicodeData.BlockShift;
    private const int BlockMask = (1 << UnicodeData.BlockShift) - 1;

    /// <summary>Gets the packed line break value of a code point (see the generator for the bit layout).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LineBreakValue(int cp)
    {
        if ((uint)cp < 0x40000)
        {
            return U16(UnicodeData.LineBreakBlocks, (U16(UnicodeData.LineBreakIndex, cp >> UnicodeData.BlockShift) << UnicodeData.BlockShift) | (cp & BlockMask));
        }

        if ((uint)(cp - 0xE0000) < 0x1000)
        {
            int b = U16(UnicodeData.LineBreakIndex, LowBlocks + ((cp - 0xE0000) >> UnicodeData.BlockShift));
            return U16(UnicodeData.LineBreakBlocks, (b << UnicodeData.BlockShift) | (cp & BlockMask));
        }

        return UnicodeData.LineBreakDefault;
    }

    /// <summary>Gets the packed grapheme value of a code point (see the generator for the bit layout).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GraphemeValue(int cp)
    {
        if ((uint)cp < 0x40000)
        {
            return UnicodeData.GraphemeBlocks[(U16(UnicodeData.GraphemeIndex, cp >> UnicodeData.BlockShift) << UnicodeData.BlockShift) | (cp & BlockMask)];
        }

        if ((uint)(cp - 0xE0000) < 0x1000)
        {
            int b = U16(UnicodeData.GraphemeIndex, LowBlocks + ((cp - 0xE0000) >> UnicodeData.BlockShift));
            return UnicodeData.GraphemeBlocks[(b << UnicodeData.BlockShift) | (cp & BlockMask)];
        }

        return UnicodeData.GraphemeDefault;
    }

    /// <summary>Mask of the Word_Break class in a word value.</summary>
    internal const int WordClassMask = 0x1F;

    /// <summary>Set in a word value for Extended_Pictographic (rule WB3c).</summary>
    internal const int WordPictographicBit = 0x80;

    /// <summary>Gets the packed word value of a code point: Word_Break class in the low five bits, Extended_Pictographic in bit 7.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WordValue(int cp)
    {
        if ((uint)cp < 0x40000)
        {
            return UnicodeData.WordBlocks[(U16(UnicodeData.WordIndex, cp >> UnicodeData.BlockShift) << UnicodeData.BlockShift) | (cp & BlockMask)];
        }

        if ((uint)(cp - 0xE0000) < 0x1000)
        {
            int b = U16(UnicodeData.WordIndex, LowBlocks + ((cp - 0xE0000) >> UnicodeData.BlockShift));
            return UnicodeData.WordBlocks[(b << UnicodeData.BlockShift) | (cp & BlockMask)];
        }

        return 0;
    }

    /// <summary>Gets the Sentence_Break class of a code point.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SentenceBreakClass SentenceClass(int cp)
    {
        if ((uint)cp < 0x40000)
        {
            return (SentenceBreakClass)UnicodeData.SentenceBlocks[(U16(UnicodeData.SentenceIndex, cp >> UnicodeData.BlockShift) << UnicodeData.BlockShift) | (cp & BlockMask)];
        }

        if ((uint)(cp - 0xE0000) < 0x1000)
        {
            int b = U16(UnicodeData.SentenceIndex, LowBlocks + ((cp - 0xE0000) >> UnicodeData.BlockShift));
            return (SentenceBreakClass)UnicodeData.SentenceBlocks[(b << UnicodeData.BlockShift) | (cp & BlockMask)];
        }

        return SentenceBreakClass.Other;
    }

    /// <summary>Reads the code point at <paramref name="index"/>; an unpaired surrogate is returned as its own value (it has no properties).</summary>
    /// <param name="text">The text.</param>
    /// <param name="index">A valid index into <paramref name="text"/>.</param>
    /// <param name="length">Receives 1 or 2, the UTF-16 length of the code point.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadForward(ReadOnlySpan<char> text, int index, out int length)
    {
        char c = text[index];
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(c, text[index + 1]);
        }

        length = 1;
        return c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int U16(ReadOnlySpan<byte> table, int index) => BinaryPrimitives.ReadUInt16LittleEndian(table.Slice(index * 2));
}
