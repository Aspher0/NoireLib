using System;

namespace NoireLib.UI;

/// <summary>
/// Marks a constructible NoireUI widget for a creation method on <see cref="NoireUI"/>. A generator emits one
/// static method per public constructor, with copied documentation; constructing the widget directly remains fully
/// supported and is not deprecated.
/// </summary>
/// <remarks>
/// Separate from <see cref="NoireFacadeAttribute"/> rather than inferred from the shape of the marked type: a type
/// carries one marker or the other, never both. The creation method is named after the widget's own name with the
/// library prefix removed; this marker carries no explicit-name override.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class NoireFacadeFactoryAttribute : Attribute
{
}
