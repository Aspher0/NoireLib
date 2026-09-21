namespace NoireLib.Remote;

/// <summary>
/// The browser console this plugin's listener serves, on a socket of its own. It is bound only while
/// <see cref="NoireRemoteOptions.EnableConsole"/> is set.
/// </summary>
public static class NoireRemoteBrowserConsole
{
    /// <summary>Gets the console instance, the same object as <c>NoireRemote.Instance.Console</c>.</summary>
    public static NoireRemoteConsole Instance => NoireRemote.Instance.Console;

    /// <summary>
    /// Gets what a plugin sets to drive the page's appearance. The console ships no theme of its own.
    /// </summary>
    public static NoireRemoteConsoleStyle Style => Instance.Style;

    /// <summary>
    /// Gets whether the console socket is bound.
    /// </summary>
    public static bool IsListening => Instance.IsListening;

    /// <summary>
    /// Gets the port the console socket is bound to, or zero when it is not listening.
    /// </summary>
    public static int Port => Instance.Port;

    /// <summary>
    /// Gets how many console sessions are live.
    /// </summary>
    public static int SessionCount => Instance.SessionCount;

    /// <summary>
    /// Gets the password a networked browser types into the console's gate, or null when nothing asks for one.
    /// </summary>
    public static string? Key => Instance.Key;

    /// <summary>
    /// Builds the URL that opens the console, with a one-use grant in its fragment.
    /// </summary>
    /// <param name="forRemote">True for the address another device on the network reaches, false for loopback.</param>
    /// <returns>The URL, or null when the console is not listening.</returns>
    public static string? Open(bool forRemote = false)
        => Instance.Open(forRemote);

    /// <summary>
    /// Ends every console session. A page already open has to be reopened from a new URL.
    /// </summary>
    public static void RevokeSessions()
        => Instance.RevokeSessions();
}
