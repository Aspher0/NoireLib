namespace NoireLib.Helpers;

/// <summary>
/// The outcome of scoring one candidate against a query.
/// </summary>
/// <remarks>
/// Carries no indices of its own on purpose. Highlighting needs them and sorting does not, and a result that allocated
/// an array to hold them would allocate once per candidate per keystroke for the far more common case that throws them
/// away. The span overload of
/// <see cref="FuzzyMatcher.TryMatch(System.ReadOnlySpan{char}, System.ReadOnlySpan{char}, System.Span{int}, out FuzzyMatch, FuzzyScoring?)"/>
/// writes them into a buffer the caller owns.
/// </remarks>
/// <param name="Success">Whether every character of the query was found, in order.</param>
/// <param name="Score">How good the match is. Higher sorts first; meaningless when <paramref name="Success"/> is false.</param>
/// <param name="MatchedCount">How many indices were written, which is the length of the query when it matched.</param>
public readonly record struct FuzzyMatch(bool Success, int Score, int MatchedCount)
{
    /// <summary>A result meaning the candidate does not contain the query at all.</summary>
    public static FuzzyMatch None => new(false, 0, 0);
}
