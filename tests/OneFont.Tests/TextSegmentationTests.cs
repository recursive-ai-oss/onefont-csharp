namespace OneFont.Tests;

/// <summary>Line break opportunities and grapheme boundaries through the public API. The conformance files are in <see cref="UnicodeConformanceTests"/>.</summary>
public class TextSegmentationTests
{
    // The pieces a line can be made of when every opportunity is taken.
    private static string[] Segments(string text)
    {
        LineBreakOpportunity[] b = TextSegmentation.GetLineBreakOpportunities(text);
        var parts = new List<string>();
        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (b[i] != LineBreakOpportunity.None && i > start)
            {
                parts.Add(text[start..i]);
                start = i;
            }
        }

        return [.. parts];
    }

    [Fact]
    public void SpacesAndHyphensBreak()
    {
        Assert.Equal(["well-", "known ", "words"], Segments("well-known words"));
        Assert.Equal(["a ", "b ", "c"], Segments("a b c"));
    }

    [Fact]
    public void SoftHyphenAndZeroWidthSpaceAreOpportunities()
    {
        Assert.Equal(["inter\u00AD", "national"], Segments("inter\u00ADnational"));
        Assert.Equal(["abc\u200B", "def"], Segments("abc\u200Bdef"));
    }

    [Fact]
    public void NoBreakSpaceGluesWordsTogether()
    {
        Assert.Equal(["Dr.\u00A0Joseph ", "Becker"], Segments("Dr.\u00A0Joseph Becker"));
        Assert.Equal(["word\u2060joiner ", "next"], Segments("word\u2060joiner next"));
    }

    [Fact]
    public void QuotationMarksFollowTheCurrentLineBreakingRules()
    {
        // LB15a: no break after an opening quotation mark, even through the spaces that follow it.
        Assert.Equal(["word ", "\u201C quoted ", "text"], Segments("word \u201C quoted text"));

        // LB15b: no break before a closing quotation mark that is followed by a space.
        Assert.Equal(["said ", "hello \u201D ", "and"], Segments("said hello \u201D and"));

        // Brackets: none after an opening one, none before a closing one, however many spaces are between.
        Assert.Equal(["call ", "( spaced ) ", "now"], Segments("call ( spaced ) now"));
    }

    [Fact]
    public void NumbersAndPunctuationStayTogether()
    {
        Assert.Equal(["Total ", "$1,234.56 ", "(approx.) ", "-5.25 ", "12.5% ", "1/2"], Segments("Total $1,234.56 (approx.) -5.25 12.5% 1/2"));
        Assert.Equal(["Wait... ", "what? ", "Yes! ", "No:x"], Segments("Wait... what? Yes! No:x"));
    }

    [Fact]
    public void EastAsianTextBreaksBetweenCharactersButNotBeforeClosingPunctuation()
    {
        Assert.Equal(["\u65E5", "\u672C", "\u8A9E\u3002", "\u4E8C", "\u4E09\u3001", "\u4E00\u3041", "\u3042", "\u300C\u65E5", "\u672C", "\u8A9E\u300D"],
            Segments("\u65E5\u672C\u8A9E\u3002\u4E8C\u4E09\u3001\u4E00\u3041\u3042\u300C\u65E5\u672C\u8A9E\u300D"));
    }

    [Fact]
    public void HardBreaksAreMandatoryAndTheEndIsAnOpportunity()
    {
        LineBreakOpportunity[] b = TextSegmentation.GetLineBreakOpportunities("ab\ncd");
        Assert.Equal(6, b.Length);
        Assert.Equal(LineBreakOpportunity.None, b[0]);
        Assert.Equal(LineBreakOpportunity.Mandatory, b[3]);
        Assert.Equal(LineBreakOpportunity.Allowed, b[5]);
        Assert.Equal(LineBreakOpportunity.Mandatory, TextSegmentation.GetLineBreakOpportunities("ab\n")[3]);
        Assert.Equal([LineBreakOpportunity.Allowed], TextSegmentation.GetLineBreakOpportunities(""));
    }

    [Fact]
    public void GraphemeBoundariesKeepMarksEmojiAndHangulTogether()
    {
        Assert.Equal([0, 2, 3, 4], TextSegmentation.GetGraphemeBoundaries("e\u0301ab"));
        Assert.Equal([0, 5, 6], TextSegmentation.GetGraphemeBoundaries("\U0001F468\u200D\U0001F469a"));
        Assert.Equal([0, 4, 6], TextSegmentation.GetGraphemeBoundaries("\U0001F1E9\U0001F1EA\U0001F1EB\U0001F1F7"[..4] + "\u1100\u1161"));
        Assert.Equal([0, 2], TextSegmentation.GetGraphemeBoundaries("\r\n"));
        Assert.Equal([0], TextSegmentation.GetGraphemeBoundaries(""));
        Assert.Equal(2, TextSegmentation.GetGraphemeLength("e\u0301x"));
        Assert.Equal(1, TextSegmentation.GetGraphemeLength("\uD800"));
        Assert.Equal(0, TextSegmentation.GetGraphemeLength(""));
    }

    [Fact]
    public void WordBoundariesJoinContractionsAndNumbersAndSplitSpaces()
    {
        Assert.Equal([0, 5, 6, 8], TextSegmentation.GetWordBoundaries("can't go"));
        Assert.Equal([0, 4, 5, 6, 7, 8], TextSegmentation.GetWordBoundaries("3.14 x,y"));
        Assert.Equal([0], TextSegmentation.GetWordBoundaries(""));
        Assert.Equal([0, 1, 2], TextSegmentation.GetWordBoundaries("\uD800a"));
        Assert.Equal([0, 2], TextSegmentation.GetWordBoundaries("\U0001F600"));
    }

    [Fact]
    public void SentenceBoundariesFollowTerminatorsAndKeepTrailingSpace()
    {
        Assert.Equal([0, 4, 7], TextSegmentation.GetSentenceBoundaries("Hi. Yo!"));
        Assert.Equal([0, 12], TextSegmentation.GetSentenceBoundaries("It is 3.5 ok"));
        Assert.Equal([0], TextSegmentation.GetSentenceBoundaries(""));
    }

    [Fact]
    public void WordAndSentenceFillAndStepMatchTheArrayFormAndValidateArguments()
    {
        const string text = "One two. Three, 4.5 four!";
        byte[] starts = new byte[text.Length + 1];
        TextSegmentation.FillWordStarts(text, starts);
        Assert.Equal(TextSegmentation.GetWordBoundaries(text), Enumerable.Range(0, starts.Length).Where(i => starts[i] == 1));
        TextSegmentation.FillSentenceStarts(text, starts);
        Assert.Equal(TextSegmentation.GetSentenceBoundaries(text), Enumerable.Range(0, starts.Length).Where(i => starts[i] == 1));
        Assert.Equal(text.Length, TextSegmentation.GetNextWordBoundary(text, text.Length));
        Assert.Equal(3, TextSegmentation.GetNextWordBoundary(text, 0));
        Assert.Equal(9, TextSegmentation.GetNextSentenceBoundary(text, 0));
        Assert.Throws<ArgumentException>(() => TextSegmentation.FillWordStarts(text, new byte[text.Length]));
        Assert.Throws<ArgumentException>(() => TextSegmentation.FillSentenceStarts(text, new byte[text.Length]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextSegmentation.GetNextWordBoundary(text, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextSegmentation.GetNextSentenceBoundary(text, text.Length + 1));
    }

    [Fact]
    public void HostileRunsStayLinear()
    {
        // A million combining marks, flags, spaces after a full stop and quotes: each must finish at once (rules WB4, WB15, SB9, SB8).
        foreach (string unit in new[] { "a\u0301", "\U0001F1E9", ". ", ")\"", "1,", "a.", "a'" })
        {
            string text = string.Concat(Enumerable.Repeat(unit, 250_000));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int words = TextSegmentation.GetWordBoundaries(text).Length;
            int sentences = TextSegmentation.GetSentenceBoundaries(text).Length;
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"{unit}: {words} words, {sentences} sentences took {clock.Elapsed}");
        }
    }
}
