namespace NoireLib.Remote;

/// <summary>Which host log lines a listener publishes on its log channel, for the console's Dalamud Logs view.</summary>
public enum NoireRemoteLogScope
{
    /// <summary>No line. The console shows no log view.</summary>
    None,

    /// <summary>The lines this plugin writes, whichever logger wrote them. The default.</summary>
    Plugin,

    /// <summary>Every line the host logs, from every plugin and Dalamud. Anyone opening the console sees them, a phone on the network included.</summary>
    Everything,
}
