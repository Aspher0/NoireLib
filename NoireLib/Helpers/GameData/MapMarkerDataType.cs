namespace NoireLib.Helpers;

/// <summary>The <see cref="MapMarkerEntry.DataType"/> values that say how to read a marker's data key.</summary>
public static class MapMarkerDataType
{
    /// <summary>The marker is decorative and keys nothing.</summary>
    public const int None = 0;

    /// <summary>The marker points at a map, and its data key is a Map row.</summary>
    public const int Map = 1;

    /// <summary>The marker points at an instance entrance, and its data key is an InstanceContent row.</summary>
    public const int InstanceEntrance = 2;

    /// <summary>The marker is a city aetheryte, and its data key is an Aetheryte row.</summary>
    public const int Aetheryte = 3;

    /// <summary>The marker is an aethernet shard, and its data key is a PlaceName row.</summary>
    public const int AethernetShard = 4;
}
