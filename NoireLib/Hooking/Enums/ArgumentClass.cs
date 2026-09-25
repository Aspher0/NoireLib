namespace NoireLib.Hooking;

// What a detour reads for an argument or a return value. Two types in the same class are interchangeable in a hook
// signature; two in different classes are not.
internal enum ArgumentClass
{
    Void,

    // Any pointer, nint, nuint, long or ulong.
    Register8,

    // bool, byte or sbyte.
    Integer1,

    // short, ushort or char.
    Integer2,

    // int or uint.
    Integer4,

    // float, passed in a vector register.
    Float4,

    // double, passed in a vector register.
    Float8,

    // A struct or any other type, compared by identity.
    Aggregate,
}
