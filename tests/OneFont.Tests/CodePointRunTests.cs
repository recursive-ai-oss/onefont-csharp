namespace OneFont.Tests;

/// <summary>The reusable-buffer overload of <c>ShapeCodePoints</c> gives exactly what the list overload gives.</summary>
public class CodePointRunTests
{
    private static string TestData(string sub) => Path.Combine(AppContext.BaseDirectory, "testdata", sub);

    private static (int[] Cps, int[] Clusters) Split(string s)
    {
        var cps = new List<int>();
        var offs = new List<int>();
        for (int i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
        {
            cps.Add(char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s, i) : s[i]);
            offs.Add(i);
        }

        return ([.. cps], [.. offs]);
    }

    [Fact]
    public void MatchesTheListOverloadOnTheCorpusWithAStaleBufferAndCarriedScript()
    {
        var faces = ShapingCorpus.Faces(TestData("fonts"));
        List<string> texts = ShapingCorpus.Texts(TestData("complex"), 400);
        var run = new CodePointRun();
        var stale = new CodePointGlyph(-1, -2, -3, -4, -5, -6);
        int compared = 0;
        foreach ((string name, FontFace face) in faces)
        {
            foreach (string text in texts)
            {
                (int[] cps, int[] clusters) = Split(text);
                foreach (bool rtl in new[] { false, true })
                {
                    uint s1 = 0x61726162, s2 = s1;
                    var list = new List<CodePointGlyph>();
                    TextShaping.ShapeCodePoints(face, cps, clusters, list, rtl, null, ref s1);

                    // Fill the buffer with junk first, as a previous, longer run would have.
                    uint junk = 0;
                    TextShaping.ShapeCodePoints(face, cps, clusters, run, !rtl, null, ref junk);
                    TextShaping.ShapeCodePoints(face, cps, clusters, run, rtl, null, ref s2);

                    Assert.True(list.Count == run.Count, $"{name} rtl={rtl}: {list.Count} vs {run.Count}");
                    Assert.Equal(s1, s2);
                    for (int i = 0; i < list.Count; i++)
                    {
                        Assert.Equal(list[i], run[i]);
                        Assert.Equal(list[i], run.Glyphs[i]);
                    }

                    compared += list.Count;
                }
            }
        }

        Assert.True(compared > 10_000);
        Assert.NotEqual(stale, default);
    }

    [Fact]
    public void ReplacesWhatTheBufferHeldAndEmptiesItOnAnEmptyRunOrAnError()
    {
        FontFace face = TestFonts.Noto;
        var run = new CodePointRun();
        uint script = 0;
        TextShaping.ShapeCodePoints(face, [0x41, 0x42, 0x43], [0, 1, 2], run, false, null, ref script);
        Assert.Equal(3, run.Count);
        TextShaping.ShapeCodePoints(face, [0x44], [7], run, false, null, ref script);
        Assert.Equal(1, run.Count);
        Assert.Equal(7, run[0].Cluster);
        TextShaping.ShapeCodePoints(face, [], [], run, false, null, ref script);
        Assert.Equal(0, run.Count);
        Assert.True(run.Glyphs.IsEmpty);

        TextShaping.ShapeCodePoints(face, [0x41], [0], run, false, null, ref script);
        uint s = 5;
        Assert.Throws<ArgumentException>(() =>
        {
            uint t = 5;
            TextShaping.ShapeCodePoints(face, [1, 2], [1], run, false, null, ref t);
        });
        Assert.Equal(0, run.Count);
        Assert.Equal(5u, s);
        Assert.Throws<ArgumentNullException>(() =>
        {
            uint t = 0;
            TextShaping.ShapeCodePoints(face, [1], [1], (CodePointRun)null!, false, null, ref t);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => run[0]);

        TextShaping.ShapeCodePoints(face, [0x41, -5, 0xD800, 0x110000], [0, 1, 2, 3], run, false, null, ref script);
        Assert.Equal([0x41, 0xFFFD, 0xFFFD, 0xFFFD], run.Glyphs.ToArray().Select(g => g.CodePoint));
    }

    [Fact]
    public void WarmCallsAllocateNothing()
    {
        FontFace face = TestFonts.Noto;
        (int[] cps, int[] clusters) = Split("The quick brown fox jumps over the lazy dog, office affine 12345.");
        var run = new CodePointRun();
        uint script = 0;
        for (int i = 0; i < 20; i++)
        {
            TextShaping.ShapeCodePoints(face, cps, clusters, run, false, null, ref script);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            script = 0;
            TextShaping.ShapeCodePoints(face, cps, clusters, run, false, null, ref script);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(run.Count >= cps.Length / 2);
    }
}
