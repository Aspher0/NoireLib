using System;

namespace NoireLib.Draw3D.Scene;

// Depth is quantized eye distance, bit-inverted for back-to-front ordering.
internal static class SortKey
{
    // 0.1-yalm steps up to about 6.5 km.
    public static ushort QuantizeDistance(float distance)
        => (ushort)Math.Clamp((int)(distance * 10f), 0, ushort.MaxValue);

    // [bucket:2][layer:8][depthQ:16][pipeline:8][material:16][seq:14]
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

    // [bucket:2][layer:8][pipeline:8][material:16][depthQ:16][seq:14]
    public static ulong MakeGrouped(int bucket, int layer, byte pipelineId, ushort materialId, ushort depthQ, int seq)
        => ((ulong)(uint)(bucket & 0b11) << 62)
         | (LayerBits(layer) << 54)
         | ((ulong)pipelineId << 46)
         | ((ulong)materialId << 30)
         | ((ulong)depthQ << 14)
         | (uint)(seq & 0x3FFF);

    private static ulong LayerBits(int layer) => (uint)Math.Clamp(layer + 128, 0, 255);
}
