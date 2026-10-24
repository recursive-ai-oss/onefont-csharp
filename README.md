# OneFont

**A font and text-shaping library for .NET with no dependency beyond .NET itself.** Load a TrueType or OpenType font, shape a run of text, and get glyphs, advances, offsets and clusters back, with the Unicode bidirectional, line breaking and grapheme algorithms and font fallback alongside. Pure C#, safe code only, NativeAOT and trimming clean.

[![CI](https://github.com/recursive-ai-oss/onefont-csharp/actions/workflows/ci.yml/badge.svg)](https://github.com/recursive-ai-oss/onefont-csharp/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](#install)
[![License](https://img.shields.io/badge/license-MIT%20OR%20Apache--2.0-blue)](#license)

## What it does

- **Fonts.** TrueType (`glyf`), OpenType with CFF outlines and TrueType Collections: `cmap`, `head`, `hhea`, `hmtx`, `OS/2`, `name`, `post`, `kern`, GDEF. Every read is bounds-checked; damaged fonts raise `FontFormatException` or, for layout tables, are ignored.
- **Shaping.** The OpenType GSUB (types 1 to 8) and GPOS (types 1 to 9) engine with every lookup flag, features and BCP 47 language, and script shapers for Arabic (with Syriac and N'Ko), the Indic scripts, the Universal Shaping Engine scripts (Khmer, Myanmar, Sinhala, Tibetan, Balinese, Javanese and more), and Thai and Lao. Work, depth and growth budgets keep hostile fonts cheap.
- **Unicode.** UAX #9 bidi (levels and visual order), UAX #14 line break opportunities and UAX #29 grapheme boundaries, on Unicode 17 data, passing the official conformance files.
- **Fallback.** A chain of faces; each grapheme cluster is shaped with the first face that has its character, using that face's own tables.
- **Outlines.** Glyph outlines as move, line, quadratic and cubic segments through a small sink interface.

Not in this package: WOFF and WOFF2, colour glyphs, hinting, vertical layout, and drawing text. OneFont shapes; laying out lines and painting glyphs is up to you.

## Use

```csharp
using OneFont;

FontFace arabic = FontFace.Load(File.ReadAllBytes("NotoSansArabic-Regular.ttf"));

foreach (ShapedCluster cluster in TextShaping.ShapeRun("\u0627\u0644\u0633\u0644\u0627\u0645", arabic, rightToLeft: true))
{
    // Start and Length index the text; Advance and the glyphs' advances and offsets are in font units.
    foreach (ShapedGlyph g in cluster.Glyphs)
    {
        GlyphOutline outline = arabic.GetOutline(g.GlyphId);
        // outline.Write(sink, transform) replays move/line/quad/cubic segments.
    }
}

// Mixed-direction paragraph: levels, then display order of the runs or clusters.
byte[] levels = TextShaping.GetBidiLevels("abc \u05E9\u05DC\u05D5\u05DD def", TextDirection.Auto, out int paragraphLevel);
int[] visual = TextShaping.GetVisualOrder(levels);

// Where a line may end, and where a user-perceived character ends.
LineBreakOpportunity[] breaks = TextSegmentation.GetLineBreakOpportunities("well-known words");
int[] graphemes = TextSegmentation.GetGraphemeBoundaries("e\u0301a");

// A face chain shapes each cluster with the first face that has its character.
var chain = new FontFallbackChain([latin, cjk]);
IReadOnlyList<ShapedCluster> clusters = chain.ShapeRun("abc \u65E5\u672C");   // cluster.FaceIndex says which face
```

Sizes are never involved: advances, offsets and metrics are in the face's design units, so scale by `size / face.UnitsPerEm`. Features and language go in `ShapingOptions`:

```csharp
var options = new ShapingOptions { Language = "tr", Kerning = KerningMode.None };
options.Features.Add(FontFeature.On("smcp"));
TextShaping.ShapeRun("Small Caps", face, options: options);
```

Shaping many runs (a paragraph engine, a table) can reuse one result and allocate nothing per call:

```csharp
var run = new ShapedRun();
TextShaping.ShapeRun("Small Caps", face, run, options: options);
foreach (ShapedClusterRange cluster in run.Clusters)
{
    ReadOnlySpan<ShapedGlyph> glyphs = run.GlyphsOf(cluster);
}
```

A run is one face, one direction and one paragraph's worth of text. Splitting a paragraph into runs (by bidi level, by face) and placing them on lines is the caller's job; `tests/OneFont.Tests/MiniLayout.cs` is a short worked example.

More detail on the shaping rules, the limits, and where the output deliberately differs from SixLabors.Fonts is in [docs/shaping.md](docs/shaping.md).

## Install

```
dotnet add package RecursiveAI.OneFont
```

Targets `net10.0`. No runtime dependencies.

## Quality

- The shaper is compared with SixLabors.Fonts 2.1.3, used as a black box through its public API in the tests, on Latin, Greek, Cyrillic, Arabic, Hebrew and 17 complex scripts (1,042 cases); every difference is listed with its reason.
- `LineBreakTest.txt`, `GraphemeBreakTest.txt`, `BidiTest.txt` and `BidiCharacterTest.txt` of Unicode 17 pass in full.
- Hand-assembled GSUB and GPOS tables test every lookup type, format and flag.
- A fuzz target (`fuzz/`) runs hostile fonts and text through the whole public surface under libFuzzer.

## Build and test

```
dotnet build
dotnet test
```

The test project needs nothing but the committed test data. `tools/` holds the generators for the Unicode tables and the scripts that cut the test fonts.

## License

`MIT OR Apache-2.0`, Copyright (c) Recursive AI, LLC. See `LICENSE-MIT`, `LICENSE-APACHE` and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
