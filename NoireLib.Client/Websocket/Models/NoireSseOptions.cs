using NoireLib.Remote;
using System;
using System.Net.Http;

namespace NoireLib.Websocket;

/// <summary>
/// What one server-sent-events connection is opened and reconnected with. Usable as-is. Every property carries a
/// working default.
/// </summary>
public sealed class NoireSseOptions
{
    /// <summary>
    /// Gets or sets the HTTP settings the stream is requested with.
    /// </summary>
    public NoireSocketHttpOptions Http { get; set; } = new();

    /// <summary>
    /// Gets or sets the reconnection schedule. A <c>retry:</c> line from the server replaces its initial delay for
    /// as long as the connection lives.
    /// </summary>
    public NoireRetryPolicy Retry { get; set; } = NoireRetryPolicy.Default;

    /// <summary>
    /// Gets or sets the thread callbacks run on. <see cref="NoireRemoteThread.Inherit"/> takes the host's own default.
    /// </summary>
    public NoireRemoteThread Thread { get; set; } = NoireRemoteThread.Inherit;

    /// <summary>
    /// Gets or sets the host callbacks are handed to. Null takes the host installed when the connection opens.
    /// </summary>
    public INoireRemoteHost? Host { get; set; } = null;

    /// <summary>
    /// Gets or sets the most characters one event's data may hold before the stream is dropped with a
    /// <see cref="NoireSocketMessageTooLargeException"/>. Zero or less removes the cap.
    /// </summary>
    public int MaxEventSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets the last-resort hook on the handler the stream is requested through, called once after every
    /// other setting has been applied.
    /// </summary>
    public Action<SocketsHttpHandler>? ConfigureHandler { get; set; } = null;

    /// <summary>
    /// Copies these options.
    /// </summary>
    /// <returns>A copy whose HTTP settings are its own.</returns>
    public NoireSseOptions Clone()
        => new()
        {
            Http = Http.Clone(),
            Retry = Retry,
            Thread = Thread,
            Host = Host,
            MaxEventSize = MaxEventSize,
            ConfigureHandler = ConfigureHandler,
        };
}
