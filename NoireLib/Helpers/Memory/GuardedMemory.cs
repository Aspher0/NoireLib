using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NoireLib.Helpers.Memory;

/// <summary>
/// Reads process memory only where VirtualQuery reports it mapped, for walking game structures past what
/// FFXIVClientStructs models. <see cref="AccessViolationException"/> is not catchable in .NET, so every span is
/// checked first and one that is not fully covered by its region is not read at all.
/// </summary>
public static unsafe class GuardedMemory
{
    private const uint MemCommit = 0x1000;
    private const uint PageGuard = 0x100;
    private const uint PageNoAccess = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public int Alignment1;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public int Alignment2;
    }

    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(nint address, out MemoryBasicInformation buffer, nuint length);

    /// <summary>
    /// Whether a region's state and protection allow reading: committed, not a guard page, and not PAGE_NOACCESS.
    /// </summary>
    /// <param name="state">The region's state, as VirtualQuery reports it.</param>
    /// <param name="protect">The region's protection, as VirtualQuery reports it.</param>
    /// <returns>True when the region is readable.</returns>
    public static bool IsReadableProtection(uint state, uint protect)
        => state == MemCommit && (protect & PageGuard) == 0 && (protect & PageNoAccess) == 0 && protect != 0;

    /// <summary>
    /// Whether a whole span sits inside a region, rather than merely starting in it.
    /// </summary>
    /// <param name="regionBase">The region's base address.</param>
    /// <param name="regionSize">The region's size in bytes.</param>
    /// <param name="address">The span's start.</param>
    /// <param name="length">The span's length in bytes.</param>
    /// <returns>True when the region covers the whole span.</returns>
    public static bool RegionCovers(long regionBase, long regionSize, long address, int length)
        => length > 0 && address >= regionBase && address + length <= regionBase + regionSize;

    /// <summary> Whether a whole span is committed and safe to dereference. </summary>
    /// <param name="address">The span's start.</param>
    /// <param name="length">The span's length in bytes.</param>
    /// <returns>True when the whole span is readable.</returns>
    public static bool IsReadable(nint address, int length)
    {
        if (address == 0 || length <= 0)
            return false;

        if (VirtualQuery(address, out var info, (nuint)sizeof(MemoryBasicInformation)) == 0)
            return false;

        return IsReadableProtection(info.State, info.Protect)
               && RegionCovers(info.BaseAddress, (long)info.RegionSize, address, length);
    }

    /// <summary>
    /// Copies a span into a buffer one mapped region at a time, stopping where the mapping ends rather than
    /// refusing outright.
    /// </summary>
    /// <param name="start">The address to read from.</param>
    /// <param name="buffer">The buffer to copy into.</param>
    /// <param name="bufferOffset">Where in the buffer to start writing.</param>
    /// <param name="length">How many bytes to try to read.</param>
    /// <returns>How many bytes were copied; short means the mapping ended there.</returns>
    public static int ReadSpan(nint start, byte[] buffer, int bufferOffset, int length)
    {
        if (start == 0 || buffer == null || bufferOffset < 0 || length <= 0)
            return 0;

        if (bufferOffset + length > buffer.Length)
            length = buffer.Length - bufferOffset;

        if (length <= 0)
            return 0;

        var copied = 0;

        while (copied < length)
        {
            var cursor = start + copied;

            if (VirtualQuery(cursor, out var info, (nuint)sizeof(MemoryBasicInformation)) == 0)
                break;

            if (!IsReadableProtection(info.State, info.Protect))
                break;

            var regionEnd = (long)info.BaseAddress + (long)info.RegionSize;
            var available = (int)Math.Min(regionEnd - (long)cursor, (long)(length - copied));

            if (available <= 0)
                break;

            Marshal.Copy(cursor, buffer, bufferOffset + copied, available);
            copied += available;
        }

        return copied;
    }

    /// <summary> Reads a whole span into a new buffer, or returns null when the span is not fully mapped. </summary>
    /// <param name="start">The address to read from.</param>
    /// <param name="length">The span's length in bytes.</param>
    /// <returns>The bytes, or null.</returns>
    public static byte[]? ReadExact(nint start, int length)
    {
        if (!IsReadable(start, length))
            return null;

        var buffer = new byte[length];

        return ReadSpan(start, buffer, 0, length) == length ? buffer : null;
    }

    /// <summary> Reads a pointer, refusing rather than dereferencing an address that is not mapped. </summary>
    /// <param name="address">Where the pointer is stored.</param>
    /// <param name="value">The pointer read.</param>
    /// <returns>True when the pointer was read.</returns>
    public static bool TryReadPointer(long address, out nint value)
    {
        value = 0;

        if (address == 0 || !IsReadable((nint)address, sizeof(nint)))
            return false;

        value = *(nint*)address;
        return true;
    }

    /// <summary>
    /// Reads a null-terminated string at a pointer, through the guard, so any pointer is safe to pass.
    /// </summary>
    /// <param name="pointer">Where the string starts.</param>
    /// <param name="buffer">The scratch buffer whose length caps how much is read.</param>
    /// <returns>The string, or empty when there is nothing readable there.</returns>
    public static string ReadNullTerminated(nint pointer, byte[] buffer)
    {
        if (pointer == 0 || buffer == null || buffer.Length == 0)
            return string.Empty;

        var read = ReadSpan(pointer, buffer, 0, buffer.Length);

        var length = 0;
        while (length < read && buffer[length] != 0)
            length++;

        return length == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, length);
    }

    /// <summary>
    /// Reads the run of printable ASCII at a pointer, stopping at the first byte outside that range.
    /// </summary>
    /// <param name="pointer">Where to start reading.</param>
    /// <param name="maxLength">How far to read at most.</param>
    /// <returns>The run, or empty when there is none.</returns>
    public static string ReadPrintableRun(nint pointer, int maxLength)
    {
        if (pointer == 0 || maxLength <= 0)
            return string.Empty;

        var buffer = new byte[maxLength];
        var read = ReadSpan(pointer, buffer, 0, maxLength);

        var length = 0;
        while (length < read && buffer[length] >= 0x20 && buffer[length] < 0x7F)
            length++;

        return length == 0 ? string.Empty : Encoding.ASCII.GetString(buffer, 0, length);
    }

    /// <summary> Formats bytes as space-separated hex. </summary>
    /// <param name="data">The bytes to format.</param>
    /// <param name="length">How many of them to format.</param>
    /// <returns>The formatted bytes, or empty.</returns>
    public static string ToHexDump(byte[] data, int length)
    {
        if (data == null || length <= 0)
            return string.Empty;

        var text = new StringBuilder(length * 3);

        for (var i = 0; i < length && i < data.Length; i++)
        {
            if (i > 0)
                text.Append(' ');

            text.Append(data[i].ToString("X2"));
        }

        return text.ToString();
    }
}
