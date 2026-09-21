using Newtonsoft.Json;
using NoireLib.Remote;
using System;
using System.IO;
using System.Text;

namespace NoireLib.Websocket;

/// <summary>
/// One reassembled WebSocket message, on either side of a connection.<br/>
/// <b><see cref="Bytes"/> is valid only for the duration of the callback it was handed to.</b> It points into a
/// pooled buffer that is recycled as soon as the callback returns. Call <see cref="ToArray"/> to keep the payload.
/// </summary>
public sealed class NoireWebsocketMessage
{
    private string? text;

    internal NoireWebsocketMessage(NoireWebsocketMessageKind kind, ReadOnlyMemory<byte> bytes)
    {
        Kind = kind;
        Bytes = bytes;
    }

    /// <summary>
    /// Gets whether the message arrived as text or as bytes.
    /// </summary>
    public NoireWebsocketMessageKind Kind { get; }

    /// <summary>
    /// Gets the payload. Only valid until the callback returns. See the note on the type.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>
    /// Gets the payload decoded as UTF-8. A <see cref="NoireWebsocketMessageKind.Binary"/> message that is not UTF-8
    /// decodes to replacement characters. Check <see cref="Kind"/> first.
    /// </summary>
    public string Text => text ??= Encoding.UTF8.GetString(Bytes.Span);

    /// <summary>
    /// Copies the payload out. The copy survives the callback.
    /// </summary>
    /// <returns>A new array holding the payload.</returns>
    public byte[] ToArray()
        => Bytes.ToArray();

    /// <summary>
    /// Reads the payload as JSON.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <returns>The deserialized value, or null when the payload is an empty document.</returns>
    public T? Json<T>()
    {
        using var reader = new JsonTextReader(new StringReader(Text));
        return NoireRemoteJson.Serializer.Deserialize<T>(reader);
    }
}
