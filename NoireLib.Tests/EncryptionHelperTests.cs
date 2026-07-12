using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for <see cref="EncryptionHelper"/>: encoding, hashing, password hashing and AES encryption round-trips.
/// </summary>
public class EncryptionHelperTests : IDisposable
{
    private sealed class SamplePayload
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private const string Password = "correct horse battery staple";

    private readonly string tempDirectory;

    public EncryptionHelperTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLib_EncTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    #region Encoding

    [Fact]
    public void Base64_String_RoundTrips()
    {
        const string original = "Hello, NoireLib! éàü 🎲";
        var encoded = original.ToBase64();
        encoded.FromBase64ToString().Should().Be(original);
    }

    [Fact]
    public void Base64_UrlSafe_HasNoPaddingOrUnsafeChars()
    {
        var data = new byte[] { 0xFB, 0xEF, 0xBE, 0xFF, 0x00 };
        var urlSafe = data.ToBase64(urlSafe: true);
        urlSafe.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
        urlSafe.FromBase64().Should().Equal(data);
    }

    [Fact]
    public void SerializeToBase64_Object_RoundTrips()
    {
        var payload = new SamplePayload { Name = "dice", Value = 42 };
        var encoded = EncryptionHelper.SerializeToBase64(payload);
        var decoded = encoded.DeserializeFromBase64<SamplePayload>();
        decoded.Should().NotBeNull();
        decoded!.Name.Should().Be("dice");
        decoded.Value.Should().Be(42);
    }

