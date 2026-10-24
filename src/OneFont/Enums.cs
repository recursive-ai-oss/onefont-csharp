namespace OneFont;

/// <summary>The base direction of a paragraph (Unicode Bidirectional Algorithm rules P2 and P3 choose it automatically).</summary>
public enum TextDirection
{
    /// <summary>The direction of the first strong character of the paragraph, left to right when there is none (the default).</summary>
    Auto,

    /// <summary>The paragraph is left to right.</summary>
    LeftToRight,

    /// <summary>The paragraph is right to left.</summary>
    RightToLeft,
}

/// <summary>Whether pair kerning from the font is applied.</summary>
public enum KerningMode
{
    /// <summary>Apply the font's kerning: GPOS pair adjustment for the Latin script, or the legacy <c>kern</c> table when there is none (the default).</summary>
    Normal,

    /// <summary>Use plain advances.</summary>
    None,
}

/// <summary>Whether a line may or must end at a position of the text (Unicode line breaking, UAX #14).</summary>
public enum LineBreakOpportunity : byte
{
    /// <summary>The line must not end here.</summary>
    None = 0,

    /// <summary>The line may end here.</summary>
    Allowed = 1,

    /// <summary>The line must end here (a paragraph or line separator ends before this position).</summary>
    Mandatory = 2,
}
