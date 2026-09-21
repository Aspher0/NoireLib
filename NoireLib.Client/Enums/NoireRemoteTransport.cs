using System;

namespace NoireLib.Remote;

/// <summary>
/// The wires a published surface is reachable on. A socket connection begins as an HTTP request on the listener that
/// is already bound. Serving both costs no second port and no second thread.
/// </summary>
[Flags]
public enum NoireRemoteTransport
{
    /// <summary>Take the setting from the class, then the default: <see cref="All"/>.</summary>
    Inherit = 0,

    /// <summary>Reachable at <c>/noire/v1/{api}/{member}</c>, one request and one answer.</summary>
    Http = 1,

    /// <summary>Reachable over the API socket. It also pushes events and job progress.</summary>
    Websocket = 2,

    /// <summary>Reachable both ways. A class serves both unless it says otherwise.</summary>
    All = Http | Websocket,
}
