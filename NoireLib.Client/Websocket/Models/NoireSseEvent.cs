using Newtonsoft.Json;
using NoireLib.Remote;
using System.IO;

namespace NoireLib.Websocket;

/// <summary>
/// One dispatched server-sent event.
/// </summary>
public sealed class NoireSseEvent
{
    internal NoireSseEvent(string type, string data, string? id)
    {
        Type = type;
        Data = data;
        Id = id;
    }

    /// <summary>Gets the event type. <c>message</c> when the stream named none.</summary>
    public string Type { get; }

    /// <summary>
    /// Gets the data lines joined with newlines, with no trailing newline.
    /// </summary>
    public string Data { get; }

    /// <summary>
    /// Gets the last event id in force when this was dispatched, or null when the stream has sent no id yet.
    /// </summary>
    public string? Id { get; }

    /// <summary>
    /// Reads the data as JSON.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <returns>The deserialized value, or null when the data is an empty document.</returns>
    public T? Json<T>()
    {
        using var reader = new JsonTextReader(new StringReader(Data));
        return NoireRemoteJson.Serializer.Deserialize<T>(reader);
    }
}
