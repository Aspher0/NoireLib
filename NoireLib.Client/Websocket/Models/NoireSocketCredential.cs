using NoireLib.Remote;
using System;

namespace NoireLib.Websocket;

/// <summary>
/// What a client authenticates to a NoireRemote listener with. A loopback listener takes the token it generated per
/// launch. A listener reached from another machine takes a value signed with the shared secret.
/// </summary>
public sealed class NoireSocketCredential
{
    private readonly string? token;
    private readonly string? secret;

    private NoireSocketCredential(string? token, string? secret)
    {
        this.token = token;
        this.secret = secret;
    }

    /// <summary>
    /// Creates a credential carrying a listener's loopback token. Accepted only on a loopback connection.
    /// </summary>
    /// <param name="token">The token, read from the listener's instance record.</param>
    /// <returns>The credential.</returns>
    /// <exception cref="ArgumentException">If the token is null or blank.</exception>
    public static NoireSocketCredential Bearer(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("A bearer credential needs a token.", nameof(token));

        return new NoireSocketCredential(token, null);
    }

    /// <summary>
    /// Creates a credential signed with the shared secret. A listener on another machine accepts this.
    /// </summary>
    /// <param name="remoteSecret">The shared secret. It never crosses the wire.</param>
    /// <returns>The credential.</returns>
    /// <exception cref="ArgumentException">If the secret is null or blank.</exception>
    public static NoireSocketCredential Signed(string remoteSecret)
    {
        if (string.IsNullOrWhiteSpace(remoteSecret))
            throw new ArgumentException("A signed credential needs the shared secret.", nameof(remoteSecret));

        return new NoireSocketCredential(null, remoteSecret);
    }

    // One nonce per upgrade. The body must match the bytes sent, or the MAC check fails.
    internal string BuildHeaderValue(string method, string path, byte[]? body = null)
        => token != null
            ? NoireRemoteHeaders.BearerScheme + " " + token
            : NoireRemoteSignature.CreateHeader(secret!, method, path, body);
}
