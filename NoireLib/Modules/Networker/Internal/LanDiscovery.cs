using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Networker.Internal;

/// <summary>
/// LAN hub discovery through UDP broadcast beacons. Only hubs run this, so the beacon port is bound
/// at most once per machine per network. Beacons carry a salted network hash, never the plaintext name.
/// </summary>
internal sealed class LanDiscovery : IDisposable
{
    private readonly HubServer hub;
    private readonly NoireNetworker owner;
    private readonly CancellationToken lifetime;
    private readonly string networkHash;
    private readonly int beaconPort;

    private UdpClient? udpClient;
    private int disposed;

    public LanDiscovery(HubServer hub, NoireNetworker owner, CancellationToken lifetime)
    {
        this.hub = hub;
        this.owner = owner;
        this.lifetime = lifetime;

        networkHash = NetworkerNames.BeaconNetworkHash(owner.NetworkName!);
        beaconPort = owner.ActiveOptions.BeaconPort ?? NetworkerNames.DeriveBeaconPort(owner.NetworkName!);
    }

    public void Start()
    {
        try
        {
            udpClient = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true,
            };

            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, beaconPort));
        }
        catch (Exception ex)
        {
            // A foreign app occupies the beacon port: LAN is disabled, same-PC operation is unaffected.
            owner.InternalLogError(ex, $"Could not bind LAN beacon port {beaconPort}; LAN discovery is disabled for this session. " +
                                       $"Override {nameof(NetworkerOptions)}.{nameof(NetworkerOptions.BeaconPort)} to use another port.");
            udpClient?.Dispose();
            udpClient = null;
            return;
        }

        _ = BeaconLoopAsync();
        _ = ReceiveLoopAsync();
    }

    private async Task BeaconLoopAsync()
    {
        var beacon = new BeaconModel
        {
            NetworkHash = networkHash,
            HubId = hub.HubId,
            Port = hub.Port,
        };

        var datagram = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(beacon));
        var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, beaconPort);

        try
        {
            while (!lifetime.IsCancellationRequested && udpClient != null)
            {
                try
                {
                    await udpClient.SendAsync(datagram, broadcastEndPoint, lifetime).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    owner.InternalLog($"Beacon send failed: {ex.Message}");
                }

                await Task.Delay(owner.ActiveOptions.BeaconInterval, lifetime).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested && udpClient != null)
            {
                UdpReceiveResult result;

                try
                {
                    result = await udpClient.ReceiveAsync(lifetime).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    continue;
                }

                BeaconModel? beacon;

                try
                {
                    beacon = JsonConvert.DeserializeObject<BeaconModel>(Encoding.UTF8.GetString(result.Buffer));
                }
                catch
                {
                    continue;
                }

                if (beacon == null
                    || beacon.NetworkHash != networkHash
                    || beacon.HubId == hub.HubId
                    || beacon.Port <= 0
                    || beacon.Port > ushort.MaxValue)
                {
                    continue;
                }

                // Deterministic dial direction: the lower hub id dials, so exactly one link forms per hub pair.
                if (hub.HubId.CompareTo(beacon.HubId) < 0)
                    _ = hub.DialHubLinkAsync(new IPEndPoint(result.RemoteEndPoint.Address, beacon.Port), beacon.HubId);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        udpClient?.Dispose();
        udpClient = null;
    }
}
