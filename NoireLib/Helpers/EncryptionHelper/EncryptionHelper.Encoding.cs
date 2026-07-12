using Newtonsoft.Json;
using System;
using System.Text;

namespace NoireLib.Helpers;

public static partial class EncryptionHelper
{
    #region Base64

    /// <summary>
    /// Encodes raw bytes as a Base64 string.
    /// </summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="urlSafe">If <see langword="true"/>, produces URL-safe Base64 (<c>-_</c> instead of <c>+/</c>, no padding).</param>
    /// <returns>The Base64 representation of the bytes.</returns>
    public static string ToBase64(this byte[] data, bool urlSafe = false)
        => urlSafe ? ToBase64Url(data) : Convert.ToBase64String(data);

    /// <summary>
    /// Encodes a string as a Base64 string using the specified encoding (UTF-8 by default).
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="urlSafe">If <see langword="true"/>, produces URL-safe Base64.</param>
    /// <param name="encoding">The encoding used to turn the string into bytes. Defaults to UTF-8.</param>
    /// <returns>The Base64 representation of the string.</returns>
    public static string ToBase64(this string text, bool urlSafe = false, Encoding? encoding = null)
    {
        var bytes = (encoding ?? Encoding.UTF8).GetBytes(text ?? string.Empty);
        return urlSafe ? ToBase64Url(bytes) : Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Serializes any object to JSON and encodes the result as a Base64 string.
    /// </summary>
    /// <param name="value">The value to serialize and encode.</param>
    /// <param name="urlSafe">If <see langword="true"/>, produces URL-safe Base64.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings.</param>
    /// <returns>The Base64 representation of the serialized value.</returns>
    public static string SerializeToBase64(object? value, bool urlSafe = false, JsonSerializerSettings? jsonSettings = null)
    {
        var bytes = ToRawBytes(value, jsonSettings);
        return urlSafe ? ToBase64Url(bytes) : Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Decodes a Base64 string (standard or URL-safe) into raw bytes.
    /// </summary>
    /// <param name="base64">The Base64 string to decode.</param>
    /// <returns>The decoded bytes.</returns>
    public static byte[] FromBase64(this string base64)
    {
        if (string.IsNullOrEmpty(base64))
            return Array.Empty<byte>();

        // Normalize URL-safe Base64 back to standard Base64 with padding.
        var normalized = base64.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
        }

        return Convert.FromBase64String(normalized);
    }

    /// <summary>
    /// Decodes a Base64 string into a string using the specified encoding (UTF-8 by default).
    /// </summary>
    /// <param name="base64">The Base64 string to decode.</param>
    /// <param name="encoding">The encoding used to turn the decoded bytes into a string. Defaults to UTF-8.</param>
    /// <returns>The decoded string.</returns>
    public static string FromBase64ToString(this string base64, Encoding? encoding = null)
        => (encoding ?? Encoding.UTF8).GetString(FromBase64(base64));

    /// <summary>
    /// Decodes a Base64 string produced by <see cref="SerializeToBase64"/> back into an object of the given type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="base64">The Base64 string to decode.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings.</param>
    /// <returns>The deserialized value, or <see langword="default"/> if the payload was empty.</returns>
    public static T? DeserializeFromBase64<T>(this string base64, JsonSerializerSettings? jsonSettings = null)
    {
        var json = FromBase64ToString(base64);
        if (string.IsNullOrEmpty(json))
            return default;

        return JsonConvert.DeserializeObject<T>(json, jsonSettings);
    }

    /// <summary>
    /// Encodes raw bytes as URL-safe Base64 (<c>-_</c> instead of <c>+/</c>, padding removed).
    /// </summary>
    /// <param name="data">The bytes to encode.</param>
    /// <returns>The URL-safe Base64 representation of the bytes.</returns>
    private static string ToBase64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    #endregion

    #region Hex

    /// <summary>
    /// Encodes raw bytes as a hexadecimal string.
    /// </summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="upperCase">If <see langword="true"/>, uses uppercase characters.</param>
    /// <returns>The hexadecimal representation of the bytes.</returns>
    public static string ToHex(this byte[] data, bool upperCase = false)
    {
        var hex = Convert.ToHexString(data);
        return upperCase ? hex : hex.ToLowerInvariant();
    }

    /// <summary>
    /// Encodes a string as a hexadecimal string using the specified encoding (UTF-8 by default).
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="upperCase">If <see langword="true"/>, uses uppercase characters.</param>
    /// <param name="encoding">The encoding used to turn the string into bytes. Defaults to UTF-8.</param>
    /// <returns>The hexadecimal representation of the string.</returns>
    public static string ToHex(this string text, bool upperCase = false, Encoding? encoding = null)
    {
        var bytes = (encoding ?? Encoding.UTF8).GetBytes(text ?? string.Empty);
        var hex = Convert.ToHexString(bytes);
        return upperCase ? hex : hex.ToLowerInvariant();
    }

    /// <summary>
    /// Decodes a hexadecimal string into raw bytes.
    /// </summary>
    /// <param name="hex">The hexadecimal string to decode. May contain an optional <c>0x</c> prefix.</param>
    /// <returns>The decoded bytes.</returns>
    public static byte[] FromHex(this string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Array.Empty<byte>();

        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        return Convert.FromHexString(hex);
    }

    /// <summary>
    /// Decodes a hexadecimal string into a string using the specified encoding (UTF-8 by default).
    /// </summary>
    /// <param name="hex">The hexadecimal string to decode.</param>
    /// <param name="encoding">The encoding used to turn the decoded bytes into a string. Defaults to UTF-8.</param>
    /// <returns>The decoded string.</returns>
    public static string FromHexToString(this string hex, Encoding? encoding = null)
        => (encoding ?? Encoding.UTF8).GetString(FromHex(hex));

    #endregion
}
