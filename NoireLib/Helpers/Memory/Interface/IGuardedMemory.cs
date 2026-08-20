namespace NoireLib.Helpers.Memory;

/// <summary>
/// Guarded reads behind an interface, so pointer arithmetic over a game structure can be run against a fake with
/// no game present.
/// </summary>
public interface IGuardedMemory
{
    /// <summary>Whether a whole span is committed and safe to dereference.</summary>
    /// <param name="address">The span's start.</param>
    /// <param name="length">The span's length in bytes.</param>
    /// <returns>True when the whole span may be dereferenced.</returns>
    bool IsReadable(long address, int length);

    /// <summary>Reads one byte, valid only where <see cref="IsReadable"/> agreed.</summary>
    /// <param name="address">The address to read.</param>
    /// <returns>The byte at that address.</returns>
    byte ReadByte(long address);

    /// <summary>Reads four bytes, valid only where <see cref="IsReadable"/> agreed.</summary>
    /// <param name="address">The address to read.</param>
    /// <returns>The value at that address.</returns>
    uint ReadUInt32(long address);

    /// <summary>Reads eight bytes, valid only where <see cref="IsReadable"/> agreed.</summary>
    /// <param name="address">The address to read.</param>
    /// <returns>The value at that address.</returns>
    long ReadInt64(long address);
}
