using Newtonsoft.Json;
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NoireLib.Helpers;

public static partial class EncryptionHelper
{
    /// <summary>
    /// The format version byte for password-based AES-GCM payloads.
    /// </summary>
    private const byte AesGcmPasswordVersion = 1;

    /// <summary>
    /// The format version byte for raw-key AES-GCM payloads.
    /// </summary>
    private const byte AesGcmKeyVersion = 2;

    #region Password-based AES-GCM

    /// <summary>
    /// Encrypts any value with AES-256-GCM using a key derived from a password.<br/>
    /// The returned buffer is self-describing: <c>[version][iterations][salt][nonce][tag][ciphertext]</c>.
    /// </summary>
    /// <param name="data">The value to encrypt. Strings are encoded as UTF-8, other objects are serialized to JSON.</param>
    /// <param name="password">The password used to derive the encryption key.</param>
    /// <param name="iterations">The number of PBKDF2 iterations used for key derivation.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings used when the value is serialized.
    /// <see cref="TypeNameHandling"/> is always <see cref="TypeNameHandling.None"/>, whatever the settings ask for;
    /// every other setting is honoured.</param>
    /// <returns>The encrypted payload.</returns>
    public static byte[] AesEncrypt(object? data, string password, int iterations = DefaultPbkdf2Iterations, JsonSerializerSettings? jsonSettings = null)
    {
        var plaintext = ToRawBytes(data, jsonSettings);
        var salt = RandomBytes(SaltSize);
        var key = DeriveKey(password, salt, iterations);

        var nonce = RandomBytes(GcmNonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[GcmTagSize];

        using (var aes = new AesGcm(key, GcmTagSize))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        CryptographicOperations.ZeroMemory(key);

        // [version(1)][iterations(4)][salt(16)][nonce(12)][tag(16)][ciphertext]
        var output = new byte[1 + 4 + SaltSize + GcmNonceSize + GcmTagSize + ciphertext.Length];
        var offset = 0;
        output[offset++] = AesGcmPasswordVersion;
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(offset), iterations);
        offset += 4;
        Buffer.BlockCopy(salt, 0, output, offset, SaltSize);
        offset += SaltSize;
        Buffer.BlockCopy(nonce, 0, output, offset, GcmNonceSize);
        offset += GcmNonceSize;
        Buffer.BlockCopy(tag, 0, output, offset, GcmTagSize);
        offset += GcmTagSize;
        Buffer.BlockCopy(ciphertext, 0, output, offset, ciphertext.Length);

        return output;
    }

    /// <summary>
    /// Encrypts any value with AES-256-GCM using a password and returns the payload as a Base64 string.
    /// </summary>
    /// <param name="data">The value to encrypt.</param>
    /// <param name="password">The password used to derive the encryption key.</param>
    /// <param name="urlSafe">If <see langword="true"/>, produces URL-safe Base64.</param>
    /// <param name="iterations">The number of PBKDF2 iterations used for key derivation.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings. <see cref="TypeNameHandling"/> is always
    /// <see cref="TypeNameHandling.None"/>, whatever the settings ask for; every other setting is honoured.</param>
    /// <returns>The Base64-encoded encrypted payload.</returns>
    public static string AesEncryptToBase64(object? data, string password, bool urlSafe = false, int iterations = DefaultPbkdf2Iterations, JsonSerializerSettings? jsonSettings = null)
        => ToBase64(AesEncrypt(data, password, iterations, jsonSettings), urlSafe);

