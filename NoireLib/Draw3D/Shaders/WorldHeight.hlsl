// NoireLib Draw3D - top-down collision height-map. Renders the cached collision mesh through a direct affine
// world->clip map (ViewProj set on the CPU so world XZ maps linearly to the target, no perspective) and outputs the
// vertex's world Y. Drawn with MAX blend, so each texel holds the highest collision Y in that XZ column - but ONLY up
// to DepthCal.x, the tallest ground-decal box top this frame: anything above it (a ceiling / roof / overhead floor)
// is discarded, so a covered room's roof never masks the ground below. Each decal further bounds the search to its
// OWN box top in the shader. Ground decals sample this to tell "elevated body" from "ground/furniture surface"
// independent of camera angle. See BuildHeightMapMatrix + the world-occlusion branch in GroundDecal.hlsl.
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
    float4 wp = mul(float4(v.pos, 1.0), World);   // World = translate(region centre): verts are region-relative
    o.svPos   = mul(wp, ViewProj);                // ViewProj = CPU-built affine XZ->clip map (see BuildHeightMapMatrix)
    o.worldY  = wp.y;
    return o;
}

float ps(PsIn i) : SV_Target
{
    // Drop overhead geometry (ceiling / roof / upper floor) above the tallest decal box top so MAX blend keeps the
    // real ground/furniture below it, not the roof. No derivatives here, so the discard is /WX-safe.
    if (i.worldY > DepthCal.x)
        discard;
    return i.worldY;
}
