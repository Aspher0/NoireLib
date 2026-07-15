// NoireLib Draw3D - outline coverage mask for a solid mesh silhouette.
// Draws the outlined object's world geometry into the outline mask WITHOUT occlusion, so the composite can trace the
// object's true silhouette and not a separate outline around every screen fragment poking through an occluder (a
// fence, a grate). Two targets:
//   SV_Target0 (rgba) : rgb = outline colour, a = coverage (the object's own colour alpha) - the FULL silhouette.
//   SV_Target1 (r)    : worldVisible - 1 where this silhouette pixel is in front of the game world, 0 where a wall /
//                       character occludes it. The composite hides the outline wherever the nearest silhouette pixel
//                       is occluded, so occlusion is applied to the finished outline shape rather than fragmenting it.
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
    float2 clipZW : TEXCOORD0; // pixel view depth (w) for the world-occlusion test
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
    // BaseColor = the outline colour (straight alpha), uploaded per outlined item. No discard - the whole silhouette
    // is marked so the composite outlines the object, not each visible piece of it.
    o.color = float4(BaseColor.rgb, BaseColor.a);
    // 1 = this silhouette pixel is in front of the world (visible), 0 = a wall / character is in front. Hard test.
    // With a null depth SRV (an x-ray outline) DepthVisibility returns visible everywhere, so nothing is occluded.
    o.vis = DepthVisibility(DisplayUv(i.svPos), i.clipZW.y, 0.0) >= 0.5 ? 1.0 : 0.0;
    return o;
}
