namespace NoireLib.Remote;

/// <summary>
/// How much the console asks a browser for before it hands out a session. The host, origin and fetch-site checks
/// hold at every value. No other website reaches the console whatever this is set to.
/// </summary>
public enum NoireRemoteConsoleAccess
{
    /// <summary>Every browser redeems a one-use grant, or presents <see cref="NoireRemoteOptions.ConsoleKey"/>.</summary>
    Locked,

    /// <summary>A browser on this machine is let in with nothing. One on the network still presents a grant or the key.</summary>
    LoopbackOpen,

    /// <summary>Nothing is asked. With <see cref="NoireRemoteOptions.EnableRemoteConsole"/> on, anyone reaching the port drives the plugin's local surface.</summary>
    Open,
}
