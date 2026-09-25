using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// Answers a signed probe by unicast and never speaks first. A scanner cannot tell this port from a closed one.
internal sealed class HttpDiscoveryResponder : IDisposable
{
    private const int MaxNonces = 1024;

    private readonly NoireRemoteOptions options;
    private readonly INoireRemoteHost host;
    private readonly Guid instance;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private readonly Dictionary<string, DateTime> nonces = new(StringComparer.Ordinal);
    private readonly Queue<string> nonceOrder = new();
    private readonly Dictionary<string, DateTime> cooldown = new(StringComparer.Ordinal);

    private UdpClient? socket;
    private int disposed;

    public HttpDiscoveryResponder(NoireRemoteOptions options, INoireRemoteHost host, Guid instance)
    {
        this.options = options;
        this.host = host;
        this.instance = instance;
    }

    public int Port { get; private set; }

    // False when another process on this machine already answers probes.
    public bool IsResponding { get; private set; }

    public void Start()
    {
        if (string.IsNullOrWhiteSpace(options.RemoteSecret))
        {
            host.Log(NoireRemoteLogLevel.Warning, "[NoireRemote] network discovery needs a remote secret. Nothing is announced.", null);
            return;
        }

        Port = options.DiscoveryPort > 0 ? options.DiscoveryPort : NoireRemoteDiscovery.DerivePort(options.RemoteSecret!);
        TryBind();
    }

    // The socket bind is the election. The holder answers for every listener out of the record folder.
    private void TryBind()
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        try
        {
            var bound = new UdpClient(AddressFamily.InterNetwork);
            bound.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            bound.Client.Bind(new IPEndPoint(IPAddress.Any, Port));

            socket = bound;
            IsResponding = true;

            _ = ReceiveLoopAsync();

            host.Log(NoireRemoteLogLevel.Info, "[NoireRemote] answering discovery probes on " + Port, null);
        }
        catch (SocketException)
        {
            // Another process holds the port and answers for this listener too.
            IsResponding = false;
            _ = RetryAsync();
        }
    }

    private async Task RetryAsync()
    {
        var interval = options.HeartbeatInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : options.HeartbeatInterval;

        try
        {
            await Task.Delay(interval, lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        TryBind();
    }

    private async Task ReceiveLoopAsync()
    {
        while (!lifetime.IsCancellationRequested && socket != null)
        {
            UdpReceiveResult received;

            try
            {
                received = await socket.ReceiveAsync(lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            try
            {
                Answer(received);
            }
            catch (Exception)
            {
                // A local network carries other people's traffic.
            }
        }
    }

    private void Answer(UdpReceiveResult received)
    {
        // A source port equal to the discovery port would let two responders answer each other forever.
        if (received.RemoteEndPoint.Port == Port)
            return;

        if (received.Buffer.Length > 4096)
            return;

        var probe = NoireRemoteJson.TryRead<NoireRemoteDiscoveryMessage>(Encoding.UTF8.GetString(received.Buffer));

        if (probe == null || !string.Equals(probe.Kind, "probe", StringComparison.Ordinal))
            return;

        if (!NoireRemoteDiscovery.Verify(options.RemoteSecret, NoireRemoteDiscovery.ProbeMethod, probe, options.RemoteClockSkew))
            return;

        if (!AcceptNonce(probe.Nonce) || !AcceptSource(received.RemoteEndPoint.Address.ToString()))
            return;

        var ports = LivePorts(probe.Endpoint);

        if (ports.Count == 0)
            return;

        var address = NoireRemoteAddress.LocalAddressFor(received.RemoteEndPoint).ToString();
        var answer = NoireRemoteDiscovery.Answer(options.RemoteSecret!, Environment.MachineName, address, ports);
        var bytes = NoireRemoteJson.WriteBytes(answer);

        socket?.Send(bytes, bytes.Length, received.RemoteEndPoint);
    }

    // Announces every live listener sharing its secret.
    private List<int> LivePorts(string? endpoint)
    {
        var ports = new List<int>();
        var wanted = NoireRemoteDiscovery.SecretId(options.RemoteSecret);
        var now = DateTime.UtcNow;

        foreach (var record in NoireRemoteDirectory.ReadAll(options.RegistryDirectory))
        {
            if (!string.Equals(record.SecretId, wanted, StringComparison.Ordinal))
                continue;

            if (!NoireRemoteDirectory.HeartbeatIsFresh(record, options.StaleAfter, now))
                continue;

            if (endpoint != null && !record.Publishes(endpoint))
                continue;

            if (!ports.Contains(record.Port))
                ports.Add(record.Port);
        }

        // A listener that has not written its record yet is still announced.
        if (ports.Count == 0 && instance != Guid.Empty)
            ports.Clear();

        return ports;
    }

    // A ring of its own. Filling it cannot exhaust the call path's nonce set.
    private bool AcceptNonce(string nonce)
    {
        lock (gate)
        {
            if (nonces.ContainsKey(nonce))
                return false;

            nonces[nonce] = DateTime.UtcNow;
            nonceOrder.Enqueue(nonce);

            while (nonceOrder.Count > MaxNonces)
                nonces.Remove(nonceOrder.Dequeue());

            return true;
        }
    }

    // One answer per address per cooldown. Useless as a reflector.
    private bool AcceptSource(string address)
    {
        var interval = options.DiscoveryAnswerCooldown <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : options.DiscoveryAnswerCooldown;

        lock (gate)
        {
            var now = DateTime.UtcNow;

            if (cooldown.TryGetValue(address, out var last) && now - last < interval)
                return false;

            cooldown[address] = now;

            if (cooldown.Count > 512)
                cooldown.Clear();

            return true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        IsResponding = false;

        try
        {
            lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        lifetime.Dispose();
        socket?.Dispose();
        socket = null;
    }
}
