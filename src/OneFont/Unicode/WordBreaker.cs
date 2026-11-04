namespace OneFont.Unicode;

/// <summary>
/// Word boundaries per Unicode Standard Annex #29, section 4.1.1 (Unicode 17.0.0), over UTF-16 text.
/// <para>
/// The segmenter walks forward once and keeps a few facts about the segment it is building (the last two significant classes
/// and the length of a regional-indicator run), so finding every boundary in a text takes time linear in its length, even for
/// hostile input such as a million combining marks or flag symbols. Rule WB4 ("ignore Extend, Format and ZWJ") is applied by
/// treating such a character as part of the one before it.
/// </para>
/// </summary>
internal static class WordBreaker
{
    private const WordBreakClass None = (WordBreakClass)0xFF;

    /// <summary>Finds the end of the word segment that starts at <paramref name="start"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="start">A boundary: 0 or a value this method returned earlier.</param>
    /// <returns>The index just past the segment; <c>text.Length</c> for the last one; <paramref name="start"/> itself only when it is at the end.</returns>
    internal static int Next(ReadOnlySpan<char> text, int start)
    {
        int n = text.Length;
        if (start >= n)
        {
            return n;
        }

        int i = start;
        int cp = UnicodeProps.ReadForward(text, i, out int len);
        WordBreakClass cur = (WordBreakClass)(UnicodeProps.WordValue(cp) & UnicodeProps.WordClassMask);
        i += len;

        // WB3 (CR × LF), WB3a (break after Newline, CR, LF).
        if (cur == WordBreakClass.CR)
        {
            return i < n && text[i] == '\n' ? i + 1 : i;
        }

        if (cur is WordBreakClass.LF or WordBreakClass.Newline)
        {
            return i;
        }

        WordBreakClass prev = cur;
        WordBreakClass beforePrev = None;
        WordBreakClass lastRaw = cur;
        int regionalIndicators = cur == WordBreakClass.RegionalIndicator ? 1 : 0;

        while (i < n)
        {
            int next = UnicodeProps.ReadForward(text, i, out int nextLength);
            int entry = UnicodeProps.WordValue(next);
            WordBreakClass np = (WordBreakClass)(entry & UnicodeProps.WordClassMask);

            // WB3b: break before Newline, CR, LF.
            if (np is WordBreakClass.CR or WordBreakClass.LF or WordBreakClass.Newline)
            {
                return i;
            }

            bool ignorable = np is WordBreakClass.Extend or WordBreakClass.Format or WordBreakClass.ZWJ;
            bool joins =
                // WB3c: no break within an emoji ZWJ sequence.
                (lastRaw == WordBreakClass.ZWJ && (entry & UnicodeProps.WordPictographicBit) != 0) ||
                // WB3d: keep horizontal white space together.
                (lastRaw == WordBreakClass.WSegSpace && np == WordBreakClass.WSegSpace) ||
                // WB4: a following Extend, Format or ZWJ belongs to the character before it.
                ignorable ||
                NoBreakBetween(text, i + nextLength, prev, beforePrev, np, regionalIndicators);

            if (!joins)
            {
                return i;
            }

            if (!ignorable)
            {
                beforePrev = prev;
                prev = np;
                regionalIndicators = np == WordBreakClass.RegionalIndicator ? regionalIndicators + 1 : 0;
            }

            lastRaw = np;
            i += nextLength;
        }

        return n;
    }

