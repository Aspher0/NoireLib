# Helper Documentation : EncryptionHelper

You are reading the documentation for the `EncryptionHelper` static helper.

## Table of Contents
- [Overview](#overview)
- [The "anything" input model](#the-anything-input-model)
- [Encoding](#encoding)
- [Hashing](#hashing)
- [Password Hashing](#password-hashing)
- [AES Encryption (in-memory)](#aes-encryption-in-memory)
- [File Encryption](#file-encryption)
- [Formats & Wire Layout](#formats--wire-layout)
- [Security Notes](#security-notes)

---

## Overview

`EncryptionHelper` is a static helper in the `NoireLib.Helpers` namespace that provides a single surface for
turning virtually anything (raw bytes, strings, files, or any serializable object) into encoded, hashed or
encrypted data.

It covers:
- **Encoding** - Base64, URL-safe Base64, and Hex (encode + decode).
- **Hashing** - SHA-256/384/512, SHA-1, MD5, and keyed HMAC, plus streamed file hashing.
- **Password hashing** - Argon2id and BCrypt (hash + verify), each producing a self-describing string.
- **Symmetric encryption** - AES-256-GCM for in-memory payloads (password- or key-based).
- **File encryption** - AES-256-CBC with HMAC-SHA256 (encrypt-then-MAC), fully streamed.

Password hashing relies on the `Konscious.Security.Cryptography.Argon2` and `BCrypt.Net-Next` packages;
everything else is built on `System.Security.Cryptography`.

---

## The "anything" input model

Most methods accept an `object?` and resolve it to raw bytes with the following rules:

| Input | Handling |
|-------|----------|
| `null` | Empty byte array |
| `byte[]`, `ArraySegment<byte>`, `Memory<byte>`, `ReadOnlyMemory<byte>` | Used as-is |
| `string` | Encoded as UTF-8 |
| Anything else | Serialized to JSON (Newtonsoft.Json), then UTF-8 |

This is why `Sha256("abc")` hashes the UTF-8 bytes of `abc`, while `Sha256(myObject)` hashes its JSON.

---

## Encoding

```csharp
// Base64
string b64      = "hello".ToBase64();                 // aGVsbG8=
string urlSafe  = data.ToBase64(urlSafe: true);        // no +/= chars
string back     = b64.FromBase64ToString();            // hello
byte[] rawBytes = b64.FromBase64();

// Any object -> Base64 (JSON under the hood)
string encoded  = EncryptionHelper.SerializeToBase64(myObject);
MyType? decoded = encoded.DeserializeFromBase64<MyType>();

// Hex
string hex      = data.ToHex();                        // lowercase
string hexUpper = data.ToHex(upperCase: true);
byte[] fromHex  = "deadbeef".FromHex();                // also accepts "0x" prefix
```

---

## Hashing

```csharp
string sha256 = EncryptionHelper.Sha256("abc");                       // hex by default
string sha512 = EncryptionHelper.Sha512(myObject);
string b64    = EncryptionHelper.Sha256("abc", EncryptionHelper.BinaryTextFormat.Base64);

// Generic
string hash   = EncryptionHelper.Hash(data, EncryptionHelper.HashAlgorithmType.Sha384);
byte[] raw    = EncryptionHelper.HashBytes(data);

// HMAC
string mac    = EncryptionHelper.Hmac(message, key);                  // key: string or byte[]

// Files (streamed, no full read into memory)
string? fileHash = EncryptionHelper.HashFile("path/to/file");
```

`BinaryTextFormat` controls output: `Hex`, `HexUpper`, `Base64`, `Base64Url`.

---

## Password Hashing

Both algorithms embed every parameter needed for verification into the returned string.

```csharp
// Argon2id -> $argon2id$v=19$m=65536,t=3,p=1$<salt>$<hash>
string argon = EncryptionHelper.HashPasswordArgon2(password);
bool ok      = EncryptionHelper.VerifyPasswordArgon2(password, argon);

// BCrypt -> standard $2a$... string
string bcrypt = EncryptionHelper.HashPasswordBcrypt(password, workFactor: 12);
bool ok2      = EncryptionHelper.VerifyPasswordBcrypt(password, bcrypt);
```

Verification is constant-time (Argon2 via `CryptographicOperations.FixedTimeEquals`, BCrypt internally).
Each call uses a fresh random salt, so hashing the same password twice yields different strings.

---

## AES Encryption (in-memory)

AES-256-GCM (authenticated). Tampered or wrong-password payloads throw `CryptographicException`.

```csharp
// Password-based (key derived with PBKDF2-SHA256)
byte[] payload  = EncryptionHelper.AesEncrypt(anything, password);
string asB64    = EncryptionHelper.AesEncryptToBase64(anything, password);

string text     = EncryptionHelper.AesDecryptToString(payload, password);
MyType? obj     = EncryptionHelper.AesDecryptToObject<MyType>(payload, password);
byte[] bytes    = EncryptionHelper.AesDecrypt(payload, password);

// Raw 256-bit key
byte[] key      = EncryptionHelper.GenerateAesKey();
byte[] enc      = EncryptionHelper.AesEncryptWithKey(anything, key);
byte[] dec      = EncryptionHelper.AesDecryptWithKey(enc, key);
```

---

## File Encryption

AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC), streamed so large files never load fully into memory.
The HMAC is verified **before** any plaintext is written, so a wrong password or tampered file yields no output.

```csharp
bool encrypted = EncryptionHelper.EncryptFile("plain.txt", "cipher.nle", password);
bool decrypted = EncryptionHelper.DecryptFile("cipher.nle", "plain.out.txt", password);
```

These return `false` (and log via `NoireLogger`) instead of throwing; on failure any partial output is deleted.
Pass `overwrite: true` to replace an existing destination.

---

## Formats & Wire Layout

| Payload | Layout |
|---------|--------|
| AES-GCM (password) | `[version=1][iterations(4, BE)][salt(16)][nonce(12)][tag(16)][ciphertext]` |
| AES-GCM (raw key) | `[version=2][nonce(12)][tag(16)][ciphertext]` |
| Encrypted file | `["NLE1"(4)][version=1][iterations(4, BE)][salt(16)][iv(16)][ciphertext...][hmac(32)]` |

The PBKDF2 iteration count is stored in the payload, so a future change to the default does not break old data.

---

## Security Notes

- MD5 and SHA-1 are provided for checksums/interop only - do **not** use them for security.
- Default PBKDF2 iterations are tuned for security; lower them only in tests where speed matters.
- Argon2id defaults: 64 MiB memory, 3 iterations, parallelism 1, 32-byte hash.
- AES keys and derived key material are zeroed (`CryptographicOperations.ZeroMemory`) after use.
