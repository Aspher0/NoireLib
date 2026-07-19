// NoireLib Draw3D - unlit world-space shader (variants: UNLIT_TEXTURED, UNLIT_INSTANCED, OPAQUE_DOMAIN).
#include "Common.hlsli"

struct VsIn
{
    float3 pos    : POSITION;
    float3 normal : NORMAL;      // unused here; kept for one shared input layout
    float2 uv     : TEXCOORD0;
    float4 color  : COLOR0;
#ifdef UNLIT_INSTANCED
    // Instance world rows are UNtransposed: float4x4(r0..r3) builds logical rows directly,
    // bypassing cbuffer packing, so mul(v, world) needs the matrix as-is.
    float4 i0 : IWORLD0; float4 i1 : IWORLD1; float4 i2 : IWORLD2; float4 i3 : IWORLD3;
    float4 iColor : ICOLOR;
#endif
};

struct PsIn
{
    float4 svPos  : SV_Position;
    float2 uv     : TEXCOORD0;
    float4 color  : COLOR0;
    float2 clipZW : TEXCOORD1;   // pixel device depth = z / w
};

PsIn vs(VsIn v)
{
    PsIn o;
#ifdef UNLIT_INSTANCED
    float4x4 world = float4x4(v.i0, v.i1, v.i2, v.i3);
    float4 tint = v.iColor;
#else
    float4x4 world = World;
    float4 tint = float4(1, 1, 1, 1);
#endif
    float4 wp = mul(float4(v.pos, 1.0), world);
    o.svPos  = mul(wp, ViewProj);
    o.uv     = v.uv;
    o.color  = v.color * BaseColor * tint;
    o.clipZW = o.svPos.zw;
    return o;
}

float4 ps(PsIn i) : SV_Target
{
    float4 c = i.color;
#ifdef UNLIT_TEXTURED
    c *= BaseTex.Sample(BaseSamp, i.uv);
#endif
    float vis = DepthVisibility(DisplayUv(i.svPos), i.clipZW.y, Params1.x);
#ifdef OPAQUE_DOMAIN
    // The Opaque bucket renders with blending DISABLED - modulating alpha would be a silent
    // no-op and a world-occluded pixel would still paint. Occlusion must kill the pixel.
    // (DepthFade therefore has no effect in this domain; it is a blended-domain feature.)
    if (vis < 0.5) discard;
    return float4(c.rgb, 1.0);
#else
    c.a *= vis;
    return float4(c.rgb * c.a, c.a);                     // premultiplied output: rgb already scaled by alpha
#endif
}
