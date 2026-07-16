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

    // Actor removal. Two modes:
    //  * Height-map world-occlusion, GATED by this decal's registered actors (DepthCal.w > 0, the new default).
    //    WorldHeight is a top-down map of the highest collision Y per XZ column, capped at the tallest decal box top
    //    (its roof is already removed). Here we bound the search to THIS decal's own box top (Params2.y): the vertical
    //    slab the decal actually paints. `groundY` is the highest collision surface WITHIN the box; `elevated` = this
    //    pixel's surface sits above it, i.e. a body/prop standing on the ground - NOT the ground itself. Camera-angle
    //    independent (unlike a view-depth test, which mistakes a sitting leg at floor depth for the floor). We remove
    //    ONLY where an excluded actor's cylinder (ActorExclusion) covers an elevated surface. Therefore:
    //      - ground (flat or sloped) sits at groundY -> never cut (no moat, no gouge);
    //      - furniture WITH collision sits at its own groundY -> never cut (stools/shelves stop being clipped);
    //      - an excluded character's body is removed at any angle; a non-listed actor is painted over (no cylinder).
    //    A surface ABOVE the box (groundY > boxTopY) is outside what this decal paints, so it drives neither the cut nor
    //    HighestOnly - the box's Y scale IS the search height (a 5cm box searches 5cm; an infinite box reaches the ceiling).
    //    DepthCal.w is the elevation band in world units (covers height-map/collision coarseness). ActorCount 0 => all painted.
    //  * Legacy cylinder (DepthCal.w <= 0): the per-decal ExcludeVolumes cut on its own (world-occlusion off).
    if (DepthCal.w > 0.0)
    {
        float groundY    = WorldGroundHeight(wp);                        // highest collision Y in this column (roof capped); -1e30 = unknown
        float boxTopY    = Params2.y;                                    // THIS decal's box top (world Y): the vertical search bound
        bool  haveGround = groundY > -1e29 && groundY <= boxTopY + DepthCal.w; // trust only a surface within this decal's own box
        float elevated   = (haveGround && wp.y > groundY + DepthCal.w) ? 1.0 : 0.0;
        vis *= 1.0 - elevated * ActorExclusion(wp);                      // remove only an excluded actor's elevated body

        // DecalProjection.HighestOnly (Params2.x = 1): skip a surface below the box's highest collision surface (the
        // floor under a table) so only the topmost surface WITHIN the box paints. Purely vertical - never touches
        // wall/object occlusion.
        if (Params2.x > 0.5 && haveGround && wp.y < groundY - DepthCal.w)
            return float4(0, 0, 0, 0);
    }
    else
    {
        vis *= 1.0 - ActorExclusion(wp);                                // registered actors removed (legacy cylinder)
    }

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
