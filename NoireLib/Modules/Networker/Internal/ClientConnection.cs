using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Networker.Internal;

/// <summary>
/// The client role: a single loopback TCP connection to this machine's hub.<br/>
/// <see cref="Completion"/> completes when the connection ends for any reason; the supervision loop then re-elects.
/// </summary>
internal sealed class ClientConnection : IDisposable
{
    private readonly NoireNetworker owner;
    private readonly NetworkerOptions options;
    private readonly CancellationTokenSource lifetime;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private FramedConnection? connection;
    private bool hubUnresponsiveReported;
    private int disposed;

    public ClientConnection(NoireNetworker owner, CancellationToken parentToken)
    {
        this.owner = owner;
        options = owner.ActiveOptions;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
    }

    /// <summary>
    /// Completes when the connection to the hub is gone (goodbye, EOF, or transport failure).
    /// </summary>
    public Task Completion => completion.Task;

    /// <summary>
    /// Connects to the hub at the given loopback port and performs the handshake. Returns true on success.
    /// </summary>
    public async Task<bool> ConnectAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            var tcpClient = new TcpClient(AddressFamily.InterNetwork);
            await tcpClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), timeout.Token).ConfigureAwait(false);

            connection = new FramedConnection(tcpClient, options.MaxFrameBytes);

            var challenge = await connection.ReceiveAsync(timeout.Token).ConfigureAwait(false);

            if (challenge is not { Kind: EnvelopeKind.Challenge } || challenge.Network != owner.NetworkName)
            {
                connection.Dispose();
                return false;
            }

            await connection.SendAsync(new Envelope
            {
                Kind = EnvelopeKind.Hello,
                Network = owner.NetworkName,
                Origin = owner.SelfId,
                Payload = Wire.ToPayload(new HelloPayload { Self = owner.CaptureSelfState() }),
            }, timeout.Token).ConfigureAwait(false);

            var welcome = await connection.ReceiveAsync(timeout.Token).ConfigureAwait(false);

            if (welcome is { Kind: EnvelopeKind.Welcome })
            {
                var payload = Wire.FromPayload<WelcomePayload>(welcome.Payload);

                if (payload == null)
                {
                    connection.Dispose();
                    return false;
                }

                owner.ResyncPeersFromWelcome(payload.Peers);

                _ = ReadLoopAsync();
                _ = WatchdogLoopAsync();
                return true;
            }

            if (welcome is { Kind: EnvelopeKind.Reject })
                owner.InternalLogWarning($"Hub rejected the connection: {Wire.FromPayload<RejectPayload>(welcome.Payload)?.Reason}");

            connection.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested)
                owner.InternalLog($"Connection to hub failed: {ex.Message}");

            connection?.Dispose();
            return false;
        }
    }

    public void Post(Envelope envelope)
        => connection?.Post(envelope, ex => owner.InternalLog($"Send to hub failed: {ex.Message}"));

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var envelope = await connection!.ReceiveAsync(lifetime.Token).ConfigureAwait(false);

                if (envelope == null)
                    break;

                if (hubUnresponsiveReported)
                {
                    hubUnresponsiveReported = false;
                    owner.InternalLog("Hub is responsive again.");
                }

                switch (envelope.Kind)
                {
                    case EnvelopeKind.Message:
                    case EnvelopeKind.Request:
                    case EnvelopeKind.Response:
                    case EnvelopeKind.Event:
                        owner.HandleInboundEnvelope(envelope);
                        break;

                    case EnvelopeKind.PeerState:
                        {
                            var state = Wire.FromPayload<PeerStateModel>(envelope.Payload);

                            if (state != null)
                                owner.ApplyPeerStateModel(state, envelope.TypeName);

                            break;
                        }

                    case EnvelopeKind.PeerLeft:
                        if (envelope.Origin != null)
                            owner.RemovePeerById(envelope.Origin.Value);

                        break;

                    case EnvelopeKind.HubGoodbye:
                        owner.InternalLog("Hub announced shutdown; re-electing immediately.");
                        connection.Dispose();
                        break;

                    case EnvelopeKind.Ping:
                        Post(new Envelope { Kind = EnvelopeKind.Pong });
                        break;

                    case EnvelopeKind.Pong:
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested)
                owner.InternalLog($"Connection to hub lost: {ex.Message}");
        }

        completion.TrySetResult();
    }

    private async Task WatchdogLoopAsync()
    {
        try
        {
            var timeoutMs = (long)options.LanLinkTimeout.TotalMilliseconds;

            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(options.PingInterval, lifetime.Token).ConfigureAwait(false);

                Post(new Envelope { Kind = EnvelopeKind.Ping });

                if (connection != null && Environment.TickCount64 - connection.LastReceivedTick > timeoutMs && !hubUnresponsiveReported)
                {
                    hubUnresponsiveReported = true;

                    // The hub process is alive (its socket is open) but not pumping - most likely paused by a debugger.
                    // Its election mutex cannot be seized from a live process, so the group stalls until it resumes.
                    owner.InternalLogWarning("Hub is unresponsive (paused process?). The network will stall until it resumes.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// Sends a goodbye and closes the connection, without blocking the caller.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        // The goodbye is what lets the hub drop this peer at once. Without one it only notices at the next ping
        // timeout, and every instance on the network keeps a departed peer in its list until then, so the frame is
        // worth getting out. It is written in the background and takes the socket down with it once it is out: this
        // runs during a plugin unload or a SetActive(false), both of which reach it from the framework thread, where
        // waiting for a socket write would stall the game's frame.
        connection?.CloseAfterSending(
            new Envelope { Kind = EnvelopeKind.Goodbye, Origin = owner.SelfId },
            ex => owner.InternalLog($"Goodbye to hub failed: {ex.Message}"));

        // Cancelling is what ends the read and watchdog loops: both wait on the token, so neither depends on the
        // socket having closed first, which is what allows the close to trail the goodbye.
        lifetime.Cancel();
        lifetime.Dispose();
        completion.TrySetResult();
    }
}
