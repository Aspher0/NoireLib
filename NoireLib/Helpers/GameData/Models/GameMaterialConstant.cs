namespace NoireLib.Helpers;

/// <summary>A shader constant's location inside the material's packed value blob.</summary>
/// <param name="Id">Identifier of the constant, resolvable through <see cref="GameShaderNames"/>.</param>
/// <param name="ByteOffset">Offset into the value blob.</param>
/// <param name="ByteSize">Size of the value in bytes.</param>
public readonly record struct GameMaterialConstant(uint Id, ushort ByteOffset, ushort ByteSize);
