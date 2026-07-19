using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NoireLib.Helpers;

/// <summary>
/// One share-string format for anything serializable: a preset, a theme, a layout, a filter set.<br/>
/// <see cref="Encode{T}"/> turns a value into a single pasteable token; <see cref="Decode{T}"/> reads one back, telling
/// you exactly why it could not when it could not. The format is versioned, compressed, checksummed and tagged with a
/// kind, so a code meant for one thing is refused by another instead of being half-applied.<br/>
/// Nothing here touches ImGui or the UI: this is a plain data helper, usable from a command, a background task or a
/// module just as readily as from a window.
/// </summary>
/// <remarks>
/// <b>The format is permanent from the first code a user pastes anywhere.</b> A change that makes an old code
/// unreadable is a change to <see cref="Prefix"/>, never a quiet reshuffle of the bytes.<br/>
/// <br/>
/// Layout, after the prefix and a URL-safe Base64 decode:
/// <code>
/// [0]      flags       bit 0 set when the payload is deflate-compressed
/// [1..4]   crc32       little-endian, over the kind bytes followed by the uncompressed payload
/// [5]      kindLength  in UTF-8 bytes
/// [6..]    kind        UTF-8
/// [...]    payload     UTF-8 JSON, deflated when the flag says so
/// </code>
/// The checksum covers the payload as it will be parsed rather than as it travels, so it validates the same bytes the
/// deserializer sees whether or not compression was worth using.
/// </remarks>
/// <example>
/// <code>
/// var code = ShareCodeHelper.Encode("myplugin.preset", preset);
///
/// var result = ShareCodeHelper.Decode&lt;PresetDto&gt;(pastedText, "myplugin.preset");
/// if (result.Success)
///     ShowPreview(result.Value);
/// else
///     ShowError(result.Message);
/// </code>
/// </example>
public static class ShareCodeHelper
{
    /// <summary>
    /// The marker every code starts with. The trailing digit is the format version: a reader that meets a different one
    /// says so rather than guessing at the bytes behind it.
    /// </summary>
    public const string Prefix = "NOIRE1-";

    private const int HeaderBytes = 6;
    private const byte FlagCompressed = 0x01;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>
    /// The ceilings decoding runs under. Part of the format rather than a preference; see <see cref="ShareCodeLimits"/>
    /// before raising any of them.
    /// </summary>
    public static ShareCodeLimits Limits { get; set; } = new();

