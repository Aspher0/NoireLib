// Constant buffer layouts every Draw3D shader binds. Matrices are pre-transposed on the CPU. Use mul(v, M) only.

// b0: per frame
cbuffer FrameCB : register(b0)
{
    float4x4 ViewProj;          // transposed on CPU
    float4x4 InvViewProj;       // transposed on CPU
    float4   EyePosTime;        // xyz = camera origin, w = time in seconds
    float4   Viewport;          // xy = display size px, zw = 1/display size
    float4   DepthUv;           // xy = depth uv scale, zw = this projection's z map: deviceZ = z + w / clipW
    float4   DepthCal;          // game depth sample = x + y / clipW, z = 1 when valid
    float4   Ambient;           // rgb = ambient color, a = intensity (Lit)
    float4   LightDirIntensity; // xyz = normalized direction toward the light, w = intensity (Lit)
    float4   LightColor;        // rgb = directional color
    float4   WorldHeightRegion; // xy = region min XZ, z = 1/regionSize, w = 1 when the height map is valid
    float4   DepthJitter;       // xy = display-uv offset of this pixel's world ray in the game's jittered depth
};

// b1: per object / material
cbuffer ObjectCB : register(b1)
{
    float4x4 World;             // transposed on CPU
    float4x4 InvWorld;          // transposed on CPU, decals: world to unit-box local
    float4   BaseColor;         // straight alpha, premultiplied in the PS
    float4   Params0;           // shape or material params
    float4   Params1;           // x = DepthFade (world units, 0 = hard), y = shapeKind,
                                //     z = outlineWidth (footprint SDF units, divided by the box world scale), w = heightFade
    float4   Params2;           // x = ground-decal projection mode (0 = all surfaces, 1 = highest only)
                                //     y = decal box top world Y, z = outline reference footprint scale (0 = constant world thickness)
    float4   OutlineColor;      // ground-decal rim colour, straight alpha, alpha 0 = BaseColor
    float4   Params3;           // spare (G-buffer injection: dye colour in rgb, strength in w)
}

// b2: per-decal actor cylinders (xy = world XZ centre, z = radius). The exact cut is the game stencil silhouette.
// CharacterStencil = the game stencil value marking characters.
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
Texture2D       WorldHeight  : register(t2); // highest collision Y per XZ, ground decals only
Texture2D<uint2> SceneStencil : register(t3); // game stencil plane (.g), marks characters
Texture2D       AuxTex0      : register(t4); // custom pipelines: second texture (game materials: normal map)
Texture2D       AuxTex1      : register(t5); // custom pipelines: third texture (game materials: specular map)
SamplerState    PointClamp   : register(s0);
SamplerState    BaseSamp     : register(s1);

// 0 when the plane is unbound. The UINT plane is unfilterable. Read jitter-aligned like the depth.
uint SceneStencilValue(float2 displayUv)
{
    uint sw, sh;
    SceneStencil.GetDimensions(sw, sh);
    int2 texel = int2((displayUv + DepthJitter.xy) * DepthUv.xy * float2(sw, sh));
    return SceneStencil.Load(int3(texel, 0)).g;
}

// -1e30 when unavailable or outside the region.
float WorldGroundHeight(float3 wp)
{
    if (WorldHeightRegion.w < 0.5)
        return -1e30;
    float2 uv = (wp.xz - WorldHeightRegion.xy) * WorldHeightRegion.z;
    if (any(uv < 0.0) || any(uv > 1.0))
        return -1e30;
    return WorldHeight.SampleLevel(PointClamp, uv, 0).r;
}

// Depth helpers. Comparisons happen in clip-w. sample = a + b/w covers reversed and standard, finite and infinite depth.

// 1e30 = the clear value.
float SceneWFromRaw(float z)
{
    float denom = z - DepthCal.x;
    // Valid written depth has denom the same sign as b.
    return (denom * DepthCal.y > 1e-12) ? DepthCal.y / denom : 1e30;
}

// The game's depth carries the jitter the layer's camera removed.
void GatherSceneDepth(float2 displayUv, out float z00, out float z10, out float z01, out float z11, out float2 f)
{
    uint dw, dh;
    SceneDepth.GetDimensions(dw, dh);
    float2 t = (displayUv + DepthJitter.xy) * DepthUv.xy * float2(dw, dh) - 0.5;
    int2 i0 = int2(floor(t));
    f = t - float2(i0);
    int2 hi = int2(int(dw) - 1, int(dh) - 1);
    z00 = SceneDepth.Load(int3(clamp(i0, int2(0, 0), hi), 0)).r;
    z10 = SceneDepth.Load(int3(clamp(i0 + int2(1, 0), int2(0, 0), hi), 0)).r;
    z01 = SceneDepth.Load(int3(clamp(i0 + int2(0, 1), int2(0, 0), hi), 0)).r;
    z11 = SceneDepth.Load(int3(clamp(i0 + int2(1, 1), int2(0, 0), hi), 0)).r;
}

bool IsContinuousSurface(float w00, float w10, float w01, float w11)
{
    float wMin = min(min(w00, w10), min(w01, w11));
    float wMax = max(max(w00, w10), max(w01, w11));
    return wMax < 1e29 && wMax - wMin <= 0.03 * wMin;
}

