using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// One fuzzy scorer, for every filter box in a plugin.<br/>
/// A candidate matches when the query's characters all appear in it in order, ignoring case and whatever sits between
/// them, so "cmbl" finds "Combat Log". The score says how good the match was, so the obvious answer sorts to the top,
/// and the matched positions come back so they can be highlighted.
/// </summary>
/// <remarks>
/// Nothing here touches ImGui: this is a plain data helper, usable from a command or a background task just as readily
/// as from a window.<br/>
/// The query is one term. Spaces in it are matched literally rather than splitting it, so "combat log" matches
/// "Combat Log" and not "Log of Combat".<br/>
/// <b>On the hot path, prefer the span overloads.</b> <see cref="Score(string?, string?, FuzzyScoring?)"/> and
/// <see cref="IsMatch(string?, string?)"/> allocate nothing either, but they cap the query length; a filter that runs
/// per keystroke over thousands of rows should rank once into a list the caller reuses rather than re-scoring per
/// frame. See <see cref="Rank{T}(List{T}, IEnumerable{T}, string?, Func{T, string}, FuzzyScoring?)"/>.
/// </remarks>
/// <example>
/// <code>
/// // Filter and order a list when the query changes.
/// FuzzyMatcher.Rank(filtered, allCommands, query, c => c.Name);
///
/// // Highlight the matched characters while drawing one row.
/// Span&lt;int&gt; hits = stackalloc int[FuzzyMatcher.MaxQueryLength];
/// if (FuzzyMatcher.TryMatch(command.Name, query, hits, out var match))
///     DrawHighlighted(command.Name, hits[..match.MatchedCount]);
/// </code>
/// </example>
public static class FuzzyMatcher
{
    /// <summary>
    /// The longest query the convenience overloads consider. A filter box nobody has typed a novel into never reaches
    /// it, and the span overloads take a query of any length.
    /// </summary>
    public const int MaxQueryLength = 128;

    /// <summary>
    /// How many alternative match positions are explored before the best one found so far is accepted.
    /// </summary>
    /// <remarks>
    /// Matching greedily takes the first occurrence of each character, which is usually right and is sometimes badly
    /// wrong: "cl" against "Combat Log" would take the C and then the l of "Combat" rather than the L of "Log", and
    /// highlight the wrong letters. Exploring the alternatives fixes that. The budget is what keeps a pathological
    /// candidate (a long string of one repeated letter) from costing exponential time.
    /// </remarks>
    public const int RecursionBudget = 10;

    /// <summary>
    /// The weights every score is computed with, unless a call is given its own.
    /// </summary>
    public static FuzzyScoring Scoring { get; set; } = new();

    #region Matching

    /// <summary>
    /// Whether the query appears in the candidate at all.
    /// </summary>
    /// <param name="candidate">The text being filtered.</param>
    /// <param name="query">What the user typed. An empty query matches everything.</param>
    /// <returns>True when every character of the query appears in order.</returns>
    public static bool IsMatch(string? candidate, string? query)
    {
        if (string.IsNullOrEmpty(query))
            return true;

        if (string.IsNullOrEmpty(candidate))
            return false;

        return IsSubsequence(candidate.AsSpan(), query.AsSpan());
    }

    /// <summary>
    /// Scores a candidate against a query, without reporting where it matched.
    /// </summary>
    /// <param name="candidate">The text being filtered.</param>
    /// <param name="query">What the user typed.</param>
    /// <param name="scoring">The weights to use. When <see langword="null"/>, <see cref="Scoring"/> is used.</param>
    /// <returns>
    /// The score, or zero when it does not match. A match always scores at least one, so zero means "no match" and
    /// nothing else, whatever weights were used. An empty query scores every candidate equally.
    /// </returns>
    public static int Score(string? candidate, string? query, FuzzyScoring? scoring = null)
        => TryMatch(candidate, query, out var match, scoring) ? match.Score : 0;

    /// <summary>
    /// Matches a candidate against a query, without reporting where it matched.
    /// </summary>
    /// <param name="candidate">The text being filtered.</param>
    /// <param name="query">What the user typed.</param>
    /// <param name="match">The outcome.</param>
    /// <param name="scoring">The weights to use. When <see langword="null"/>, <see cref="Scoring"/> is used.</param>
    /// <returns>True when it matched.</returns>
    public static bool TryMatch(string? candidate, string? query, out FuzzyMatch match, FuzzyScoring? scoring = null)
    {
        if (string.IsNullOrEmpty(query))
        {
            match = new FuzzyMatch(true, 0, 0);
            return true;
        }

        if (string.IsNullOrEmpty(candidate))
        {
            match = FuzzyMatch.None;
            return false;
        }

        var trimmed = query.Length > MaxQueryLength ? query.AsSpan(0, MaxQueryLength) : query.AsSpan();

        Span<int> indices = stackalloc int[MaxQueryLength];
        return TryMatch(candidate.AsSpan(), trimmed, indices, out match, scoring);
    }

