using System;
using System.Net.WebSockets;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket;

public sealed partial class NoireWebsocketClient
{
    // Decoding invalid UTF-8 would hand the application replacement characters.
    internal static bool IsDeliverable(NoireWebsocketMessageKind kind, ReadOnlySpan<byte> payload)
        => kind != NoireWebsocketMessageKind.Text || Utf8.IsValid(payload);

    private async Task<NoireWebsocketClose> ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        var size = Math.Max(NoireWebsocketClientOptions.MinimumReceiveBufferSize, active.ReceiveBufferSize);
        var buffer = new byte[size];
        var assembler = new MessageAssembler(active.MaxMessageSize, size);
        var idleTimeout = active.IdleTimeout;

        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);

        NoireSocketMessageTooLargeException? oversized = null;

        while (true)
        {
            if (idleTimeout > TimeSpan.Zero)
                idle.CancelAfter(idleTimeout);

            ValueWebSocketReceiveResult result;

            try
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(), idle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                socket.Abort();
                Report(new NoireSocketTimeoutException("Nothing arrived on the connection inside the idle timeout.", idleTimeout));

                return new NoireWebsocketClose((int)NoireWebsocketCloseCode.AbnormalClosure, "idle",
                    NoireWebsocketCloseInitiator.Transport);
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                var code = socket.CloseStatus == null
                    ? (int)NoireWebsocketCloseCode.NoStatusReceived
                    : (int)socket.CloseStatus.Value;

                await CompleteCloseAsync(socket, token).ConfigureAwait(false);

                return new NoireWebsocketClose(code, socket.CloseStatusDescription,
                    NoireWebsocketCloseInitiator.Remote);
            }

            if (oversized == null)
            {
                try
                {
                    assembler.Append(buffer.AsSpan(0, result.Count));
                }
                catch (NoireSocketMessageTooLargeException exception)
                {
                    // The rest of the message is still read. Skipping it would never resynchronize.
                    oversized = exception;
                }
            }

            if (!result.EndOfMessage)
                continue;

            if (oversized != null)
            {
                assembler.Reset();
                Report(oversized);
                oversized = null;
                continue;
            }

            var kind = result.MessageType == WebSocketMessageType.Text
                ? NoireWebsocketMessageKind.Text
                : NoireWebsocketMessageKind.Binary;

            var payload = assembler.Payload;

            if (!IsDeliverable(kind, payload.Span))
            {
                Report(new NoireSocketProtocolException("A text message carried bytes that are not valid UTF-8.",
                    NoireWebsocketCloseCode.InvalidPayload));

                await SendCloseFrameAsync(socket, NoireWebsocketCloseCode.InvalidPayload, "invalid utf-8", token)
                    .ConfigureAwait(false);

                return new NoireWebsocketClose((int)NoireWebsocketCloseCode.InvalidPayload, "invalid utf-8",
                    NoireWebsocketCloseInitiator.Local);
            }

            await DispatchMessageAsync(new NoireWebsocketMessage(kind, payload), token).ConfigureAwait(false);
            assembler.Reset();
        }
    }

    // The payload points into the read loop's buffer. The next frame waits for every handler.
    private async Task DispatchMessageAsync(NoireWebsocketMessage message, CancellationToken token)
    {
        if (!messageHandlers.HasHandlers)
            return;

        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        pump.Enqueue(async () =>
        {
            try
            {
                await messageHandlers.DispatchAsync(message).ConfigureAwait(false);
            }
            finally
            {
                delivered.TrySetResult();
            }
        });

        await delivered.Task.WaitAsync(token).ConfigureAwait(false);
    }

    private async Task CompleteCloseAsync(ClientWebSocket socket, CancellationToken token)
    {
        try
        {
            if (socket.State == WebSocketState.CloseReceived)
                await SendCloseFrameAsync(socket, NoireWebsocketCloseCode.Normal, null, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            socket.Abort();
        }
    }

    // The buffer is reused. The payload is valid only while the callback runs.
    internal sealed class MessageAssembler
    {
        private readonly int maxSize;

        private byte[] buffer;
        private int length;

        public MessageAssembler(int maxSize, int initialCapacity)
        {
            this.maxSize = maxSize < 1 ? 1 : maxSize;
            buffer = new byte[Math.Clamp(initialCapacity, 1, this.maxSize)];
        }

        public int Length => length;

        public ReadOnlyMemory<byte> Payload => buffer.AsMemory(0, length);

        public void Append(ReadOnlySpan<byte> fragment)
        {
            var total = (long)length + fragment.Length;

            if (total > maxSize)
                throw new NoireSocketMessageTooLargeException(total, maxSize);

            if (total > buffer.Length)
            {
                var capacity = (long)buffer.Length;

                while (capacity < total)
                    capacity *= 2;

                Array.Resize(ref buffer, (int)Math.Min(capacity, maxSize));
            }

            fragment.CopyTo(buffer.AsSpan(length));
            length += fragment.Length;
        }

        public void Reset()
            => length = 0;
    }
}
