// NoireLib Draw3D - terrain-hugging decal shader (unit-box volume, CullFront, depth Disabled, Premultiplied).
// Reconstructs the world position under each covered pixel from the game's depth buffer and evaluates an SDF shape in
// the decal's local footprint space, so the decal projects onto the ground, terrain AND walls exactly like a real
// projected decal. The only surfaces it removes are the caller's registered actors (ExcludeVolumes) - a tight,
// anti-aliased cut, never a soft hole. Variants: DECAL_TEXTURED.
#include "Common.hlsli"

struct VsIn
{
    float3 pos    : POSITION;
    float3 normal : NORMAL;
    float2 uv     : TEXCOORD0;
    float4 color  : COLOR0;
};

float4 vs(VsIn v) : SV_Position
{
    float4 wp = mul(float4(v.pos, 1.0), World);
    return mul(wp, ViewProj);
}

// Anti-aliased coverage of the registered excluded actors (1 = fully removed, 0 = paint normally), so an actor
// standing in the decal is cut cleanly at its silhouette with NO bleed onto the feet and NO aliasing. The excluded
// region is the INTERSECTION of the actor's vertical cylinder (its XZ footprint) with the half-space above its feet
// plane - so flat ground inside the cylinder still paints (an over-wide radius leaves no moat) and only the raised
// body/shoes are removed. The edge is AA'd in screen space by SdfCoverage's fwidth, exactly like the footprint edge.
float ActorExclusion(float3 wp)
{
    // Accumulate the UNION signed-distance field of every actor's excluded region (< 0 inside any actor's body
    // column), then take the screen-space-AA'd coverage ONCE. The AA (fwidth) must stay OUT of this varying-count loop:
    // a gradient inside a loop with a runtime iteration count forces an unroll and the game compiles shaders with
    // warnings-as-errors (X3570), so calling SdfCoverage per actor here would fail to compile at runtime.
    float sd = 1e9;
    for (uint ai = 0; ai < ActorCount; ai++)
    {
        float2 dxz  = wp.xz - Actors[ai].xy;
        float  aCyl = length(dxz) - Actors[ai].z;         // signed distance to the cylinder wall: < 0 inside
        float  aTop = (Actors[ai].w + 0.03) - wp.y;       // < 0 when raised above the feet plane (+3cm): body / shoes
        sd = min(sd, max(aCyl, aTop));                    // union of each cylinder-∩-above-feet region
    }
    return SdfCoverage(sd);                               // one AA coverage of the excluded region (fwidth ~ 1px)
}

float4 ps(float4 svPos : SV_Position, out float outDepth : SV_Depth) : SV_Target
{
    float2 uv  = DisplayUv(svPos);
    // Reconstruct the world surface under this pixel from the game depth buffer (sky = no surface).
    float w = SceneSurfaceW(uv);
    bool hasSurface = w < 1e29;
    // Emit OUR reversed-Z device z of that ground point as the fragment depth, so the private-depth GE test lets
    // nearer 3D objects (cubes, donut) occlude the decal instead of it painting over them. Sky/unwritten -> far (0);
    // its colour is 0 there too, so the value is harmless either way.
    outDepth = hasSurface ? DepthUv.z + DepthUv.w / max(w, 1e-6) : 0.0;

    // Why `return 0` instead of `discard`: this shader relies on fwidth() for edge AA, and screen-space derivatives
    // come from 2x2 pixel quads - discard before a derivative makes neighboring lanes formally undefined. Under
    // premultiplied blending, float4(0,0,0,0) is a mathematically exact no-op pixel, so rejection costs nothing.
    if (!hasSurface) return float4(0, 0, 0, 0);
    float3 wp  = WorldFromDepth(uv, outDepth);           // depth -> world

    float3 lp  = mul(float4(wp, 1.0), InvWorld).xyz;     // into unit-box local space
    if (any(abs(lp) > 0.5)) return float4(0, 0, 0, 0);   // outside the decal volume

    float2 p = lp.xz * 2.0;                              // footprint space: edge at |p| = 1
    float vis = 1.0 - smoothstep(0.35, 0.5, abs(lp.y)) * Params1.w; // Y feather near the box top/bottom
    vis *= 1.0 - ActorExclusion(wp);                     // registered actors removed (AA, tight, no bleed)

#ifdef DECAL_TEXTURED
    float4 t = BaseTex.Sample(BaseSamp, p * 0.5 + 0.5);
    float4 c0 = t * BaseColor;
    c0.a *= vis;
    return float4(c0.rgb * c0.a, c0.a);
#else
    float sd;
    int kind = (int)Params1.y;
    if      (kind == 0) sd = length(p) - 1.0;                                        // Circle
    else if (kind == 1) sd = max(length(p) - 1.0, Params0.x - length(p));            // Ring   (x = inner ratio)
    else if (kind == 2) {                                                            // Sector (x = halfAngle, y = innerRatio; oriented +Z)
        float r  = length(p);
        float an = abs(atan2(p.x, p.y));                  // 0 at +Z
        sd = max(max(r - 1.0, Params0.y - r), (an - Params0.x) * r);                 // angular edge scaled to arc-length units
    }
    else                sd = max(abs(p.x), abs(p.y)) - 1.0;                          // Rect (lines = scaled rects)

    float fill    = SdfCoverage(sd);
    float outline = Params1.z > 0.0
        ? SdfCoverage(abs(sd + Params1.z * 0.5) - Params1.z * 0.5)                   // band hugging the edge, inside
        : 0.0;

    float4 c = BaseColor;
    c.a *= max(fill * Params0.w, outline) * vis;          // classic decal: strong rim, translucent fill (Params0.w = fill opacity)
    return float4(c.rgb * c.a, c.a);
#endif
}
