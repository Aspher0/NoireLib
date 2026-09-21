using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NoireLib.Remote;

/// <summary>
/// The credential scheme a non-loopback call uses. The shared secret never crosses the wire. A request carries a
/// timestamp, a nonce and an HMAC over the request line and the body.
/// </summary>
public static class NoireRemoteSignature
{
    /// <summary>
    /// Builds the value of the authorization header for a signed request.
    /// </summary>
    /// <param name="secret">The shared secret, known to both sides and never sent.</param>
    /// <param name="method">The HTTP verb, uppercase.</param>
    /// <param name="path">The absolute path, including the leading slash and any query.</param>
    /// <param name="body">The request body as it is sent. Null is treated as empty.</param>
    /// <param name="timestamp">Unix seconds at the time of the call.</param>
    /// <param name="nonce">A value used once. Sixteen random bytes as hex is the shape the listener expects.</param>
    /// <returns>The complete header value, scheme included.</returns>
    /// <exception cref="ArgumentException">If the secret is null or blank.</exception>
    public static string CreateHeader(string secret, string method, string path, byte[]? body, long timestamp, string nonce)
    {
        var mac = ComputeMac(secret, method, path, body, timestamp, nonce);

        return NoireRemoteHeaders.SignedScheme
            + " ts=" + timestamp.ToString(CultureInfo.InvariantCulture)
            + ",nonce=" + nonce
            + ",mac=" + mac;
    }

    /// <summary>
    /// Builds the value of the authorization header for a signed request, taking the timestamp and the nonce from
    /// the current time and the system random source.
    /// </summary>
    /// <param name="secret">The shared secret, known to both sides and never sent.</param>
    /// <param name="method">The HTTP verb, uppercase.</param>
    /// <param name="path">The absolute path, including the leading slash and any query.</param>
    /// <param name="body">The request body as it is sent. Null is treated as empty.</param>
    /// <returns>The complete header value, scheme included.</returns>
    /// <exception cref="ArgumentException">If the secret is null or blank.</exception>
    public static string CreateHeader(string secret, string method, string path, byte[]? body)
        => CreateHeader(secret, method, path, body, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), CreateNonce());

    /// <summary>
    /// Computes the message authentication code a signed request carries.
    /// </summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="method">The HTTP verb, uppercase.</param>
    /// <param name="path">The absolute path.</param>
    /// <param name="body">The request body. Null is treated as empty.</param>
    /// <param name="timestamp">Unix seconds.</param>
    /// <param name="nonce">The nonce.</param>
    /// <returns>The code as uppercase hex.</returns>
    /// <exception cref="ArgumentException">If the secret is null or blank.</exception>
    public static string ComputeMac(string secret, string method, string path, byte[]? body, long timestamp, string nonce)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("A signed request needs a secret.", nameof(secret));

        var bodyHash = Convert.ToHexString(SHA256.HashData(body ?? []));

        var message = timestamp.ToString(CultureInfo.InvariantCulture)
            + "\n" + nonce
            + "\n" + method
            + "\n" + path
            + "\n" + bodyHash;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
    }

    /// <summary>
    /// Creates a nonce of the shape the listener expects.
    /// </summary>
    /// <returns>Sixteen random bytes as uppercase hex.</returns>
    public static string CreateNonce()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// Creates a credential of the shape a listener generates for loopback callers.
    /// </summary>
    /// <returns>Thirty-two random bytes, base64 encoded without padding or separators.</returns>
    public static string CreateToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", string.Empty)
            .Replace("/", string.Empty)
            .Replace("=", string.Empty);

    /// <summary>
    /// Reads the three fields out of a signed authorization header value.
    /// </summary>
    /// <param name="headerValue">The header value, with or without the scheme.</param>
    /// <param name="timestamp">Receives the unix seconds the request claims.</param>
    /// <param name="nonce">Receives the nonce.</param>
    /// <param name="mac">Receives the message authentication code.</param>
    /// <returns>True when all three fields were present and the timestamp parsed.</returns>
    public static bool TryParseHeader(string? headerValue, out long timestamp, out string nonce, out string mac)
    {
        timestamp = 0;
        nonce = string.Empty;
        mac = string.Empty;

        if (string.IsNullOrWhiteSpace(headerValue))
            return false;

        var value = headerValue!.Trim();

        if (value.StartsWith(NoireRemoteHeaders.SignedScheme + " ", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(NoireRemoteHeaders.SignedScheme.Length + 1);

        foreach (var part in value.Split(','))
        {
            var separator = part.IndexOf('=');

            if (separator <= 0)
                continue;

            var key = part.Substring(0, separator).Trim();
            var item = part.Substring(separator + 1).Trim();

            if (key.Equals("ts", StringComparison.OrdinalIgnoreCase))
                long.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out timestamp);
            else if (key.Equals("nonce", StringComparison.OrdinalIgnoreCase))
                nonce = item;
            else if (key.Equals("mac", StringComparison.OrdinalIgnoreCase))
                mac = item;
        }

        return timestamp != 0 && nonce.Length > 0 && mac.Length > 0;
    }

    /// <summary>
    /// Compares two credential strings without leaking where they first differ.
    /// </summary>
    /// <param name="left">The value read from the request.</param>
    /// <param name="right">The expected value.</param>
    /// <returns>True when both are non-null and equal.</returns>
    public static bool FixedTimeEquals(string? left, string? right)
    {
        if (left == null || right == null)
            return false;

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