    /// <summary>
    /// Decrypts a payload produced by <see cref="AesEncrypt"/> and returns the raw plaintext bytes.
    /// </summary>
    /// <param name="payload">The encrypted payload.</param>
    /// <param name="password">The password used to derive the encryption key.</param>
    /// <returns>The decrypted bytes.</returns>
    /// <exception cref="ArgumentException">The payload is malformed or truncated.</exception>
    /// <exception cref="CryptographicException">Authentication failed (wrong password or tampered data).</exception>
    public static byte[] AesDecrypt(byte[] payload, string password)
    {
        const int headerSize = 1 + 4 + SaltSize + GcmNonceSize + GcmTagSize;
        if (payload is null || payload.Length < headerSize)
            throw new ArgumentException("The encrypted payload is malformed or truncated.", nameof(payload));

        var offset = 0;
        var version = payload[offset++];
        if (version != AesGcmPasswordVersion)
            throw new ArgumentException($"Unsupported password payload version: {version}.", nameof(payload));

        var iterations = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset));
        offset += 4;

        var salt = payload.AsSpan(offset, SaltSize).ToArray();
        offset += SaltSize;
        var nonce = payload.AsSpan(offset, GcmNonceSize).ToArray();
        offset += GcmNonceSize;
        var tag = payload.AsSpan(offset, GcmTagSize).ToArray();
        offset += GcmTagSize;

        var ciphertext = payload.AsSpan(offset).ToArray();
        var key = DeriveKey(password, salt, iterations);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, GcmTagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return plaintext;
    }

    /// <summary>
    /// Decrypts a Base64 payload produced by <see cref="AesEncryptToBase64"/> and returns the raw plaintext bytes.
    /// </summary>
    /// <param name="base64Payload">The Base64-encoded encrypted payload (standard or URL-safe).</param>
    /// <param name="password">The password used to derive the encryption key.</param>
    /// <returns>The decrypted bytes.</returns>
    public static byte[] AesDecryptFromBase64(string base64Payload, string password)
        => AesDecrypt(FromBase64(base64Payload), password);

    /// <summary>
    /// Decrypts a payload produced by <see cref="AesEncrypt"/> and returns the plaintext as a UTF-8 string.
    /// </summary>
    /// <param name="payload">The encrypted payload.</param>
    /// <param name="password">The password used to derive the encryption key.</param>
    /// <returns>The decrypted string.</returns>
    public static string AesDecryptToString(byte[] payload, string password)
        => System.Text.Encoding.UTF8.GetString(AesDecrypt(payload, password));

    /// <summary>
    /// Decrypts a Base64 payload produced by <see cref="AesEncryptToBase64"/> and returns the plaintext as a UTF-8 string.
    /// </summary>
    /// <param name="base64Payload">The Base64-encoded encrypted payload.</param>
    /// <param name="password">The password used to derive the encryption key.</param>
    /// <returns>The decrypted string.</returns>
    public static string AesDecryptFromBase64ToString(string base64Payload, string password)
        => System.Text.Encoding.UTF8.GetString(AesDecryptFromBase64(base64Payload, password));

    /// <summary>
    /// Decrypts a payload produced by <see cref="AesEncrypt"/> (from a serialized object) and deserializes it.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="payload">The encrypted payload.</param>
    /// <param name="password">The password used to derive the encryption key.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings. <see cref="TypeNameHandling"/> is always
    /// <see cref="TypeNameHandling.None"/>, whatever the settings ask for; every other setting is honoured.</param>
    /// <returns>The decrypted, deserialized value.</returns>
    public static T? AesDecryptToObject<T>(byte[] payload, string password, JsonSerializerSettings? jsonSettings = null)
    {
        var json = AesDecryptToString(payload, password);
        return string.IsNullOrEmpty(json) ? default : FromJson<T>(json, jsonSettings);
    }

    #endregion

    #region Raw-key AES-GCM

    /// <summary>
    /// Encrypts any value with AES-256-GCM using a raw 256-bit key.<br/>
    /// The returned buffer is self-describing: <c>[version][nonce][tag][ciphertext]</c>.
    /// </summary>
    /// <param name="data">The value to encrypt. Strings are encoded as UTF-8, other objects are serialized to JSON.</param>
    /// <param name="key">The 32-byte (256-bit) AES key.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings. <see cref="TypeNameHandling"/> is always
    /// <see cref="TypeNameHandling.None"/>, whatever the settings ask for; every other setting is honoured.</param>
    /// <returns>The encrypted payload.</returns>
    /// <exception cref="ArgumentException">The key is not 32 bytes long.</exception>
    public static byte[] AesEncryptWithKey(object? data, byte[] key, JsonSerializerSettings? jsonSettings = null)
    {
        ValidateKey(key);

        var plaintext = ToRawBytes(data, jsonSettings);
        var nonce = RandomBytes(GcmNonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[GcmTagSize];

        using (var aes = new AesGcm(key, GcmTagSize))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // [version(1)][nonce(12)][tag(16)][ciphertext]
        var output = new byte[1 + GcmNonceSize + GcmTagSize + ciphertext.Length];
        var offset = 0;
        output[offset++] = AesGcmKeyVersion;
        Buffer.BlockCopy(nonce, 0, output, offset, GcmNonceSize);
        offset += GcmNonceSize;
        Buffer.BlockCopy(tag, 0, output, offset, GcmTagSize);
        offset += GcmTagSize;
        Buffer.BlockCopy(ciphertext, 0, output, offset, ciphertext.Length);

        return output;
    }

    /// <summary>
    /// Decrypts a payload produced by <see cref="AesEncryptWithKey"/> and returns the raw plaintext bytes.
    /// </summary>
    /// <param name="payload">The encrypted payload.</param>
    /// <param name="key">The 32-byte (256-bit) AES key.</param>
    /// <returns>The decrypted bytes.</returns>
    /// <exception cref="ArgumentException">The payload is malformed or the key is invalid.</exception>
    /// <exception cref="CryptographicException">Authentication failed (wrong key or tampered data).</exception>
    public static byte[] AesDecryptWithKey(byte[] payload, byte[] key)
    {
        ValidateKey(key);

        const int headerSize = 1 + GcmNonceSize + GcmTagSize;
        if (payload is null || payload.Length < headerSize)
            throw new ArgumentException("The encrypted payload is malformed or truncated.", nameof(payload));

        var offset = 0;
        var version = payload[offset++];
        if (version != AesGcmKeyVersion)
            throw new ArgumentException($"Unsupported key payload version: {version}.", nameof(payload));

        var nonce = payload.AsSpan(offset, GcmNonceSize).ToArray();
        offset += GcmNonceSize;
        var tag = payload.AsSpan(offset, GcmTagSize).ToArray();
        offset += GcmTagSize;

        var ciphertext = payload.AsSpan(offset).ToArray();
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, GcmTagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    /// <summary>
    /// Generates a new cryptographically secure 256-bit AES key.
    /// </summary>
    /// <returns>A 32-byte key.</returns>
    public static byte[] GenerateAesKey() => RandomBytes(AesKeySize);

    /// <summary>
    /// Validates that a key is exactly 32 bytes (256-bit).
    /// </summary>
    private static void ValidateKey(byte[] key)
    {
        if (key is null || key.Length != AesKeySize)
            throw new ArgumentException($"The AES key must be exactly {AesKeySize} bytes (256-bit).", nameof(key));
    }

    #endregion
}
