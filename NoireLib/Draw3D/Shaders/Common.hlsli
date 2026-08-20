// Constant buffer layouts every Draw3D shader binds.
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
                                // z = outlineWidth (SDF units of the unit footprint; the decal PS divides it by the
                                //     box world scale so the rim is a constant world thickness), w = heightFade (decal Y feather)
    float4   Params2;           // x = ground-decal projection mode (0 = all surfaces, 1 = highest only)
                                // y = decal box top world Y (the height-map's vertical search bound)
                                // z = outline reference footprint scale (0 = constant world thickness under any box scale;
                                //     immediate shapes pass their built footprint scale for a proportional rim)
    float4   OutlineColor;      // ground-decal rim colour, straight alpha; alpha 0 means unset and the rim uses BaseColor
    float4   Params3;           // spare per-shader slot (G-buffer injection: dye colour in rgb, dye strength in w)
}

// ---- b2: per-decal excluded-actor gate + stencil key (ground-decal ExcludeObjects) --
// The actors this decal skips painting on, uploaded per decal draw. Each is a vertical cylinder used only as a coarse
// gate; the exact cut is the game stencil silhouette, so a generous radius never holes the ground.
// xy = world XZ centre, z = radius, w unused. ActorCount 0 excludes nothing.
// CharacterStencil = the game stencil value that marks characters.
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
Texture2D       AuxTex0      : register(t4); // custom pipelines only: second material texture (game materials: normal map)
Texture2D       AuxTex1      : register(t5); // custom pipelines only: third material texture (game materials: specular map)
SamplerState    PointClamp   : register(s0);
SamplerState    BaseSamp     : register(s1);

// The game stencil value under a display uv, 0 when the stencil plane is unbound and so excludes nothing.
// Loaded as an integer because the stencil plane is UINT and unfilterable; DepthUv.xy maps the display uv onto the
// depth-stencil's rendered region within its allocated size.
uint SceneStencilValue(float2 displayUv)
{
    uint sw, sh;
    SceneStencil.GetDimensions(sw, sh);
    int2 texel = int2(displayUv * DepthUv.xy * float2(sw, sh));
    return SceneStencil.Load(int3(texel, 0)).g;
}

// Highest collision-world Y at a world position's XZ column.
// Returns -1e30 for no ground: the height-map is unavailable, or the point is outside the sampled region.
float WorldGroundHeight(float3 wp)
{
    if (WorldHeightRegion.w < 0.5)
        return -1e30;
    float2 uv = (wp.xz - WorldHeightRegion.xy) * WorldHeightRegion.z;
    if (any(uv < 0.0) || any(uv > 1.0))
        return -1e30;
    return WorldHeight.SampleLevel(PointClamp, uv, 0).r;
}

// ---- depth helpers (the only place the depth convention lives) ------------
// All comparisons happen in clip-w space, the perspective view depth in world units. The game buffer's value
// convention is not assumed: DepthCal (a, b) is fitted at runtime from raycast ground truth, and sample = a + b/w
// covers reversed and standard, finite and infinite alike.

// clip-w of the world surface under a display uv; 1e30 = sky, unwritten, or calibration off.
float SceneSurfaceW(float2 displayUv)
{
    if (DepthCal.z < 0.5)
        return 1e30;
    float z = SceneDepth.Sample(PointClamp, displayUv * DepthUv.xy).r;
    float denom = z - DepthCal.x;
    // Valid written depth has denom the same sign as b; anything else is the clear value.
    return (denom * DepthCal.y > 1e-12) ? DepthCal.y / denom : 1e30;
}

// 1 = fully visible, 0 = occluded by world. pixelW is the fragment's clip w; fadeWorld <= 0 makes the test hard.
float DepthVisibility(float2 displayUv, float pixelW, float fadeWorld)
{
    float sceneW = SceneSurfaceW(displayUv);
    if (fadeWorld <= 0.0)
        return pixelW <= sceneW ? 1.0 : 0.0;              // the smaller view depth wins
    return saturate((sceneW - pixelW) / fadeWorld + 1.0);
}

float3 WorldFromDepth(float2 displayUv, float sceneDeviceZ)
{
    float2 ndc = float2(displayUv.x * 2.0 - 1.0, 1.0 - displayUv.y * 2.0);
    float4 world = mul(float4(ndc, sceneDeviceZ, 1.0), InvViewProj);
    return world.xyz / world.w;
}

// World position of the scene surface under a display uv, for decal reconstruction.
// The game's depth value is converted to this projection's device z through the calibrated w, so InvViewProj
// round-trips exactly. valid is false for sky and unwritten pixels.
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

// ---- texture-space shading helpers (game materials, glTF PBR) ----------------
// Textures authored sRGB are uploaded UNORM, so a sample returns the encoded value; shaders that light in linear
// space decode on the way in and re-encode on the way out, since the layer holds encoded values.
float3 SrgbToLinear(float3 c)
{
    c = saturate(c);
    return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

float3 LinearToSrgb(float3 c)
{
    c = saturate(c);
    return c <= 0.0031308 ? c * 12.92 : (1.055 * pow(c, 1.0 / 2.4)) - 0.055;
}

// The authored tangent frame, used whenever the mesh carries one; tangent w is its handedness and is 0 only when no
// frame was imported. A normal map's X and Y only mean anything inside the frame they were painted for, so this wins
// over the derivative fallback below.
float3 ApplyNormalMapAuthored(float3 geometricNormal, float4 worldTangent, float3 tangentNormal, float strength)
{
    float3 n = normalize(geometricNormal);
    if (strength <= 0.0)
        return n;

    // Gram-Schmidt keeps the frame orthogonal after interpolation; a tangent that collapsed onto the normal leaves
    // the surface normal standing rather than a normalize() of zero.
    float3 t = worldTangent.xyz - (n * dot(n, worldTangent.xyz));
    float lenSq = dot(t, t);
    if (lenSq < 1e-8)
        return n;

    t *= rsqrt(lenSq);
    float3 b = cross(n, t) * worldTangent.w;

    float3 m = normalize(float3(tangentNormal.xy * strength, max(tangentNormal.z, 1e-4)));
    return normalize((t * m.x) + (b * m.y) + (n * m.z));
}

// Tangent frame recovered from screen-space derivatives, the fallback for meshes carrying no authored frame. It
// reconstructs the frame from how the UVs land on screen, which runs several degrees off the authored frame where
// relief is strong.
float3 ApplyNormalMap(float3 geometricNormal, float3 worldPos, float2 uv, float3 tangentNormal, float strength)
{
    float3 n = normalize(geometricNormal);
    if (strength <= 0.0)
        return n;

    float3 dp1 = ddx(worldPos);
    float3 dp2 = ddy(worldPos);
    float2 duv1 = ddx(uv);
    float2 duv2 = ddy(uv);

    // A face with no uv variation across the quad leaves the frame undefined, so the geometric normal stands
    // rather than a normalize() of zero.
    float det = (duv1.x * duv2.y) - (duv2.x * duv1.y);
    if (abs(det) < 1e-12)
        return n;

    float3 t = ((dp1 * duv2.y) - (dp2 * duv1.y)) / det;
    t = normalize(t - (n * dot(n, t)));          // Gram-Schmidt against the interpolated normal
    float3 b = cross(n, t);

    // Strength scales the tangent-space tilt rather than blending toward flat, so values above 1 exaggerate the
    // surface instead of clamping.
    float3 m = normalize(float3(tangentNormal.xy * strength, max(tangentNormal.z, 1e-4)));
    return normalize((t * m.x) + (b * m.y) + (n * m.z));
}
