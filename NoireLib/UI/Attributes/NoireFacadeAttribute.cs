using System;

namespace NoireLib.UI;

/// <summary>
/// Marks a NoireUI surface for a grouped entry under <see cref="NoireUI"/>. A generator emits a nested static class
/// carrying a forward, with copied documentation, for every public static member of the marked type; the marked
/// type itself never moves and is not deprecated.
/// </summary>
/// <remarks>
/// Internal only: the generated entries extend the <see cref="NoireUI"/> partial class, which a consumer's assembly
/// cannot extend.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class NoireFacadeAttribute : Attribute
{
    /// <summary>
    /// Groups the surface under its own name with the library prefix removed.
    /// </summary>
    public NoireFacadeAttribute()
    {
    }

    /// <summary>
    /// Groups the surface under an explicit name.
    /// </summary>
    /// <param name="name">The name the surface is reached by under <see cref="NoireUI"/>.</param>
    public NoireFacadeAttribute(string name) => Name = name;

    /// <summary>
    /// The explicit grouped name, or <see langword="null"/> to take the name from the surface's own.
    /// </summary>
    public string? Name { get; }
}
