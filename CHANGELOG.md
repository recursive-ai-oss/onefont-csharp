# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

- First version. Fonts: TrueType, OpenType with CFF outlines, TrueType Collections. Shaping: GSUB and GPOS with every lookup type and flag; Arabic, Indic, Universal Shaping Engine, Thai and Lao shapers. Unicode 17: bidi levels and order (UAX #9), line break opportunities (UAX #14), grapheme boundaries (UAX #29). Font fallback chain. Glyph outlines through a sink.
- `TextShaping.ShapeRun` returns clusters that start at offset 0 and follow each other to the end of the text, even when the shaper drops characters (a joiner before an Indic letter).
