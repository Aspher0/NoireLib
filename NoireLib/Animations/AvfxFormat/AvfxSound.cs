using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace NoireLib.Animations.AvfxFormat;

/// <summary>
/// Reads the sound files an .avfx visual effect plays.
/// </summary>
public static class AvfxSound
{
    // The chunk the file opens with, whose body holds every other chunk.
    private const string FileTag = "AVFX";

    // A chunk header is its four-character tag and the length of its body.
    private const int HeaderLength = 8;

    // Chunks start on a four-byte boundary, so a body of an odd length is followed by padding.
    private const int Alignment = 4;

    // How deep the walk follows nested chunks before it stops.
    private const int MaxDepth = 10;

    private const string SoundExtension = ".scd";

    // The shortest body that can hold a readable path and its terminator.
    private const int MinTextLength = 4;

    /// <summary>
    /// Every sound file the effect names, in the order they appear in the file.
    /// </summary>
    /// <param name="avfxBytes">The .avfx file's bytes.</param>
    /// <returns>The sound files named, empty when the effect names none or the bytes are not a readable .avfx.</returns>
    public static IReadOnlyList<string> SoundPaths(byte[] avfxBytes)
    {
        var paths = new List<string>();

        if (avfxBytes is not { Length: > HeaderLength } || TagAt(avfxBytes, 0) != FileTag)
            return paths;

        var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(avfxBytes.AsSpan(4, sizeof(int)));

        if (bodyLength <= 0)
            return paths;

        Walk(avfxBytes, HeaderLength, Math.Min(avfxBytes.Length, HeaderLength + bodyLength), 0, paths);

        return paths;
    }

    /// <summary>
    /// Whether the effect plays any sound at all.
    /// </summary>
    /// <param name="avfxBytes">The .avfx file's bytes.</param>
    /// <returns>True when the effect names at least one sound file.</returns>
    public static bool HasSound(byte[] avfxBytes) => SoundPaths(avfxBytes).Count > 0;

    private static void Walk(byte[] data, int start, int end, int depth, List<string> paths)
    {
        var position = start;

        while (position + HeaderLength <= end)
        {
            if (TagAt(data, position) is not { Length: > 0 })
                return;

            var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position + 4, sizeof(int)));
            var body = position + HeaderLength;

            if (bodyLength < 0 || body + bodyLength > end)
                return;

            var text = TextAt(data, body, bodyLength);

            if (text != null)
            {
                if (text.EndsWith(SoundExtension, StringComparison.OrdinalIgnoreCase))
                    paths.Add(text);
            }
            else if (depth < MaxDepth && HoldsChunks(data, body, body + bodyLength))
            {
                Walk(data, body, body + bodyLength, depth + 1, paths);
            }

            position = body + bodyLength + Padding(bodyLength);
        }
    }

    // Whether a body reads end to end as chunks of its own, rather than as a value.
    private static bool HoldsChunks(byte[] data, int start, int end)
    {
        if (end - start < HeaderLength)
            return false;

        var position = start;
        var chunks = 0;

        while (position + HeaderLength <= end)
        {
            if (TagAt(data, position) is not { Length: > 0 })
                return false;

            var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position + 4, sizeof(int)));

            if (bodyLength < 0 || position + HeaderLength + bodyLength > end)
                return false;

            position += HeaderLength + bodyLength + Padding(bodyLength);
            chunks++;
        }

        // The last chunk's padding can run past the body it sits in, so anything short of a header is a fit.
        return chunks > 0 && end - position < HeaderLength;
    }

    // A chunk's tag, stored back to front and padded with spaces or nulls.
    private static string TagAt(byte[] data, int offset)
    {
        var characters = new char[4];
        var length = 0;

        for (var index = 3; index >= 0; index--)
        {
            var value = data[offset + index];

            if (value == 0 || value == ' ')
                continue;

            if (value < 0x20 || value >= 0x7f)
                return string.Empty;

            characters[length++] = (char)value;
        }

        return new string(characters, 0, length);
    }

    // A body read as a null-terminated ASCII string, or null when it is not one.
    private static string? TextAt(byte[] data, int offset, int length)
    {
        if (length < MinTextLength)
            return null;

        var builder = new StringBuilder(length);

        for (var index = 0; index < length; index++)
        {
            var value = data[offset + index];

            if (value == 0)
            {
                for (var rest = index; rest < length; rest++)
                {
                    if (data[offset + rest] != 0)
                        return null;
                }

                break;
            }

            if (value < 0x20 || value >= 0x7f)
                return null;

            builder.Append((char)value);
        }

        return builder.Length >= MinTextLength - 1 ? builder.ToString() : null;
    }

    private static int Padding(int bodyLength) => (Alignment - bodyLength % Alignment) % Alignment;
}
