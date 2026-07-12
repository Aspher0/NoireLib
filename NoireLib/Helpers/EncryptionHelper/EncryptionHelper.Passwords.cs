using Konscious.Security.Cryptography;
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NoireLib.Helpers;

public static partial class EncryptionHelper
{
    #region Argon2id

    /// <summary>
    /// The Argon2 version encoded into the PHC hash string (0x13 = 19).
    /// </summary>
    private const int Argon2Version = 0x13;

    /// <summary>
    /// Hashes a password using Argon2id and returns a self-describing PHC string
    /// (<c>$argon2id$v=19$m=...,t=...,p=...$salt$hash</c>) that embeds all parameters needed for verification.
    /// </summary>
    /// <param name="password">The password to hash.</param>
    /// <param name="memoryKb">The memory cost in kibibytes (default 64 MiB).</param>
    /// <param name="iterations">The number of iterations / time cost (default 3).</param>
    /// <param name="parallelism">The degree of parallelism (default 1).</param>
    /// <param name="hashLength">The length of the derived hash in bytes (default 32).</param>
    /// <returns>The encoded Argon2id PHC string.</returns>
    public static string HashPasswordArgon2(string password, int memoryKb = 65536, int iterations = 3, int parallelism = 1, int hashLength = 32)
    {
        var salt = RandomBytes(SaltSize);
        var hash = ComputeArgon2(password, salt, memoryKb, iterations, parallelism, hashLength);

        return string.Format(
            CultureInfo.InvariantCulture,
            "$argon2id$v={0}$m={1},t={2},p={3}${4}${5}",
            Argon2Version,
            memoryKb,
            iterations,
            parallelism,
            Convert.ToBase64String(salt).TrimEnd('='),
            Convert.ToBase64String(hash).TrimEnd('='));
    }

    /// <summary>
    /// Verifies a password against an Argon2id PHC string produced by <see cref="HashPasswordArgon2"/>.
    /// </summary>
    /// <param name="password">The password to verify.</param>
    /// <param name="encodedHash">The encoded Argon2id PHC string.</param>
    /// <returns><see langword="true"/> if the password matches; otherwise, <see langword="false"/>.</returns>
    public static bool VerifyPasswordArgon2(string password, string encodedHash)
    {
        try
        {
            if (string.IsNullOrEmpty(encodedHash))
                return false;

            // Expected layout: ["", "argon2id", "v=19", "m=..,t=..,p=..", "<salt>", "<hash>"]
            var parts = encodedHash.Split('$');
            if (parts.Length != 6 || parts[1] != "argon2id")
                return false;

            var parameters = parts[3].Split(',');
            if (parameters.Length != 3)
                return false;

            var memoryKb = int.Parse(parameters[0].AsSpan(2), CultureInfo.InvariantCulture);
            var iterations = int.Parse(parameters[1].AsSpan(2), CultureInfo.InvariantCulture);
            var parallelism = int.Parse(parameters[2].AsSpan(2), CultureInfo.InvariantCulture);

            var salt = DecodeUnpaddedBase64(parts[4]);
            var expected = DecodeUnpaddedBase64(parts[5]);

            var actual = ComputeArgon2(password, salt, memoryKb, iterations, parallelism, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Failed to verify Argon2id hash.", LogPrefix);
            return false;
        }
    }

    /// <summary>
    /// Runs the Argon2id key derivation with the given parameters.
    /// </summary>
    private static byte[] ComputeArgon2(string password, byte[] salt, int memoryKb, int iterations, int parallelism, int hashLength)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password ?? string.Empty))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(hashLength);
    }

    /// <summary>
    /// Decodes an unpadded (PHC-style) Base64 string into raw bytes.
    /// </summary>
    private static byte[] DecodeUnpaddedBase64(string value)
    {
        switch (value.Length % 4)
        {
            case 2: value += "=="; break;
            case 3: value += "="; break;
        }

        return Convert.FromBase64String(value);
    }

    #endregion

    #region BCrypt

    /// <summary>
    /// Hashes a password using BCrypt and returns the standard BCrypt hash string (which embeds the salt and cost).
    /// </summary>
    /// <param name="password">The password to hash.</param>
    /// <param name="workFactor">The BCrypt cost factor (4-31, default 12). Each increment doubles the work.</param>
    /// <returns>The BCrypt hash string.</returns>
    public static string HashPasswordBcrypt(string password, int workFactor = 12)
        => BCrypt.Net.BCrypt.HashPassword(password ?? string.Empty, workFactor);

    /// <summary>
    /// Verifies a password against a BCrypt hash string produced by <see cref="HashPasswordBcrypt"/>.
    /// </summary>
    /// <param name="password">The password to verify.</param>
    /// <param name="hash">The BCrypt hash string.</param>
    /// <returns><see langword="true"/> if the password matches; otherwise, <see langword="false"/>.</returns>
    public static bool VerifyPasswordBcrypt(string password, string hash)
    {
        try
        {
            if (string.IsNullOrEmpty(hash))
                return false;

            return BCrypt.Net.BCrypt.Verify(password ?? string.Empty, hash);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Failed to verify BCrypt hash.", LogPrefix);
            return false;
        }
    }

    #endregion
}
