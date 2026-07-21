using System;

namespace NoireLib.UI;

/// <summary>
/// Marks a NoireUI surface for a grouped entry under <see cref="NoireUI"/>, so that a plugin author reaches it by
/// typing one root name instead of recalling the surface's own.<br/>
/// A generator emits a nested static class carrying a forward, with copied documentation, for every public static
/// member of the marked type. The marked type itself never moves and is not deprecated.
/// </summary>
/// <remarks>
/// Internal on purpose, unlike the library's consumer-facing generator attributes. The generated entries extend the
/// <see cref="NoireUI"/> partial class, and a partial class cannot be extended from another assembly, so a marker a
/// consumer could apply would never produce working output.
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
    /// Groups the surface under an explicit name, for the surfaces whose own name reads badly against the root.
    /// </summary>
    /// <param name="name">The name the surface is reached by under <see cref="NoireUI"/>.</param>
    public NoireFacadeAttribute(string name) => Name = name;

    /// <summary>
    /// The explicit grouped name, or <see langword="null"/> to take the name from the surface's own.
    /// </summary>
    public string? Name { get; }
}
