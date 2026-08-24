namespace NoireLib.Helpers;

/// <summary>
/// What a fuzzy match is worth: the bonuses that pull a result up the list and the penalties that push it down.
/// </summary>
public sealed class FuzzyScoring
{
    /// <summary>What a match is worth before any bonus or penalty, so an ordinary match is a positive number.</summary>
    public int Base { get; set; } = 100;

    /// <summary>
    /// Added for each character matched directly after the previous one, multiplied by how long the run is so far.
    /// </summary>
    public int SequentialBonus { get; set; } = 15;

    /// <summary>Added for a match directly after a space, underscore, hyphen, dot or slash.</summary>
    public int SeparatorBonus { get; set; } = 30;

    /// <summary>Added for a match on a capital that follows a lower-case letter, which is a word boundary in a name.</summary>
    public int CamelBonus { get; set; } = 30;

    /// <summary>
    /// Added when the match starts at the first character of the candidate, on top of
    /// <see cref="SeparatorBonus"/>, which the first character also earns for being the start of a word.
    /// </summary>
    public int FirstLetterBonus { get; set; } = 15;

    /// <summary>Added for each character whose case matches the query exactly, breaking ties towards the obvious one.</summary>
    public int ExactCaseBonus { get; set; } = 4;

    /// <summary>Subtracted for each character skipped before the first match. Negative.</summary>
    public int LeadingPenalty { get; set; } = -5;

    /// <summary>The most <see cref="LeadingPenalty"/> can take off in total, so a long prefix is not fatal. Negative.</summary>
    public int MaxLeadingPenalty { get; set; } = -15;

    /// <summary>Subtracted for each character of the candidate left unmatched, which favours shorter candidates. Negative.</summary>
    public int UnmatchedPenalty { get; set; } = -1;

    /// <summary>
    /// The most <see cref="UnmatchedPenalty"/> can take off in total. Negative.
    /// </summary>
    public int MaxUnmatchedPenalty { get; set; } = -50;

    /// <summary>The characters treated as word separators for <see cref="SeparatorBonus"/>.</summary>
    public char[] Separators { get; set; } = [' ', '_', '-', '.', '/', '\\', ':', ','];

    /// <summary>
    /// Creates an independent copy, so a variant can be adjusted without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public FuzzyScoring Clone()
    {
        var clone = (FuzzyScoring)MemberwiseClone();
        clone.Separators = (char[])Separators.Clone();
        return clone;
    }
}
