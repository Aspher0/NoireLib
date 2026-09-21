// Top-down collision height map, MAX-blended. Each texel holds the highest collision Y up to DepthCal.x.
#include "Common.hlsli"

struct VsIn
{
    float3 pos    : POSITION;
    float3 normal : NORMAL;
    float2 uv     : TEXCOORD0;
    float4 color  : COLOR0;
};

struct PsIn
{
    float4 svPos  : SV_Position;
    float  worldY : TEXCOORD0;
};

PsIn vs(VsIn v)
{
    PsIn o;
    float4 wp = mul(float4(v.pos, 1.0), World);   // region-relative verts
    o.svPos   = mul(wp, ViewProj);                // CPU-built affine XZ-to-clip map
    o.worldY  = wp.y;
    return o;
}

float ps(PsIn i) : SV_Target
{
    // Reads b0 in the pixel stage. RenderWorldHeight must bind it there, or every surface above Y 0 is discarded.
    if (i.worldY > DepthCal.x)
        discard;
    return i.worldY;
}
