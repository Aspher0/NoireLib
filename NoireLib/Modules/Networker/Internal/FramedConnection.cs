using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Networker.Internal;

/// <summary>
/// A length-prefixed frame connection over TCP. Writes are serialized by a semaphore;
/// a bounded pending-send counter keeps a slow consumer from growing memory unboundedly.
/// </summary>
internal sealed class FramedConnection : IDisposable
{
    private const int MaxPendingSends = 4096;

    private readonly TcpClient tcpClient;
    private readonly NetworkStream stream;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly int maxFrameBytes;
    private int pendingSends;
    private int disposed;

    public FramedConnection(TcpClient tcpClient, int maxFrameBytes)
    {
        this.tcpClient = tcpClient;
        this.maxFrameBytes = maxFrameBytes;
        tcpClient.NoDelay = true;
        stream = tcpClient.GetStream();

        var remote = tcpClient.Client.RemoteEndPoint as IPEndPoint;
        RemoteEndPoint = remote;
        IsLoopback = remote != null && IPAddress.IsLoopback(remote.Address);
    }

    public IPEndPoint? RemoteEndPoint { get; }

    public bool IsLoopback { get; }

    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    /// <summary>
    /// The <see cref="Environment.TickCount64"/> of the last successfully received frame.
    /// </summary>
    public long LastReceivedTick { get; private set; } = Environment.TickCount64;

    public async Task SendAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        var body = Wire.Encode(envelope);

        if (body.Length > maxFrameBytes)
            throw new InvalidOperationException($"Frame of {body.Length} bytes exceeds the maximum of {maxFrameBytes} bytes.");

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// Sends without awaiting. Failures dispose the connection (the read loop notices and tears down);
    /// overflow of the pending counter drops the frame and reports it.
    /// </summary>
    public void Post(Envelope envelope, Action<Exception>? onError = null)
    {
        if (IsDisposed)
            return;

        if (Interlocked.Increment(ref pendingSends) > MaxPendingSends)
        {
            Interlocked.Decrement(ref pendingSends);
            onError?.Invoke(new InvalidOperationException("Outbound queue overflow; frame dropped."));
            return;
        }

        _ = PostCoreAsync(envelope, onError);
    }

    private async Task PostCoreAsync(Envelope envelope, Action<Exception>? onError)
    {
        try
        {
            await SendAsync(envelope, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                onError?.Invoke(ex);
                Dispose();
            }
        }
        finally
        {
            Interlocked.Decrement(ref pendingSends);
        }
    }

    /// <summary>
    /// Receives the next frame, or null on clean end-of-stream. Throws on transport failure or protocol violation.
    /// </summary>
    public async Task<Envelope?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var header = new byte[4];
        var read = await stream.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

        if (read == 0)
            return null;

        if (read < 4)
            throw new EndOfStreamException("Connection closed mid-frame.");

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);

        if (length <= 0 || length > maxFrameBytes)
            throw new InvalidDataException($"Invalid frame length {length}.");

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);

        LastReceivedTick = Environment.TickCount64;

        return Wire.Decode(body) ?? throw new InvalidDataException("Received an undecodable frame.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        try
        {
            tcpClient.Close();
        }
        catch
        {
            // Best effort.
        }
    }
}
