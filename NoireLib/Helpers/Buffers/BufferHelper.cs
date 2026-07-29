using System;
using System.Numerics;
using System.Text;

namespace NoireLib.Helpers;

/// <summary>
/// Positional reads over a byte buffer, for the parts of a binary file addressed by offset rather than walked in
/// order. Use <see cref="ByteCursor"/> when the read is sequential.
/// </summary>
public static class BufferHelper
{
    /// <summary>
    /// Reads a null-terminated string.
    /// </summary>
    /// <param name="data">The buffer; slice it to bound the search to a string table.</param>
    /// <param name="start">Index of the first byte of the string.</param>
    /// <param name="encoding">Text encoding; UTF-8 when null.</param>
    /// <returns>The string, ending at the terminator or at the end of <paramref name="data"/>.</returns>
    public static string ReadNullTerminatedString(ReadOnlySpan<byte> data, int start, Encoding? encoding = null)
        => ReadNullTerminatedString(data, start, out _, encoding);

    /// <summary>
    /// Reads a null-terminated string and reports where the next one begins, for walking a run of strings packed back
    /// to back.
    /// </summary>
    /// <param name="data">The buffer; slice it to bound the search to a string table.</param>
    /// <param name="start">Index of the first byte of the string.</param>
    /// <param name="nextStart">Index just past the terminator, which may be past the end of <paramref name="data"/> for the last string.</param>
    /// <param name="encoding">Text encoding; UTF-8 when null.</param>
    /// <returns>The string, ending at the terminator or at the end of <paramref name="data"/>.</returns>
    public static string ReadNullTerminatedString(ReadOnlySpan<byte> data, int start, out int nextStart, Encoding? encoding = null)
    {
        if (start < 0 || start >= data.Length)
        {
            nextStart = start + 1;
            return string.Empty;
        }

        var end = start;
        while (end < data.Length && data[end] != 0)
            end++;

        nextStart = end + 1;
        return (encoding ?? Encoding.UTF8).GetString(data[start..end]);
    }

    /// <summary>
    /// Reads three little-endian floats as a vector.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="offset">Byte offset of the first component.</param>
    /// <returns>The vector.</returns>
    public static Vector3 ReadVector3(ReadOnlySpan<byte> data, int offset)
        => new(
            BitConverter.ToSingle(data[offset..]),
            BitConverter.ToSingle(data[(offset + 4)..]),
            BitConverter.ToSingle(data[(offset + 8)..]));

    /// <summary>
    /// Reads four little-endian floats as a vector, the sixteen-byte row a constant buffer is laid out in.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="offset">Byte offset of the first component.</param>
    /// <returns>The vector.</returns>
    public static Vector4 ReadVector4(ReadOnlySpan<byte> data, int offset)
        => new(
            BitConverter.ToSingle(data[offset..]),
            BitConverter.ToSingle(data[(offset + 4)..]),
            BitConverter.ToSingle(data[(offset + 8)..]),
            BitConverter.ToSingle(data[(offset + 12)..]));
}
