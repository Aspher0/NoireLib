using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NoireLib.Helpers;

public static partial class EncryptionHelper
{
    /// <summary>
    /// The hashing algorithm to use.
    /// </summary>
    public enum HashAlgorithmType
    {
        /// <summary>MD5 (128-bit). Not collision resistant, use only for checksums.</summary>
        Md5,

        /// <summary>SHA-1 (160-bit). Not collision resistant, avoid for security.</summary>
        Sha1,

        /// <summary>SHA-256 (256-bit).</summary>
        Sha256,

        /// <summary>SHA-384 (384-bit).</summary>
        Sha384,

        /// <summary>SHA-512 (512-bit).</summary>
        Sha512,
    }

    #region Generic hashing

    /// <summary>
    /// Computes the hash of any value (raw bytes, string or serializable object) and returns the raw hash bytes.
    /// </summary>
    /// <param name="data">The value to hash. Strings are encoded as UTF-8, other objects are serialized to JSON.</param>
    /// <param name="algorithm">The hashing algorithm to use.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings used when the value is serialized.
    /// <see cref="TypeNameHandling"/> is always <see cref="TypeNameHandling.None"/>, whatever the settings ask for;
    /// every other setting is honoured.</param>
    /// <returns>The raw hash bytes.</returns>
    public static byte[] HashBytes(object? data, HashAlgorithmType algorithm = HashAlgorithmType.Sha256, JsonSerializerSettings? jsonSettings = null)
        => ComputeHash(ToRawBytes(data, jsonSettings), algorithm);

    /// <summary>
    /// Computes the hash of any value (raw bytes, string or serializable object) and returns it in the specified format.
    /// </summary>
    /// <param name="data">The value to hash. Strings are encoded as UTF-8, other objects are serialized to JSON.</param>
    /// <param name="algorithm">The hashing algorithm to use.</param>
    /// <param name="format">The textual representation of the resulting hash.</param>
    /// <param name="jsonSettings">Optional JSON serializer settings used when the value is serialized.
    /// <see cref="TypeNameHandling"/> is always <see cref="TypeNameHandling.None"/>, whatever the settings ask for;
    /// every other setting is honoured.</param>
    /// <returns>The formatted hash string.</returns>
    public static string Hash(object? data, HashAlgorithmType algorithm = HashAlgorithmType.Sha256, BinaryTextFormat format = BinaryTextFormat.Hex, JsonSerializerSettings? jsonSettings = null)
        => FormatBytes(HashBytes(data, algorithm, jsonSettings), format);

    /// <summary>
    /// Computes the SHA-256 hash of any value and returns it in the specified format (lowercase hex by default).
    /// </summary>
    /// <param name="data">The value to hash.</param>
    /// <param name="format">The textual representation of the resulting hash.</param>
    /// <returns>The formatted hash string.</returns>
    public static string Sha256(object? data, BinaryTextFormat format = BinaryTextFormat.Hex)
        => Hash(data, HashAlgorithmType.Sha256, format);

    /// <summary>
    /// Computes the SHA-384 hash of any value and returns it in the specified format (lowercase hex by default).
    /// </summary>
    /// <param name="data">The value to hash.</param>
    /// <param name="format">The textual representation of the resulting hash.</param>
    /// <returns>The formatted hash string.</returns>
    public static string Sha384(object? data, BinaryTextFormat format = BinaryTextFormat.Hex)
        => Hash(data, HashAlgorithmType.Sha384, format);

    /// <summary>
    /// Computes the SHA-512 hash of any value and returns it in the specified format (lowercase hex by default).
    /// </summary>
    /// <param name="data">The value to hash.</param>
    /// <param name="format">The textual representation of the resulting hash.</param>
    /// <returns>The formatted hash string.</returns>
    public static string Sha512(object? data, BinaryTextFormat format = BinaryTextFormat.Hex)
        => Hash(data, HashAlgorithmType.Sha512, format);

    /// <summary>
    /// Computes the SHA-1 hash of any value and returns it in the specified format (lowercase hex by default).
    /// </summary>
    /// <remarks>SHA-1 is not collision resistant and should not be used for security purposes.</remarks>
    /// <param name="data">The value to hash.</param>
    /// <param name="format">The textual representation of the resulting hash.</param>
    /// <returns>The formatted hash string.</returns>
    public static string Sha1(object? data, BinaryTextFormat format = BinaryTextFormat.Hex)
        => Hash(data, HashAlgorithmType.Sha1, format);

    /// <summary>
    /// Computes the MD5 hash of any value and returns it in the specified format (lowercase hex by default).
    /// </summary>
    /// <remarks>MD5 is not collision resistant and should only be used for non-security checksums.</remarks>
    /// <param name="data">The value to hash.</param>
    /// <param name="format">The textual representation of the resulting hash.</param>
    /// <returns>The formatted hash string.</returns>
    public static string Md5(object? data, BinaryTextFormat format = BinaryTextFormat.Hex)
        => Hash(data, HashAlgorithmType.Md5, format);

    #endregion

    #region HMAC

