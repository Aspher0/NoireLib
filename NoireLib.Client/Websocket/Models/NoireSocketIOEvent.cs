using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PackageResponse = SocketIOClient.SocketIOResponse;

namespace NoireLib.Websocket;

/// <summary>
/// One Socket.IO event as it arrived: the name it was emitted under, its arguments and any binary attachments.<br/>
/// There is no argument count, because the protocol package exposes none. <see cref="Get{T}(int)"/> answering with
/// the default is how a caller finds the end.
/// </summary>
public sealed class NoireSocketIOEvent
{
    private static readonly byte[][] NoAttachments = [];

    internal NoireSocketIOEvent(string name, PackageResponse underlying)
    {
        Name = name;
        Underlying = underlying;
    }

    /// <summary>
    /// Gets the name the event was emitted under.
    /// </summary>
    public string Name { get; }

    /// <summary>Gets the protocol package's own response.</summary>
    public PackageResponse Underlying { get; }

    /// <summary>
    /// Gets the binary attachments that arrived with the event, empty when it carried none.
    /// </summary>
    public IReadOnlyList<byte[]> Attachments => Underlying.InComingBytes ?? (IReadOnlyList<byte[]>)NoAttachments;

    /// <summary>
    /// Reads one of the event's arguments.
    /// </summary>
    /// <typeparam name="T">What the argument is deserialized into.</typeparam>
    /// <param name="index">Which argument, counted from zero.</param>
    /// <returns>The argument, or the default for <typeparamref name="T"/> once the arguments run out.</returns>
    public T? Get<T>(int index = 0)
    {
        try
        {
            return Underlying.GetValue<T>(index);
        }
        catch (ArgumentOutOfRangeException)
        {
            return default;
        }
        catch (IndexOutOfRangeException)
        {
            return default;
        }
    }

    /// <summary>
    /// Answers the event with the acknowledgement its emitter is waiting for.
    /// </summary>
    /// <param name="data">The values sent back.</param>
    /// <returns>A task completing once the acknowledgement has been written.</returns>
    public Task ReplyAsync(params object?[] data)
        => Underlying.CallbackAsync(data);

    /// <summary>
    /// Answers the event with the acknowledgement its emitter is waiting for, stopping if the token is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <param name="data">The values sent back.</param>
    /// <returns>A task completing once the acknowledgement has been written.</returns>
    public Task ReplyAsync(CancellationToken cancellationToken, params object?[] data)
        => Underlying.CallbackAsync(cancellationToken, data);
}
