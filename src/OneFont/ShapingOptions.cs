using OneFont.Fonts;
using OneFont.Shaping;

namespace OneFont;

/// <summary>Options for <see cref="TextShaping.ShapeRun(ReadOnlySpan{char}, FontFace, bool, ShapingOptions?)"/>.</summary>
public sealed class ShapingOptions
{
    /// <summary>
    /// Gets or sets the BCP 47 language tag the font's language-specific features are chosen for (for example <c>tr</c> or <c>sr</c>).
    /// <see langword="null"/> (the default) selects the font's default language system.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>Gets or sets whether pair kerning is applied. The default is <see cref="KerningMode.Normal"/>.</summary>
    public KerningMode Kerning { get; set; }

    /// <summary>
    /// Gets the OpenType feature settings applied on top of the script's default features (<see cref="FontFeature"/> with a four-letter tag
    /// and a value; 0 switches a feature off). The list is empty by default and is created on first access, so an options object that only
    /// sets <see cref="Language"/> or <see cref="Kerning"/> allocates nothing more.
    /// </summary>
    /// <remarks>
    /// Shaping only reads the options. One instance may be shared by any number of calls and threads as long as nobody changes it while
    /// they run; shaping never creates the list, so a shared instance whose <see cref="Features"/> was never touched is safe to read
    /// concurrently. Reading <see cref="Features"/> yourself on a shared instance creates the list and is not thread-safe.
    /// </remarks>
    public IList<FontFeature> Features => _features ??= [];

    private List<FontFeature>? _features;

    // The feature set the current settings stand for. Shaping asks on every call, so the last answer is kept with the settings it was
    // built from and reused while they have not changed (a caller shaping many runs with one options object allocates nothing here).
    private volatile Snapshot? _snapshot;

    internal FeatureSet GetFeatureSet()
    {
        List<FontFeature>? features = _features;
        int n = features?.Count ?? 0;
        if (Kerning == KerningMode.Normal && n == 0)
        {
            return FeatureSet.Empty;
        }

        KerningMode kerning = Kerning;
        Snapshot? s = _snapshot;
        if (s is not null && s.Kerning == kerning && s.Features.Length == n)
        {
            bool same = true;
            for (int i = 0; i < n && same; i++)
            {
                same = s.Features[i].Equals(features![i]);
            }

            if (same)
            {
                return s.Set;
            }
        }

        var copy = new FontFeature[n];
        features?.CopyTo(copy, 0);
        var settings = new List<(uint, int)>(n + 1);
        if (kerning != KerningMode.Normal)
        {
            settings.Add((Tag.Kern, 0));
        }

        foreach (FontFeature f in copy)
        {
            if (f.Tag is not null)
            {
                settings.Add((Be.Tag(f.Tag), f.Value));
            }
        }

        FeatureSet set = FeatureSet.Create(settings);
        _snapshot = new Snapshot(kerning, copy, set);
        return set;
    }

    private sealed class Snapshot(KerningMode kerning, FontFeature[] features, FeatureSet set)
    {
        public KerningMode Kerning { get; } = kerning;

        public FontFeature[] Features { get; } = features;

        public FeatureSet Set { get; } = set;
    }
}
