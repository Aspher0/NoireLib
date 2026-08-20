namespace NoireLib.Helpers.Memory;

/// <summary>How a lookup in a native ordered map came back.</summary>
public enum StdMapLookup
{
    /// <summary>A guarded read refused, or the descent ran deeper than a real tree can be; says nothing about whether the key is there.</summary>
    Unreadable,

    /// <summary>The map was read and does not hold the key.</summary>
    Missing,

    /// <summary>The map holds the key.</summary>
    Found,
}
