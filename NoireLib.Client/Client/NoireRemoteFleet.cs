using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

// A signed probe is broadcast, answers are collected for a window, then each address is confirmed with a signed liveness call.
internal static class NoireRemoteFleet
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan SecondProbe = TimeSpan.FromMilliseconds(120);

    public static async Task<IReadOnlyList<NoireRemoteInstance>> DiscoverAsync(
        NoireRemoteClientOptions options,
        string? endpointName,
        CancellationToken cancellationToken)
    {
        var local = NoireRemoteClient.Resolver.Discover(options, endpointName);

        if (string.IsNullOrWhiteSpace(options.NetworkSecret))
            return local;

        var secret = options.NetworkSecret!;
        var port = options.DiscoveryPort > 0 ? options.DiscoveryPort : NoireRemoteDiscovery.DerivePort(secret);
        var answers = await ProbeAsync(secret, port, endpointName, options, cancellationToken).ConfigureAwait(false);

        return await MergeAsync(local, answers, secret, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<NoireRemoteDiscoveryMessage>> ProbeAsync(
        string secret,
        int port,
        string? endpointName,
        NoireRemoteClientOptions options,
        CancellationToken cancellationToken)
    {
        var answers = new List<NoireRemoteDiscoveryMessage>();

        using var socket = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        socket.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var targets = new List<IPAddress> { IPAddress.Broadcast };
        targets.AddRange(NoireRemoteAddress.BroadcastAddresses());

        // Sent twice. A dropped datagram would read as a missing machine.
        for (var pass = 0; pass < 2; pass++)
        {
            var probe = NoireRemoteDiscovery.Probe(secret, endpointName);
            var bytes = NoireRemoteJson.WriteBytes(probe);

            foreach (var target in targets)
            {
                try
                {
                    await socket.SendAsync(bytes, bytes.Length, new IPEndPoint(target, port)).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                }
            }

            if (pass == 0)
                await Task.Delay(SecondProbe, cancellationToken).ConfigureAwait(false);
        }

        var deadline = DateTime.UtcNow + Window;

        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;

            if (remaining <= TimeSpan.Zero)
                break;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(remaining);

            UdpReceiveResult received;

            try
            {
                received = await socket.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            var answer = NoireRemoteJson.TryRead<NoireRemoteDiscoveryMessage>(Encoding.UTF8.GetString(received.Buffer));

            if (answer == null || !string.Equals(answer.Kind, "answer", StringComparison.Ordinal))
                continue;

            // Without this an impostor is listed and later calls send arguments to it in cleartext.
            if (!NoireRemoteDiscovery.Verify(secret, NoireRemoteDiscovery.AnswerMethod, answer, options.RemoteClockSkew))
                continue;

            if (string.IsNullOrEmpty(answer.Address))
                answer.Address = received.RemoteEndPoint.Address.ToString();

            answers.Add(answer);
        }

        return answers;
    }

    private static async Task<IReadOnlyList<NoireRemoteInstance>> MergeAsync(
        IReadOnlyList<NoireRemoteInstance> local,
        List<NoireRemoteDiscoveryMessage> answers,
        string secret,
        NoireRemoteClientOptions options,
        CancellationToken cancellationToken)
    {
        var merged = new List<NoireRemoteInstance>(local);
        var seenIds = new HashSet<Guid>();
        var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in local)
        {
            seenIds.Add(instance.Id);
            seenAddresses.Add(instance.Address + ":" + instance.Port.ToString(CultureInfo.InvariantCulture));
        }

        var confirmations = new List<Task<NoireRemoteInstance?>>();

        foreach (var answer in answers)
        {
            foreach (var port in answer.Ports ?? [])
            {
                var key = answer.Address + ":" + port.ToString(CultureInfo.InvariantCulture);

                // The same instance arrives once per broadcast address.
                if (!seenAddresses.Add(key))
                    continue;

                confirmations.Add(ConfirmAsync(answer, port, secret, cancellationToken));
            }
        }

        foreach (var confirmation in confirmations)
        {
            var instance = await confirmation.ConfigureAwait(false);

            if (instance == null)
                continue;

            if (instance.Id != Guid.Empty && !seenIds.Add(instance.Id))
                continue;

            merged.Add(instance);
        }

        merged.Sort(static (left, right) =>
        {
            var byMachine = string.CompareOrdinal(left.Machine, right.Machine);

            if (byMachine != 0)
                return byMachine;

            var byLabel = string.CompareOrdinal(left.Label, right.Label);

            return byLabel != 0 ? byLabel : left.Port.CompareTo(right.Port);
        });

        _ = options;

        return merged;
    }

    private static async Task<NoireRemoteInstance?> ConfirmAsync(
        NoireRemoteDiscoveryMessage answer,
        int port,
        string secret,
        CancellationToken cancellationToken)
    {
        var instance = new NoireRemoteInstance(answer.Address!, port, secret) { Machine = answer.Machine ?? string.Empty };

        try
        {
            var ping = await instance.PingAsync(cancellationToken).ConfigureAwait(false);

            instance.Id = ping.Instance;
            instance.Apply(ping);

            return instance;
        }
        catch (NoireRemoteException)
        {
            // Listed, marked unreachable.
            instance.IsReachable = false;

            return instance;
        }
    }
}
