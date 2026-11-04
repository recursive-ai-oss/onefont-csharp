using System.Text;
using OneFont.Unicode;

namespace OneFont.Tests;

/// <summary>The official Unicode conformance files, run in full against the line and grapheme breakers.</summary>
public class UnicodeConformanceTests
{
    private static string DataPath(string file) => System.IO.Path.Combine(AppContext.BaseDirectory, "testdata", "unicode", file);

    // Each line is "÷ 0041 × 0042 ÷": the string's code points with ÷ (break) or × (no break) between and around them.
    private static IEnumerable<(string Text, bool[] Break, int Line)> Cases(string file)
    {
        int n = 0;
        foreach (string raw in File.ReadLines(DataPath(file)))
        {
            n++;
            string line = raw;
            int hash = line.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var sb = new StringBuilder();
            var breaks = new List<bool>();
            foreach (string tok in line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok == "\u00F7")
                {
                    breaks.Add(true);
                }
                else if (tok == "\u00D7")
                {
                    breaks.Add(false);
                }
                else
                {
                    sb.Append(char.ConvertFromUtf32(Convert.ToInt32(tok, 16)));
                }
            }

            yield return (sb.ToString(), [.. breaks], n);
        }
    }

    [Fact]
    public void LineBreakTestPassesInFull()
    {
        int cases = 0;
        var failures = new List<string>();
        foreach ((string text, bool[] expected, int line) in Cases("LineBreakTest.txt"))
        {
            cases++;
            byte[] got = new byte[text.Length + 1];
            LineBreaker.Fill(text, got);

            // expected[k] is the status before the k-th code point (and after the last one).
            int k = 0;
            for (int i = 0; i <= text.Length; k++)
            {
                bool want = expected[k];
                bool have = got[i] != 0;
                if (i == 0)
                {
                    // Never a break at the start (LB2).
                    have = false;
                    want = false;
                }

                if (want != have && failures.Count < 20)
                {
                    failures.Add($"line {line} position {k}: expected {(want ? "break" : "no break")}, got {(have ? "break" : "none")}: {string.Join(' ', text.EnumerateRunes().Select(r => r.Value.ToString("X4")))}");
                }

                if (i == text.Length)
                {
                    break;
                }

                i += char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            }
        }

        TestReport.Line($"LINEBREAK conformance: {cases} cases, {failures.Count} failing (Unicode {UnicodeData.Version})");
        Assert.True(cases > 19000, $"only {cases} cases read");
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void GraphemeBreakTestPassesInFull()
    {
        int cases = 0;
        var failures = new List<string>();
        foreach ((string text, bool[] expected, int line) in Cases("GraphemeBreakTest.txt"))
        {
            cases++;
            var got = new List<int> { 0 };
            for (int i = 0; i < text.Length;)
            {
                i += Graphemes.NextLength(text.AsSpan(i));
                got.Add(i);
            }

            // Boundaries the file expects, as UTF-16 offsets.
            var want = new List<int>();
            int pos = 0;
            int idx = 0;
            while (true)
            {
                if (expected[idx])
                {
                    want.Add(pos);
                }

                if (pos >= text.Length)
                {
                    break;
                }

                pos += char.IsHighSurrogate(text[pos]) && pos + 1 < text.Length ? 2 : 1;
                idx++;
            }

            if (!got.SequenceEqual(want) && failures.Count < 20)
            {
                failures.Add($"line {line}: expected [{string.Join(',', want)}], got [{string.Join(',', got)}]: {string.Join(' ', text.EnumerateRunes().Select(r => r.Value.ToString("X4")))}");
            }
        }

        TestReport.Line($"GRAPHEME conformance: {cases} cases, {failures.Count} failing (Unicode {UnicodeData.Version})");
        Assert.True(cases > 700, $"only {cases} cases read");
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    // The expected boundaries of a case as UTF-16 offsets: a break before the k-th code point and after the last one.
    private static List<int> ExpectedBoundaries(string text, bool[] expected)
    {
        var want = new List<int>();
        int pos = 0;
        int idx = 0;
        while (true)
        {
            if (expected[idx])
            {
                want.Add(pos);
            }

            if (pos >= text.Length)
            {
                break;
            }

            pos += char.IsHighSurrogate(text[pos]) && pos + 1 < text.Length ? 2 : 1;
            idx++;
        }

        // The file puts a break at the very start (WB1/SB1); the API lists 0 for non-empty text too, and [0] for empty text.
        return want;
    }

    private static void RunSegmentationFile(string file, string label, int minCases,
        Func<string, int[]> boundaries, Action<string, byte[]> fill, Func<string, int, int> next)
    {
        int cases = 0;
        var failures = new List<string>();
        foreach ((string text, bool[] expected, int line) in Cases(file))
        {
            cases++;
            List<int> want = ExpectedBoundaries(text, expected);
            if (text.Length == 0)
            {
                want = [0];
            }

            int[] got = boundaries(text);

            // The start-marker form and the stepping form must agree with the array form.
            byte[] marks = new byte[text.Length + 3];
            marks[text.Length + 1] = 7;
            fill(text, marks);
            var fromMarks = new List<int>();
            for (int i = 0; i <= text.Length; i++)
            {
                if (marks[i] == 1)
                {
                    fromMarks.Add(i);
                }
            }

            var stepped = new List<int>();
            if (text.Length > 0)
            {
                stepped.Add(0);
                for (int i = 0; i < text.Length;)
                {
                    i = next(text, i);
                    stepped.Add(i);
                }
            }
            else
            {
                stepped.Add(0);
            }

            bool ok = got.SequenceEqual(want) && fromMarks.SequenceEqual(want) && stepped.SequenceEqual(want) && marks[text.Length + 1] == 7;
            if (!ok && failures.Count < 20)
            {
                failures.Add($"line {line}: expected [{string.Join(',', want)}], got [{string.Join(',', got)}] marks [{string.Join(',', fromMarks)}] steps [{string.Join(',', stepped)}]: {string.Join(' ', text.EnumerateRunes().Select(r => r.Value.ToString("X4")))}");
            }
        }

        TestReport.Line($"{label} conformance: {cases} cases, {failures.Count} failing (Unicode {UnicodeData.Version})");
        Assert.True(cases > minCases, $"only {cases} cases read");
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void WordBreakTestPassesInFull()
        => RunSegmentationFile("WordBreakTest.txt", "WORDBREAK", 1800,
            t => TextSegmentation.GetWordBoundaries(t),
            (t, b) => TextSegmentation.FillWordStarts(t, b),
            (t, i) => TextSegmentation.GetNextWordBoundary(t, i));

    [Fact]
    public void SentenceBreakTestPassesInFull()
        => RunSegmentationFile("SentenceBreakTest.txt", "SENTENCEBREAK", 400,
            t => TextSegmentation.GetSentenceBoundaries(t),
            (t, b) => TextSegmentation.FillSentenceStarts(t, b),
            (t, i) => TextSegmentation.GetNextSentenceBoundary(t, i));
}
