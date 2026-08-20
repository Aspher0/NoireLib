namespace NoireLib.Helpers.Memory;

/// <summary>
/// The live implementation: <see cref="GuardedMemory"/> answers the readability question and the reads dereference
/// process memory directly.
/// </summary>
public sealed unsafe class ProcessGuardedMemory : IGuardedMemory
{
    /// <summary>The shared stateless instance.</summary>
    public static readonly ProcessGuardedMemory Instance = new();

    /// <summary>Prevents external construction, since the type is stateless.</summary>
    private ProcessGuardedMemory()
    {
    }

    /// <inheritdoc/>
    public bool IsReadable(long address, int length) => GuardedMemory.IsReadable((nint)address, length);

    /// <inheritdoc/>
    public byte ReadByte(long address) => *(byte*)address;

    /// <inheritdoc/>
    public uint ReadUInt32(long address) => *(uint*)address;

    /// <inheritdoc/>
    public long ReadInt64(long address) => *(long*)address;
}
