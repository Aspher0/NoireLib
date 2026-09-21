using NoireLib.Remote.Internal;
using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>The browser console a listener serves on its own socket, generated from the manifest.</summary>
public sealed class NoireRemoteConsole
{
    private readonly NoireRemoteServer server;

    internal NoireRemoteConsole(NoireRemoteServer server)
    {
        this.server = server;
        Style = new NoireRemoteConsoleStyle();

        // A runtime restyle reaches the page.
        Style.Changed += () => server.InvalidateConsole(NoireRemoteServer.ConsoleScopes.Settings);
    }

    /// <summary>
    /// Gets what a plugin sets to drive the page's appearance. The console ships no theme of its own.
    /// </summary>
    public NoireRemoteConsoleStyle Style { get; }

    /// <summary>
    /// Gets whether the console socket is bound.
    /// </summary>
    public bool IsListening => Socket?.IsListening == true;

    /// <summary>
    /// Gets the port the console socket is bound to, or zero when it is not listening.
    /// </summary>
    public int Port => Socket?.Port ?? 0;

    /// <summary>
    /// Gets the password a networked browser types into the console's gate. It is
    /// <see cref="NoireRemoteOptions.ConsoleKey"/> when that is set, a generated one when the remote console is on
    /// without it, and null when nothing asks for a key.
    /// </summary>
    public string? Key => Socket?.Key;

    /// <summary>
    /// Builds the URL that opens the console, with a one-use grant in its fragment. A fragment never reaches the
    /// server, a log or a referrer.
    /// </summary>
    /// <param name="forRemote">True for the address another device on the network reaches, false for loopback.</param>
    /// <returns>The URL, or null when the console is not listening.</returns>
    public string? Open(bool forRemote = false)
    {
        var socket = Socket;

        if (socket == null || !socket.IsListening)
            return null;

        var address = forRemote && server.Options.EnableRemoteConsole ? NoireRemoteAddress.LocalAddress() : "127.0.0.1";

        return "http://" + address + ":" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + NoireRemoteConsolePaths.Page + "#g=" + socket.Sessions.CreateGrant();
    }

    /// <summary>
    /// Ends every console session. A page already open has to be reopened from a new URL.
    /// </summary>
    public void RevokeSessions()
        => Socket?.Sessions.RevokeAll();

    /// <summary>
    /// Gets how many console sessions are live.
    /// </summary>
    public int SessionCount => Socket?.Sessions.Count ?? 0;

    /// <summary>
    /// Reads the page's own source, before a nonce is written into it. A plugin replacing the page with
    /// <see cref="NoireRemoteOptions.ConsolePagePath"/> starts from this.
    /// </summary>
    /// <returns>The HTML.</returns>
    public string PageSource()
        => HttpConsolePage.Embedded();

    internal HttpConsoleSocket? Socket { get; set; }
}

/// <summary>
/// What a plugin sets to drive the console page's appearance. Every colour, radius, font and spacing on the page is a
/// CSS custom property, and the values here are written after the page's own.
/// </summary>
public sealed class NoireRemoteConsoleStyle
{
    /// <summary>
    /// Gets the custom properties written onto the page, keyed without the <c>--noire-</c> prefix. A name matches
    /// <c>[a-z0-9-]</c> and is at most forty characters. A value is at most two hundred and carries no
    /// <c>&lt;</c>, <c>;</c> or <c>}</c>.
    /// </summary>
    public IDictionary<string, string> Variables => variables;

    private readonly NotifyingDictionary<string> variables;
    private readonly NotifyingDictionary<NoireRemoteConsoleAsset> assets;
    private string? styleSheet;
    private string? title;

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteConsoleStyle"/> class.
    /// </summary>
    public NoireRemoteConsoleStyle()
    {
        variables = new NotifyingDictionary<string>(Raise);
        assets = new NotifyingDictionary<NoireRemoteConsoleAsset>(Raise);
    }

    // An open page redraws in the new style.
    internal event Action? Changed;

    private void Raise()
        => Changed?.Invoke();

    /// <summary>
    /// Gets or sets a stylesheet written verbatim after everything else. It wins, is capped at sixty-four
    /// kilobytes, and is not checked, because a plugin that sets it already runs code in this process.
    /// </summary>
    public string? StyleSheet
    {
        get => styleSheet;
        set
        {
            if (string.Equals(styleSheet, value, StringComparison.Ordinal))
                return;

            styleSheet = value;
            Raise();
        }
    }

    /// <summary>
    /// Gets the files served under the console's asset route, keyed by name. A font or a logo goes here.
    /// </summary>
    public IDictionary<string, NoireRemoteConsoleAsset> Assets => assets;

    /// <summary>
    /// Gets or sets the title the page and the browser tab carry. Null uses the host's name.
    /// </summary>
    public string? Title
    {
        get => title;
        set
        {
            if (string.Equals(title, value, StringComparison.Ordinal))
                return;

            title = value;
            Raise();
        }
    }

    /// <summary>
    /// The variable names the page reads. Setting anything else is allowed and has no effect.
    /// </summary>
    public static IReadOnlyList<string> KnownVariables { get; } =
    [
        "bg", "surface", "surface-raised", "border", "text", "text-dim", "accent", "accent-text",
        "accent-soft", "chip", "code", "ok", "warn", "error",
        "tok-key", "tok-str", "tok-num", "tok-bool", "tok-null",
        "radius", "font", "font-mono", "font-size", "gap", "page-width", "sidebar-width", "dock-height",
    ];
}

/// <summary>
/// One file the console serves beside its page.
/// </summary>
/// <param name="ContentType">The media type written on the answer, such as <c>font/woff2</c>.</param>
/// <param name="Bytes">The file.</param>
public sealed record NoireRemoteConsoleAsset(string ContentType, byte[] Bytes);

// A write the page never hears about would leave the page on the old style.
internal sealed class NotifyingDictionary<TValue> : IDictionary<string, TValue>
{
    private readonly Dictionary<string, TValue> inner = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action changed;

    public NotifyingDictionary(Action changed)
        => this.changed = changed;

    public TValue this[string key]
    {
        get => inner[key];
        set
        {
            if (inner.TryGetValue(key, out var existing) && EqualityComparer<TValue>.Default.Equals(existing, value))
                return;

            inner[key] = value;
            changed();
        }
    }

    public ICollection<string> Keys => inner.Keys;

    public ICollection<TValue> Values => inner.Values;

    public int Count => inner.Count;

    public bool IsReadOnly => false;

    public void Add(string key, TValue value)
    {
        inner.Add(key, value);
        changed();
    }

    public void Add(KeyValuePair<string, TValue> item)
        => Add(item.Key, item.Value);

    public void Clear()
    {
        if (inner.Count == 0)
            return;

        inner.Clear();
        changed();
    }

    public bool Contains(KeyValuePair<string, TValue> item)
        => ((ICollection<KeyValuePair<string, TValue>>)inner).Contains(item);

    public bool ContainsKey(string key)
        => inner.ContainsKey(key);

    public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
        => ((ICollection<KeyValuePair<string, TValue>>)inner).CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
        => inner.GetEnumerator();

    public bool Remove(string key)
    {
        if (!inner.Remove(key))
            return false;

        changed();
        return true;
    }

    public bool Remove(KeyValuePair<string, TValue> item)
    {
        if (!((ICollection<KeyValuePair<string, TValue>>)inner).Remove(item))
            return false;

        changed();
        return true;
    }

    public bool TryGetValue(string key, out TValue value)
        => inner.TryGetValue(key, out value!);

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        => inner.GetEnumerator();
}
