using BenchmarkDotNet.Attributes;

namespace OneFont.Benchmarks;

/// <summary>Shaping from code points: into a new list per run, into a kept list, and into a reusable <see cref="CodePointRun"/>.</summary>
[MemoryDiagnoser]
public class CodePointBenchmarks
{
    private readonly int[] _cps = Codes(Data.LatinParagraph);
    private readonly int[] _clusters;
    private readonly List<CodePointGlyph> _kept = [];
    private readonly CodePointRun _run = new();

    public CodePointBenchmarks()
    {
        _clusters = new int[_cps.Length];
        for (int i = 0; i < _clusters.Length; i++)
        {
            _clusters[i] = i;
        }
    }

    private static int[] Codes(string s) => [.. s.EnumerateRunes().Select(r => r.Value)];

    [Benchmark(Baseline = true)]
    public int NewListPerRun()
    {
        var list = new List<CodePointGlyph>();
        uint script = 0;
        TextShaping.ShapeCodePoints(Data.Latin, _cps, _clusters, list, false, null, ref script);
        return list.Count;
    }

    [Benchmark]
    public int KeptList()
    {
        _kept.Clear();
        uint script = 0;
        TextShaping.ShapeCodePoints(Data.Latin, _cps, _clusters, _kept, false, null, ref script);
        return _kept.Count;
    }

    [Benchmark]
    public int ReusableRun()
    {
        uint script = 0;
        TextShaping.ShapeCodePoints(Data.Latin, _cps, _clusters, _run, false, null, ref script);
        return _run.Count;
    }
}
