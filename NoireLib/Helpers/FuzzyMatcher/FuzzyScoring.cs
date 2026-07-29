namespace NoireLib.Helpers;

/// <summary>
/// What a fuzzy match is worth: the bonuses that pull a result up the list and the penalties that push it down.
/// </summary>
/// <remarks>
/// The shipped weights are tuned for the thing this is normally filtering, which is a list of short human-readable
/// labels: command names, setting titles, item names. They favour matches that a person would call obvious, in this
/// order: a run of consecutive characters, a match at the start of a word, and a match at the start of the string.<br/>
/// Change them when the thing being filtered is not that. A list of file paths wants a larger
/// <see cref="SeparatorBonus"/>; a list of identifiers wants a larger <see cref="CamelBonus"/>.
/// </remarks>
public sealed class FuzzyScoring
{
    /// <summary>What a match is worth before any bonus or penalty, so an ordinary match is a positive number.</summary>
    public int Base { get; set; } = 100;

    /// <summary>
    /// Added for each character matched directly after the previous one, multiplied by how long the run is so far.
    /// </summary>
    /// <remarks>
    /// Multiplied rather than flat because run length is the signal, not run existence: two characters that happen to
    /// be adjacent say very little, and six in a row mean the query is simply a substring of the candidate and should
    /// beat everything else in the list. A flat bonus cannot express that, and lets a single word-boundary match
    /// outrank a complete run.
    /// </remarks>
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
    /// <remarks>
    /// Length is meant to break ties between otherwise comparable candidates, not to decide the ranking. Uncapped it
    /// does decide it: against a few thousand characters the penalty reaches into the thousands and buries every bonus
    /// the match earned, so a long candidate containing the query outright loses to a short one that barely matches.
    /// It also drives a real match's score below zero, which costs
    /// <see cref="FuzzyMatcher.Score(string?, string?, FuzzyScoring?)"/> its "zero means no match" contract.
    /// </remarks>
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
