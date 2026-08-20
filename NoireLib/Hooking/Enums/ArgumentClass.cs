namespace NoireLib.Hooking;

/// <summary>
/// What a detour reads for an argument or a return value. Two types in the same class are interchangeable in a
/// hook signature; two in different classes are not.
/// </summary>
internal enum ArgumentClass
{
    /// <summary>
    /// Nothing is returned.
    /// </summary>
    Void,

    /// <summary>
    /// A full general-purpose register: any pointer, <c>nint</c>, <c>nuint</c>, <c>long</c> or <c>ulong</c>.
    /// </summary>
    Register8,

    /// <summary>
    /// One byte: <c>bool</c>, <c>byte</c> or <c>sbyte</c>.
    /// </summary>
    Integer1,

    /// <summary>
    /// Two bytes: <c>short</c>, <c>ushort</c> or <c>char</c>.
    /// </summary>
    Integer2,

    /// <summary>
    /// Four bytes: <c>int</c> or <c>uint</c>.
    /// </summary>
    Integer4,

    /// <summary>
    /// A single-precision <c>float</c>, passed in a vector register.
    /// </summary>
    Float4,

    /// <summary>
    /// A double-precision <c>double</c>, passed in a vector register.
    /// </summary>
    Float8,

    /// <summary>
    /// A struct or any other type, compared by identity.
    /// </summary>
    Aggregate,
}
