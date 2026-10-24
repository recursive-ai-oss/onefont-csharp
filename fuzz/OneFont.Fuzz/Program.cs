using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OneFont.Fuzz;
using SharpFuzz;

// One fuzz executable, one target per input surface. The target is chosen by the ONEFONT_FUZZ_TARGET environment
// variable because libFuzzer's .NET host starts the program without command-line arguments.
//
// To add a target: write a static method `Run(ReadOnlySpan<byte> data)` that lets only the library's typed exceptions
// escape, then add one line to `Targets` below.
//
// Without a libFuzzer host there are two local modes:
//   replay <target> <file>...                 runs saved inputs once each (reproduces a finding)
//   seeds <target> <out dir> <font dir>       writes starting inputs for a target (used by run.sh)
internal static class Program
{
    private delegate void Target(ReadOnlySpan<byte> data);

    private static readonly Dictionary<string, Target> Targets = new()
    {
        ["shape"] = ShapeTarget.Run,
    };

    private static int Main(string[] args)
    {
        if (args.Length > 2 && args[0] == "replay")
        {
            return Replay(args);
        }

        if (args.Length > 3 && args[0] == "seeds")
        {
            return ShapeTarget.WriteSeeds(args[2], args[3]);
        }

        string? name = Environment.GetEnvironmentVariable("ONEFONT_FUZZ_TARGET") ?? (args.Length > 0 ? args[0] : null);
        if (name is null || !Targets.TryGetValue(name, out Target? target))
        {
            Console.Error.WriteLine("usage: set ONEFONT_FUZZ_TARGET to one of: " + string.Join(", ", Targets.Keys));
            return 2;
        }

        Fuzzer.LibFuzzer.Run(data => target(data));
        return 0;
    }

    private static int Replay(string[] args)
    {
        if (!Targets.TryGetValue(args[1], out Target? target))
        {
            Console.Error.WriteLine("unknown target " + args[1]);
            return 2;
        }

        int failures = 0;
        foreach (string f in args.Skip(2))
        {
            var sw = Stopwatch.StartNew();
            try
            {
                target(File.ReadAllBytes(f));
                Console.WriteLine($"ok {f} ({sw.Elapsed.TotalSeconds:F2}s)");
            }
#pragma warning disable CA1031 // Replay reports any escaping exception as a failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failures++;
                Console.WriteLine($"FAIL {f}: {ex}");
            }
        }

        return failures == 0 ? 0 : 1;
    }
}
