namespace NoireLib.Helpers;

/// <summary>Storage format of one vertex element inside a game model's vertex buffer.</summary>
public enum GameVertexType : byte
{
    /// <summary>One 32-bit float.</summary>
    Single1 = 0,
    /// <summary>Two 32-bit floats.</summary>
    Single2 = 1,
    /// <summary>Three 32-bit floats.</summary>
    Single3 = 2,
    /// <summary>Four 32-bit floats.</summary>
    Single4 = 3,
    /// <summary>Four bytes, used unscaled (bone indices).</summary>
    UByte4 = 5,
    /// <summary>Two 16-bit signed integers.</summary>
    Short2 = 6,
    /// <summary>Four 16-bit signed integers.</summary>
    Short4 = 7,
    /// <summary>Four bytes normalized to 0..1.</summary>
    NByte4 = 8,
    /// <summary>Two 16-bit signed integers normalized to -1..1.</summary>
    NShort2 = 9,
    /// <summary>Four 16-bit signed integers normalized to -1..1.</summary>
    NShort4 = 10,
    /// <summary>Two half floats.</summary>
    Half2 = 13,
    /// <summary>Four half floats.</summary>
    Half4 = 14,
    /// <summary>Two 16-bit unsigned integers.</summary>
    UShort2 = 16,
    /// <summary>Four 16-bit unsigned integers.</summary>
    UShort4 = 17,
}
