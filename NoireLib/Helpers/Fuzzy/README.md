# Helper Documentation : FuzzyMatcher

You are reading the documentation for the `FuzzyMatcher` static helper.

## Table of Contents
- [Overview](#overview)
- [Matching](#matching)
- [Ranking a list](#ranking-a-list)
- [Highlighting the match](#highlighting-the-match)
- [How the score is built](#how-the-score-is-built)
- [Tuning the weights](#tuning-the-weights)
- [Cost](#cost)
- [Used by](#used-by)
- [See Also](#see-also)

---

## Overview

`FuzzyMatcher` is a static helper in the `NoireLib.Helpers` namespace that answers three questions about a piece of
text and something a user typed: does it match, how good is the match, and which characters matched.

A candidate matches when the query's characters all appear in it in order, ignoring case and whatever sits between
them, so `cmbl` finds `Combat Log`.

It is a plain data helper. It touches no ImGui and depends on nothing in `NoireLib.UI`, so it works just as readily
from a command handler or a background task as it does from a filter box.

The query is **one term**. Spaces are matched literally rather than splitting it, so `combat log` matches `Combat Log`
and not `Log of Combat`.

---

## Matching

```csharp
FuzzyMatcher.IsMatch("Combat Log", "cmbl");     // true
FuzzyMatcher.Score("Combat Log", "cmbl");       // 168 - higher sorts first
```

An empty query matches everything, which is what a filter box nobody has typed in should show.

**A score of zero means "did not match", and nothing else.** A successful match always scores at least one, whatever
weights were used, so `Score(...) > 0` is a valid test even with weights of your own.

---

## Ranking a list

The thing most callers actually want:

```csharp
// The destination is yours, so it can be reused across keystrokes instead of reallocated.
FuzzyMatcher.Rank(filtered, allCommands, query, command => command.Name);
```

An empty query keeps every item in its original order. Ties fall back to the original order too, so a list does not
reshuffle itself between keystrokes that happen to score the same.

There is an allocating overload returning a new list for when that does not matter.

**Rank when the query changes, not every frame.** Scoring a few thousand rows per keystroke is nothing; doing it at 144
FPS for a query that has not changed is waste.

---

## Highlighting the match

Showing which characters matched is most of what makes a fuzzy filter feel trustworthy rather than arbitrary. Without
it, a list that quietly reorders itself looks like it is guessing.

```csharp
Span<int> hits = stackalloc int[FuzzyMatcher.MaxQueryLength];

if (FuzzyMatcher.TryMatch(command.Name, query, hits, out var match))
    NoireText.Highlighted(command.Name, hits[..match.MatchedCount]);
```

The indices are positions in the candidate, ascending, one per query character. The buffer must be at least as long as
the query or the call **refuses** rather than reporting a partial match, because half a set of positions highlights the
wrong characters.

`NoireText.Highlighted` draws it, picking the matched characters out in the theme's accent. It stays on one line.

---

## How the score is built

Every match starts from a base and collects bonuses and penalties. The weights are tuned for what this is normally
filtering: short human-readable labels like command names, setting titles and item names.

| Signal | Default | Why |
|---|---|---|
| Consecutive run | `+15 x run length` | The strongest signal there is, and **it grows with the run**. Two adjacent characters say little; six in a row mean the query is simply a substring of the answer. |
| Word start | `+30` | A match after a space, underscore, hyphen, dot or slash. This is what makes initialisms work. |
| CamelCase boundary | `+30` | A capital following a lower-case letter is a word start inside an identifier. |
| First character | `+15` | On top of the word-start bonus, which the first character also earns for being the start of a word. |
| Exact case | `+4` | Small on purpose: it breaks ties towards the obvious answer without overriding anything real. |
| Leading skip | `-5` each, `-15` total | A match further into the string is worse, but not fatally. |
| Unmatched characters | `-1` each, `-50` total | Prefers shorter candidates. **Capped**, because length is a tiebreaker and must not become the ranking. |

Two of these are worth knowing about, because they decide the results you will actually see:

**A run bonus that grows means an exact substring beats everything.** `colour` ranks `Colour Picker` first even though
several other entries match, because six consecutive characters outweigh any combination of boundary bonuses.

**A word-start bonus larger than a two-character run means initialisms win.** For `cl`, `Copy Link` and `Combat Log`
rank above `Close Window`, because reading the query as initials is what a command palette is for. If your list is
better served by prefixes, raise `SequentialBonus` or lower `SeparatorBonus`.

---

## Tuning the weights

Every weight is a public property on `FuzzyScoring`. Set the default for the whole plugin, or hand one call its own:

```csharp
// A list of file paths cares more about path segments.
FuzzyMatcher.Scoring = new FuzzyScoring { SeparatorBonus = 45 };

// Or per call, leaving the default alone.
var score = FuzzyMatcher.Score(path, query, pathScoring);
```

`Separators` is the set of characters treated as word boundaries, and `Clone()` copies it rather than sharing it.

---

## Cost

Matching is a single pass to reject non-matches, then a bounded search for the best positions among matches. Nothing
allocates on the matching path: the convenience overloads use the stack, and the span overload writes into a buffer you
own.

The search explores alternative positions because greedy matching picks the wrong ones: `cl` against `Combat Log` taken
greedily is the `C` and the `l` of `Combat`, which highlights the wrong letters and scores as a mid-word match.
`RecursionBudget` bounds that exploration, which is what stops a pathological candidate (a long run of one repeated
character) from costing exponential time.

`MaxQueryLength` caps the query the convenience overloads consider. The span overload takes a query of any length.

---

## Used by

- `NoireComboBox<T>`, whose dropdown filter matches and orders through this by default (`FilterFuzzy`), and picks out
  the matched characters in the option list (`FilterHighlight`).
- `NoireText.Highlighted`, which draws the matched characters.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [NoireUI Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/UI/README.md)