    [Fact]
    public void Hex_RoundTrips_AndMatchesKnownVector()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        bytes.ToHex().Should().Be("deadbeef");
        bytes.ToHex(upperCase: true).Should().Be("DEADBEEF");
        "0xDEADBEEF".FromHex().Should().Equal(bytes);
    }

    #endregion

    #region Hashing

    [Fact]
    public void Sha256_MatchesKnownVector()
    {
        // SHA-256("abc") known vector.
        EncryptionHelper.Sha256("abc")
            .Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void Sha256_Base64Format_MatchesRawHash()
    {
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("abc")));
        EncryptionHelper.Sha256("abc", EncryptionHelper.BinaryTextFormat.Base64).Should().Be(expected);
    }

    [Fact]
    public void Hmac_MatchesKnownVector()
    {
        // HMAC-SHA256 with key "key" over "The quick brown fox jumps over the lazy dog".
        var result = EncryptionHelper.Hmac("The quick brown fox jumps over the lazy dog", "key");
        result.Should().Be("f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8");
    }

    [Fact]
    public void HashFile_MatchesInMemoryHash()
    {
        var path = Path.Combine(tempDirectory, "hash-me.bin");
        var content = RandomNumberGenerator.GetBytes(4096);
        File.WriteAllBytes(path, content);

        EncryptionHelper.HashFile(path).Should().Be(EncryptionHelper.Sha256(content));
    }

    #endregion

    #region Password hashing

    [Fact]
    public void Argon2_HashVerifies_AndRejectsWrongPassword()
    {
        var hash = EncryptionHelper.HashPasswordArgon2(Password, memoryKb: 8192, iterations: 2);
        hash.Should().StartWith("$argon2id$v=19$");
        EncryptionHelper.VerifyPasswordArgon2(Password, hash).Should().BeTrue();
        EncryptionHelper.VerifyPasswordArgon2("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public void Argon2_ProducesDifferentHashesForSamePassword()
    {
        var a = EncryptionHelper.HashPasswordArgon2(Password, memoryKb: 8192, iterations: 2);
        var b = EncryptionHelper.HashPasswordArgon2(Password, memoryKb: 8192, iterations: 2);
        a.Should().NotBe(b); // random salt
    }

    [Fact]
    public void Bcrypt_HashVerifies_AndRejectsWrongPassword()
    {
        var hash = EncryptionHelper.HashPasswordBcrypt(Password, workFactor: 6);
        EncryptionHelper.VerifyPasswordBcrypt(Password, hash).Should().BeTrue();
        EncryptionHelper.VerifyPasswordBcrypt("wrong", hash).Should().BeFalse();
    }

    #endregion

    #region AES in-memory

    [Fact]
    public void AesPassword_RoundTripsString()
    {
        const string secret = "The dice are cast. 🎲";
        var payload = EncryptionHelper.AesEncrypt(secret, Password, iterations: 10000);
        EncryptionHelper.AesDecryptToString(payload, Password).Should().Be(secret);
    }

    [Fact]
    public void AesPassword_Base64RoundTripsObject()
    {
        var original = new SamplePayload { Name = "roulette", Value = 7 };
        var encoded = EncryptionHelper.AesEncryptToBase64(original, Password, iterations: 10000);
        var decrypted = EncryptionHelper.AesDecryptToObject<SamplePayload>(
            encoded.FromBase64(), Password);

        decrypted.Should().NotBeNull();
        decrypted!.Name.Should().Be("roulette");
        decrypted.Value.Should().Be(7);
    }

    [Fact]
    public void AesPassword_WrongPasswordThrows()
    {
        var payload = EncryptionHelper.AesEncrypt("secret", Password, iterations: 10000);
        var act = () => EncryptionHelper.AesDecryptToString(payload, "wrong password");
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void AesPassword_TamperedPayloadThrows()
    {
        var payload = EncryptionHelper.AesEncrypt("secret", Password, iterations: 10000);
        payload[^1] ^= 0xFF; // flip a ciphertext bit
        var act = () => EncryptionHelper.AesDecryptToString(payload, Password);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void AesKey_RoundTripsBytes()
    {
        var key = EncryptionHelper.GenerateAesKey();
        var data = RandomNumberGenerator.GetBytes(1024);
        var payload = EncryptionHelper.AesEncryptWithKey(data, key);
        EncryptionHelper.AesDecryptWithKey(payload, key).Should().Equal(data);
    }

    [Fact]
    public void AesKey_InvalidKeyLengthThrows()
    {
        var act = () => EncryptionHelper.AesEncryptWithKey("data", new byte[16]);
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region File encryption

    [Fact]
    public void EncryptFile_DecryptFile_RoundTripsLargeFile()
    {
        var source = Path.Combine(tempDirectory, "plain.bin");
        var encrypted = Path.Combine(tempDirectory, "cipher.nle");
        var decrypted = Path.Combine(tempDirectory, "plain.out.bin");

        // Larger than the streaming buffer to exercise chunking.
        var content = RandomNumberGenerator.GetBytes(200_000);
        File.WriteAllBytes(source, content);

        EncryptionHelper.EncryptFile(source, encrypted, Password, iterations: 10000).Should().BeTrue();
        File.Exists(encrypted).Should().BeTrue();
        File.ReadAllBytes(encrypted).Should().NotEqual(content);

        EncryptionHelper.DecryptFile(encrypted, decrypted, Password).Should().BeTrue();
        File.ReadAllBytes(decrypted).Should().Equal(content);
    }

    [Fact]
    public void DecryptFile_WrongPasswordFails_AndWritesNoOutput()
    {
        var source = Path.Combine(tempDirectory, "plain2.bin");
        var encrypted = Path.Combine(tempDirectory, "cipher2.nle");
        var decrypted = Path.Combine(tempDirectory, "plain2.out.bin");

        File.WriteAllBytes(source, RandomNumberGenerator.GetBytes(5000));
        EncryptionHelper.EncryptFile(source, encrypted, Password, iterations: 10000).Should().BeTrue();

        EncryptionHelper.DecryptFile(encrypted, decrypted, "wrong password").Should().BeFalse();
        File.Exists(decrypted).Should().BeFalse();
    }

    [Fact]
    public void DecryptFile_TamperedFileFails()
    {
        var source = Path.Combine(tempDirectory, "plain3.bin");
        var encrypted = Path.Combine(tempDirectory, "cipher3.nle");
        var decrypted = Path.Combine(tempDirectory, "plain3.out.bin");

        File.WriteAllBytes(source, RandomNumberGenerator.GetBytes(5000));
        EncryptionHelper.EncryptFile(source, encrypted, Password, iterations: 10000).Should().BeTrue();

        // Flip a byte inside the ciphertext region (past the 41-byte header).
        var bytes = File.ReadAllBytes(encrypted);
        bytes[100] ^= 0xFF;
        File.WriteAllBytes(encrypted, bytes);

        EncryptionHelper.DecryptFile(encrypted, decrypted, Password).Should().BeFalse();
        File.Exists(decrypted).Should().BeFalse();
    }

    #endregion
}