// 1e30 = sky, unwritten, or depth off. Raw depth is interpolated on a continuous surface, nearest texel across an edge.
float SceneSurfaceW(float2 displayUv)
{
    if (DepthCal.z < 0.5)
        return 1e30;

    float z00, z10, z01, z11;
    float2 f;
    GatherSceneDepth(displayUv, z00, z10, z01, z11, f);
    if (IsContinuousSurface(SceneWFromRaw(z00), SceneWFromRaw(z10), SceneWFromRaw(z01), SceneWFromRaw(z11)))
        return SceneWFromRaw(lerp(lerp(z00, z10, f.x), lerp(z01, z11, f.x), f.y));

    float2 r = round(f);
    return SceneWFromRaw(lerp(lerp(z00, z10, r.x), lerp(z01, z11, r.x), r.y));
}

float DepthTestOne(float pixelW, float sceneW, float fadeWorld)
{
    if (fadeWorld <= 0.0)
        return pixelW <= sceneW ? 1.0 : 0.0;              // the smaller view depth wins
    return saturate((sceneW - pixelW) / fadeWorld + 1.0);
}

// 1 = visible, 0 = occluded. fadeWorld <= 0 makes the test hard. Across an object's edge the four verdicts are blended.
float DepthVisibility(float2 displayUv, float pixelW, float fadeWorld)
{
    if (DepthCal.z < 0.5)
        return 1.0;

    float z00, z10, z01, z11;
    float2 f;
    GatherSceneDepth(displayUv, z00, z10, z01, z11, f);
    float w00 = SceneWFromRaw(z00);
    float w10 = SceneWFromRaw(z10);
    float w01 = SceneWFromRaw(z01);
    float w11 = SceneWFromRaw(z11);

    if (IsContinuousSurface(w00, w10, w01, w11))
    {
        float z = lerp(lerp(z00, z10, f.x), lerp(z01, z11, f.x), f.y);
        return DepthTestOne(pixelW, SceneWFromRaw(z), fadeWorld);
    }

    float v00 = DepthTestOne(pixelW, w00, fadeWorld);
    float v10 = DepthTestOne(pixelW, w10, fadeWorld);
    float v01 = DepthTestOne(pixelW, w01, fadeWorld);
    float v11 = DepthTestOne(pixelW, w11, fadeWorld);
    return lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
}

float3 WorldFromDepth(float2 displayUv, float sceneDeviceZ)
{
    float2 ndc = float2(displayUv.x * 2.0 - 1.0, 1.0 - displayUv.y * 2.0);
    float4 world = mul(float4(ndc, sceneDeviceZ, 1.0), InvViewProj);
    return world.xyz / world.w;
}

// Through this projection's device z so InvViewProj round-trips exactly. valid is false for sky and unwritten pixels.
float3 SceneWorldPos(float2 displayUv, out bool valid)
{
    float w = SceneSurfaceW(displayUv);
    valid = w < 1e29;
    float deviceZ = DepthUv.z + DepthUv.w / max(w, 1e-6);
    return WorldFromDepth(displayUv, deviceZ);
}

float2 DisplayUv(float4 svPos) { return svPos.xy * Viewport.zw; }

// sd <= 0 inside.
float SdfCoverage(float sd) { float aa = fwidth(sd); return saturate(0.5 - sd / max(aa, 1e-6)); }

// sRGB textures are uploaded UNORM. Linear-lit shaders decode on the way in and re-encode on the way out.
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

// Tangent w is the handedness, 0 when absent.
float3 ApplyNormalMapAuthored(float3 geometricNormal, float4 worldTangent, float3 tangentNormal, float strength)
{
    float3 n = normalize(geometricNormal);
    if (strength <= 0.0)
        return n;

    // A tangent collapsed onto the normal leaves the surface normal.
    float3 t = worldTangent.xyz - (n * dot(n, worldTangent.xyz));
    float lenSq = dot(t, t);
    if (lenSq < 1e-8)
        return n;

    t *= rsqrt(lenSq);
    float3 b = cross(n, t) * worldTangent.w;

    float3 m = normalize(float3(tangentNormal.xy * strength, max(tangentNormal.z, 1e-4)));
    return normalize((t * m.x) + (b * m.y) + (n * m.z));
}

// The fallback for meshes with no authored frame. Several degrees off where relief is strong.
float3 ApplyNormalMap(float3 geometricNormal, float3 worldPos, float2 uv, float3 tangentNormal, float strength)
{
    float3 n = normalize(geometricNormal);
    if (strength <= 0.0)
        return n;

    float3 dp1 = ddx(worldPos);
    float3 dp2 = ddy(worldPos);
    float2 duv1 = ddx(uv);
    float2 duv2 = ddy(uv);

    // No uv variation across the quad leaves the frame undefined.
    float det = (duv1.x * duv2.y) - (duv2.x * duv1.y);
    if (abs(det) < 1e-12)
        return n;

    float3 t = ((dp1 * duv2.y) - (dp2 * duv1.y)) / det;
    t = normalize(t - (n * dot(n, t)));          // Gram-Schmidt against the interpolated normal
    float3 b = cross(n, t);

    // Above 1 exaggerates the surface.
    float3 m = normalize(float3(tangentNormal.xy * strength, max(tangentNormal.z, 1e-4)));
    return normalize((t * m.x) + (b * m.y) + (n * m.z));
}
