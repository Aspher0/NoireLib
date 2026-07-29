using System;

namespace NoireLib.Helpers;

/// <summary>
/// CRC-32 over the reflected IEEE 802.3 polynomial, in the standard configuration and in the open one a format may
/// require.<br/>
/// This detects damage, not tampering: a CRC is public and deterministic, so anyone who edits a payload can recompute
/// it. Use <see cref="EncryptionHelper"/> when the question is who wrote something.
/// </summary>
public static class Crc32Helper
{
    private const uint Polynomial = 0xEDB88320u;

    private static readonly uint[] Table = BuildTable();

    /// <summary>
    /// Computes the CRC-32 of a byte span.
    /// </summary>
    /// <param name="data">The bytes to checksum.</param>
    /// <returns>The checksum.</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
        => Accumulate(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;

    /// <summary>
    /// Computes the CRC-32 of two byte spans as though they had been concatenated.
    /// </summary>
    /// <param name="first">The first span.</param>
    /// <param name="second">The span appended to it.</param>
    /// <returns>The checksum.</returns>
    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
        => Accumulate(Accumulate(0xFFFFFFFFu, first), second) ^ 0xFFFFFFFFu;

    /// <summary>
    /// Computes the CRC-32 of a byte span under a caller-supplied initial value and final inversion, for a format that
    /// does not use the standard pair.
    /// </summary>
    /// <param name="data">The bytes to checksum.</param>
    /// <param name="seed">Initial register value; the standard configuration uses <c>0xFFFFFFFF</c>.</param>
    /// <param name="finalXor">Value xored into the result; the standard configuration uses <c>0xFFFFFFFF</c>.</param>
    /// <returns>The checksum.</returns>
    public static uint Compute(ReadOnlySpan<byte> data, uint seed, uint finalXor)
        => Accumulate(seed, data) ^ finalXor;

    private static uint Accumulate(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (var i = 0u; i < 256u; i++)
        {
            var entry = i;
            for (var bit = 0; bit < 8; bit++)
                entry = (entry & 1) != 0 ? (entry >> 1) ^ Polynomial : entry >> 1;

            table[i] = entry;
        }

        return table;
    }
}
