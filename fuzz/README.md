# Fuzzing

`fuzz/OneFont.Fuzz` holds the fuzz targets; SharpFuzz instruments `OneFont.dll` and libFuzzer drives it.

| Target | Input | Surface |
|---|---|---|
| `shape` | flags, a font pick, a short text, then optionally a font | loading a face, shaping with features, language and direction, outlines, bidi levels and order, line breaks, graphemes, fallback, the code point entry points (carried script, glyph origins, bidi with removed marker, byte fills, properties, raw tables); the clusters must follow each other and cover the text |

Only `FontFormatException` may come out of a library call. Any other exception, a failed invariant, a hang (`-timeout`) or a memory blow-up (`-rss_limit_mb`) is a finding.

## Running

    export ONEFONT_FUZZ_TOOLS=/path/to/tools     # holds fuzz/libfuzzer-dotnet and sharpfuzz/sharpfuzz
    export ONEFONT_FUZZ_WORK=/path/to/workdir    # builds, corpora, findings; must be outside the checkout
    fuzz/run.sh shape 600                        # fuzz for 10 minutes
    fuzz/run.sh replay shape some-input...       # run saved inputs once, without libFuzzer

`run.sh` builds the harness in release mode into `$ONEFONT_FUZZ_WORK/bin`, writes seeds (the committed test fonts with sample text in many scripts, and small fonts followed by text for mutation) into `$ONEFONT_FUZZ_WORK/corpus/shape/`, instruments `OneFont.dll`, and runs libFuzzer. Findings land in `$ONEFONT_FUZZ_WORK/findings/shape/`. `replay` rebuilds without instrumentation.

The tools:

* `libfuzzer-dotnet`: the C++ host from the SharpFuzz project, compiled with `clang++ -fsanitize=fuzzer`.
* `sharpfuzz`: `dotnet tool install SharpFuzz.CommandLine --tool-path <dir>/sharpfuzz`.

Reading the output: `ft` (features) is the number to watch. The host's own `cov` stays small because only the .NET side is instrumented. Run one fuzz process at a time.

## Findings

Minimize the input, save it under `tests/testdata/fuzz/` (under 1 MB, from our own or redistributable files), add a test that feeds it and asserts a typed result.
