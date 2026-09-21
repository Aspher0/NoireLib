using System;
using System.Collections.Generic;

namespace NoireLib.Remote.Internal;

// A grant travels in the URL fragment. The fragment never reaches a server, a log or a referrer. It is exchanged once for a session token.
internal sealed class HttpConsoleSessions
{
    private const int MaxSessions = 4;

    private static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);

    private readonly object gate = new();
    private readonly Dictionary<string, DateTime> grants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);

    private sealed class Session
    {
        public DateTime StartedUtc;
        public DateTime TouchedUtc;
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                Sweep();
                return sessions.Count;
            }
        }
    }

    public string CreateGrant()
    {
        var grant = NoireRemoteSignature.CreateToken();

        lock (gate)
        {
            Sweep();
            grants[grant] = DateTime.UtcNow + GrantLifetime;
        }

        return grant;
    }

    // One use. A grant read twice is a grant somebody else also has.
    public string? Redeem(string? grant)
    {
        if (string.IsNullOrWhiteSpace(grant))
            return null;

        lock (gate)
        {
            Sweep();

            if (!grants.TryGetValue(grant!, out var expiry) || expiry <= DateTime.UtcNow)
                return null;

            grants.Remove(grant!);

            return Mint();
        }
    }

    // For an address the options let in on its own.
    public string Create()
    {
        lock (gate)
        {
            Sweep();

            return Mint();
        }
    }

    private string Mint()
    {
        var token = NoireRemoteSignature.CreateToken();
        var now = DateTime.UtcNow;

        sessions[token] = new Session { StartedUtc = now, TouchedUtc = now };

        while (sessions.Count > MaxSessions)
        {
            var oldest = string.Empty;
            var oldestTouch = DateTime.MaxValue;

            foreach (var pair in sessions)
            {
                if (pair.Value.TouchedUtc < oldestTouch)
                {
                    oldestTouch = pair.Value.TouchedUtc;
                    oldest = pair.Key;
                }
            }

            sessions.Remove(oldest);
        }

        return token;
    }

    public bool Accept(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        lock (gate)
        {
            Sweep();

            foreach (var pair in sessions)
            {
                if (!NoireRemoteSignature.FixedTimeEquals(pair.Key, token))
                    continue;

                pair.Value.TouchedUtc = DateTime.UtcNow;

                return true;
            }

            return false;
        }
    }

    public void RevokeAll()
    {
        lock (gate)
        {
            grants.Clear();
            sessions.Clear();
        }
    }

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        List<string>? expired = null;

        foreach (var pair in grants)
        {
            if (pair.Value <= now)
                (expired ??= []).Add(pair.Key);
        }

        if (expired != null)
        {
            foreach (var key in expired)
                grants.Remove(key);

            expired.Clear();
        }

        foreach (var pair in sessions)
        {
            if (now - pair.Value.TouchedUtc > IdleLifetime || now - pair.Value.StartedUtc > AbsoluteLifetime)
                (expired ??= []).Add(pair.Key);
        }

        if (expired == null)
            return;

        foreach (var key in expired)
            sessions.Remove(key);
    }
}
