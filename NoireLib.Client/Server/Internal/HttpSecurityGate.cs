using System;
using System.Collections.Generic;
using System.Globalization;

namespace NoireLib.Remote.Internal;

// The API socket and the console socket answer it differently.
internal interface IHttpGate
{
    HttpFailure? CheckHeaders(HttpRequestData request, NoireRemoteOptions options, string token, int port, bool isLoopback, string remoteAddress, out bool signed);

    HttpFailure? CheckBody(HttpRequestData request, NoireRemoteOptions options, string remoteAddress, bool signed);
}

// The order is the documented one. The browser check comes first: it does not depend on a browser honouring anything.
internal sealed class HttpSecurityGate(INoireRemoteHost host) : IHttpGate
{
    private readonly object gate = new();
    private readonly Dictionary<string, FailureCount> failures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> nonces = new(StringComparer.Ordinal);
    private DateTime lastSweepUtc = DateTime.UtcNow;

    private sealed class FailureCount
    {
        public int Count;
        public DateTime WindowStartUtc;
        public DateTime BlockedUntilUtc;
    }

    // A refused request never has its body read. Only the signed scheme needs the body.
    public HttpFailure? CheckHeaders(
        HttpRequestData request,
        NoireRemoteOptions options,
        string token,
        int port,
        bool isLoopback,
        string remoteAddress,
        out bool signed)
    {
        signed = false;

        if (IsBlocked(remoteAddress))
            return new HttpFailure(403, NoireRemoteErrorCodes.Forbidden, "Too many rejected credentials from this address.");

        foreach (var header in NoireRemoteHeaders.BrowserHeaders)
        {
            if (request.HasHeader(header))
                return new HttpFailure(403, NoireRemoteErrorCodes.Forbidden,
                    "A request carrying '" + header + "' is refused. This listener is not reachable from a browser.");
        }

        if (!HostIsAccepted(request.Header("host"), options, port))
            return new HttpFailure(403, NoireRemoteErrorCodes.Forbidden, "The host header does not name this listener.");

        var authorization = request.Header("authorization");

        if (string.IsNullOrEmpty(authorization))
        {
            RecordFailure(remoteAddress, options);
            return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "This route needs an authorization header.");
        }

        // The file routes are the one place a body is not JSON.
        var isFileRoute = HttpQueryString.PathOf(request.Path)
            .StartsWith(NoireRemotePaths.Prefix + NoireRemotePaths.Files, StringComparison.Ordinal);

        if ((request.Method == "POST" || request.Method == "PUT") && request.DeclaredBodyLength > 0 && !isFileRoute)
        {
            var contentType = request.Header("content-type");

            if (contentType == null || contentType.IndexOf(NoireRemoteHeaders.JsonContentType, StringComparison.OrdinalIgnoreCase) < 0)
                return new HttpFailure(415, NoireRemoteErrorCodes.BadContentType, "A body is read as " + NoireRemoteHeaders.JsonContentType + ".");
        }

        if (authorization!.StartsWith(NoireRemoteHeaders.BearerScheme + " ", StringComparison.OrdinalIgnoreCase))
        {
            if (!isLoopback)
            {
                RecordFailure(remoteAddress, options);
                return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "The bearer credential is accepted on a loopback connection only.");
            }

            var supplied = authorization.Substring(NoireRemoteHeaders.BearerScheme.Length + 1).Trim();

