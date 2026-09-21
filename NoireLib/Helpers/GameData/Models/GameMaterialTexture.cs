namespace NoireLib.Helpers;

/// <summary>A texture slot of a game material.</summary>
/// <param name="Path">Archive path of the texture.</param>
/// <param name="Flags">Slot flags. Bit 0x8000 marks the DirectX 11 variant of the file.</param>
public readonly record struct GameMaterialTexture(string Path, ushort Flags)
{
    /// <summary>Whether this slot refers to the DirectX 11 variant of the texture.</summary>
    public bool IsDx11 => (Flags & 0x8000) != 0;
}
