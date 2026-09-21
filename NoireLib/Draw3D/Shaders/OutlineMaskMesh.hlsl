// Outline mask of a mesh's full silhouette, without occlusion.
// SV_Target0: rgb = outline colour, a = coverage. SV_Target1: r = 1 where in front of the game world.
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
    float2 clipZW : TEXCOORD0;
};

struct MaskOut
{
    float4 color : SV_Target0;
    float  vis   : SV_Target1;
};

PsIn vs(VsIn v)
{
    PsIn o;
    float4 wp = mul(float4(v.pos, 1.0), World);
    o.svPos  = mul(wp, ViewProj);
    o.clipZW = o.svPos.zw;
    return o;
}

MaskOut ps(PsIn i)
{
    MaskOut o;
    o.color = float4(BaseColor.rgb, BaseColor.a);
    // A null depth SRV, an x-ray outline, reads visible everywhere.
    o.vis = DepthVisibility(DisplayUv(i.svPos), i.clipZW.y, 0.0) >= 0.5 ? 1.0 : 0.0;
    return o;
}
