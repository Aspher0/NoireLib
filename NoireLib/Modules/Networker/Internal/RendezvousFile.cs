using Newtonsoft.Json;
using System;
using System.IO.MemoryMappedFiles;

namespace NoireLib.Networker.Internal;

/// <summary>
/// The hub's rendezvous data, published through a named memory-mapped file so clients can find its ephemeral port.
/// </summary>
internal sealed class RendezvousData
{
    [JsonProperty("n")]
    public string Network { get; set; } = string.Empty;

    [JsonProperty("p")]
    public int Port { get; set; }

    [JsonProperty("g")]
    public long Generation { get; set; }

    [JsonProperty("pid")]
    public int ProcessId { get; set; }
}

/// <summary>
/// Publishes and reads the hub rendezvous.<br/>
/// Content is always a hint to be verified by handshake, never truth: surviving client handles can keep a dead
/// hub's mapped file alive, so a new hub opens-or-creates and overwrites with a bumped generation.
/// </summary>
internal static class RendezvousFile
{
    private const int Capacity = 4096;

    /// <summary>
    /// Publishes rendezvous data, bumping the generation of any previous publication.
    /// Returns a holder that keeps the mapped file alive; dispose it when the hub stops.
    /// </summary>
    public static IDisposable Publish(string mapName, string networkName, int port)
    {
        var previous = TryRead(mapName);

        var mappedFile = MemoryMappedFile.CreateOrOpen(mapName, Capacity);

        try
        {
            var data = new RendezvousData
            {
                Network = networkName,
                Port = port,
                Generation = (previous?.Generation ?? 0) + 1,
                ProcessId = Environment.ProcessId,
            };

            Write(mappedFile, data);
            return mappedFile;
        }
        catch
        {
            mappedFile.Dispose();
            throw;
        }
    }

    public static RendezvousData? TryRead(string mapName)
    {
        try
        {
            using var mappedFile = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
            using var accessor = mappedFile.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.Read);

            var length = accessor.ReadInt32(0);

            if (length <= 0 || length > Capacity - 4)
                return null;

            var bytes = new byte[length];
            accessor.ReadArray(4, bytes, 0, length);

            return Wire.DecodeModel<RendezvousData>(bytes);
        }
        catch
        {
            // Missing map, permission issues, or torn/corrupt content - all mean "no usable rendezvous right now".
            return null;
        }
    }

    private static void Write(MemoryMappedFile mappedFile, RendezvousData data)
    {
        var bytes = Wire.EncodeModel(data);

        if (bytes.Length > Capacity - 4)
            throw new InvalidOperationException("Rendezvous data exceeds the mapped file capacity.");

        using var accessor = mappedFile.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.ReadWrite);

        // Invalidate first so a concurrent reader can never pair the new length with old bytes.
        accessor.Write(0, 0);
        accessor.WriteArray(4, bytes, 0, bytes.Length);
        accessor.Write(0, bytes.Length);
        accessor.Flush();
    }
}
