# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.2.0] - 2026-10-24

- Added `TextShaping.ShapeCodePoints`: shapes a run given as code points with caller-supplied cluster values and a script carried from run to run (`ref uint script`), so digits and spaces after Arabic take the Arabic script. It returns `CodePointGlyph` values in the shaper's own order, each with its code point and cluster.
- Added `ShapedRun.Origins` (`ShapedGlyphOrigin`): for every glyph of a run the first code point it stands for, its cluster offset and its position in the shaper's order before clusters were put in logical order.
- Added code point bidi: `TextShaping.ResolveBidiLevels` (keeps the rule X9 marker `TextShaping.BidiRemoved` instead of filling it) and `TextShaping.ResetTrailingWhitespace` (rule L1 per line). `GetVisualOrder(levels, order)` accepts levels with the marker and returns how many characters are shown.
- Added `TextSegmentation.IsEastAsianWide`, `FillLineBreakOpportunities` and `FillGraphemeStarts` (one byte per UTF-16 offset), and `UnicodeProperties.GetJoiningKind` / `IsSyllableScript`.
- Added `FontFace.GetTable` and `HasTable`: the raw bytes of any table (`COLR`, `CPAL`, `vhea`, `vmtx`, `VORG`, `fpgm`, `prep`, `cvt ` and so on).
- Existing behaviour and output are unchanged; the 1.0.0 digests still match.

## [1.1.0] - 2026-10-24

- Added `ShapedRun`, `ShapedClusterRange` and the `TextShaping.ShapeRun(text, face, result, ...)` and `FontFallbackChain.ShapeRun(text, result, ...)` overloads: the same clusters and glyphs as the list API in flat, reusable arrays, with no allocation per call once warm. The list API is built on the same path and allocates under a third as much as before (about 75 bytes per character instead of about 270 for Latin).
- Shaping reuses glyph buffers, lookup engines and syllable work arrays per thread, and `ShapingOptions` caches its feature set. Output is unchanged: the tests compare against digests made by 1.0.0.
- Added buffer overloads `TextShaping.GetBidiLevels(text, direction, levels, out paragraphLevel)`, `TextShaping.GetVisualOrder(levels, order)` and `TextSegmentation.GetLineBreakOpportunities(text, opportunities)`.

## [1.0.0] - 2026-10-23

- First version. Fonts: TrueType, OpenType with CFF outlines, TrueType Collections. Shaping: GSUB and GPOS with every lookup type and flag; Arabic, Indic, Universal Shaping Engine, Thai and Lao shapers. Unicode 17: bidi levels and order (UAX #9), line break opportunities (UAX #14), grapheme boundaries (UAX #29). Font fallback chain. Glyph outlines through a sink.
- `TextShaping.ShapeRun` returns clusters that start at offset 0 and follow each other to the end of the text, even when the shaper drops characters (a joiner before an Indic letter).

[1.2.0]: https://github.com/recursive-ai-oss/onefont-csharp/releases/tag/v1.2.0
[1.1.0]: https://github.com/recursive-ai-oss/onefont-csharp/releases/tag/v1.1.0
[1.0.0]: https://github.com/recursive-ai-oss/onefont-csharp/releases/tag/v1.0.0
