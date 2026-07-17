using Newtonsoft.Json;
using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NoireLib.Helpers;

/// <summary>
/// A static helper class for encoding, hashing, password hashing and symmetric encryption.<br/>
/// It provides a unified surface that can turn virtually anything (raw bytes, strings, files or any
/// serializable object) into Base64/Hex, hash it (SHA family, MD5, HMAC), derive password hashes
/// (Argon2id, BCrypt) or encrypt it with AES.
/// </summary>
/// <remarks>
/// Password hashing relies on the <c>Konscious.Security.Cryptography.Argon2</c> and <c>BCrypt.Net-Next</c>
/// packages, everything else is built on top of <see cref="System.Security.Cryptography"/>.<br/>
/// Encryption uses AES-256-GCM for in-memory payloads and AES-256-CBC with HMAC-SHA256 (encrypt-then-MAC)
/// for file streaming.
/// </remarks>
public static partial class EncryptionHelper
{
    private const string LogPrefix = "[EncryptionHelper] ";

    /// <summary>
    /// The number of PBKDF2 iterations used when deriving an encryption key from a password.
    /// </summary>
    private const int DefaultPbkdf2Iterations = 210_000;

    /// <summary>
    /// The size, in bytes, of the salt used for password-based key derivation.
    /// </summary>
    private const int SaltSize = 16;

    /// <summary>
    /// The size, in bytes, of the AES key (256-bit).
    /// </summary>
    private const int AesKeySize = 32;

    /// <summary>
    /// The size, in bytes, of the nonce used by AES-GCM.
    /// </summary>
    private const int GcmNonceSize = 12;

    /// <summary>
    /// The size, in bytes, of the authentication tag used by AES-GCM.
    /// </summary>
    private const int GcmTagSize = 16;

    /// <summary>
    /// Describes the textual representation used when turning raw bytes into a string.
    /// </summary>
    public enum BinaryTextFormat
    {
        /// <summary>Lowercase hexadecimal (e.g. <c>"9f86d0"</c>).</summary>
        Hex,

        /// <summary>Uppercase hexadecimal (e.g. <c>"9F86D0"</c>).</summary>
        HexUpper,

        /// <summary>Standard Base64.</summary>
        Base64,

        /// <summary>URL-safe Base64 (<c>+/</c> replaced with <c>-_</c>, padding removed).</summary>
        Base64Url,
    }

    /// <summary>
    /// The serializer used when the caller supplies no settings.
    /// </summary>
    private static readonly JsonSerializer DefaultJsonSerializer = CreateJsonSerializer(null);

    /// <summary>
    /// Builds the serializer that backs every JSON conversion here, which is the single point where a value handed to
    /// this class becomes JSON and where JSON becomes a value again.
    /// </summary>
    /// <param name="jsonSettings">The caller-supplied settings, or null for the defaults.</param>
    /// <returns>A serializer that honours <paramref name="jsonSettings"/> without leaving the format open to the process.</returns>
    private static JsonSerializer CreateJsonSerializer(JsonSerializerSettings? jsonSettings)
    {
        // JsonSerializer.Create resolves every setting from the object it is given. The JsonConvert overloads and
        // JsonSerializer.CreateDefault instead merge in JsonConvert.DefaultSettings, a process-global that any other
        // code loaded into this process can assign, so a caller whose settings do not mention a property would silently
        // inherit that code's choice for it. That global also changes at runtime, which would let the JSON behind a
        // hash or a ciphertext differ between the moment it is written and the moment it is read back. The settings
        // object itself is never mutated; the serializer is the copy.
        var serializer = JsonSerializer.Create(jsonSettings);

        // Type resolution driven by payload content is what turns a decrypted or decoded document into an instruction
        // to construct arbitrary types. It stays off for every caller regardless of what was passed.
        serializer.TypeNameHandling = TypeNameHandling.None;

        // A payload holds exactly one JSON document, so anything after it means it is corrupt or has been tampered
        // with. The JsonConvert deserialization overloads turn this on for their callers implicitly; setting it
        // explicitly keeps content after the document rejected rather than quietly ignored.
        serializer.CheckAdditionalContent = true;

        return serializer;
    }

