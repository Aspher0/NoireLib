using System;
using System.Collections.Generic;

namespace NoireLib.Websocket.Internal;

internal enum SocketSlotResult
{
    Claimed,
    TooManySockets,
    TooManyForAddress,
}

// Claimed before the 101. A refusal stays an ordinary HTTP answer.
internal sealed class SocketSlots
{
    private readonly object gate = new();
    private readonly Dictionary<string, int> perAddress = new(StringComparer.OrdinalIgnoreCase);

    private int total;

    public int Count
    {
        get
        {
            lock (gate)
                return total;
        }
    }

    public int CountFor(string address)
    {
        lock (gate)
            return perAddress.TryGetValue(address, out var held) ? held : 0;
    }

    public SocketSlotResult TryClaim(string address, int maxSockets, int maxPerAddress)
    {
        lock (gate)
        {
            if (total >= Math.Max(1, maxSockets))
                return SocketSlotResult.TooManySockets;

            perAddress.TryGetValue(address, out var held);

            if (held >= Math.Max(1, maxPerAddress))
                return SocketSlotResult.TooManyForAddress;

            perAddress[address] = held + 1;
            total++;

            return SocketSlotResult.Claimed;
        }
    }

    public void Release(string address)
    {
        lock (gate)
        {
            if (perAddress.TryGetValue(address, out var held))
            {
                if (held <= 1)
                    perAddress.Remove(address);
                else
                    perAddress[address] = held - 1;
            }

            if (total > 0)
                total--;
        }
    }
}
