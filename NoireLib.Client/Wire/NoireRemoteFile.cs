using Newtonsoft.Json;
using System;

namespace NoireLib.Remote;

/// <summary>
/// A file crossing the wire as a reference. A member takes one as a parameter or returns one, and the bytes
/// move in chunks on the file routes.
/// </summary>
public sealed class NoireRemoteFile
{
    /// <summary>
    /// Gets or sets the transfer id. The file routes address it by this.
    /// </summary>
    [JsonProperty("$file")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file's name, for a caller writing it to disk.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many bytes the file carries.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 of the file, as hexadecimal.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media type, or null when the caller named none.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Reads the whole file into memory. A member takes this on a file it was handed.
    /// </summary>
    /// <returns>The bytes.</returns>
    /// <exception cref="InvalidOperationException">If the transfer is no longer held, or it never completed.</exception>
    public byte[] ReadAllBytes()
        => Reader?.Invoke() ?? throw new InvalidOperationException("This file reference is not backed by a held transfer.");

    /// <summary>
    /// Builds a file a member returns, from bytes it has in hand.
    /// </summary>
    /// <param name="name">The file's name.</param>
    /// <param name="bytes">The file.</param>
    /// <param name="contentType">The media type, or null.</param>
    /// <returns>The file, ready to return. The listener parks it and writes its reference in its place.</returns>
    /// <exception cref="ArgumentNullException">If the bytes are null.</exception>
    public static NoireRemoteFile FromBytes(string name, byte[] bytes, string? contentType = null)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        return new NoireRemoteFile
        {
            Name = name ?? string.Empty,
            Size = bytes.LongLength,
            ContentType = contentType,
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            Pending = bytes,
        };
    }

    internal Func<byte[]>? Reader { get; set; }

    internal byte[]? Pending { get; set; }
}

/// <summary>
/// The body a caller posts to declare a file it is about to send.
/// </summary>
public sealed class NoireRemoteFileRequest
{
    /// <summary>
    /// Gets or sets the file's name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many bytes the file carries.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 the finished transfer has to match, as hexadecimal. Empty skips the check.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media type, or null.
    /// </summary>
    public string? ContentType { get; set; }
}

/// <summary>
/// What a declared or resumed transfer looks like.
/// </summary>
public sealed class NoireRemoteFileStatus
{
    /// <summary>
    /// Gets or sets whether the transfer is held.
    /// </summary>
    public bool Ok { get; set; } = true;

    /// <summary>
    /// Gets or sets the transfer id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the largest chunk the listener accepts. It equals the listener's request cap.</summary>
    public int ChunkBytes { get; set; }

    /// <summary>
    /// Gets or sets how many bytes have arrived.
    /// </summary>
    public long Received { get; set; }

    /// <summary>
    /// Gets or sets how many bytes the file carries in total.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets whether every byte has arrived and the checksum matched.
    /// </summary>
    public bool Complete { get; set; }

    /// <summary>
    /// Gets or sets when the transfer is dropped if nothing touches it.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
