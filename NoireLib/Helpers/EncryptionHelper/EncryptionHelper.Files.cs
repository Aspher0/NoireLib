using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace NoireLib.Helpers;

public static partial class EncryptionHelper
{
    /// <summary>Magic bytes identifying a NoireLib encrypted file ("NLE1").</summary>
    private static readonly byte[] FileMagic = { 0x4E, 0x4C, 0x45, 0x31 };

    /// <summary>The encrypted file format version.</summary>
    private const byte FileFormatVersion = 1;

    /// <summary>The size, in bytes, of the AES-CBC initialization vector.</summary>
    private const int CbcIvSize = 16;

    /// <summary>The size, in bytes, of the HMAC-SHA256 authentication tag.</summary>
    private const int FileMacSize = 32;

    /// <summary>The size, in bytes, of the fixed file header (magic + version + iterations + salt + iv).</summary>
    private const int FileHeaderSize = 4 + 1 + 4 + SaltSize + CbcIvSize;

    /// <summary>The buffer size used for streaming file operations.</summary>
    private const int FileBufferSize = 81920;

    /// <summary>
    /// Encrypts a file with AES-256-CBC and authenticates it with HMAC-SHA256 (encrypt-then-MAC).<br/>
    /// The encryption and MAC keys are derived from the password with PBKDF2, and the file is streamed
    /// so arbitrarily large files can be processed without loading them fully into memory.
    /// </summary>
    /// <param name="sourcePath">The path to the plaintext file to encrypt.</param>
    /// <param name="destinationPath">The path where the encrypted file is written.</param>
    /// <param name="password">The password used to derive the keys.</param>
    /// <param name="overwrite">If <see langword="true"/>, overwrites the destination if it already exists.</param>
    /// <param name="iterations">The number of PBKDF2 iterations used for key derivation.</param>
    /// <returns><see langword="true"/> if the file was encrypted successfully; otherwise, <see langword="false"/>.</returns>
    public static bool EncryptFile(string sourcePath, string destinationPath, string password, bool overwrite = false, int iterations = DefaultPbkdf2Iterations)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                NoireLogger.LogError($"Cannot encrypt file, source does not exist: {sourcePath}", LogPrefix);
                return false;
            }

            if (!overwrite && File.Exists(destinationPath))
            {
                NoireLogger.LogError($"Cannot encrypt file, destination already exists: {destinationPath}", LogPrefix);
                return false;
            }

            var salt = RandomBytes(SaltSize);
            var iv = RandomBytes(CbcIvSize);
            var (encKey, macKey) = DeriveFileKeys(password, salt, iterations);

            try
            {
                using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

                // Header: [magic(4)][version(1)][iterations(4)][salt(16)][iv(16)]
                var header = new byte[FileHeaderSize];
                var offset = 0;
                Buffer.BlockCopy(FileMagic, 0, header, offset, FileMagic.Length);
                offset += FileMagic.Length;
                header[offset++] = FileFormatVersion;
                BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(offset), iterations);
                offset += 4;
                Buffer.BlockCopy(salt, 0, header, offset, SaltSize);
                offset += SaltSize;
                Buffer.BlockCopy(iv, 0, header, offset, CbcIvSize);

                destination.Write(header, 0, header.Length);

                using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey);
                hmac.AppendData(header);

                using (var aes = Aes.Create())
                {
                    aes.Key = encKey;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using var encryptor = aes.CreateEncryptor();
                    var tee = new MacTeeStream(destination, hmac);

                    using (var crypto = new CryptoStream(tee, encryptor, CryptoStreamMode.Write))
                    using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        source.CopyTo(crypto, FileBufferSize);
                    }
                }

                var mac = hmac.GetHashAndReset();
                destination.Write(mac, 0, mac.Length);

                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encKey);
                CryptographicOperations.ZeroMemory(macKey);
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to encrypt file: {sourcePath}", LogPrefix);
            TryDeleteFile(destinationPath);
            return false;
        }
    }

    /// <summary>
    /// Decrypts a file produced by <see cref="EncryptFile"/>.<br/>
    /// The HMAC is verified before any plaintext is written, so a wrong password or tampered file
    /// yields no output.
    /// </summary>
    /// <param name="sourcePath">The path to the encrypted file.</param>
    /// <param name="destinationPath">The path where the decrypted file is written.</param>
    /// <param name="password">The password used to derive the keys.</param>
    /// <param name="overwrite">If <see langword="true"/>, overwrites the destination if it already exists.</param>
    /// <returns><see langword="true"/> if the file was decrypted and authenticated successfully; otherwise, <see langword="false"/>.</returns>
    public static bool DecryptFile(string sourcePath, string destinationPath, string password, bool overwrite = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                NoireLogger.LogError($"Cannot decrypt file, source does not exist: {sourcePath}", LogPrefix);
                return false;
            }

            if (!overwrite && File.Exists(destinationPath))
            {
                NoireLogger.LogError($"Cannot decrypt file, destination already exists: {destinationPath}", LogPrefix);
                return false;
            }

            var fileLength = new FileInfo(sourcePath).Length;
            if (fileLength < FileHeaderSize + FileMacSize)
            {
                NoireLogger.LogError($"Cannot decrypt file, it is too small to be a valid encrypted file: {sourcePath}", LogPrefix);
                return false;
            }

            // Read and validate the header.
            var header = new byte[FileHeaderSize];
            using (var reader = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                ReadExactly(reader, header, header.Length);

            for (var i = 0; i < FileMagic.Length; i++)
            {
                if (header[i] != FileMagic[i])
                {
                    NoireLogger.LogError($"Cannot decrypt file, invalid magic bytes: {sourcePath}", LogPrefix);
                    return false;
                }
            }

            var offset = FileMagic.Length;
            var version = header[offset++];
            if (version != FileFormatVersion)
            {
                NoireLogger.LogError($"Cannot decrypt file, unsupported format version {version}: {sourcePath}", LogPrefix);
                return false;
            }

            var iterations = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(offset));
            offset += 4;
            var salt = header.AsSpan(offset, SaltSize).ToArray();
            offset += SaltSize;
            var iv = header.AsSpan(offset, CbcIvSize).ToArray();

            var (encKey, macKey) = DeriveFileKeys(password, salt, iterations);

            try
            {
                var cipherLength = fileLength - FileHeaderSize - FileMacSize;

                // Pass 1: verify the HMAC before producing any output.
                if (!VerifyFileMac(sourcePath, header, cipherLength, macKey))
                {
                    NoireLogger.LogError($"Cannot decrypt file, authentication failed (wrong password or tampered file): {sourcePath}", LogPrefix);
                    return false;
                }

                // Pass 2: decrypt the ciphertext region into the destination.
                using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                {
                    Position = FileHeaderSize,
                };

                using var bounded = new BoundedReadStream(source, cipherLength);
                using var aes = Aes.Create();
                aes.Key = encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                using var crypto = new CryptoStream(bounded, decryptor, CryptoStreamMode.Read);
                using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

                crypto.CopyTo(destination, FileBufferSize);

                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encKey);
                CryptographicOperations.ZeroMemory(macKey);
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to decrypt file: {sourcePath}", LogPrefix);
            TryDeleteFile(destinationPath);
            return false;
        }
    }

    #region Internals

    /// <summary>
    /// Derives independent 256-bit encryption and MAC keys from a password and salt.
    /// </summary>
    private static (byte[] EncKey, byte[] MacKey) DeriveFileKeys(string password, byte[] salt, int iterations)
    {
        var keyMaterial = DeriveKey(password, salt, iterations, AesKeySize * 2);
        var encKey = keyMaterial.AsSpan(0, AesKeySize).ToArray();
        var macKey = keyMaterial.AsSpan(AesKeySize, AesKeySize).ToArray();
        CryptographicOperations.ZeroMemory(keyMaterial);
        return (encKey, macKey);
    }

    /// <summary>
    /// Recomputes the HMAC over the header and ciphertext region and compares it against the stored tag.
    /// </summary>
    private static bool VerifyFileMac(string sourcePath, byte[] header, long cipherLength, byte[] macKey)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read)
        {
            Position = FileHeaderSize,
        };

        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey);
        hmac.AppendData(header);

        var buffer = new byte[FileBufferSize];
        var remaining = cipherLength;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = source.Read(buffer, 0, toRead);
            if (read <= 0)
                break;

            hmac.AppendData(buffer, 0, read);
            remaining -= read;
        }

        var storedMac = new byte[FileMacSize];
        ReadExactly(source, storedMac, storedMac.Length);

        var computedMac = hmac.GetHashAndReset();
        return CryptographicOperations.FixedTimeEquals(computedMac, storedMac);
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes from a stream, throwing if the stream ends early.
    /// </summary>
    private static void ReadExactly(Stream stream, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, total, count - total);
            if (read <= 0)
                throw new EndOfStreamException("Unexpected end of stream while reading encrypted file.");

            total += read;
        }
    }

    /// <summary>
    /// Attempts to delete a file, swallowing any error (used to clean up partial output on failure).
    /// </summary>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// A write-only stream that forwards writes to an inner stream while feeding them into an HMAC.
    /// The inner stream is intentionally left open on dispose.
    /// </summary>
    private sealed class MacTeeStream : Stream
    {
        private readonly Stream inner;
        private readonly IncrementalHash hmac;

        public MacTeeStream(Stream inner, IncrementalHash hmac)
        {
            this.inner = inner;
            this.hmac = hmac;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            hmac.AppendData(buffer, offset, count);
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>
    /// A read-only stream that exposes at most a fixed number of bytes from an inner stream.
    /// The inner stream is intentionally left open on dispose.
    /// </summary>
    private sealed class BoundedReadStream : Stream
    {
        private readonly Stream inner;
        private long remaining;

        public BoundedReadStream(Stream inner, long length)
        {
            this.inner = inner;
            remaining = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (remaining <= 0)
                return 0;

            var toRead = (int)Math.Min(count, remaining);
            var read = inner.Read(buffer, offset, toRead);
            remaining -= read;
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
}