    /// <summary>
    /// Matches a candidate against a query and reports which characters matched, for highlighting.
    /// </summary>
    /// <remarks>
    /// The indices are positions in <paramref name="candidate"/>, ascending, and there are as many of them as the
    /// query is long. <paramref name="matchedIndices"/> must be at least that long or the call refuses rather than
    /// reporting a partial match.
    /// </remarks>
    /// <param name="candidate">The text being filtered.</param>
    /// <param name="query">What the user typed.</param>
    /// <param name="matchedIndices">Receives the matched positions. At least as long as the query.</param>
    /// <param name="match">The outcome, carrying the score and how many indices were written.</param>
    /// <param name="scoring">The weights to use. When <see langword="null"/>, <see cref="Scoring"/> is used.</param>
    /// <returns>True when it matched.</returns>
    public static bool TryMatch(ReadOnlySpan<char> candidate, ReadOnlySpan<char> query, Span<int> matchedIndices, out FuzzyMatch match, FuzzyScoring? scoring = null)
    {
        match = FuzzyMatch.None;

        if (query.IsEmpty)
        {
            match = new FuzzyMatch(true, 0, 0);
            return true;
        }

        if (candidate.IsEmpty || matchedIndices.Length < query.Length)
            return false;

        // Rejected here rather than inside the search, because the great majority of candidates in any list do not
        // match at all and this answers that in one pass without exploring anything.
        if (!IsSubsequence(candidate, query))
            return false;

        Span<int> working = query.Length <= MaxQueryLength ? stackalloc int[query.Length] : new int[query.Length];

        var state = new MatchState
        {
            Candidate = candidate,
            Query = query,
            Working = working,
            Best = matchedIndices,
            Scoring = scoring ?? Scoring,
        };

        Search(ref state, 0, 0, 0);

        if (!state.HasBest)
            return false;

        match = new FuzzyMatch(true, state.BestScore, query.Length);
        return true;
    }

    #endregion

    #region Ranking

    /// <summary>
    /// Fills a list with the items that match, best first.
    /// </summary>
    /// <remarks>
    /// The destination is the caller's so it can be reused across keystrokes rather than reallocated. It is cleared
    /// first.<br/>
    /// An empty query keeps every item in its original order, which is what a filter box that has just been opened
    /// should show.
    /// </remarks>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="destination">Receives the matches. Cleared before filling.</param>
    /// <param name="source">The items to filter.</param>
    /// <param name="query">What the user typed.</param>
    /// <param name="text">Reads the text to match against an item.</param>
    /// <param name="scoring">The weights to use. When <see langword="null"/>, <see cref="Scoring"/> is used.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/>, <paramref name="source"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public static void Rank<T>(List<T> destination, IEnumerable<T> source, string? query, Func<T, string> text, FuzzyScoring? scoring = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(text);

        destination.Clear();

        if (string.IsNullOrEmpty(query))
        {
            destination.AddRange(source);
            return;
        }

        var scored = new List<(T Item, int Score, int Order)>();
        var order = 0;

        foreach (var item in source)
        {
            if (TryMatch(text(item), query, out var match, scoring))
                scored.Add((item, match.Score, order));

            order++;
        }

        // Ties fall back to the original order rather than to whatever the sort happens to do with them, so a list
        // does not reshuffle itself between keystrokes that score the same.
        scored.Sort(static (left, right) => right.Score != left.Score
            ? right.Score.CompareTo(left.Score)
            : left.Order.CompareTo(right.Order));