    /// <summary>
    /// Gets the serializer for the given settings, reusing the shared instance when there are none to honour.
    /// </summary>
    /// <param name="jsonSettings">The caller-supplied settings, or null for the defaults.</param>
    /// <returns>The serializer to use.</returns>
    private static JsonSerializer GetJsonSerializer(JsonSerializerSettings? jsonSettings)
        => jsonSettings == null ? DefaultJsonSerializer : CreateJsonSerializer(jsonSettings);

    /// <summary>
    /// Deserializes a JSON document produced by one of the serializing entry points.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="json">The JSON document.</param>
    /// <param name="jsonSettings">The caller-supplied settings, or null for the defaults.</param>
    /// <returns>The deserialized value.</returns>
    private static T? FromJson<T>(string json, JsonSerializerSettings? jsonSettings)
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader);

        return GetJsonSerializer(jsonSettings).Deserialize<T>(jsonReader);
    }

    /// <summary>
    /// Resolves an arbitrary value into its raw byte representation.<br/>
    /// <see langword="null"/> becomes an empty array, byte buffers are returned as-is, strings are
    /// encoded as UTF-8, and any other object is serialized to JSON before being encoded as UTF-8.
    /// </summary>
    /// <param name="data">The value to convert.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings used when the value is serialized.</param>
    /// <returns>The raw bytes representing the value.</returns>
    private static byte[] ToRawBytes(object? data, JsonSerializerSettings? jsonSettings = null)
    {
        switch (data)
        {
            case null:
                return Array.Empty<byte>();
            case byte[] bytes:
                return bytes;
            case ArraySegment<byte> segment:
                return segment.ToArray();
            case ReadOnlyMemory<byte> readOnlyMemory:
                return readOnlyMemory.ToArray();
            case Memory<byte> memory:
                return memory.ToArray();
            case string text:
                return Encoding.UTF8.GetBytes(text);
            default:
                return Encoding.UTF8.GetBytes(ToJson(data, jsonSettings));
        }
    }

    /// <summary>
    /// Serializes a value to the JSON that stands in for it when it is hashed, encrypted or encoded.
    /// </summary>
    /// <param name="data">The value to serialize.</param>
    /// <param name="jsonSettings">The caller-supplied settings, or null for the defaults.</param>
    /// <returns>The JSON representation of the value.</returns>
    private static string ToJson(object data, JsonSerializerSettings? jsonSettings)
    {
        var builder = new StringBuilder(256);

        using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            GetJsonSerializer(jsonSettings).Serialize(jsonWriter, data);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats raw bytes as a string using the specified representation.
    /// </summary>
    /// <param name="bytes">The bytes to format.</param>
    /// <param name="format">The textual representation to use.</param>
    /// <returns>The formatted string.</returns>
    private static string FormatBytes(byte[] bytes, BinaryTextFormat format)
    {
        return format switch
        {
            BinaryTextFormat.Hex => Convert.ToHexString(bytes).ToLowerInvariant(),
            BinaryTextFormat.HexUpper => Convert.ToHexString(bytes),
            BinaryTextFormat.Base64 => Convert.ToBase64String(bytes),
            BinaryTextFormat.Base64Url => ToBase64Url(bytes),
            _ => Convert.ToHexString(bytes).ToLowerInvariant(),
        };
    }

    /// <summary>
    /// Derives a 256-bit AES key from a password and salt using PBKDF2 (HMAC-SHA256).
    /// </summary>
    /// <param name="password">The password to derive from.</param>
    /// <param name="salt">The salt to use.</param>
    /// <param name="iterations">The number of PBKDF2 iterations.</param>
    /// <param name="length">The length of the derived key in bytes.</param>
    /// <returns>The derived key bytes.</returns>
    private static byte[] DeriveKey(string password, byte[] salt, int iterations, int length = AesKeySize)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? string.Empty), salt, iterations, HashAlgorithmName.SHA256, length);

    /// <summary>
    /// Generates a cryptographically secure random byte array of the specified size.
    /// </summary>
    /// <param name="size">The number of bytes to generate.</param>
    /// <returns>The generated bytes.</returns>
    private static byte[] RandomBytes(int size) => RandomNumberGenerator.GetBytes(size);
}
