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
public static partial class EncryptionHelper
{
    private const string LogPrefix = "[EncryptionHelper] ";

    // The number of PBKDF2 iterations used when deriving an encryption key from a password.
    private const int DefaultPbkdf2Iterations = 210_000;

    // The size, in bytes, of the salt used for password-based key derivation.
    private const int SaltSize = 16;

    private const int AesKeySize = 32;

    // The size, in bytes, of the nonce used by AES-GCM.
    private const int GcmNonceSize = 12;

    // The size, in bytes, of the authentication tag used by AES-GCM.
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

    private static readonly JsonSerializer DefaultJsonSerializer = CreateJsonSerializer(null);

    // Builds the serializer that backs every JSON conversion here, which is the single point where a value handed to
    // this class becomes JSON and where JSON becomes a value again.
    private static JsonSerializer CreateJsonSerializer(JsonSerializerSettings? jsonSettings)
    {
        // Create rather than CreateDefault, which merges the mutable process-global JsonConvert.DefaultSettings and
        // would let the JSON behind a hash or ciphertext differ between when it was written and when it is read back.
        var serializer = JsonSerializer.Create(jsonSettings);

        // Type resolution driven by payload content would turn a decrypted or decoded document into an instruction
        // to construct arbitrary types. It stays off for every caller regardless of what was passed.
        serializer.TypeNameHandling = TypeNameHandling.None;

        // A payload holds exactly one JSON document, so anything after it means it is corrupt or has been tampered
        // with. The JsonConvert deserialization overloads turn this on for their callers implicitly; setting it
        // explicitly keeps content after the document rejected rather than quietly ignored.
        serializer.CheckAdditionalContent = true;

        return serializer;
    }

    // Gets the serializer for the given settings, reusing the shared instance when there are none to honour.
    private static JsonSerializer GetJsonSerializer(JsonSerializerSettings? jsonSettings)
        => jsonSettings == null ? DefaultJsonSerializer : CreateJsonSerializer(jsonSettings);

    // Deserializes a JSON document produced by one of the serializing entry points.
    private static T? FromJson<T>(string json, JsonSerializerSettings? jsonSettings)
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader);

        return GetJsonSerializer(jsonSettings).Deserialize<T>(jsonReader);
    }

    // Resolves an arbitrary value into its raw byte representation.  becomes an empty array, byte buffers are
    // returned as-is, strings are encoded as UTF-8, and any other object is serialized to JSON before being encoded
    // as UTF-8.
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

    // Serializes a value to the JSON that stands in for it when it is hashed, encrypted or encoded.
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

    // Formats raw bytes as a string using the specified representation.
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

    // Derives a 256-bit AES key from a password and salt using PBKDF2 (HMAC-SHA256).
    private static byte[] DeriveKey(string password, byte[] salt, int iterations, int length = AesKeySize)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? string.Empty), salt, iterations, HashAlgorithmName.SHA256, length);

    // Generates a cryptographically secure random byte array of the specified size.
    private static byte[] RandomBytes(int size) => RandomNumberGenerator.GetBytes(size);
}
