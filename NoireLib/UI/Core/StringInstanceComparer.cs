using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NoireLib.UI;

// Matches strings by reference rather than by content: only interned compile-time literals, caller-info arguments
// and the ids UiIds caches qualify as keys.
internal sealed class StringInstanceComparer : IEqualityComparer<string>
{
    internal static readonly StringInstanceComparer Instance = new();

    private StringInstanceComparer()
    {
    }

    public bool Equals(string? left, string? right) => ReferenceEquals(left, right);

    public int GetHashCode(string value) => RuntimeHelpers.GetHashCode(value);
}