    /// <summary>
    /// Computes a keyed hash (HMAC) of any value and returns the raw bytes.
    /// </summary>
    /// <param name="data">The value to authenticate. Strings are encoded as UTF-8, other objects are serialized to JSON.</param>
    /// <param name="key">The secret key (raw bytes or a string, which is encoded as UTF-8).</param>
    /// <param name="algorithm">The underlying hashing algorithm (SHA-1/256/384/512 are supported).</param>
    /// <param name="jsonSettings">Optional JSON serializer settings used when the value is serialized.
    /// <see cref="TypeNameHandling"/> is always <see cref="TypeNameHandling.None"/>, whatever the settings ask for;
    /// every other setting is honoured.</param>
    /// <returns>The raw HMAC bytes.</returns>
    public static byte[] HmacBytes(object? data, object key, HashAlgorithmType algorithm = HashAlgorithmType.Sha256, JsonSerializerSettings? jsonSettings = null)
    {
        var keyBytes = ToRawBytes(key, jsonSettings);
        var dataBytes = ToRawBytes(data, jsonSettings);

        return algorithm switch
        {
            HashAlgorithmType.Sha1 => HMACSHA1.HashData(keyBytes, dataBytes),
            HashAlgorithmType.Sha256 => HMACSHA256.HashData(keyBytes, dataBytes),
            HashAlgorithmType.Sha384 => HMACSHA384.HashData(keyBytes, dataBytes),
            HashAlgorithmType.Sha512 => HMACSHA512.HashData(keyBytes, dataBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "HMAC only supports SHA-1/256/384/512."),
        };
    }

    /// <summary>
    /// Computes a keyed hash (HMAC) of any value and returns it in the specified format (lowercase hex by default).
    /// </summary>
    /// <param name="data">The value to authenticate.</param>
    /// <param name="key">The secret key (raw bytes or a string, which is encoded as UTF-8).</param>
    /// <param name="algorithm">The underlying hashing algorithm (SHA-1/256/384/512 are supported).</param>
    /// <param name="format">The textual representation of the resulting HMAC.</param>
    /// <returns>The formatted HMAC string.</returns>
    public static string Hmac(object? data, object key, HashAlgorithmType algorithm = HashAlgorithmType.Sha256, BinaryTextFormat format = BinaryTextFormat.Hex)
        => FormatBytes(HmacBytes(data, key, algorithm), format);

    #endregion

    #region File hashing

    /// <summary>
    /// Computes the hash of a file and returns the raw hash bytes.
    /// </summary>
    /// <param name="filePath">The path to the file to hash.</param>
    /// <param name="algorithm">The hashing algorithm to use.</param>
    /// <returns>The raw hash bytes, or <see langword="null"/> if the file could not be read.</returns>
    public static byte[]? HashFileBytes(string filePath, HashAlgorithmType algorithm = HashAlgorithmType.Sha256)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                NoireLogger.LogError($"Cannot hash file, it does not exist: {filePath}", LogPrefix);
                return null;
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var hasher = CreateHashAlgorithm(algorithm);
            return hasher.ComputeHash(stream);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to hash file: {filePath}", LogPrefix);
            return null;
        }
    }

    /// <summary>
    /// Computes the hash of a file and returns it in the specified format (lowercase hex by default).
    /// </summary>
    /// <param name="filePath">The path to the file to hash.</param>
    /// <param name="algorithm">The hashing algorithm to use.</param>
    /// <param name="format">The textual representation of the resulting hash.</param>
    /// <returns>The formatted hash string, or <see langword="null"/> if the file could not be read.</returns>
    public static string? HashFile(string filePath, HashAlgorithmType algorithm = HashAlgorithmType.Sha256, BinaryTextFormat format = BinaryTextFormat.Hex)
    {
        var bytes = HashFileBytes(filePath, algorithm);
        return bytes is null ? null : FormatBytes(bytes, format);
    }

    #endregion

    #region Internals

    /// <summary>
    /// Computes the hash of the given bytes using the specified algorithm.
    /// </summary>
    private static byte[] ComputeHash(byte[] data, HashAlgorithmType algorithm)
    {
        return algorithm switch
        {
            HashAlgorithmType.Md5 => MD5.HashData(data),
            HashAlgorithmType.Sha1 => SHA1.HashData(data),
            HashAlgorithmType.Sha256 => SHA256.HashData(data),
            HashAlgorithmType.Sha384 => SHA384.HashData(data),
            HashAlgorithmType.Sha512 => SHA512.HashData(data),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported hash algorithm."),
        };
    }

    /// <summary>
    /// Creates a disposable <see cref="HashAlgorithm"/> instance for the specified algorithm, used for streaming.
    /// </summary>
    private static HashAlgorithm CreateHashAlgorithm(HashAlgorithmType algorithm)
    {
        return algorithm switch
        {
            HashAlgorithmType.Md5 => MD5.Create(),
            HashAlgorithmType.Sha1 => SHA1.Create(),
            HashAlgorithmType.Sha256 => SHA256.Create(),
            HashAlgorithmType.Sha384 => SHA384.Create(),
            HashAlgorithmType.Sha512 => SHA512.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported hash algorithm."),
        };
    }

    #endregion
}
