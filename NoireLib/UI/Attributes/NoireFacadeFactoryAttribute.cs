using System;

namespace NoireLib.UI;

// Marks a constructible NoireUI widget for a creation method on NoireUI, one static method per public constructor.
// A type carries this marker or NoireFacadeAttribute, never both; the method is named after the widget with the
// library prefix removed, with no explicit-name override.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class NoireFacadeFactoryAttribute : Attribute
{
}
