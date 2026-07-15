using System;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// 64-bit draw ordering key: [bucket:2][layer:8][depthQ:16][pipeline:8][material:16][seq:14].<br/>
/// Depth is quantized eye distance (VP-only contract - no view matrix required): ascending for opaque
/// (front-to-back), bit-inverted for transparent (back-to-front). One array sort per frame orders and
/// groups everything.
/// </summary>
internal static class SortKey
{
    /// <summary>Quantizes an eye distance into 16 bits (0.1-yalm steps up to ~6.5 km).</summary>
    public static ushort QuantizeDistance(float distance)
        => (ushort)Math.Clamp((int)(distance * 10f), 0, ushort.MaxValue);

    /// <summary>
    /// Depth-dominant key - [bucket:2][layer:8][depthQ:16][pipeline:8][material:16][seq:14].<br/>
    /// Used by the transparent bucket (strict back-to-front when <paramref name="backToFront"/>).
    /// </summary>
    public static ulong Make(int bucket, int layer, ushort depthQ, byte pipelineId, ushort materialId, int seq, bool backToFront)
    {
        var depthBits = backToFront ? (ulong)(ushort)~depthQ : depthQ;
        return ((ulong)(uint)(bucket & 0b11) << 62)
             | (LayerBits(layer) << 54)
             | (depthBits << 38)
             | ((ulong)pipelineId << 30)
             | ((ulong)materialId << 14)
             | (uint)(seq & 0x3FFF);
    }

    /// <summary>
    /// State-grouped key - [bucket:2][layer:8][pipeline:8][material:16][depthQ:16][seq:14].<br/>
    /// Used by the opaque bucket (depth order is only an early-z hint there) and by transparent
    /// materials that opted into unordered batching: identical materials become adjacent, so
    /// instanced runs form regardless of distance.
    /// </summary>
    public static ulong MakeGrouped(int bucket, int layer, byte pipelineId, ushort materialId, ushort depthQ, int seq)
        => ((ulong)(uint)(bucket & 0b11) << 62)
         | (LayerBits(layer) << 54)
         | ((ulong)pipelineId << 46)
         | ((ulong)materialId << 30)
         | ((ulong)depthQ << 14)
         | (uint)(seq & 0x3FFF);

    private static ulong LayerBits(int layer) => (uint)Math.Clamp(layer + 128, 0, 255);
}
