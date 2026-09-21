using NoireLib.Remote;
using System;
using System.Net.Http;

namespace NoireLib.Websocket;

/// <summary>
/// What one long-polling run issues its requests with. Usable as-is. Every property carries a working default.
/// </summary>
public sealed class NoireLongPollOptions
{
    /// <summary>
    /// Gets or sets the HTTP settings every poll is issued with.
    /// </summary>
    public NoireSocketHttpOptions Http { get; set; } = new();

    /// <summary>
    /// Gets or sets the verb each poll is issued with.
    /// </summary>
    public NoireLongPollMethod Method { get; set; } = NoireLongPollMethod.Get;

    /// <summary>
    /// Gets or sets the value sent as the request body, serialized as JSON. It needs
    /// <see cref="NoireLongPollMethod.Post"/>. Leaving it set with a GET throws.
    /// </summary>
    public object? Body { get; set; } = null;

    /// <summary>
    /// Gets or sets the schedule a failed poll backs off through. An empty answer is not a failure and uses
    /// <see cref="EmptyPollDelay"/> instead.
    /// </summary>
    public NoireRetryPolicy Retry { get; set; } = NoireRetryPolicy.Default;

    /// <summary>
    /// Gets or sets the thread callbacks run on. <see cref="NoireRemoteThread.Inherit"/> takes the host's own default.
    /// </summary>
    public NoireRemoteThread Thread { get; set; } = NoireRemoteThread.Inherit;

    /// <summary>
    /// Gets or sets the host callbacks are handed to. Null takes the host installed when the run starts.
    /// </summary>
    public INoireRemoteHost? Host { get; set; } = null;

    /// <summary>
    /// Gets or sets how long one poll may be held before it is abandoned and reissued. Keep it longer than the
    /// server's own hold time.
    /// </summary>
    public TimeSpan PollTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets how long the loop waits after an answer that reported nothing. Zero polls again immediately.
    /// </summary>
    public TimeSpan EmptyPollDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the query parameter the cursor is sent as. Null sends it in no query parameter.
    /// </summary>
    public string? CursorParameter { get; set; } = "since";

    /// <summary>
    /// Gets or sets the header the cursor is sent as, and the response header the next cursor is read from. Null
    /// sends and reads no header.
    /// </summary>
    public string? CursorHeader { get; set; } = null;

    /// <summary>
    /// Gets or sets the path the next cursor is read from in a JSON answer, as understood by
    /// <see cref="Newtonsoft.Json.Linq.JToken.SelectToken(string)"/>. Null reads none from the body.
    /// </summary>
    public string? CursorField { get; set; } = "cursor";

    /// <summary>
    /// Gets or sets the cursor the first poll of a run is issued with. Null starts with no cursor.
    /// </summary>
    public string? InitialCursor { get; set; } = null;

    /// <summary>
    /// Gets or sets a reader that replaces <see cref="CursorHeader"/> and <see cref="CursorField"/> entirely, for a
    /// server that derives the next cursor from the answer some other way. Returning null keeps the current cursor.
    /// </summary>
    public Func<NoireLongPollResponse, string?>? ReadCursor { get; set; } = null;

    /// <summary>
    /// Gets or sets the last-resort hook on the handler the polls are issued through, called once after every other
    /// setting has been applied.
    /// </summary>
    public Action<SocketsHttpHandler>? ConfigureHandler { get; set; } = null;

    /// <summary>
    /// Copies these options.
    /// </summary>
    /// <returns>A copy whose HTTP settings are its own.</returns>
    public NoireLongPollOptions Clone()
        => new()
        {
            Http = Http.Clone(),
            Method = Method,
            Body = Body,
            Retry = Retry,
            Thread = Thread,
            Host = Host,
            PollTimeout = PollTimeout,
            EmptyPollDelay = EmptyPollDelay,
            CursorParameter = CursorParameter,
            CursorHeader = CursorHeader,
            CursorField = CursorField,
            InitialCursor = InitialCursor,
            ReadCursor = ReadCursor,
            ConfigureHandler = ConfigureHandler,
        };
}
