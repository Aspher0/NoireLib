using System;

namespace NoireLib.UI;

// A generator emits a nested static class under NoireUI carrying a forward, with copied documentation, for every
// public static member of the marked type; the marked type itself never moves and is not deprecated.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class NoireFacadeAttribute : Attribute
{
    // Groups the surface under its own name with the library prefix removed.
    public NoireFacadeAttribute()
    {
    }

    public NoireFacadeAttribute(string name) => Name = name;

    // Null takes the name from the surface's own.
    public string? Name { get; }
}
