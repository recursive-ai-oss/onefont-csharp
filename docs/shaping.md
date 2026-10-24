# Shaping, bidi and segmentation: what OneFont does

This page describes behavior that goes beyond the API docs: which tables are read, in what order features run, the limits that protect against hostile fonts, and where the output deliberately differs from SixLabors.Fonts 2.1.3, which the tests use as a black-box reference through its public API.

## Shaping

Code points go through the font's character map, then GSUB substitutions, then GPOS positioning. The result is a run of glyphs with advances and offsets in font design units.

- **Tables.** `GDEF` glyph classes, mark attachment classes and mark glyph sets. `GSUB` lookup types 1 single, 2 multiple, 3 alternate, 4 ligature, 5 context, 6 chained context (all three formats of each), 7 extension and 8 reverse chaining single. `GPOS` types 1 single, 2 pair (formats 1 and 2), 3 cursive, 4 mark-to-base, 5 mark-to-ligature, 6 mark-to-mark, 7 context, 8 chained context and 9 extension. All lookup flags apply: ignore base glyphs, ligatures and marks, mark attachment type, and mark filtering sets (a filtering set wins over an attachment type). Device and variation-index tables inside value records and anchors are skipped: outlines are unhinted and a variable font is read at its default instance.
- **Lookup order.** Lookups of the enabled features run in lookup-list order, as the OpenType specification says, in two stages: `ccmp` and `locl` first, then everything else. So `smcp` on `fi` changes the letters to small capitals before `liga` looks for a ligature, and no ligature forms. SixLabors.Fonts ligates first.
- **Script and language.** Each stretch of one script (spaces, digits and punctuation join the stretch they are in; combining marks follow their base; nothing substitutes or kerns across a change of script) is shaped with that script's feature set, falling back to `DFLT`, `dflt`, then `latn` when the font lacks the script. `ShapingOptions.Language` (a BCP 47 tag such as `sr`, `ro`, `tr`, or an OpenType language tag in capitals such as `SRB`) picks the font's language system for the script; a language the font does not have uses the script's default.
- **Features.** The defaults for horizontal text are `ccmp`, `locl`, `rlig`, `liga`, `clig`, `calt`, `rclt`, `kern`, `mark`, `mkmk` and `curs`. `ShapingOptions.Features` takes `FontFeature` settings in order, later ones replacing earlier ones: `FontFeature.On("smcp")`, `FontFeature.Off("liga")`, or a value (`new FontFeature("salt", 2)` picks the second alternate; a value past the font's alternates leaves the glyph alone). `Kerning = KerningMode.None` is the same as `Off("kern")`, and an explicit `kern` setting wins over it. A tag the font does not have does nothing. A feature outside the default stages (`smcp`, `onum`, `dlig`, `ss01`...) runs in the second stage with the defaults.
- **Marks.** A mark is placed from the anchors of the font's `mark` and `mkmk` lookups: on the base, on a ligature's component, or on the mark before it. A combining sequence the font composes through `ccmp` is substituted before positioning. Kerning does not cross a change of direction.
- **Clusters.** `ShapedCluster` covers the characters from its `Start` to the next cluster. A ligature is one cluster for several characters. A base and its combining marks, and the glyphs of one character the font splits into several, share one cluster, as grapheme clusters do. A zero width non-joiner, a zero width space and a soft hyphen are not shaped as part of a run by a caller that splits runs there (see below), so nothing ligates or kerns across them.
- **Limits.** Fonts are untrusted. Every read is bounds-checked and a subtable that cannot be read is skipped. Contexts nest lookups at most 6 deep. A run may grow to eight glyphs per character plus 256, and may attempt at most 65,536 + 1,024 per character subtable applications (2^26 overall; a subtable that fails to read counts as 2,048, and one the engine skips because it has a type it cannot use counts as 1, so a lookup of tens of thousands of them cannot cost that many steps at every glyph). A run that exhausts the budget keeps what it has and applies no further lookups. A damaged GSUB, GPOS or GDEF header means the table is ignored, as if the font had none. Shaping does not throw on damaged layout tables; damaged glyph data raises `FontFormatException` when an outline is read.

Not done: canonical reordering and composition of combining marks (a mark sequence is shaped as typed; the font's own `ccmp` handles the compositions it has), contour-point anchors, feature ranges inside a string, Apple `morx`/`kerx` layout, and the GSUB/GPOS FeatureVariations table.

The paragraph-level work around the shaper (splitting a paragraph into runs of one direction, dropping format characters, ending a run at a zero width non-joiner, zero width space or soft hyphen, carrying the script from run to run) belongs to the caller. `tests/OneFont.Tests/MiniLayout.cs` is a short, complete example.

## Direction and right-to-left text

`TextShaping.GetBidiLevels` runs the Unicode Bidirectional Algorithm (UAX #9, Unicode 17 data) on one paragraph; the official `BidiTest.txt` and `BidiCharacterTest.txt` pass in full and are committed as test data. `TextDirection.Auto` takes the paragraph direction from the first strong character and falls back to left to right; `LeftToRight` and `RightToLeft` force the paragraph level, and the algorithm still reorders embedded runs of the other direction. `GetVisualOrder` applies rule L2 to levels per character or per cluster. Brackets are mirrored by the shaper (rule L4 and bracket pairs, rule N0), by code point, or through the font's `rtlm` feature when it has one. Explicit embeddings, overrides and isolates up to depth 125 are honored. Embedding depth is bounded at 125 and the bracket stack at 63, as the algorithm says, and hostile text lays out in time linear in its length.

### Arabic and Hebrew

Arabic, Syriac and N'Ko go through a joining shaper. Each letter's form comes from the Joining_Type of its neighbours (transparent marks and ZWJ/ZWNJ are looked through or break the join as Unicode says), and the form features run in this order: `ccmp`, `locl`, `rtlm`; then `isol`, `fina`, `fin2`, `fin3`, `medi`, `med2`, `init` (each only on the letters of its form); then `rlig`; then `calt`; then `liga`, `clig`, `rclt`, `mset`; and `kern`, `mark`, `mkmk`, `curs` for positioning. Lam-alef and other ligatures come from the font's lookups, so a font without them gets plain joined forms. Digits after Arabic text pick up the Arabic script's lookups. Syriac Alaph forms (`fin2`, `fin3`, `med2` selection) are not implemented. Cursive attachment in right-to-left runs is implemented but no test font has `curs`, so it is untested.

Hebrew needs no joining: the generic shaper and the bidirectional algorithm place the letters, and points and cantillation marks attach through the font's `mark` and `mkmk` lookups.

A right-to-left run is shaped in logical order with offsets given for display order. The caller reverses the glyphs of the run (and of each cluster) when it reorders a line; `MiniLayout` shows how.

Agreement with SixLabors.Fonts 2.1.3 is tested on a merged Noto Sans / Noto Sans Arabic / Noto Sans Hebrew subset: glyph ids are equal and glyph origins agree to 0.01 px except in these deliberate cases.

- **Kerning across Hebrew points.** The font's kern lookup ignores marks, so we kern the letters on either side of a point; SixLabors.Fonts does not. Up to 2 px at 40 px; identical with kerning off.
- **The space after a Hebrew letter** across a change of direction is 10 font units wider in SixLabors.Fonts; we use the font's advance.
- **Direction controls.** SixLabors.Fonts gives embedding, isolate and override controls a 0.6 em advance each; we give them none. Glyph ids still agree.
- **Forced direction.** With a forced left-to-right direction SixLabors.Fonts does no reordering at all, and with right-to-left it reverses the whole line. We apply UAX #9 with the paragraph level forced.
- **Fallback faces.** With a fallback face SixLabors.Fonts applies the primary font's GDEF and GPOS to fallback glyph ids. `FontFallbackChain` shapes each face with its own tables.

## Complex scripts: Indic, Southeast Asian and the Universal Shaping Engine

Three shapers are chosen by the script of a run. Script tags and the character properties come from generated Unicode tables (`tools/gen_unicode_tables.py`, Unicode 17.0.0: Script, Indic_Syllabic_Category, Indic_Positional_Category and the canonical two-part decompositions).

- **Indic shaper:** Devanagari, Bengali, Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada and Malayalam, following Microsoft's Indic script-development documents (the v2 tags `dev2`, `bng2`... first, then the v1 tags). Text is cut into syllables, split vowels are decomposed, the base consonant is found, a reph is moved, pre-base matras go to the front, and features run in the documented order: `locl`, `ccmp`, then `nukt`, `akhn`, `rphf`, `rkrf`, `pref`, `blwf`, `abvf`, `half`, `pstf`, `vatu`, `cjct`, then `init`, `pres`, `abvs`, `blws`, `psts`, `haln`, `dist`, `abvm`, `blwm` with the default positioning features. A mark with no base gets a dotted circle (U+25CC) as its base.
- **Universal Shaping Engine shaper:** Khmer, Myanmar, Sinhala, Tibetan, Balinese, Javanese and the other scripts of the USE list, from the USE specification, with Khmer and Myanmar specifics: coeng + ro, pre-base vowels and medials, the Khmer split vowels, kinzi and stacked consonants.
- **Thai and Lao:** SARA AM (U+0E33, U+0EB3) is split into NIKHAHIT and SARA AA, and the nikhahit moves in front of the tone mark that precedes it, as the Thai script document says.
- **Limits.** A syllable is cut after 48 glyphs; runs over 4,096 glyphs are shaped in pieces of 2,048 glyphs cut at syllable boundaries, so the cost is linear.
- **Choices that follow the documents, not the reference.** ZWJ and ZWNJ are never skipped by lookup flags; their glyphs are removed before positioning. ZWJ after a consonant and halant asks for the half form, ZWNJ denies it. A reph forms only when the font's `rphf` changes Ra + halant. Malayalam needs a ZWJ for a reph. The base consonant is found with probes of the font's `pref`, `blwf` and `pstf` lookups. A syllable's characters share one cluster value.

### Agreement with SixLabors.Fonts

Checked through its public API with 17 Noto subsets (`tests/testdata/fonts/Noto*-Subset-Regular.ttf`) on 1,042 cases (`tests/testdata/complex/*.txt`). Glyph ids must be equal and glyph origins within 0.01 font units. The reference throws on 26 of the cases; those are not compared. Every difference is listed in `tests/testdata/complex/known-differences.txt` with a reason; the test fails if a listed case starts to match or an unlisted one differs.

- **broken-cluster.** A mark without a base: we insert the dotted circle first, in logical order, and the mark follows it (a pre-base matra moves in front of the circle as it would in front of a consonant). The reference puts the circle after the mark.
- **myanmar-reorder.** The reference does no reordering for Myanmar. We follow USE: the pre-base vowel E (U+1031) and medial Ra (U+103C) move before the consonant, a virama joins two consonants into the font's stacked form, and a consonant + asat + virama before a consonant becomes the font's kinzi.
- **tibetan-extra-glyph.** For U+0F40 followed by U+0F73 or U+0F81 we output the consonant with aa, then the vowel sign; the reference appends an extra dotted circle.
- **joiner-kerning.** ZWJ/ZWNJ glyphs are removed after substitution, so pair adjustment sees the neighbours next to each other.
- **ignore-marks-pair, below-mark-offset.** Malayalam i/ii: a pair lookup with IgnoreMarks does not apply to a second glyph the GDEF table calls a mark (OpenType lookup flags); the reference applies it. Gujarati u/uu/vocalic r: the font's contextual `dist`/`blwm` single positioning applies in ours and not in the reference.
- **sara-am.** The nikhahit goes in front of the tone mark; the reference leaves them overlapping.
- **kannada-conjunct.** We form the font's conjunct and conjunct + matra ligatures; the reference breaks the ligature.
- **bengali-form, contextual-form, prebase-reorder.** The font's contextual variants or the ordering follow the font's tables and the documents in ours; the reference skips them. Some members were not derived from a rule in the documents, so treat those as unproven and not as known errors of the reference.

A font with the older v1 script tags only is shaped through the v1 tags; the v1-specific behaviors of older engines are not reproduced.

## Line breaking and graphemes

`TextSegmentation.GetLineBreakOpportunities` is the Unicode line breaking algorithm, UAX #14, Unicode 17.0 data, default rules as written, with the small kana of class CJ treated as NS (the annex's strict reading). All of `LineBreakTest.txt` and `GraphemeBreakTest.txt` pass; the tests read the unmodified files from `tests/testdata/unicode`.

- **What breaks.** After spaces, hyphens and dashes, around the soft hyphen, after the zero width space, between ideographs and kana but not before closing punctuation or small kana, after a solidus before a letter, between emoji sequences and flag pairs but never inside one, and after hard breaks. It does not break in "$1,234.56", "-5.25", "12.5%", inside a word, after an opening bracket or quote even across spaces, before a closing bracket or quote, around the no-break space or the word joiner, or inside a Korean syllable block.
- **Thai, Lao, Khmer, Myanmar.** UAX #14 without dictionaries: their letters are class SA and are treated as letters, so there is an opportunity only at spaces, punctuation and explicit zero width spaces.
- **Grapheme clusters.** `GetGraphemeBoundaries` and `GetGraphemeLength` follow UAX #29 extended grapheme clusters (combining marks, emoji ZWJ sequences and modifiers, flags, Hangul jamo and syllables, Indic conjuncts).

## Fallback

`FontFallbackChain` picks, per grapheme cluster, the first face with a glyph for the cluster's first character, shapes each stretch of one face with that face's own tables, and keeps characters that no face covers on the first face (its missing-glyph box). Invisible characters (controls, zero width spaces and joiners, soft hyphens, bidi controls) stay with the face of the cluster before them.

## Fonts

TrueType (`glyf`), OpenType with CFF (CFF1) outlines, and TrueType Collections; `cmap` (a Unicode subtable is required), `head`, `hhea`, `hmtx`, `maxp`, `OS/2`, `name`, `post`, `kern`. Not read: WOFF, WOFF2, CFF2, variations (the default instance is used), colour tables, hinting instructions, vertical metrics.

Bounds: at most 256 faces per collection, 256 tables per face, 128 MiB per table, 8,192 cached outlines per face, 2^18 segments per outline. Every offset and length from the font is range-checked.
