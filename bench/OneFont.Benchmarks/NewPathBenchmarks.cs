using BenchmarkDotNet.Attributes;

namespace OneFont.Benchmarks;

/// <summary>The same workloads through the reusable <see cref="ShapedRun"/> overloads.</summary>
[MemoryDiagnoser]
public class NewPathBenchmarks
{
    private readonly FontFallbackChain _chain = new([Data.Latin, Data.Arabic, Data.Devanagari]);
    private readonly ShapedRun _run = new();

    [Benchmark]
    public int Latin()
    {
        TextShaping.ShapeRun(Data.LatinParagraph, Data.Latin, _run);
        return _run.ClusterCount;
    }

    [Benchmark]
    public int Arabic()
    {
        TextShaping.ShapeRun(Data.ArabicParagraph, Data.Arabic, _run, rightToLeft: true);
        return _run.ClusterCount;
    }

    [Benchmark]
    public int Devanagari()
    {
        TextShaping.ShapeRun(Data.DevanagariParagraph, Data.Devanagari, _run);
        return _run.ClusterCount;
    }

    [Benchmark]
    public int Cells40k()
    {
        int n = 0;
        string[] cells = Data.Cells;
        for (int i = 0; i < 40_000; i++)
        {
            TextShaping.ShapeRun(cells[i & 255], Data.Latin, _run);
            n += _run.ClusterCount;
        }

        return n;
    }

    [Benchmark]
    public int MixedFallback()
    {
        _chain.ShapeRun(Data.Mixed, _run);
        return _run.ClusterCount;
    }
}