        foreach (var entry in scored)
            destination.Add(entry.Item);
    }

    /// <summary>
    /// Returns the items that match, best first.
    /// </summary>
    /// <remarks>
    /// Allocates a list per call. Use
    /// <see cref="Rank{T}(List{T}, IEnumerable{T}, string?, Func{T, string}, FuzzyScoring?)"/> where the query changes
    /// often enough for that to matter.
    /// </remarks>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="source">The items to filter.</param>
    /// <param name="query">What the user typed.</param>
    /// <param name="text">Reads the text to match against an item.</param>
    /// <param name="scoring">The weights to use. When <see langword="null"/>, <see cref="Scoring"/> is used.</param>
    /// <returns>The matching items, best first.</returns>
    public static List<T> Rank<T>(IEnumerable<T> source, string? query, Func<T, string> text, FuzzyScoring? scoring = null)
    {
        var destination = new List<T>();
        Rank(destination, source, query, text, scoring);
        return destination;
    }

    #endregion

    #region Searching

    /// <summary>
    /// The working set of one search. A ref struct so the spans can be carried without copying them through every
    /// level of the recursion.
    /// </summary>
    private ref struct MatchState
    {
        public ReadOnlySpan<char> Candidate;
        public ReadOnlySpan<char> Query;
        public Span<int> Working;
        public Span<int> Best;
        public FuzzyScoring Scoring;
        public int BestScore;
        public bool HasBest;
        public int Recursions;
    }

    /// <summary>
    /// Whether every character of the query appears in the candidate in order. The cheap rejection.
    /// </summary>
    private static bool IsSubsequence(ReadOnlySpan<char> candidate, ReadOnlySpan<char> query)
    {
        var queryIndex = 0;

        for (var i = 0; i < candidate.Length && queryIndex < query.Length; i++)
        {
            if (Same(candidate[i], query[queryIndex]))
                queryIndex++;
        }

        return queryIndex == query.Length;
    }

    /// <summary>
    /// Walks the candidate matching the query, exploring the alternative position for each character it could have
    /// taken, and keeps the best-scoring complete match it finds.
    /// </summary>
    private static void Search(ref MatchState state, int candidateIndex, int queryIndex, int matchCount)
    {
        while (queryIndex < state.Query.Length && candidateIndex < state.Candidate.Length)
        {
            if (Same(state.Candidate[candidateIndex], state.Query[queryIndex]))
            {
                // The same query character may also match later in the candidate, and that later position may be the
                // better one. Explore it first, within the budget, then take this one and carry on.
                if (state.Recursions < RecursionBudget)
                {
                    state.Recursions++;
                    Search(ref state, candidateIndex + 1, queryIndex, matchCount);
                }

                state.Working[matchCount++] = candidateIndex;
                queryIndex++;
            }

            candidateIndex++;
        }

        if (queryIndex < state.Query.Length)
            return;

        var score = ScoreMatch(ref state, matchCount);

        if (state.HasBest && score <= state.BestScore)
            return;

        state.HasBest = true;
        state.BestScore = score;
        state.Working[..matchCount].CopyTo(state.Best);
    }

    /// <summary>
    /// Scores one complete match from where its characters landed.
    /// </summary>
    private static int ScoreMatch(ref MatchState state, int matchCount)
    {
        var scoring = state.Scoring;
        var score = scoring.Base;

        var leading = state.Working[0];
        score += Math.Max(scoring.LeadingPenalty * leading, scoring.MaxLeadingPenalty);
        score += Math.Max(scoring.UnmatchedPenalty * (state.Candidate.Length - matchCount), scoring.MaxUnmatchedPenalty);

        var run = 0;

        for (var i = 0; i < matchCount; i++)
        {
            var index = state.Working[i];

            if (i > 0 && state.Working[i - 1] == index - 1)
            {
                run++;
                score += scoring.SequentialBonus * run;
            }
            else
            {
                run = 0;
            }

            if (index == 0)
            {
                // The first character of the candidate starts a word as much as one after a space does, so it earns
                // the boundary bonus as well. Without that a match at the very start scores no better than one in the
                // middle, which is the opposite of what a person filtering a list expects.
                score += scoring.SeparatorBonus + scoring.FirstLetterBonus;
            }
            else
            {
                var current = state.Candidate[index];
                var previous = state.Candidate[index - 1];

                if (char.IsUpper(current) && char.IsLower(previous))
                    score += scoring.CamelBonus;

                if (Array.IndexOf(scoring.Separators, previous) >= 0)
                    score += scoring.SeparatorBonus;
            }

            if (state.Candidate[index] == state.Query[i])
                score += scoring.ExactCaseBonus;
        }

        // Floored so that a score of zero always means "did not match", whatever weights were handed in. The shipped
        // weights cannot reach zero, but a caller's can, and a real match reported as a non-match is a silent one.
        return Math.Max(score, 1);
    }

    private static bool Same(char left, char right)
        => left == right || char.ToUpperInvariant(left) == char.ToUpperInvariant(right);

    #endregion
}
