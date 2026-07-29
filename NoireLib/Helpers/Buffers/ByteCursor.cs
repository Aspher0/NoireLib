using System;

namespace NoireLib.Helpers;

/// <summary>
/// A sequential little-endian reader over a byte buffer, for walking a binary file header field by field without
/// tracking an offset by hand.<br/>
/// Reads past the end throw, so a truncated file fails at the read rather than returning a plausible wrong value.
/// Positional reads that do not advance are in <see cref="BufferHelper"/>.
/// </summary>
/// <param name="data">The buffer to read.</param>
public struct ByteCursor(byte[] data)
{
    private readonly byte[] data = data;

    /// <summary>
    /// Current read position, settable to seek.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Reads one byte and advances.
    /// </summary>
    /// <returns>The byte.</returns>
    public byte U8() => data[Position++];

    /// <summary>
    /// Reads a little-endian unsigned 16-bit value and advances.
    /// </summary>
    /// <returns>The value.</returns>
    public ushort U16()
    {
        var value = BitConverter.ToUInt16(data, Position);
        Position += sizeof(ushort);
        return value;
    }

    /// <summary>
    /// Reads a little-endian unsigned 32-bit value and advances.
    /// </summary>
    /// <returns>The value.</returns>
    public uint U32()
    {
        var value = BitConverter.ToUInt32(data, Position);
        Position += sizeof(uint);
        return value;
    }

    /// <summary>
    /// Advances without reading.
    /// </summary>
    /// <param name="count">Bytes to skip; may be negative to go back.</param>
    public void Skip(int count) => Position += count;
}
