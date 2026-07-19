using System;

namespace NoireLib.Helpers;

/// <summary>
/// CRC-32 (IEEE 802.3), the integrity check written into a share code.
/// </summary>
/// <remarks>
/// This detects damage, not tampering. A CRC is public and deterministic, so anyone who edits a payload can recompute
/// it; what it catches is a code that was truncated by a chat client, re-wrapped by a forum, or half-selected on the way
/// to the clipboard. Nothing here says who wrote a code. Only a signature does, which is what
/// <c>EncryptionHelper</c> is for.
/// </remarks>
internal static class ShareCodeCrc32
{
    private const uint Polynomial = 0xEDB88320u;

    private static readonly uint[] Table = BuildTable();

    /// <summary>
    /// Computes the CRC-32 of one or two byte spans, as if they had been concatenated.
    /// </summary>
    /// <param name="first">The first span.</param>
    /// <param name="second">The second span, appended to the first.</param>
    /// <returns>The checksum.</returns>
    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var crc = 0xFFFFFFFFu;
        crc = Accumulate(crc, first);
        crc = Accumulate(crc, second);
        return crc ^ 0xFFFFFFFFu;
    }

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
