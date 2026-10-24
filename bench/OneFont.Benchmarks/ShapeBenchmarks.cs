using BenchmarkDotNet.Attributes;

namespace OneFont.Benchmarks;

/// <summary>Shaping through the 1.0 API (<see cref="TextShaping.ShapeRun(ReadOnlySpan{char}, FontFace, bool, ShapingOptions?)"/>).</summary>
[MemoryDiagnoser]
public class ShapeBenchmarks
{
    private readonly FontFallbackChain _chain = new([Data.Latin, Data.Arabic, Data.Devanagari]);

    [Benchmark]
    public int Latin() => TextShaping.ShapeRun(Data.LatinParagraph, Data.Latin).Count;

    [Benchmark]
    public int Arabic() => TextShaping.ShapeRun(Data.ArabicParagraph, Data.Arabic, rightToLeft: true).Count;

    [Benchmark]
    public int Devanagari() => TextShaping.ShapeRun(Data.DevanagariParagraph, Data.Devanagari).Count;

    [Benchmark]
    public int Cells40k()
    {
        int n = 0;
        string[] cells = Data.Cells;
        for (int i = 0; i < 40_000; i++)
        {
            n += TextShaping.ShapeRun(cells[i & 255], Data.Latin).Count;
        }

        return n;
    }

    [Benchmark]
    public int MixedFallback() => _chain.ShapeRun(Data.Mixed).Count;
}
