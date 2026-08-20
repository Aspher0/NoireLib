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

/// <summary>
/// Reads an MSVC <c>std::map</c> out of another process's memory, against the C++ runtime's <c>_Tree_node</c> layout:
/// left, parent and right pointers, a nil flag, then the key and value. The head node's parent is the tree's root, and
/// the descent lands on a lower bound that still has to be checked for equality. Every read goes through
/// <see cref="IGuardedMemory"/>, so a wild pointer or a cycle comes back as <see cref="StdMapLookup.Unreadable"/>.
/// </summary>
public static class StdMapReader
{
    /// <summary>Offset of the node's left child pointer.</summary>
    public const int NodeLeftOffset = 0x00;

    /// <summary>Offset of the node's parent pointer, which on the head node is the tree's root.</summary>
    public const int NodeParentOffset = 0x08;

    /// <summary>Offset of the node's right child pointer.</summary>
    public const int NodeRightOffset = 0x10;

    /// <summary>Offset of the flag marking the head and the leaf sentinels.</summary>
    public const int NodeIsNilOffset = 0x19;

    /// <summary>Offset of the node's key.</summary>
    public const int NodeKeyOffset = 0x20;

    /// <summary>Offset of the node's value.</summary>
    public const int NodeValueOffset = 0x28;

    /// <summary>Size of one node.</summary>
    public const int NodeSize = 0x30;

    /// <summary>Depth at which a descent is treated as a cycle, far past any real map's height.</summary>
    public const int MaxTreeDepth = 32;

    /// <summary>Looks a 32-bit key up in a map, descending to the smallest node not below it and then demanding equality.</summary>
    /// <param name="memory">The guard every read goes through.</param>
    /// <param name="head">The map's head node.</param>
    /// <param name="key">The key to find.</param>
    /// <param name="value">The value stored against the key, when it was found.</param>
    /// <returns>Whether the key was found, was absent, or could not be looked for at all.</returns>
    public static StdMapLookup TryFind(IGuardedMemory memory, long head, uint key, out long value)
    {
        value = 0;

        if (memory == null || head == 0 || !memory.IsReadable(head, NodeSize))
            return StdMapLookup.Unreadable;

        var candidate = head;
        var node = memory.ReadInt64(head + NodeParentOffset);

        for (var depth = 0; ; depth++)
        {
            if (depth >= MaxTreeDepth)
                return StdMapLookup.Unreadable;

            if (node == 0 || !memory.IsReadable(node, NodeSize))
                return StdMapLookup.Unreadable;

            if (memory.ReadByte(node + NodeIsNilOffset) != 0)
                break;

            if (memory.ReadUInt32(node + NodeKeyOffset) >= key)
            {
                candidate = node;
                node = memory.ReadInt64(node + NodeLeftOffset);
            }
            else
            {
                node = memory.ReadInt64(node + NodeRightOffset);
            }
        }

        // The candidate is either the head or a node the loop already guarded.
        if (memory.ReadByte(candidate + NodeIsNilOffset) != 0)
            return StdMapLookup.Missing;

        // The descent stops at the smallest key not below the one asked for, which is a different key whenever the
        // map does not hold it.
        if (key < memory.ReadUInt32(candidate + NodeKeyOffset))
            return StdMapLookup.Missing;

        value = memory.ReadInt64(candidate + NodeValueOffset);
        return StdMapLookup.Found;
    }
}
