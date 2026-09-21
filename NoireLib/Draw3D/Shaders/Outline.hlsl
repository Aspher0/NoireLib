// Selection outline composite. Each rim pixel takes the world visibility of its nearest silhouette pixel.
Texture2D    MaskTex    : register(t0);   // rgb = outline colour, a = coverage
Texture2D    VisTex     : register(t1);   // r = worldVisible per silhouette pixel
SamplerState PointClamp : register(s0);

cbuffer OutlineCB : register(b0)
{
    float4 OutlineParams;   // x = width px, yz = 1/viewport
};

// The runtime width is clamped to this.
#define OUTLINE_MAX_RADIUS 8

void vs(uint id : SV_VertexID, out float4 pos : SV_Position, out float2 uv : TEXCOORD0)
{
    uv  = float2((id << 1) & 2, id & 2);
    pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 ps(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float  width = clamp(OutlineParams.x, 1.0, (float)OUTLINE_MAX_RADIUS);
    float2 texel = OutlineParams.yz;

    float4 center = MaskTex.SampleLevel(PointClamp, uv, 0);

    // Every whole-pixel offset within the width keeps the rim gap-free.
    float  bestA    = 0.0;
    float3 bestRgb  = 0.0;
    float  bestDist = 1e9;
    float2 bestUv   = uv;
    [loop] for (int dy = -OUTLINE_MAX_RADIUS; dy <= OUTLINE_MAX_RADIUS; dy++)
    {
        [loop] for (int dx = -OUTLINE_MAX_RADIUS; dx <= OUTLINE_MAX_RADIUS; dx++)
        {
            if (dx == 0 && dy == 0)
                continue;
            float dist = sqrt((float)(dx * dx + dy * dy));
            if (dist > width)
                continue;
            float2 suv = uv + float2(dx, dy) * texel;
            float4 s = MaskTex.SampleLevel(PointClamp, suv, 0);
            if (s.a > 0.001 && dist < bestDist)
            {
                bestDist = dist;
                bestA    = s.a;
                bestRgb  = s.rgb;
                bestUv   = suv;
            }
        }
    }

    float rim = bestA * saturate(1.0 - center.a);
    rim *= VisTex.SampleLevel(PointClamp, bestUv, 0).r;

    return float4(bestRgb * rim, rim);                    // premultiplied
}