            if (!NoireRemoteSignature.FixedTimeEquals(supplied, token))
            {
                RecordFailure(remoteAddress, options);
                return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "The credential is not this listener's.");
            }

            ClearFailures(remoteAddress);
            return null;
        }

        if (authorization.StartsWith(NoireRemoteHeaders.SignedScheme + " ", StringComparison.OrdinalIgnoreCase))
        {
            signed = true;
            return null;
        }

        RecordFailure(remoteAddress, options);
        return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "The authorization scheme is not one this listener reads.");
    }

    // Run once the body has arrived. The signature covers it.
    public HttpFailure? CheckBody(HttpRequestData request, NoireRemoteOptions options, string remoteAddress, bool signed)
    {
        if (!signed)
            return null;

        var failure = CheckSignature(request, options, remoteAddress);

        if (failure != null)
            return failure;

        ClearFailures(remoteAddress);
        return null;
    }

    private HttpFailure? CheckSignature(HttpRequestData request, NoireRemoteOptions options, string remoteAddress)
    {
        if (string.IsNullOrWhiteSpace(options.RemoteSecret))
        {
            RecordFailure(remoteAddress, options);
            return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "This listener has no remote secret configured.");
        }

        if (!NoireRemoteSignature.TryParseHeader(request.Header("authorization"), out var timestamp, out var nonce, out var mac))
        {
            RecordFailure(remoteAddress, options);
            return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "The signed credential is missing a field.");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (Math.Abs(now - timestamp) > options.RemoteClockSkew.TotalSeconds)
        {
            RecordFailure(remoteAddress, options);
            return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "The signed credential's timestamp is outside the accepted window.");
        }

        if (!AcceptNonce(nonce, options))
        {
            RecordFailure(remoteAddress, options);
            return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "This nonce has already been used.");
        }

        var expected = NoireRemoteSignature.ComputeMac(options.RemoteSecret!, request.Method, request.Target, request.Body, timestamp, nonce);

        if (!NoireRemoteSignature.FixedTimeEquals(mac, expected))
        {
            RecordFailure(remoteAddress, options);
            return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "The signature does not match the request.");
        }

        return null;
    }

    private bool AcceptNonce(string nonce, NoireRemoteOptions options)
    {
        lock (gate)
        {
            var now = DateTime.UtcNow;

            if (now - lastSweepUtc > TimeSpan.FromSeconds(60))
            {
                Sweep(now, options.RemoteNonceRetention);
                lastSweepUtc = now;
            }

            // Past the bound new nonces are refused. An old one is never forgotten early.
            if (nonces.Count >= 100_000)
                return false;

            if (nonces.ContainsKey(nonce))
                return false;

            nonces[nonce] = now;
            return true;
        }
    }

    private void Sweep(DateTime now, TimeSpan retention)
    {
        var expired = new List<string>();

        foreach (var pair in nonces)
        {
            if (now - pair.Value > retention)
                expired.Add(pair.Key);
        }

        foreach (var key in expired)
            nonces.Remove(key);
    }

    public static bool HostIsAccepted(string? host, NoireRemoteOptions options, int port)
    {
        if (string.IsNullOrEmpty(host))
            return false;

        var suffix = ":" + port.ToString(CultureInfo.InvariantCulture);
        var name = host!.EndsWith(suffix, StringComparison.Ordinal) ? host.Substring(0, host.Length - suffix.Length) : null;

        if (name == null)
            return false;

        if (name.Equals("127.0.0.1", StringComparison.Ordinal)
            || name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || name.Equals("[::1]", StringComparison.Ordinal)
            || name.Equals("::1", StringComparison.Ordinal))
            return true;

        if (name.Equals(options.BindAddress.ToString(), StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var allowed in options.AllowedHosts)
        {
            if (name.Equals(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool IsBlocked(string remoteAddress)
    {
        lock (gate)
        {
            if (!failures.TryGetValue(remoteAddress, out var entry))
                return false;

            return entry.BlockedUntilUtc > DateTime.UtcNow;
        }
    }

    private void RecordFailure(string remoteAddress, NoireRemoteOptions options)
    {
        lock (gate)
        {
            var now = DateTime.UtcNow;

            if (!failures.TryGetValue(remoteAddress, out var entry))
            {
                entry = new FailureCount { WindowStartUtc = now };
                failures[remoteAddress] = entry;
            }

            if (now - entry.WindowStartUtc > TimeSpan.FromMinutes(1))
            {
                entry.WindowStartUtc = now;
                entry.Count = 0;
            }

            entry.Count++;

            if (entry.Count >= options.FailedAuthLockoutThreshold && entry.BlockedUntilUtc <= now)
            {
                entry.BlockedUntilUtc = now + options.FailedAuthLockout;
                host.Log(NoireRemoteLogLevel.Warning,
                    "[NoireRemote] blocking " + remoteAddress + " for "
                    + options.FailedAuthLockout.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)
                    + " minutes after " + entry.Count.ToString(CultureInfo.InvariantCulture) + " rejected credentials",
                    null);
            }
        }
    }

    private void ClearFailures(string remoteAddress)
    {
        lock (gate)
            failures.Remove(remoteAddress);
    }
}
