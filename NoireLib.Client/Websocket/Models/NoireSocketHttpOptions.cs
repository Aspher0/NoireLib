using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace NoireLib.Websocket;

/// <summary>
/// The HTTP-level settings a transport opens its connection with. Every one of these is honoured on all four
/// transports, including Socket.IO, whose protocol package reads only a subset directly.
/// </summary>
public sealed class NoireSocketHttpOptions
{
    /// <summary>
    /// The value sent when a caller sets no User-Agent of their own. Some web application firewalls reject a request
    /// carrying none at all.
    /// </summary>
    public const string DefaultUserAgent = "NoireLib.Client";

    /// <summary>
    /// Gets the request headers. A User-Agent is present by default. Replacing the entry replaces the value sent.
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["User-Agent"] = DefaultUserAgent,
    };

    /// <summary>
    /// Gets or sets an explicit proxy. Setting one takes precedence over <see cref="UseSystemProxy"/>.
    /// </summary>
    public IWebProxy? Proxy { get; set; } = null;

    /// <summary>
    /// Gets or sets whether the system proxy is used when no explicit one is set.
    /// </summary>
    public bool UseSystemProxy { get; set; } = true;

    /// <summary>
    /// Gets or sets the cookie container carried across requests and reconnects.
    /// </summary>
    public CookieContainer? Cookies { get; set; } = null;

    /// <summary>
    /// Gets or sets the client certificates offered during the TLS handshake.
    /// </summary>
    public X509CertificateCollection? ClientCertificates { get; set; } = null;

    /// <summary>
    /// Gets or sets the credentials used for HTTP authentication schemes the server challenges with.
    /// </summary>
    public ICredentials? Credentials { get; set; } = null;

    /// <summary>
    /// Gets or sets the server certificate check. Returning true for everything disables validation.
    /// </summary>
    public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; } = null;

    /// <summary>
    /// Gets or sets how long a single connection attempt may take before it is abandoned.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets or sets how long a pooled connection is reused before it is replaced. Keeps a long-lived client from
    /// pinning a stale DNS answer.
    /// </summary>
    public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets what the connection authenticates to a NoireRemote listener with. Null sends no credential, for
    /// a connection to anything else.
    /// </summary>
    public NoireSocketCredential? Credential { get; set; } = null;

    /// <summary>
    /// Copies these options.
    /// </summary>
    /// <returns>A copy whose header dictionary is its own.</returns>
    public NoireSocketHttpOptions Clone()
    {
        var copy = new NoireSocketHttpOptions
        {
            Proxy = Proxy,
            UseSystemProxy = UseSystemProxy,
            Cookies = Cookies,
            ClientCertificates = ClientCertificates,
            Credentials = Credentials,
            RemoteCertificateValidationCallback = RemoteCertificateValidationCallback,
            ConnectionTimeout = ConnectionTimeout,
            PooledConnectionLifetime = PooledConnectionLifetime,
            Credential = Credential,
        };

        copy.Headers.Clear();

        foreach (var pair in Headers)
            copy.Headers[pair.Key] = pair.Value;

        return copy;
    }
}
