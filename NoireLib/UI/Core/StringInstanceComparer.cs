using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NoireLib.UI;

/// <summary>
/// Matches strings by reference rather than by content, for dictionaries whose keys are guaranteed to arrive as the
/// same instance every time.<br/>
/// Only interned compile-time literals, including <see cref="CallerFilePathAttribute"/> and
/// <see cref="CallerMemberNameAttribute"/> arguments, and the ids <see cref="UiIds"/> caches qualify as keys.
/// </summary>
internal sealed class StringInstanceComparer : IEqualityComparer<string>
{
    /// <summary>The shared instance.</summary>
    internal static readonly StringInstanceComparer Instance = new();

    private StringInstanceComparer()
    {
    }

    /// <inheritdoc/>
    public bool Equals(string? left, string? right) => ReferenceEquals(left, right);

    /// <inheritdoc/>
    public int GetHashCode(string value) => RuntimeHelpers.GetHashCode(value);
}
