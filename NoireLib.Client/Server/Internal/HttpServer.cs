using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// Reading, parsing and the gate run on pool threads. Only the member hops to the framework thread. A frozen frame never stalls an error answer.
internal sealed class HttpServer : IDisposable
{
    private readonly NoireRemoteOptions options;
    private readonly HttpRouterContext context;
    private readonly IHttpGate gate;
    private readonly INoireRemoteHost host;
    private readonly Func<HttpRequestData, bool, CancellationToken, Task<HttpRouteOutcome>> route;
    private readonly bool bindEveryAddress;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim connections;
    private readonly string token;

    private TcpListener? listener;
    private int disposed;

    public HttpServer(NoireRemoteOptions options, HttpRouterContext context, string token, INoireRemoteHost host)
        : this(options, context, token, host, new HttpSecurityGate(host), null, options.EnableRemote, Math.Max(0, options.Port))
    {
    }

    public HttpServer(
        NoireRemoteOptions options,
        HttpRouterContext context,
        string token,
        INoireRemoteHost host,
        IHttpGate gate,
        Func<HttpRequestData, bool, CancellationToken, Task<HttpRouteOutcome>>? route,
        bool bindEveryAddress,
        int port)
    {
        this.options = options;
        this.context = context;
        this.token = token;
        this.host = host;
        this.gate = gate;
        this.bindEveryAddress = bindEveryAddress;
        this.route = route ?? ((request, isLoopback, token) => HttpRouter.RouteAsync(request, isLoopback, context, token));
        RequestedPort = port;
        connections = new SemaphoreSlim(Math.Max(1, options.MaxConnections));
    }

    public int RequestedPort { get; }

    public int Port { get; private set; }

    public IPAddress BoundAddress { get; private set; } = IPAddress.Loopback;

    public void Start()
    {
        var address = bindEveryAddress ? (options.BindAddress.Equals(IPAddress.Loopback) ? IPAddress.Any : options.BindAddress) : IPAddress.Loopback;

        listener = new TcpListener(address, RequestedPort);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);

        if (options.EnableDualStack && address.AddressFamily == AddressFamily.InterNetworkV6)
            listener.Server.DualMode = true;

        listener.Start();

        BoundAddress = address;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener!.AcceptTcpClientAsync(lifetime.Token).ConfigureAwait(false);

                if (!await connections.WaitAsync(0, lifetime.Token).ConfigureAwait(false))
                {
                    _ = RefuseAsync(client);
                    continue;
                }

                _ = HandleAsync(client);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            if (!lifetime.IsCancellationRequested)
                host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] the accept loop stopped", exception);
        }
    }

    private async Task RefuseAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var failure = new HttpFailure(503, NoireRemoteErrorCodes.Busy, "This listener holds as many connections as it accepts.") { RetryAfterSeconds = 1 };
                await HttpResponseWriter.WriteFailureAsync(stream, failure, context.Instance, null, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        var isLoopback = false;
        var remoteAddress = "unknown";
        var adopted = false;
        System.IO.Stream? open = null;

        try
        {
            try
            {
                client.NoDelay = true;

                if (client.Client.RemoteEndPoint is IPEndPoint remote)
                {
                    isLoopback = IPAddress.IsLoopback(remote.Address);
                    remoteAddress = remote.Address.ToString();
                }

                var stream = client.GetStream();
                open = stream;

                using var headerDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                headerDeadline.CancelAfter(options.HeaderReadTimeout);

                var headers = await HttpRequestParser.ReadHeadersAsync(stream, options, headerDeadline.Token).ConfigureAwait(false);

                if (headers.Failure != null)
                {
                    await HttpResponseWriter.WriteFailureAsync(stream, headers.Failure, context.Instance, null, lifetime.Token).ConfigureAwait(false);
                    return;
                }

                var request = headers.Request!;
                var refusal = gate.CheckHeaders(request, options, token, Port, isLoopback, remoteAddress, out var signed);

                if (refusal != null)
                {
                    await HttpResponseWriter.WriteFailureAsync(stream, refusal, context.Instance, null, lifetime.Token).ConfigureAwait(false);
                    return;
                }

                var bodyFailure = await HttpRequestParser.ReadBodyAsync(stream, headers, headerDeadline.Token).ConfigureAwait(false);

                if (bodyFailure != null)
                {
                    await HttpResponseWriter.WriteFailureAsync(stream, bodyFailure, context.Instance, null, lifetime.Token).ConfigureAwait(false);
                    return;
                }

                refusal = gate.CheckBody(request, options, remoteAddress, signed);

                if (refusal != null)
                {
                    await HttpResponseWriter.WriteFailureAsync(stream, refusal, context.Instance, null, lifetime.Token).ConfigureAwait(false);
                    return;
                }

                var outcome = await route(request, isLoopback, lifetime.Token).ConfigureAwait(false);

                if (outcome.Adopt != null)
                {
                    // The callee owns the client from here.
                    adopted = true;
                    await AdoptAsync(outcome.Adopt, client, stream, headers.Carried, isLoopback, remoteAddress).ConfigureAwait(false);
                    return;
                }

                if (outcome.Write != null)
                {
                    // The connection closes when it returns.
                    await outcome.Write(stream, lifetime.Token).ConfigureAwait(false);
                    return;
                }

                await HttpResponseWriter.WriteAsync(
                    stream, outcome.Status, outcome.Body, context.Instance, outcome.RetryAfterSeconds, outcome.IsIdempotentReplay,
                    outcome.ExtraHeaders, lifetime.Token).ConfigureAwait(false);
            }
            finally
            {
                if (!adopted)
                    client.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (System.IO.IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (Exception exception)
        {
            host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] a connection from " + remoteAddress + " failed", exception);

            // The failure is written back before the connection goes. An adopted socket may already carry a 101.
            if (open != null && !adopted)
                await TryWriteFaultAsync(open, exception).ConfigureAwait(false);
        }
        finally
        {
            // A connection in flight at dispose releases a semaphore that is gone. An escaping exception on a pool thread kills the process.
            try
            {
                connections.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    // Slot, registration, then the 101. Any failure ends as a closed socket.
    private async Task AdoptAsync(
        Func<HttpConnectionAdoption, Task> adopt, TcpClient client, System.IO.Stream stream, ReadOnlyMemory<byte> carried, bool isLoopback, string remoteAddress)
    {
        try
        {
            await adopt(new HttpConnectionAdoption(client, stream, carried, isLoopback, remoteAddress)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] an upgrade from " + remoteAddress + " failed", exception);

            try
            {
                client.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task TryWriteFaultAsync(System.IO.Stream stream, Exception exception)
    {
        try
        {
            var failure = new HttpFailure(500, NoireRemoteErrorCodes.HandlerFault,
                "The listener failed while answering this request.",
                options.SendExceptionType ? exception.GetType().Name : null);

            await HttpResponseWriter.WriteFailureAsync(stream, failure, context.Instance, null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        try
        {
            lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            listener?.Stop();
        }
        catch (Exception)
        {
        }

        lifetime.Dispose();
        connections.Dispose();
    }
}