    /// <summary>
    /// Turns a value into a share code.
    /// </summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="kind">What this code carries, for example <c>"myplugin.preset"</c>. Namespace it with your plugin
    /// so two plugins cannot claim the same tag. Refused by <see cref="Decode{T}"/> when it does not match.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings. <see cref="TypeNameHandling"/> is always
    /// <see cref="TypeNameHandling.None"/>, whatever the settings ask for; every other setting is honoured.</param>
    /// <returns>The share code, ready to paste.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="kind"/> is blank or longer than
    /// <see cref="ShareCodeLimits.MaxKindBytes"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the value does not fit inside <see cref="Limits"/>.
    /// Producing a code no conformant reader would accept would only move the failure to whoever pasted it.</exception>
    public static string Encode<T>(string kind, T value, JsonSerializerSettings? jsonSettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var limits = Limits;
        var kindBytes = Utf8.GetBytes(kind);

        if (kindBytes.Length > Math.Min(limits.MaxKindBytes, byte.MaxValue))
            throw new ArgumentException($"The kind tag is {kindBytes.Length} bytes, over the {limits.MaxKindBytes} byte limit.", nameof(kind));

        var raw = Utf8.GetBytes(Serialize(value, jsonSettings));

        if (raw.Length > limits.MaxDecodedBytes)
            throw new InvalidOperationException($"The payload is {raw.Length} bytes, over the {limits.MaxDecodedBytes} byte share-code limit. Share less in one code.");

        var crc = ShareCodeCrc32.Compute(kindBytes, raw);
        var compressed = Deflate(raw);
        var useCompression = compressed.Length < raw.Length;
        var payload = useCompression ? compressed : raw;

        var buffer = new byte[HeaderBytes + kindBytes.Length + payload.Length];
        buffer[0] = useCompression ? FlagCompressed : (byte)0;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 4), crc);
        buffer[5] = (byte)kindBytes.Length;
        kindBytes.CopyTo(buffer, HeaderBytes);
        payload.CopyTo(buffer, HeaderBytes + kindBytes.Length);

        var code = Prefix + buffer.ToBase64(urlSafe: true);

        if (code.Length > limits.MaxEncodedCharacters)
            throw new InvalidOperationException($"The encoded code is {code.Length} characters, over the {limits.MaxEncodedCharacters} character share-code limit. Share less in one code.");

        return code;
    }

    /// <summary>
    /// Reads a share code back into a value.
    /// </summary>
    /// <remarks>
    /// <b>Decode into an inert data type, never into a live object.</b> Newtonsoft runs property setters while it
    /// deserializes, so decoding a stranger's code straight onto a live configuration hands them your setter side
    /// effects and your disk writes before you have looked at a single field. Decode into a plain DTO, show the user
    /// what would change, and copy the fields across yourself once they agree.
    /// </remarks>
    /// <typeparam name="T">The payload type. See the remarks: this should be an inert data type.</typeparam>
    /// <param name="code">The pasted text. Surrounding whitespace is ignored.</param>
    /// <param name="expectedKind">The kind this importer accepts. Pass an empty string to accept any kind, which is
    /// only appropriate for a tool that inspects codes rather than applying them.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings. <see cref="TypeNameHandling"/> is always
    /// <see cref="TypeNameHandling.None"/>, whatever the settings ask for; every other setting is honoured.</param>
    /// <returns>The decoded value, or the reason it could not be read.</returns>
    public static ShareCodeResult<T> Decode<T>(string? code, string expectedKind, JsonSerializerSettings? jsonSettings = null)
    {
        var limits = Limits;

        if (string.IsNullOrWhiteSpace(code))
            return ShareCodeResult<T>.Fail(ShareCodeError.Empty, "There is nothing to import.");

        var trimmed = code.Trim();

        if (trimmed.Length > limits.MaxEncodedCharacters)
            return ShareCodeResult<T>.Fail(ShareCodeError.TooLarge, $"This code is {trimmed.Length} characters, over the {limits.MaxEncodedCharacters} character limit.");

        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.StartsWith("NOIRE", StringComparison.OrdinalIgnoreCase)
                ? ShareCodeResult<T>.Fail(ShareCodeError.WrongVersion, "This share code was made by a newer version. Update the plugin to import it.")
                : ShareCodeResult<T>.Fail(ShareCodeError.NotAShareCode, "This is not a share code. A code starts with " + Prefix);
        }

        byte[] buffer;
        try
        {
            buffer = trimmed[Prefix.Length..].FromBase64();
        }
        catch (FormatException)
        {
            return ShareCodeResult<T>.Fail(ShareCodeError.Malformed, "This code is damaged. Copy the whole code, including the part after the last dash.");
        }

        if (buffer.Length < HeaderBytes)
            return ShareCodeResult<T>.Fail(ShareCodeError.Malformed, "This code is too short to be complete. It was probably cut off when it was copied.");

        var compressed = (buffer[0] & FlagCompressed) != 0;
        var expectedCrc = BitConverter.ToUInt32(buffer, 1);
        var kindLength = buffer[5];

        if (buffer.Length < HeaderBytes + kindLength)
            return ShareCodeResult<T>.Fail(ShareCodeError.Malformed, "This code is damaged. It was probably cut off when it was copied.");

        var kind = Utf8.GetString(buffer, HeaderBytes, kindLength);
        var payload = buffer.AsSpan(HeaderBytes + kindLength).ToArray();

        if (!string.IsNullOrEmpty(expectedKind) && !string.Equals(kind, expectedKind, StringComparison.Ordinal))
        {
            return ShareCodeResult<T>.Fail(
                ShareCodeError.WrongKind,
                $"This code carries '{kind}', and this imports '{expectedKind}'.",
                kind);
        }

        byte[] raw;
        if (compressed)
        {
            if (!TryInflate(payload, limits.MaxDecodedBytes, out raw, out var inflateError))
                return ShareCodeResult<T>.Fail(inflateError, InflateMessage(inflateError, limits), kind);
        }
        else
        {
            if (payload.Length > limits.MaxDecodedBytes)
                return ShareCodeResult<T>.Fail(ShareCodeError.TooLarge, $"This code holds more than the {limits.MaxDecodedBytes} byte limit allows.", kind);

            raw = payload;
        }

        if (ShareCodeCrc32.Compute(Utf8.GetBytes(kind), raw) != expectedCrc)
            return ShareCodeResult<T>.Fail(ShareCodeError.ChecksumMismatch, "This code does not match its own checksum, so it was altered on the way here. Ask for it again.", kind);

        try
        {
            var value = Deserialize<T>(Utf8.GetString(raw), limits.MaxDepth, jsonSettings);
            return ShareCodeResult<T>.Ok(value, kind);
        }
        catch (JsonException ex)
        {
            NoireLogger.LogDebug($"A share code of kind '{kind}' did not deserialize: {ex.Message}", nameof(ShareCodeHelper));
            return ShareCodeResult<T>.Fail(ShareCodeError.Unreadable, "This code is not the shape this import expects. It may have been made by a different plugin or an older version.", kind);
        }
    }

    /// <summary>
    /// Reads the kind tag out of a code without decoding its payload, for deciding what to do with a paste before
    /// committing to a type.
    /// </summary>
    /// <param name="code">The pasted text.</param>
    /// <param name="kind">The kind tag, or an empty string when the code could not be read.</param>
    /// <returns>True when a kind was read.</returns>
    public static bool TryReadKind(string? code, out string kind)
    {
        kind = string.Empty;

        if (string.IsNullOrWhiteSpace(code))
            return false;

        var trimmed = code.Trim();
        if (trimmed.Length > Limits.MaxEncodedCharacters || !trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var buffer = trimmed[Prefix.Length..].FromBase64();
            if (buffer.Length < HeaderBytes || buffer.Length < HeaderBytes + buffer[5])
                return false;

            kind = Utf8.GetString(buffer, HeaderBytes, buffer[5]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a piece of text looks like a share code at all, for deciding whether a paste is worth trying.<br/>
    /// This is a shape check, not a validity check: a code that looks right can still fail to decode.
    /// </summary>
    /// <param name="text">The text to test.</param>
    /// <returns>True when the text starts with the share-code marker.</returns>
    public static bool LooksLikeShareCode(string? text)
        => !string.IsNullOrWhiteSpace(text) && text.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    private static string Serialize<T>(T value, JsonSerializerSettings? jsonSettings)
    {
        var serializer = CreateSerializer(jsonSettings);
        using var writer = new StringWriter();
        serializer.Serialize(writer, value);
        return writer.ToString();
    }

    private static T? Deserialize<T>(string json, int maxDepth, JsonSerializerSettings? jsonSettings)
    {
        var serializer = CreateSerializer(jsonSettings);

        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader) { MaxDepth = maxDepth };
        return serializer.Deserialize<T>(jsonReader);
    }

    /// <summary>
    /// Builds the serializer both directions use, with the two settings that are not the caller's to choose.
    /// </summary>
    private static JsonSerializer CreateSerializer(JsonSerializerSettings? jsonSettings)
    {
        // Create rather than CreateDefault: CreateDefault merges JsonConvert.DefaultSettings, a process-global any other
        // code in this process can assign and reassign at runtime, which would let the JSON behind a code differ between
        // the moment it was written and the moment it is read back.
        var serializer = JsonSerializer.Create(jsonSettings);

        // Type resolution driven by payload content is what turns a stranger's paste into an instruction to construct
        // arbitrary types. It stays off for every caller regardless of what was passed.
        serializer.TypeNameHandling = TypeNameHandling.None;

        // A code holds exactly one JSON document, so anything after it means it is damaged or was appended to.
        serializer.CheckAdditionalContent = true;

        return serializer;
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var output = new MemoryStream();

        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);

        return output.ToArray();
    }

    /// <summary>
    /// Decompresses a payload, giving up the moment it grows past the ceiling.
    /// </summary>
    /// <remarks>
    /// The ceiling is checked on every chunk rather than on the finished buffer, which is the whole point: a zip bomb is
    /// small until it is decompressed, so measuring afterwards means the damage is already done.
    /// </remarks>
    private static bool TryInflate(byte[] compressed, int maxBytes, out byte[] result, out ShareCodeError error)
    {
        result = Array.Empty<byte>();
        error = ShareCodeError.None;

        try
        {
            using var input = new MemoryStream(compressed);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            var buffer = new byte[8192];
            int read;

            while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > maxBytes)
                {
                    error = ShareCodeError.TooLarge;
                    return false;
                }

                output.Write(buffer, 0, read);
            }

            result = output.ToArray();
            return true;
        }
        catch (InvalidDataException)
        {
            error = ShareCodeError.Malformed;
            return false;
        }
    }

    private static string InflateMessage(ShareCodeError error, ShareCodeLimits limits) => error == ShareCodeError.TooLarge
        ? $"This code expands to more than the {limits.MaxDecodedBytes} byte limit allows, so it was not read."
        : "This code is damaged and could not be unpacked. It was probably cut off when it was copied.";
}
