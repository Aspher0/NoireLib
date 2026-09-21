namespace NoireLib.Remote;

/// <summary>
/// The routes the console socket serves. They sit apart from the API routes because the socket does, and because a
/// browser never reaches the API one.
/// </summary>
public static class NoireRemoteConsolePaths
{
    /// <summary>The prefix every console route sits under, with both slashes.</summary>
    public const string Prefix = "/noire/console/";

    /// <summary>The page itself.</summary>
    public const string Page = "/noire/console/";

    /// <summary>The route the page exchanges its one-use grant for a session token on.</summary>
    public const string Session = "/noire/console/session";

    /// <summary>The route the page reads its bootstrap from, carrying the manifest, the styling and what the listener serves.</summary>
    public const string Bootstrap = "/noire/console/bootstrap";

    /// <summary>The route the page upgrades its live channel on, carrying events out and subscriptions back.</summary>
    public const string Live = "/noire/console/live";

    /// <summary>The prefix of the route a plugin's own asset is served under, followed by its name.</summary>
    public const string Asset = "/noire/console/asset/";

    /// <summary>The route listing the other listeners on the network, and the one a call is proxied through.</summary>
    public const string Fleet = "/noire/console/fleet";

    /// <summary>The route the page reads another listener's manifest from. Aiming at one redraws the page as it.</summary>
    public const string FleetManifest = "/noire/console/fleet/_manifest";

    /// <summary>The route the page reads and acts on another listener's sockets through.</summary>
    public const string FleetSockets = "/noire/console/fleet/_sockets";
}
