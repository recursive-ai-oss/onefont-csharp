namespace OneFont.Unicode;

/// <summary>
/// Sentence boundaries per Unicode Standard Annex #29, section 5.1.1 (Unicode 17.0.0), over UTF-16 text.
/// <para>
/// The segmenter keeps a small state machine for the pattern <c>SATerm Close* Sp* ParaSep?</c> that rules SB8a to SB11 describe,
/// so text is read once. Rule SB8 needs to look ahead for a lower-case letter; that look-ahead stops at the next sentence
/// terminator, so the total work stays linear.
/// </para>
/// </summary>
internal static class SentenceBreaker
{
    /// <summary>Finds the end of the sentence that starts at <paramref name="start"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="start">A boundary: 0 or a value this method returned earlier.</param>
    /// <returns>The index just past the sentence; <c>text.Length</c> for the last one.</returns>
    internal static int Next(ReadOnlySpan<char> text, int start)
    {
        int n = text.Length;
        if (start >= n)
        {
            return n;
        }

        int i = start;
        int cp = UnicodeProps.ReadForward(text, i, out int len);
        SentenceBreakClass cur = UnicodeProps.SentenceClass(cp);
        i += len;

        // SB3 (CR × LF) and SB4 (break after a paragraph separator).
        if (cur == SentenceBreakClass.CR)
        {
            return i < n && text[i] == '\n' ? i + 1 : i;
        }

        if (cur is SentenceBreakClass.LF or SentenceBreakClass.Sep)
        {
            return i;
        }

        // phase: 0 outside the terminator pattern; 1 right after SATerm; 2 after SATerm Close+; 3 after SATerm Close* Sp+.
        int phase = 0;
        bool termIsATerm = false;
        bool termFollowsLetter = false; // SB7: the character before the ATerm is Upper or Lower
        SentenceBreakClass lastSignificant = cur;
        if (cur is SentenceBreakClass.ATerm or SentenceBreakClass.STerm)
        {
            phase = 1;
            termIsATerm = cur == SentenceBreakClass.ATerm;
        }

        while (i < n)
        {
            int next = UnicodeProps.ReadForward(text, i, out int nextLength);
            SentenceBreakClass np = UnicodeProps.SentenceClass(next);

            // SB5: Extend and Format characters belong to the character before them.
            if (np is SentenceBreakClass.Extend or SentenceBreakClass.Format)
            {
                i += nextLength;
                continue;
            }

            bool paraSep = np is SentenceBreakClass.CR or SentenceBreakClass.LF or SentenceBreakClass.Sep;
            if (phase != 0)
            {
                bool noBreak = false;
                if (phase == 1 && termIsATerm && np == SentenceBreakClass.Numeric)
                {
                    noBreak = true; // SB6
                }
                else if (phase == 1 && termIsATerm && np == SentenceBreakClass.Upper && termFollowsLetter)
                {
                    noBreak = true; // SB7
                }
                else if (paraSep || np == SentenceBreakClass.Sp || (np == SentenceBreakClass.Close && phase <= 2))
                {
                    // SB9 and SB10. Rule SB8 would also forbid the break here, so the look-ahead is not needed.
                    noBreak = true;
                }

                if (!noBreak && termIsATerm)
                {
                    // SB8: ATerm Close* Sp* × (¬(OLetter | Upper | Lower | ParaSep | SATerm))* Lower
                    int afterLower = FindLowerAfterNonLetters(text, i);
                    if (afterLower >= 0)
                    {
                        i = afterLower;
                        lastSignificant = SentenceBreakClass.Lower;
                        phase = 0;
                        continue;
                    }
                }

                if (!noBreak && np is SentenceBreakClass.SContinue or SentenceBreakClass.ATerm or SentenceBreakClass.STerm)
                {
                    noBreak = true; // SB8a
                }

                if (!noBreak)
                {
                    return i; // SB11
                }
            }

            i += nextLength;
            if (paraSep)
            {
                // SB3 and SB4: the separator ends the sentence, a CR takes a following LF with it.
                return np == SentenceBreakClass.CR && i < n && text[i] == '\n' ? i + 1 : i;
            }

            if (np is SentenceBreakClass.ATerm or SentenceBreakClass.STerm)
            {
                phase = 1;
                termIsATerm = np == SentenceBreakClass.ATerm;
                termFollowsLetter = lastSignificant is SentenceBreakClass.Upper or SentenceBreakClass.Lower;
            }
            else if (phase != 0 && np == SentenceBreakClass.Close && phase <= 2)
            {
                phase = 2;
            }
            else if (phase != 0 && np == SentenceBreakClass.Sp)
            {
                phase = 3;
            }
            else
            {
                phase = 0;
            }

            lastSignificant = np;
        }

        return n;
    }

    // Skips characters outside OLetter, Upper, Lower, ParaSep and SATerm; returns the index past the Lower that ends the run, or -1.
    private static int FindLowerAfterNonLetters(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length)
        {
            int cp = UnicodeProps.ReadForward(text, index, out int len);
            SentenceBreakClass p = UnicodeProps.SentenceClass(cp);
            if (p == SentenceBreakClass.Lower)
            {
                return index + len;
            }

            if (p is SentenceBreakClass.OLetter or SentenceBreakClass.Upper or SentenceBreakClass.CR or SentenceBreakClass.LF or SentenceBreakClass.Sep or SentenceBreakClass.ATerm or SentenceBreakClass.STerm)
            {
                return -1;
            }

            index += len;
        }

        return -1;
    }
}
