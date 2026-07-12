using Newtonsoft.Json;
using System;
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
                return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data, jsonSettings));
        }
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
