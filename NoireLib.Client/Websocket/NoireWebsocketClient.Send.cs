using NoireLib.Remote;
using NoireLib.Websocket.Internal;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket;

public sealed partial class NoireWebsocketClient
{
    /// <summary>
    /// Queues a text message.
    /// </summary>
    /// <param name="text">The payload, sent as UTF-8.</param>
    /// <param name="cancellationToken">A token abandoning the wait for room under <see cref="NoireSocketSendOverflow.Block"/>.</param>
    /// <returns>A task completing when the message has been written to the socket, or when the overflow policy discarded it.</returns>
    /// <exception cref="NoireSocketClosedException">If the client is not connected and not attempting to be.</exception>
    /// <exception cref="NoireSocketQueueFullException">If the queue is full under <see cref="NoireSocketSendOverflow.Fail"/>.</exception>
    public Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SendAsync(Encoding.UTF8.GetBytes(text), NoireWebsocketMessageKind.Text, cancellationToken);
    }

    /// <summary>
    /// Queues a message. The payload is copied. The caller's buffer is free the moment this returns.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="kind">Whether the peer reads it as text or as bytes.</param>
    /// <param name="cancellationToken">A token abandoning the wait for room under <see cref="NoireSocketSendOverflow.Block"/>.</param>
    /// <returns>A task completing when the message has been written to the socket, or when the overflow policy discarded it.</returns>
    /// <exception cref="NoireSocketClosedException">If the client is not connected and not attempting to be.</exception>
    /// <exception cref="NoireSocketQueueFullException">If the queue is full under <see cref="NoireSocketSendOverflow.Fail"/>.</exception>
    public async Task SendAsync(ReadOnlyMemory<byte> payload,
        NoireWebsocketMessageKind kind = NoireWebsocketMessageKind.Binary,
        CancellationToken cancellationToken = default)
    {
        var pending = queue;

        if (pending == null || Volatile.Read(ref disposed) != 0 || !CanQueue(State))
            throw new NoireSocketClosedException("The connection is " + State + ". There is nothing to send on.");

        var item = new SendItem(payload.ToArray(), kind);
        await pending.EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
        await item.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Queues a value serialized with the library's JSON contract, as a text message.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="cancellationToken">A token abandoning the wait for room under <see cref="NoireSocketSendOverflow.Block"/>.</param>
    /// <returns>A task completing when the message has been written to the socket, or when the overflow policy discarded it.</returns>
    public Task SendJsonAsync(object? value, CancellationToken cancellationToken = default)
        => SendAsync(NoireRemoteJson.WriteBytes(value), NoireWebsocketMessageKind.Text, cancellationToken);

    // Queueing while down preserves order across a reconnect.
    private static bool CanQueue(NoireSocketState state)
        => state is NoireSocketState.Connecting or NoireSocketState.Connected or NoireSocketState.Reconnecting;

    // One writer for the client's lifetime. A message queued during an outage goes out after the next handshake.
    private async Task WriteLoopAsync(CancellationToken token)
    {
        var pending = queue;

        if (pending == null)
            return;

        while (!token.IsCancellationRequested)
        {
            SendItem? item = null;

            try
            {
                item = await pending.DequeueAsync(token).ConfigureAwait(false);

                if (item == null)
                    break;

                var message = item;

                while (!token.IsCancellationRequested)
                {
                    var live = await Volatile.Read(ref connectionSignal).Task.WaitAsync(token).ConfigureAwait(false);

                    try
                    {
                        await SendFrameAsync(live.Socket, message, token).ConfigureAwait(false);
                        pending.Settle(message, null);
                        item = null;
                        break;
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        // Waiting for the replacement socket avoids spinning on a dead connection.
                        await live.Ended.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref disposed) == 0)
                    Report(exception);
            }
            finally
            {
                if (item != null)
                    pending.Requeue(item);
            }
        }
    }

    private async Task SendFrameAsync(ClientWebSocket socket, SendItem item, CancellationToken token)
    {
        var type = item.Kind == NoireWebsocketMessageKind.Text
            ? WebSocketMessageType.Text
            : WebSocketMessageType.Binary;

        await sendGate.WaitAsync(token).ConfigureAwait(false);

        try
        {
            await socket.SendAsync(item.Payload.AsMemory(), type, true, token).ConfigureAwait(false);
        }
        finally
        {
            sendGate.Release();
        }
    }

    // A socket accepts one write at a time.
    private async Task SendCloseFrameAsync(ClientWebSocket socket, NoireWebsocketCloseCode code, string? reason,
        CancellationToken token)
    {
        await sendGate.WaitAsync(token).ConfigureAwait(false);

        try
        {
            await socket.CloseOutputAsync((WebSocketCloseStatus)(int)code, reason, token).ConfigureAwait(false);
        }
        finally
        {
            sendGate.Release();
        }
    }
}
