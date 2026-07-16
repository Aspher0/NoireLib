// NoireLib Draw3D - shared shader header (see docs/Draw3D V2 Proposal.md §10.2).
// All matrices in cbuffers are pre-transposed on the CPU; consume with mul(v, M) only.

// ---- b0: per frame -------------------------------------------------------
cbuffer FrameCB : register(b0)
{
    float4x4 ViewProj;          // transposed on CPU; use mul(v, M)
    float4x4 InvViewProj;       // transposed on CPU
    float4   EyePosTime;        // xyz = camera origin (world), w = time seconds
    float4   Viewport;          // xy = display size px, zw = 1/display size
    float4   DepthUv;           // xy = depth uv scale; zw = OUR projection's z map: deviceZ = z + w / clipW
    float4   DepthCal;          // game depth sample = x + y / clipW (runtime-calibrated); z = 1 when valid
    float4   Ambient;           // rgb = ambient color, a = ambient intensity      (Lit)
    float4   LightDirIntensity; // xyz = normalized dir *toward* light, w = intensity (Lit)
    float4   LightColor;        // rgb = directional color, a unused
    float4   WorldHeightRegion; // xy = region min XZ (world), z = 1/regionSize, w = 1 when the height-map is valid
};

// ---- b1: per object / material ------------------------------------------
cbuffer ObjectCB : register(b1)
{
    float4x4 World;             // transposed on CPU
    float4x4 InvWorld;          // transposed on CPU (decals: world -> unit-box local)
    float4   BaseColor;         // straight alpha; premultiplied inside the PS
    float4   Params0;           // shape params / material params
    float4   Params1;           // x = DepthFade (world units, 0 = hard), y = shapeKind,
                                // z = outlineWidth (0..1 of SDF units), w = heightFade (decal Y feather)
    float4   Params2;           // x = ground-decal projection mode (0 = all surfaces, 1 = highest only)
                                // y = decal box top world Y (the height-map's vertical search bound)
}

// ---- b2: per-decal excluded-actor gate + stencil key (ground-decal ExcludeObjects) --
// The actors THIS decal skips painting on, uploaded per decal draw. Each is a vertical cylinder used ONLY as a coarse
// gate to pick which characters to exclude - the exact cut is the game stencil silhouette, so the radius may be generous
// without ever holing the ground. xy = world XZ centre, z = radius, w unused. ActorCount = 0 = exclude nothing.
// CharacterStencil = the game stencil value that marks characters (discovered via /noire3d stencil).
#define MAX_DECAL_ACTORS 64
cbuffer ActorCB : register(b2)
{
    uint   ActorCount;
    uint   CharacterStencil;
    uint2  _actorPad;
    float4 Actors[MAX_DECAL_ACTORS];
};

Texture2D       SceneDepth   : register(t0);
Texture2D       BaseTex      : register(t1);
Texture2D       WorldHeight  : register(t2); // top-down highest collision Y per XZ (ground decals; see WorldHeightRegion)
Texture2D<uint2> SceneStencil : register(t3); // game depth-stencil's STENCIL plane (uint; .g = stencil), marks characters
SamplerState    PointClamp   : register(s0);
SamplerState    BaseSamp     : register(s1);

// The game stencil value under a display uv (0 when the stencil plane is unbound/unavailable, which excludes nothing).
// Integer .Load (stencil is a UINT plane, unfilterable): the display uv maps to the depth-stencil's rendered region via
// DepthUv.xy (actual/allocated) times the texture's allocated size.
uint SceneStencilValue(float2 displayUv)
{
    uint sw, sh;
    SceneStencil.GetDimensions(sw, sh);
    int2 texel = int2(displayUv * DepthUv.xy * float2(sw, sh));
    return SceneStencil.Load(int3(texel, 0)).g;
}

// Highest collision-world Y at a world position's XZ column (WorldHeight sampled through WorldHeightRegion).
// Returns -1e30 when the height-map is unavailable or the point is outside the sampled region (treat as "no ground").
float WorldGroundHeight(float3 wp)
{
    if (WorldHeightRegion.w < 0.5)
        return -1e30;
    float2 uv = (wp.xz - WorldHeightRegion.xy) * WorldHeightRegion.z;
    if (any(uv < 0.0) || any(uv > 1.0))
        return -1e30;
    return WorldHeight.SampleLevel(PointClamp, uv, 0).r;
}

// ---- depth helpers (THE ONLY place depth convention lives) ----------------
// All comparisons happen in clip-w space (w after v·ViewProj - the perspective view depth, world
// units). The game buffer's value convention is NOT assumed: DepthCal (a, b) is fitted at runtime
// from raycast ground truth (sample = a + b/w covers reversed/standard, finite/infinite alike).

// clip-w of the world surface under a display uv; 1e30 = sky / unwritten / calibration off.
float SceneSurfaceW(float2 displayUv)
{
    if (DepthCal.z < 0.5)
        return 1e30;
    float z = SceneDepth.Sample(PointClamp, displayUv * DepthUv.xy).r;
    float denom = z - DepthCal.x;
    // Valid written depth has denom the same sign as b; anything else is the clear value.
    return (denom * DepthCal.y > 1e-12) ? DepthCal.y / denom : 1e30;
}

// 1 = fully visible, 0 = occluded by world. pixelW = the fragment's clip w. fadeWorld <= 0 -> hard test.
float DepthVisibility(float2 displayUv, float pixelW, float fadeWorld)
{
    float sceneW = SceneSurfaceW(displayUv);
    if (fadeWorld <= 0.0)
        return pixelW <= sceneW ? 1.0 : 0.0;              // nearer (smaller view depth) wins
    return saturate((sceneW - pixelW) / fadeWorld + 1.0);
}

float3 WorldFromDepth(float2 displayUv, float sceneDeviceZ)
{
    float2 ndc = float2(displayUv.x * 2.0 - 1.0, 1.0 - displayUv.y * 2.0);
    float4 world = mul(float4(ndc, sceneDeviceZ, 1.0), InvViewProj);
    return world.xyz / world.w;
}

// World position of the scene surface under a display uv (decal reconstruction).
// The game's depth value is converted to OUR projection's device z through the calibrated w,
// so InvViewProj round-trips exactly. valid = false for sky/unwritten pixels.
float3 SceneWorldPos(float2 displayUv, out bool valid)
{
    float w = SceneSurfaceW(displayUv);
    valid = w < 1e29;
    float deviceZ = DepthUv.z + DepthUv.w / max(w, 1e-6);
    return WorldFromDepth(displayUv, deviceZ);
}

// screen-space uv of the current pixel from SV_Position
float2 DisplayUv(float4 svPos) { return svPos.xy * Viewport.zw; }

// anti-aliased SDF coverage (sd <= 0 inside)
float SdfCoverage(float sd) { float aa = fwidth(sd); return saturate(0.5 - sd / max(aa, 1e-6)); }
