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
    /// and a value; 0 switches a feature off). The list is empty by default.
    /// </summary>
    public IList<FontFeature> Features { get; } = [];
}