    // Rules WB5 to WB16 for the pair (prev, np). `after` is the index just past np, where the lookahead rules continue.
    private static bool NoBreakBetween(ReadOnlySpan<char> text, int after, WordBreakClass prev, WordBreakClass beforePrev, WordBreakClass np, int regionalIndicators)
    {
        bool prevLetter = prev is WordBreakClass.ALetter or WordBreakClass.HebrewLetter;
        bool npLetter = np is WordBreakClass.ALetter or WordBreakClass.HebrewLetter;

        // WB5: AHLetter × AHLetter.
        if (prevLetter && npLetter)
        {
            return true;
        }

        // WB8, WB9, WB10: digits join digits and letters.
        if ((prev == WordBreakClass.Numeric && (np == WordBreakClass.Numeric || npLetter)) || (prevLetter && np == WordBreakClass.Numeric))
        {
            return true;
        }

        // WB13: Katakana × Katakana.
        if (prev == WordBreakClass.Katakana && np == WordBreakClass.Katakana)
        {
            return true;
        }

        // WB13a, WB13b: ExtendNumLet (underscore and friends) joins letters, digits and Katakana on both sides.
        if (np == WordBreakClass.ExtendNumLet && (prevLetter || prev is WordBreakClass.Numeric or WordBreakClass.Katakana or WordBreakClass.ExtendNumLet))
        {
            return true;
        }

        if (prev == WordBreakClass.ExtendNumLet && (npLetter || np is WordBreakClass.Numeric or WordBreakClass.Katakana))
        {
            return true;
        }

        // WB15, WB16: regional indicators pair up; a break falls after every second one.
        if (prev == WordBreakClass.RegionalIndicator && np == WordBreakClass.RegionalIndicator)
        {
            return (regionalIndicators & 1) == 1;
        }

        // WB7a: Hebrew_Letter × Single_Quote.
        if (prev == WordBreakClass.HebrewLetter && np == WordBreakClass.SingleQuote)
        {
            return true;
        }

        // The remaining rules look at three characters: one before, one after, or both.
        bool npMidLetterQ = np is WordBreakClass.MidLetter or WordBreakClass.MidNumLet or WordBreakClass.SingleQuote;
        bool prevMidLetterQ = prev is WordBreakClass.MidLetter or WordBreakClass.MidNumLet or WordBreakClass.SingleQuote;
        bool npMidNumQ = np is WordBreakClass.MidNum or WordBreakClass.MidNumLet or WordBreakClass.SingleQuote;
        bool prevMidNumQ = prev is WordBreakClass.MidNum or WordBreakClass.MidNumLet or WordBreakClass.SingleQuote;
        bool beforeLetter = beforePrev is WordBreakClass.ALetter or WordBreakClass.HebrewLetter;

        // WB7: AHLetter (MidLetter | MidNumLetQ) × AHLetter.
        if (beforeLetter && prevMidLetterQ && npLetter)
        {
            return true;
        }

        // WB7c: Hebrew_Letter Double_Quote × Hebrew_Letter.
        if (beforePrev == WordBreakClass.HebrewLetter && prev == WordBreakClass.DoubleQuote && np == WordBreakClass.HebrewLetter)
        {
            return true;
        }

        // WB11: Numeric (MidNum | MidNumLetQ) × Numeric.
        if (beforePrev == WordBreakClass.Numeric && prevMidNumQ && np == WordBreakClass.Numeric)
        {
            return true;
        }

        // WB6: AHLetter × (MidLetter | MidNumLetQ) AHLetter.
        if (prevLetter && npMidLetterQ)
        {
            WordBreakClass ahead = ClassAfter(text, after);
            if (ahead is WordBreakClass.ALetter or WordBreakClass.HebrewLetter)
            {
                return true;
            }
        }

        // WB7b: Hebrew_Letter × Double_Quote Hebrew_Letter.
        if (prev == WordBreakClass.HebrewLetter && np == WordBreakClass.DoubleQuote && ClassAfter(text, after) == WordBreakClass.HebrewLetter)
        {
            return true;
        }

        // WB12: Numeric × (MidNum | MidNumLetQ) Numeric.
        if (prev == WordBreakClass.Numeric && npMidNumQ && ClassAfter(text, after) == WordBreakClass.Numeric)
        {
            return true;
        }

        return false; // WB999
    }

    // The class of the first character at or after `index` that rule WB4 does not ignore.
    private static WordBreakClass ClassAfter(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length)
        {
            int cp = UnicodeProps.ReadForward(text, index, out int len);
            WordBreakClass p = (WordBreakClass)(UnicodeProps.WordValue(cp) & UnicodeProps.WordClassMask);
            if (p is not (WordBreakClass.Extend or WordBreakClass.Format or WordBreakClass.ZWJ))
            {
                return p;
            }

            index += len;
        }

        return None;
    }
}
