using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NoireLib.Remote;

/// <summary>
/// Works out which of this machine's addresses reaches a given peer. A machine with several interfaces has several
/// answers and only the routing table knows which one applies.
/// </summary>
public static class NoireRemoteAddress
{
    /// <summary>
    /// Finds the local address that reaches a peer by asking the routing table.
    /// </summary>
    /// <param name="peer">The peer to reach. Loopback answers loopback.</param>
    /// <returns>The local address, or loopback when the routing table has no answer.</returns>
    public static IPAddress LocalAddressFor(IPEndPoint peer)
    {
        if (peer == null)
            return IPAddress.Loopback;

        try
        {
            // Connecting a datagram socket sends nothing. It binds to the interface the route picks.
            using var probe = new Socket(peer.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(peer);

            return probe.LocalEndPoint is IPEndPoint local ? local.Address : IPAddress.Loopback;
        }
        catch (SocketException)
        {
            return IPAddress.Loopback;
        }
        catch (ObjectDisposedException)
        {
            return IPAddress.Loopback;
        }
    }

    /// <summary>
    /// Finds the address another machine on the local network would use to reach this one.
    /// </summary>
    /// <returns>The address as text, or <c>127.0.0.1</c> when no route leaves this machine.</returns>
    public static string LocalAddress()
    {
        // Any routable address off this machine picks the default route.
        var address = LocalAddressFor(new IPEndPoint(IPAddress.Parse("203.0.113.1"), 9));

        return IPAddress.IsLoopback(address) ? "127.0.0.1" : address.ToString();
    }

    /// <summary>
    /// Lists the directed broadcast address of every operational IPv4 interface. A directed broadcast reaches one
    /// subnet where the global one may be dropped.
    /// </summary>
    /// <returns>The broadcast addresses found, with no duplicates.</returns>
    public static IReadOnlyList<IPAddress> BroadcastAddresses()
    {
        var found = new List<IPAddress>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        NetworkInterface[] interfaces;

        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return found;
        }

        foreach (var adapter in interfaces)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null)
                    continue;

                var broadcast = Broadcast(unicast.Address, unicast.IPv4Mask);

                if (broadcast != null && seen.Add(broadcast.ToString()))
                    found.Add(broadcast);
            }
        }

        return found;
    }

    private static IPAddress? Broadcast(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        if (addressBytes.Length != 4 || maskBytes.Length != 4)
            return null;

        var broadcast = new byte[4];

        for (var index = 0; index < 4; index++)
            broadcast[index] = (byte)(addressBytes[index] | (byte)~maskBytes[index]);

        return new IPAddress(broadcast);
    }
}
