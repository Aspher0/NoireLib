namespace NoireLib.Localizer;

/// <summary>The plural categories defined by Unicode CLDR.</summary>
public enum PluralCategory
{
    /// <summary>Zero, in languages that single it out (Arabic).</summary>
    Zero,

    /// <summary>One.</summary>
    One,

    /// <summary>Two, in languages that single it out (Arabic, Hebrew, Slovenian).</summary>
    Two,

    /// <summary>A few (Slavic languages, Arabic, Romanian).</summary>
    Few,

    /// <summary>Many.</summary>
    Many,

    /// <summary>Everything else; the only category in Japanese, Korean and Chinese.</summary>
    Other,
}
