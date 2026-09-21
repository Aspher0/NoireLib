using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NoireLib.Remote;

/// <summary>
/// What both halves of network discovery agree on: the port, the identifier that tells a record's secret without
/// carrying it, and the two documents a probe and its answer are.
/// </summary>
public static class NoireRemoteDiscovery
{
    /// <summary>The label mixed into every derivation. Nothing here collides with another system's.</summary>
    public const string Label = "NoireRemoteDiscovery.v1";

    /// <summary>The method name the probe's message authentication code covers.</summary>
    public const string ProbeMethod = "PROBE";

    /// <summary>The method name the answer's message authentication code covers.</summary>
    public const string AnswerMethod = "ANSWER";

    /// <summary>The path both codes cover. A captured probe can never be replayed as a call.</summary>
    public const string Path = "/noire/v1/_discover";

    /// <summary>The lowest port the derivation can land on.</summary>
    public const int FirstPort = 45000;

    /// <summary>How many ports the derivation can land on.</summary>
    public const int PortRange = 15000;

    /// <summary>
    /// Works out the port a probe is sent to and answered on. A caller holding the secret computes the same one.
    /// Nothing needs configuring on either side.
    /// </summary>
    /// <param name="secret">The shared secret both sides hold.</param>
    /// <returns>A port between 45000 and 59999.</returns>
    /// <exception cref="ArgumentException">If the secret is null or blank.</exception>
    public static int DerivePort(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("A secret is required.", nameof(secret));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Label + "|" + secret));
        var value = BitConverter.ToUInt32(digest, 0);

        return FirstPort + (int)(value % PortRange);
    }

    /// <summary>
    /// Works out the identifier written into a record, letting a responder tell which records share its secret. It
    /// is one way: a record never carries a usable credential.
    /// </summary>
    /// <param name="secret">The shared secret.</param>
    /// <returns>Thirty-two hexadecimal characters, or an empty string when there is no secret.</returns>
    public static string SecretId(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return string.Empty;

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Label + "|" + secret));

        return Convert.ToHexString(digest, 0, 16);
    }

    /// <summary>
    /// Signs a probe or an answer, over the document with its own code field left out.
    /// </summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="method">The method name, one of the two on this class.</param>
    /// <param name="document">The document to sign. Its <c>mac</c> field is ignored.</param>
    /// <returns>The code, as hexadecimal.</returns>
    public static string Sign(string secret, string method, NoireRemoteDiscoveryMessage document)
    {
        var copy = document.WithoutMac();
        var body = NoireRemoteJson.WriteBytes(copy);

        return NoireRemoteSignature.ComputeMac(secret, method, Path, body, document.Ts, document.Nonce);
    }

    /// <summary>
    /// Checks a probe or an answer's code, its timestamp and nothing else. The nonce is the caller's to remember.
    /// </summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="method">The method name the code should cover.</param>
    /// <param name="document">The document to check.</param>
    /// <param name="clockSkew">How far the timestamp may be from this machine's clock.</param>
    /// <returns>True when the code matches and the timestamp is inside the window.</returns>
    public static bool Verify(string? secret, string method, NoireRemoteDiscoveryMessage? document, TimeSpan clockSkew)
    {
        if (string.IsNullOrWhiteSpace(secret) || document == null || string.IsNullOrEmpty(document.Mac) || string.IsNullOrEmpty(document.Nonce))
            return false;

        if (document.Protocol != NoireRemotePaths.Protocol)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (Math.Abs(now - document.Ts) > clockSkew.TotalSeconds)
            return false;

        return NoireRemoteSignature.FixedTimeEquals(document.Mac, Sign(secret!, method, document));
    }

    /// <summary>
    /// Builds a signed probe.
    /// </summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="endpoint">Only ask for hosts publishing this endpoint. Null asks for every host.</param>
    /// <returns>The probe, ready to send.</returns>
    public static NoireRemoteDiscoveryMessage Probe(string secret, string? endpoint = null)
    {
        var message = new NoireRemoteDiscoveryMessage
        {
            Kind = "probe",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = NoireRemoteSignature.CreateNonce(),
            Endpoint = endpoint,
        };

        message.Mac = Sign(secret, ProbeMethod, message);

        return message;
    }

    /// <summary>
    /// Builds a signed answer. It carries pointers to the listeners, because four records overflow the practical
    /// datagram size and a fragmented one is dropped by middleboxes.
    /// </summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="machine">The machine's name.</param>
    /// <param name="address">The local address that reaches the prober.</param>
    /// <param name="ports">The ports the listeners on this machine are bound to.</param>
    /// <returns>The answer, ready to send.</returns>
    public static NoireRemoteDiscoveryMessage Answer(string secret, string machine, string address, IReadOnlyList<int> ports)
    {
        var message = new NoireRemoteDiscoveryMessage
        {
            Kind = "answer",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = NoireRemoteSignature.CreateNonce(),
            Machine = machine,
            Address = address,
            Ports = ports,
        };

        message.Mac = Sign(secret, AnswerMethod, message);

        return message;
    }

    /// <summary>
    /// Formats a port for a discovery message.
    /// </summary>
    /// <param name="port">The port.</param>
    /// <returns>The port as text.</returns>
    public static string Format(int port)
        => port.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// One probe or one answer. Both sides share one shape, and one signing routine covers both.
/// </summary>
public sealed class NoireRemoteDiscoveryMessage
{
    /// <summary>
    /// Gets or sets the protocol version the sender speaks.
    /// </summary>
    public int Protocol { get; set; } = NoireRemotePaths.Protocol;

    /// <summary>
    /// Gets or sets which of the two documents this is, <c>probe</c> or <c>answer</c>.
    /// </summary>
    public string Kind { get; set; } = "probe";

    /// <summary>
    /// Gets or sets the unix seconds the sender claims.
    /// </summary>
    public long Ts { get; set; }

    /// <summary>
    /// Gets or sets the value that makes a captured document useless twice.
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the endpoint a probe asks for, or null for every host.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the answering machine's name.
    /// </summary>
    public string? Machine { get; set; }

    /// <summary>
    /// Gets or sets the answering machine's address that reaches the prober.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the ports the answering machine's listeners are bound to.
    /// </summary>
    public IReadOnlyList<int>? Ports { get; set; }

    /// <summary>
    /// Gets or sets the message authentication code over everything else.
    /// </summary>
    public string Mac { get; set; } = string.Empty;

    /// <summary>
    /// Copies this message with its code left out. The code is computed over this copy.
    /// </summary>
    /// <returns>The copy.</returns>
    public NoireRemoteDiscoveryMessage WithoutMac()
        => new()
        {
            Protocol = Protocol,
            Kind = Kind,
            Ts = Ts,
            Nonce = Nonce,
            Endpoint = Endpoint,
            Machine = Machine,
            Address = Address,
            Ports = Ports,
            Mac = string.Empty,
        };
}
