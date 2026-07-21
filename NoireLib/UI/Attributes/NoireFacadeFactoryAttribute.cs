using System;

namespace NoireLib.UI;

/// <summary>
/// Marks a constructible NoireUI widget for a creation method on <see cref="NoireUI"/>, so that a widget an author
/// builds and drives is browsable from the same root as the surfaces they call statically.<br/>
/// A generator emits one static method per public constructor, with copied documentation. Constructing the widget
/// directly stays fully supported and is not deprecated: the creation method is an additional door, never a
/// replacement one.
/// </summary>
/// <remarks>
/// Separate from <see cref="NoireFacadeAttribute"/> rather than inferred from the shape of the marked type, because a
/// type that gained a public static member would otherwise turn its creation method into a nested class of the same
/// name and break every call site that used it. A type carries one marker or the other, never both: one system gets
/// one entry point.<br/>
/// The creation method is named after the widget's own name with the library prefix removed. No widget has yet read
/// badly against the root the way two surfaces do, so this marker carries no explicit-name override; it gains one the
/// day a widget needs it.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class NoireFacadeFactoryAttribute : Attribute
{
}
