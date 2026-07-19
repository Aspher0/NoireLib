// NoireLib Draw3D - terrain-hugging decal shader (unit-box volume, CullFront, depth Disabled, Premultiplied).
// Reconstructs the world position under each covered pixel from the game's depth buffer and evaluates an SDF shape in
// the decal's local footprint space, so the decal projects onto the ground, terrain AND walls exactly like a real
// projected decal. Excluded CHARACTERS are removed along their exact game-stencil silhouette (no volume): the ExcludeObjects
// cylinders are only a coarse gate that picks which characters, and the stencil provides the cut. Variants: DECAL_TEXTURED.
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

// Character removal along the game-stencil silhouette. Returns 1 where this pixel must be cut (an EXCLUDED character's
// body), 0 where it paints. The cut is EXACT (the game marks character pixels in stencil = CharacterStencil); the
// per-actor cylinders are only a coarse gate selecting WHICH characters to exclude, so their radius may be generous
// without ever removing ground - only stencil-character pixels inside an excluded actor's footprint are removed. A
// character the caller did not list (outside every cylinder) is painted over; the ground is never touched.
float CharacterMask(float3 wp, float2 uv)
{
    if (CharacterStencil == 0u || ActorCount == 0u)
        return 0.0;                                       // feature off / nothing excluded (stencil unbound reads 0 too)
    if (SceneStencilValue(uv) != CharacterStencil)
        return 0.0;                                       // not a character pixel: keep painting

    for (uint ai = 0; ai < ActorCount; ai++)              // gate: inside an excluded actor's XZ footprint?
    {
        float2 d = wp.xz - Actors[ai].xy;
        if (dot(d, d) < Actors[ai].z * Actors[ai].z)
            return 1.0;
    }
    return 0.0;
}

float4 ps(float4 svPos : SV_Position, out float outDepth : SV_Depth) : SV_Target
{
    float2 uv  = DisplayUv(svPos);
    // Reconstruct the world surface under this pixel from the game depth buffer (sky = no surface).
    float w = SceneSurfaceW(uv);
    bool hasSurface = w < 1e29;
    // Emit OUR reversed-Z device z of that ground point as the fragment depth, so the private-depth GE test lets
    // nearer 3D objects (cubes, donut) occlude the decal instead of it painting over them. Sky/unwritten becomes far (0);
    // its colour is 0 there too, so the value is harmless either way.
    outDepth = hasSurface ? DepthUv.z + DepthUv.w / max(w, 1e-6) : 0.0;

    // Why `return 0` instead of `discard`: this shader relies on fwidth() for edge AA, and screen-space derivatives
    // come from 2x2 pixel quads - discard before a derivative makes neighboring lanes formally undefined. Under
    // premultiplied blending, float4(0,0,0,0) is a mathematically exact no-op pixel, so rejection costs nothing.
    if (!hasSurface) return float4(0, 0, 0, 0);
    float3 wp  = WorldFromDepth(uv, outDepth);           // depth to world
    float3 lp  = mul(float4(wp, 1.0), InvWorld).xyz;     // into unit-box local space
    if (any(abs(lp) > 0.5)) return float4(0, 0, 0, 0);   // outside the decal volume

    // One projection: the shape lives in the box's local XZ footprint, swept along local Y. Which surface it lands on
    // (floor vs wall) is decided entirely by the box's orientation - and the DecalSurface mode CONSTRAINS that orientation
    // on the CPU (SceneNode.ResolveWorld) so a Ground decal stays horizontal and a Wall decal stays vertical. The shader
    // itself is surface-mode-agnostic.
    float2 p = lp.xz * 2.0;                              // footprint space: edge at |p| = 1
    float vis = 1.0 - smoothstep(0.35, 0.5, abs(lp.y)) * Params1.w; // feather near the box top/bottom (local Y = sweep)

    // Character removal: cut the decal along an excluded character's EXACT game-stencil silhouette (no volume, no
    // collision). The registered cylinders only pick which characters; the stencil supplies the cut, so legs/feet/tail
    // are removed exactly and the ground is never holed. A non-excluded character is painted over.
    vis *= 1.0 - CharacterMask(wp, uv);

    // DecalProjection.HighestOnly (Params2.x = 1) still uses the collision height-map (DepthCal.w > 0): skip a surface
    // below the box's highest collision surface (the floor under a table) so only the topmost surface within the box
    // paints. Purely vertical - never touches wall/object occlusion.
    if (Params2.x > 0.5 && DepthCal.w > 0.0)
    {
        float groundY = WorldGroundHeight(wp);
        float boxTopY = Params2.y;
        if (groundY > -1e29 && groundY <= boxTopY + DepthCal.w && wp.y < groundY - DepthCal.w)
            return float4(0, 0, 0, 0);
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
    // Rim width. The SDF lives in the normalized footprint (edge at |p| = 1), so an unscaled band widens in world space
    // as the box grows. Dividing by the footprint's world size (the box X/Z axis lengths, averaged) cancels that.
    // Params2.z is the reference footprint scale that keeps the rim's meaning fixed: a scene decal passes 0, so the rim
    // is a constant world thickness no matter how the box is scaled; an immediate shape passes its own built footprint
    // scale, so its rim stays proportional to the radius it was drawn with.
    float footprintScale = 0.5 * (length(World[0].xyz) + length(World[2].xyz));
    float outlineRef = Params2.z > 0.0 ? Params2.z : 1.0;
    float bandW = Params1.z * outlineRef / max(footprintScale, 1e-4);
    float outline = bandW > 0.0
        ? SdfCoverage(abs(sd + bandW * 0.5) - bandW * 0.5)                           // band hugging the edge, inside
        : 0.0;

    // Rim colour: an OutlineColor with alpha > 0 overrides the decal colour for the band; alpha 0 leaves the rim the
    // decal's own colour, so rim and fill differ only in opacity (the classic look, and the default).
    float4 rim = OutlineColor.a > 0.0 ? OutlineColor : BaseColor;

    float fillA = BaseColor.a * fill * Params0.w;         // translucent interior (Params0.w = fill opacity)
    float rimA  = rim.a * outline;                        // strong band hugging the edge
    float top   = max(fillA, rimA);
    float a     = top * vis;
    // Where the band covers, its colour wins; the interior keeps the decal colour. The ratio is the AA-smooth crossfade.
    float3 rgb  = lerp(BaseColor.rgb, rim.rgb, top > 1e-6 ? rimA / top : 0.0);
    return float4(rgb * a, a);
#endif
}
