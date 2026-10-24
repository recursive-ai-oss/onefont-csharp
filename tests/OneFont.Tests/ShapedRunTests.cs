using System.Text;
using OneFont.Shaping;

namespace OneFont.Tests;

/// <summary>The allocation-light shaping path: same results as the 1.0 cluster list, nothing allocated per call, safe for concurrent callers.</summary>
public class ShapedRunTests
{
    private static string FontsDir => Path.Combine(AppContext.BaseDirectory, "testdata", "fonts");

    private static string ComplexDir => Path.Combine(AppContext.BaseDirectory, "testdata", "complex");

    private static string[] Golden => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "testdata", "shaping", "golden.txt"));

    // Same format as ShapingCorpus.Describe, from the flat arrays.
    private static string Describe(ShapedRun run)
    {
        var sb = new StringBuilder();
        foreach (ShapedClusterRange c in run.Clusters)
        {
            sb.Append('[').Append(c.Start).Append(',').Append(c.Length).Append(',').Append(c.FaceIndex).Append(',').Append(c.Advance).Append(',').Append(c.HasMissingGlyph ? 1 : 0).Append(':');
            foreach (ShapedGlyph g in run.GlyphsOf(c))
            {
                sb.Append(g.GlyphId).Append('/').Append(g.XAdvance).Append('/').Append(g.XOffset).Append('/').Append(g.YOffset).Append(' ');
            }

            sb.Append(']');
        }

        return sb.ToString();
    }

    [Fact]
    public void TheListApiStillGivesWhatVersion100Gave()
    {
        // The golden digests were made by the 1.0.0 build over the same corpus (fixed texts, seeded random text, every test face, four
        // settings, both directions, two fallback chains).
        string[] lines = [.. ShapingCorpus.Digests(FontsDir, ComplexDir,
            (t, f, rtl, o) => ShapingCorpus.Describe(TextShaping.ShapeRun(t, f, rtl, o)),
            (t, c, rtl, o) => ShapingCorpus.Describe(c.ShapeRun(t, rtl, o)))];
        Assert.Equal(Golden, lines);
    }

    [Fact]
    public void TheReusableResultGivesWhatVersion100Gave()
    {
        var run = new ShapedRun();
        string[] lines = [.. ShapingCorpus.Digests(FontsDir, ComplexDir,
            (t, f, rtl, o) =>
            {
                TextShaping.ShapeRun(t, f, run, rtl, o);
                return Describe(run);
            },
            (t, c, rtl, o) =>
            {
                c.ShapeRun(t, run, rtl, o);
                return Describe(run);
            })];
        Assert.Equal(Golden, lines);
    }

    [Fact]
    public void ClustersAndGlyphsLineUp()
    {
        var run = new ShapedRun();
        FontFace deva = TestFonts.Public("NotoSansDevanagari-Subset-Regular.ttf");
        string text = "\u0915\u094D\u0937 \u0915\u093F\u0924\u093E\u092C \u0930\u094D\u0915 abc";
        TextShaping.ShapeRun(text, deva, run);
        int at = 0;
        int glyphs = 0;
        foreach (ShapedClusterRange c in run.Clusters)
        {
            Assert.Equal(at, c.Start);
            Assert.True(c.Length >= 1);
            Assert.Equal(glyphs, c.GlyphStart);
            Assert.Equal(c.GlyphCount, run.GlyphsOf(c).Length);
            Assert.Equal(c.Advance, run.GlyphsOf(c).ToArray().Sum(g => g.XAdvance));
            at += c.Length;
            glyphs += c.GlyphCount;
        }

        Assert.Equal(text.Length, at);
        Assert.Equal(run.GlyphCount, glyphs);
        Assert.Equal(run.Glyphs.Length, run.GlyphCount);
        Assert.Equal(run.Clusters.Length, run.ClusterCount);
    }

    [Fact]
    public void ACallReplacesTheEarlierContentsAndEmptyTextGivesNothing()
    {
        var run = new ShapedRun();
        TextShaping.ShapeRun("Hello world", TestFonts.Noto, run);
        Assert.Equal(11, run.ClusterCount);
        TextShaping.ShapeRun("Hi", TestFonts.Noto, run);
        Assert.Equal(2, run.ClusterCount);
        Assert.Equal(2, run.GlyphCount);
        TextShaping.ShapeRun("", TestFonts.Noto, run);
        Assert.Equal(0, run.ClusterCount);
        Assert.Equal(0, run.GlyphCount);
        Assert.Throws<ArgumentNullException>(() => TextShaping.ShapeRun("a", TestFonts.Noto, null!));
        Assert.Throws<ArgumentNullException>(() => TextShaping.ShapeRun("a", null!, run));
    }

    [Fact]
    public void AClusterWithNoGlyphsStillCoversItsText()
    {
        var run = new ShapedRun();
        FontFace deva = TestFonts.Public("NotoSansDevanagari-Subset-Regular.ttf");
        TextShaping.ShapeRun("\u200D", deva, run);
        Assert.Equal(1, run.ClusterCount);
        Assert.Equal(1, run.Clusters[0].Length);
        Assert.Equal(0, run.Clusters[0].GlyphCount);
        Assert.Equal(0, run.Clusters[0].Advance);
        Assert.True(run.GlyphsOf(run.Clusters[0]).IsEmpty);
    }

    [Fact]
    public void ShapingAWarmedUpRunAllocatesNothing()
    {
        FontFace latin = TestFonts.Noto;
        FontFace arabic = TestFonts.Public(BidiHarness.Arabic);
        FontFace deva = TestFonts.Public("NotoSansDevanagari-Subset-Regular.ttf");
        var chain = new FontFallbackChain([latin, arabic, deva]);
        var options = new ShapingOptions { Language = "tr" };
        options.Features.Add(new FontFeature("smcp"));
        string latinText = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog, AVATAR office.", 12));
        string arabicText = string.Join(' ', Enumerable.Repeat("\u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0627\u0644\u0631\u062D\u0645\u0646 \u0627\u0644\u0631\u062D\u064A\u0645", 20));
        string devaText = string.Join(' ', Enumerable.Repeat("\u0915\u093F\u0924\u093E\u092C \u0915\u094D\u0937\u0924\u094D\u0930 \u0930\u094D\u0915 \u0938\u0902\u0938\u094D\u0915\u0943\u0924", 20));
        string mixed = latinText + " " + arabicText + " " + devaText;
        var run = new ShapedRun();

        (string Name, Action Call)[] calls =
        [
            ("latin", () => TextShaping.ShapeRun(latinText, latin, run)),
            ("latin with options", () => TextShaping.ShapeRun(latinText, latin, run, options: options)),
            ("arabic", () => TextShaping.ShapeRun(arabicText, arabic, run, rightToLeft: true)),
            ("devanagari", () => TextShaping.ShapeRun(devaText, deva, run)),
            ("fallback chain", () => chain.ShapeRun(mixed, run)),
        ];
        var report = new List<string>();
        foreach ((string name, Action call) in calls)
        {
            for (int i = 0; i < 10; i++)
            {
                call();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
            {
                call();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated != 0)
            {
                report.Add($"{name}: {allocated / 20} bytes per call");
            }
        }

        Assert.Empty(report);
    }

    [Fact]
    public void TheClusterListAllocatesAboutAClusterObjectPerCluster()
    {
        FontFace latin = TestFonts.Noto;
        string text = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog", 20));
        for (int i = 0; i < 10; i++)
        {
            TextShaping.ShapeRun(text, latin);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++)
        {
            TextShaping.ShapeRun(text, latin);
        }

        long perChar = (GC.GetAllocatedBytesForCurrentThread() - before) / (10 * text.Length);
        Assert.InRange(perChar, 1, 120);
    }

    [Fact]
    public void ConcurrentCallersEachGetTheSerialResult()
    {
        FontFace latin = TestFonts.Noto;
        FontFace arabic = TestFonts.Public(BidiHarness.Arabic);
        FontFace deva = TestFonts.Public("NotoSansDevanagari-Subset-Regular.ttf");
        (FontFace Face, string Text, bool Rtl)[] cases =
        [
            (latin, "Office waffle AVATAR typography 12.50", false),
            (arabic, "\u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0627\u0644\u0631\u062D\u0645\u0646", true),
            (deva, "\u0915\u094D\u0937\u0924\u094D\u0930 \u0915\u093F\u0924\u093E\u092C \u0930\u094D\u0915", false),
            (latin, new string('x', 5000), false),
        ];
        string[] expected = [.. cases.Select(c => ShapingCorpus.Describe(TextShaping.ShapeRun(c.Text, c.Face, c.Rtl)))];
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
        {
            var run = new ShapedRun();
            for (int i = 0; i < 300; i++)
            {
                int k = (i + t) % cases.Length;
                string got;
                if ((i & 1) == 0)
                {
                    TextShaping.ShapeRun(cases[k].Text, cases[k].Face, run, cases[k].Rtl);
                    got = Describe(run);
                }
                else
                {
                    got = ShapingCorpus.Describe(TextShaping.ShapeRun(cases[k].Text, cases[k].Face, cases[k].Rtl));
                }

                if (got != expected[k])
                {
                    failures.Add($"thread {t} case {k} round {i}");
                }
            }
        });
        Assert.Empty(failures);
    }

    [Fact]
    public void ClustersThatComeOutOfOrderAreSortedWithTheirGlyphs()
    {
        // A reordering shaper can emit a cluster's glyphs out of logical order; the result puts clusters in order and keeps each one's glyphs together.
        ShapeScratch scratch = ShapeScratch.Current;
        var run = new ShapedRun();
        var glyphs = new List<ShapedRunGlyph>
        {
            new() { GlyphId = 10, Cluster = 4, XAdvance = 1 },
            new() { GlyphId = 11, Cluster = 4, XAdvance = 2 },
            new() { GlyphId = 12, Cluster = 0, XAdvance = 4 },
            new() { GlyphId = 13, Cluster = 2, XAdvance = 8 },
            new() { GlyphId = 14, Cluster = 2, XAdvance = 16 },
            new() { GlyphId = 0, Cluster = 5, XAdvance = 32, CodePoint = 'x' },
        };
        TextShaping.AppendClusters(run, scratch, glyphs, 100, 8, 3);
        Assert.Equal(4, run.ClusterCount);
        Assert.Equal([100, 102, 104, 105], run.Clusters.ToArray().Select(c => c.Start));
        Assert.Equal([2, 2, 1, 3], run.Clusters.ToArray().Select(c => c.Length));
        Assert.Equal([4, 24, 3, 32], run.Clusters.ToArray().Select(c => c.Advance));
        Assert.All(run.Clusters.ToArray(), c => Assert.Equal(3, c.FaceIndex));
        Assert.Equal([12, 13, 14, 10, 11, 0], run.Glyphs.ToArray().Select(g => g.GlyphId));
        Assert.Equal([false, false, false, true], run.Clusters.ToArray().Select(c => c.HasMissingGlyph));
        Assert.Equal([0, 1, 3, 5], run.Clusters.ToArray().Select(c => c.GlyphStart));
    }

    [Fact]
    public void ChangedOptionsTakeEffectOnTheNextCall()
    {
        FontFace face = TestFonts.Noto;
        var options = new ShapingOptions();
        var run = new ShapedRun();
        TextShaping.ShapeRun("AV", face, run, options: options);
        int kerned = run.Clusters[0].Advance;
        options.Kerning = KerningMode.None;
        TextShaping.ShapeRun("AV", face, run, options: options);
        int plain = run.Clusters[0].Advance;
        Assert.True(kerned < plain);
        options.Kerning = KerningMode.Normal;
        TextShaping.ShapeRun("AV", face, run, options: options);
        Assert.Equal(kerned, run.Clusters[0].Advance);

        options.Features.Add(FontFeature.Off("kern"));
        TextShaping.ShapeRun("AV", face, run, options: options);
        Assert.Equal(plain, run.Clusters[0].Advance);
        options.Features[0] = FontFeature.On("kern");
        TextShaping.ShapeRun("AV", face, run, options: options);
        Assert.Equal(kerned, run.Clusters[0].Advance);
    }

    [Fact]
    public void SpanOverloadsOfBidiAndLineBreaksMatchTheArrayForms()
    {
        string text = "ab \u05D0\u05D1 cd \u0627\u0644 12 \U0001F600 end.\nnext";
        byte[] levels = TextShaping.GetBidiLevels(text, TextDirection.Auto, out int para);
        var into = new byte[text.Length + 3];
        bool allZero = TextShaping.GetBidiLevels(text, TextDirection.Auto, into, out int para2);
        Assert.False(allZero);
        Assert.True(TextShaping.GetBidiLevels("plain text", TextDirection.Auto, new byte[16], out _));
        Assert.True(TextShaping.GetBidiLevels("", TextDirection.Auto, new byte[1], out _));
        Assert.Equal(para, para2);
        Assert.Equal(levels, into.AsSpan(0, text.Length).ToArray());
        Assert.Throws<ArgumentException>(() => TextShaping.GetBidiLevels(text, TextDirection.Auto, new byte[text.Length - 1], out _));

        int[] order = TextShaping.GetVisualOrder(levels);
        var orderInto = new int[levels.Length];
        int shown = TextShaping.GetVisualOrder(levels, orderInto);
        Assert.Equal(order, orderInto.AsSpan(0, shown).ToArray());
        Assert.Throws<ArgumentException>(() => TextShaping.GetVisualOrder(levels, new int[levels.Length - 1]));

        LineBreakOpportunity[] breaks = TextSegmentation.GetLineBreakOpportunities(text);
        var breaksInto = new LineBreakOpportunity[text.Length + 1];
        TextSegmentation.GetLineBreakOpportunities(text, breaksInto);
        Assert.Equal(breaks, breaksInto);
        Assert.Throws<ArgumentException>(() => TextSegmentation.GetLineBreakOpportunities(text, new LineBreakOpportunity[text.Length]));
    }

    [Fact]
    public void OptionsCreateTheFeatureListOnlyWhenAsked()
    {
        var shared = new ShapingOptions { Language = "tr" };
        string text = "The quick brown fox AVATAR";
        string expected = ShapingCorpus.Describe(TextShaping.ShapeRun(text, TestFonts.Noto, false, shared));
        var failures = new System.Collections.Concurrent.ConcurrentBag<int>();
        Parallel.For(0, 8, t =>
        {
            var run = new ShapedRun();
            for (int i = 0; i < 200; i++)
            {
                TextShaping.ShapeRun(text, TestFonts.Noto, run, false, shared);
                if (Describe(run) != expected)
                {
                    failures.Add(t);
                }
            }
        });
        Assert.Empty(failures);
        Assert.Empty(shared.Features);
    }
}
