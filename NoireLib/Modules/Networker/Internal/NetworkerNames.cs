using System;
using System.Security.Cryptography;
using System.Text;

namespace NoireLib.Networker.Internal;

/// <summary>
/// Derives kernel object names, the LAN beacon port, and the beacon network hash from a network name.<br/>
/// Strong hashing makes cross-network collisions a non-issue; the full network name is verified in handshakes anyway.
/// </summary>
internal static class NetworkerNames
{
    private const string KernelSalt = "NoireNetworker.v1|";
    private const string BeaconSalt = "NoireNetworkerBeacon.v1|";
    private const int BeaconPortRangeStart = 41000;
    private const int BeaconPortRangeSize = 20000;

    public static string MutexName(string networkName)
        => @"Local\NoireNetworker_" + HashHex(KernelSalt + networkName) + "_mtx";

    public static string MapName(string networkName)
        => @"Local\NoireNetworker_" + HashHex(KernelSalt + networkName) + "_map";

    /// <summary>
    /// The salted hash identifying the network inside LAN beacons - the plaintext name never crosses the wire.
    /// </summary>
    public static string BeaconNetworkHash(string networkName)
        => HashHex(BeaconSalt + networkName);

    public static int DeriveBeaconPort(string networkName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(BeaconSalt + networkName));
        var value = BitConverter.ToUInt32(hash, 0);
        return BeaconPortRangeStart + (int)(value % BeaconPortRangeSize);
    }

    /// <summary>
    /// Computes the HMAC proof for a handshake challenge using the LAN secret.
    /// </summary>
    public static string ComputeProof(string secret, string nonce)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(nonce)));
    }

    public static string CreateNonce()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static string HashHex(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)).AsSpan(0, 16));
}
