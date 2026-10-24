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
}
