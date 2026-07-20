// NoireLib Draw3D - writes a mesh into the GAME's G-buffer so the game's own deferred lighting pass lights it.
//
// This shader does not light anything. It describes a surface in the layout the game's lighting pass expects,
// and that pass then applies every lamp, the sun, ambient, shadow maps, tonemapping and exposure - identically
// to how it treats the wall behind the object, because deferred lighting runs over pixels and cannot tell one
// object's pixels from another's.
//
// The target layout and every value written here were measured, not guessed; see
// docs/Draw3D Game Asset Knowledge Base.md, "Measured: the game's G-buffer layout", for the readback that
// produced them and for how to re-derive them after a patch. The measured values live in Draw3DGameLit and
// arrive through the constant buffer rather than being compiled in, because several of them describe a
// channel whose meaning is still unknown and those are settled by changing them against a live frame.
//
// ViewProj must be the GAME's captured view-projection (CameraConstantCapture), not Draw3D's own. That is what
// puts the geometry in the right pixels AND writes depth in the game's own convention, so the world occludes
// the object correctly.
#include "Common.hlsli"

// ---- the shared ObjectCB slots, as this shader uses them ------------------
// Every value the injection writes that was measured off the game rather than derived arrives here, so it can
// be changed against a live frame instead of recompiled. Draw3DGameLit holds the defaults and documents what
// each one was measured at.
#define MaterialFallback  Params0.xyz   // rtv1 rgb when the material carries no specular map
#define MaterialOverride  Params0.w     // how much MaterialFallback replaces a sampled specular map
#define MiscChannels      Params1       // rtv3 rgba, written verbatim
#define NormalStrength    Params2.x
#define ShadingModelId    Params2.y     // rtv0 alpha, already divided by 255 on the CPU
#define DyeReference      Params2.z     // 0 = the dye multiplies the authored colour instead
#define MaterialCeiling   Params2.w     // rtv1's top of range selects a mode, so the channels are held below it
#define DyeColorStrength  Params3       // rgb = dye colour, w = how strongly to apply it (0 = undyed)
#define AlbedoOverride    OutlineColor  // rgb = flat albedo, a = how much of it replaces the sampled albedo

// The game stores colour maps sRGB-encoded and this renderer uploads them as UNORM, so a sample returns the
// encoded value. The G-buffer's albedo target holds encoded values too, so a texture written straight through
// is already correct - but a dye has to be applied in linear light and re-encoded, because multiplying two
// encoded values is not the same operation and lands on a different colour. Same pair as GameMaterial.hlsl.
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

// The colour map's alpha is a dyeable mask, not coverage: where it is high the texture is authored
// near-neutral so a colour can be applied, and where it is low the texture already carries its final colour
// and must be left alone. Tinting the whole surface would darken the fixed detail that is most of what a
// piece of furniture shows.
float3 ApplyDye(float3 encodedAlbedo, float maskSource)
{
    float mask = saturate(maskSource) * saturate(DyeColorStrength.w);
    if (mask <= 0.0)
        return encodedAlbedo;

    // The colour arrives in LINEAR light and is used as it comes. Which encoding it was in is knowable on the
    // CPU, where the colour's origin is known, and not knowable here: a dye picked from the game's table is
    // display-encoded and a material's diffuse constant is not, and both reach this as three floats in 0..1.
    // Converting here assumed the first and silently darkened the second - 0.78 became 0.57 - which is what
    // made the material's own constant look like the wrong value for years.
    //
    // Kept identical to the scene-pass shader so the two paths cannot disagree about a colour. Reference 0
    // multiplies the authored colour, which is what the game itself does - measured against three of its own
    // stains, each within 0.004 per channel. A reference above 0 divides by it first, so an area authored at
    // that value lands on the dye exactly; that reading is an authoring tool, not a model of the game.
    float3 dyeMul = DyeColorStrength.rgb;
    if (DyeReference > 0.0)
        dyeMul /= max(SrgbToLinear(DyeReference.xxx).r, 1e-4);

    float3 linearAlbedo = SrgbToLinear(encodedAlbedo) * lerp(float3(1.0, 1.0, 1.0), dyeMul, mask);
    return LinearToSrgb(linearAlbedo);
}

// A game normal map stores XY and reconstructs Z; the blue channel carries other data and must not be used
// as the Z component.
float3 NormalizeTangentNormal(float4 sampled)
{
    float2 xy = sampled.xy * 2.0 - 1.0;
    return float3(xy, sqrt(saturate(1.0 - dot(xy, xy))));
}

struct VsIn
{
    float3 pos     : POSITION;
    float3 normal  : NORMAL;
    float2 uv      : TEXCOORD0;
    float4 color   : COLOR0;
    float4 tangent : TANGENT;
};

struct PsIn
{
    float4 svPos        : SV_Position;
    float2 uv           : TEXCOORD0;
    float4 color        : COLOR0;
    float3 worldNormal  : TEXCOORD1;
    float3 worldPos     : TEXCOORD2;
    float4 worldTangent : TEXCOORD3;
};

// The five targets the game binds for its geometry pass, in bind order.
struct GBufferOut
{
    float4 normalId  : SV_Target0;   // rgb = shading normal (world, n*0.5+0.5), a = shading-model id
    float4 material  : SV_Target1;   // packed material scalars, a is always 0
    float4 albedo    : SV_Target2;   // rgb = albedo, a is always 1
    float4 misc      : SV_Target3;   // rg = 0 on the game's furniture, b is occlusion-shaped, a = 1
    float4 geoNormal : SV_Target4;   // rgb = geometric normal (world, no normal-map detail), a ~ 0
};

PsIn vs(VsIn v)
{
    PsIn o;

    float4 wp = mul(float4(v.pos, 1.0), World);
    o.svPos = mul(wp, ViewProj);
    o.uv    = v.uv;
    o.color = v.color * BaseColor;

    // Exact for rotation and uniform scale; non-uniform scale skews the normal, which is the same accepted
    // limitation the rest of the renderer carries.
    o.worldNormal = mul(float4(v.normal, 0.0), World).xyz;
    o.worldPos    = wp.xyz;

    // The handedness rides through untouched: it is a convention, not a direction, and w == 0 is the
    // "no authored frame" signal the pixel shader keys on.
    o.worldTangent = float4(mul(float4(v.tangent.xyz, 0.0), World).xyz, v.tangent.w);
    return o;
}

// The authored tangent frame, used whenever the mesh carries one (tangent w is its handedness, 0 only when
// no frame was imported). This is what matches the game: its shading normal measured about ten degrees off
// ours on strong relief with the derivative frame, and the map's X and Y only mean what the author saw
// inside the frame they were painted for. Kept identical to GameMaterial.hlsl so the injected and ordinary
// paths shade the same relief.
float3 ApplyNormalMapAuthored(float3 n, float4 worldTangent, float3 tangentNormal, float strength)
{
    float3 t = worldTangent.xyz - (n * dot(n, worldTangent.xyz));
    float lenSq = dot(t, t);
    if (lenSq < 1e-8)
        return n;

    t *= rsqrt(lenSq);
    float3 b = cross(n, t) * worldTangent.w;

    float3 m = normalize(float3(tangentNormal.xy * strength, max(tangentNormal.z, 1e-4)));
    return normalize((t * m.x) + (b * m.y) + (n * m.z));
}

// Builds a tangent frame from screen-space derivatives, the fallback for meshes carrying no authored frame.
// Same approach the Draw3D game-material shader uses.
float3 ApplyNormalMap(float3 n, float2 uv, float3 worldPos, float3 tangentNormal, float strength)
{
    float3 dp1 = ddx(worldPos);
    float3 dp2 = ddy(worldPos);
    float2 duv1 = ddx(uv);
    float2 duv2 = ddy(uv);

    float3 dp2perp = cross(dp2, n);
    float3 dp1perp = cross(n, dp1);
    float3 t = dp2perp * duv1.x + dp1perp * duv2.x;
    float3 b = dp2perp * duv1.y + dp1perp * duv2.y;

    float invMax = rsqrt(max(dot(t, t), dot(b, b)) + 1e-8);
    float3 m = normalize(float3(tangentNormal.xy * strength, max(tangentNormal.z, 1e-4)));
    return normalize(t * invMax * m.x + b * invMax * m.y + n * m.z);
}

GBufferOut ps(PsIn i)
{
    float3 albedo = i.color.rgb;
    float3 n = normalize(i.worldNormal);

#ifdef GBUFFER_TEXTURED
    // Alpha is read as data, not coverage: it is the dyeable mask.
    float4 texel = BaseTex.Sample(BaseSamp, i.uv);
    albedo = ApplyDye(albedo * texel.rgb, texel.a);
#endif

#ifdef GBUFFER_MAPS
    // The carvings the game's normal buffer shows come from the material's normal map. Writing only the
    // interpolated vertex normal loses every surface detail the material carries, which reads as a flat
    // gradient where the game shows relief.
    float3 tn = NormalizeTangentNormal(AuxTex0.Sample(BaseSamp, i.uv));
    n = i.worldTangent.w != 0.0
        ? ApplyNormalMapAuthored(n, i.worldTangent, tn, NormalStrength)
        : ApplyNormalMap(n, i.uv, i.worldPos, tn, NormalStrength);
#endif

    // Forces a flat albedo, which is how "the G-buffer is wrong" is told apart from "something downstream
    // never reads it": an object whose albedo is driven to black and which stays bright on screen is not
    // being lit from the albedo it wrote. Alpha 0 (the default) leaves the material's own albedo alone.
    albedo = lerp(albedo, AlbedoOverride.rgb, saturate(AlbedoOverride.a));

    // Normals are world space, encoded n * 0.5 + 0.5. Verified by readback: a floor reads green, one wall
    // magenta, the opposite teal, and a flat floor patch averages (126, 254, 125) = straight up.
    float3 encoded = n * 0.5 + 0.5;

    // The geometric normal is the mesh's own surface, before any map. The game writes both, and they differ
    // exactly where a material has relief.
    float3 encodedGeo = normalize(i.worldNormal) * 0.5 + 0.5;

    GBufferOut o;
    o.normalId = float4(encoded, ShadingModelId);

#ifdef GBUFFER_MAPS
    // rtv1 is per-pixel material data, not a constant: the game's copy shows this material's carvings and
    // rings through it, which is the specular map's content. A constant here flattens the whole material
    // response, which is what the first version did.
    //
    // The blend toward the flat scalars is deliberately available even with a map bound. rtv1 drives the
    // specular response, which is the only lighting term that ignores albedo, and the meaning of a game
    // specular map's channels has been misread more than once - so writing them raw stays an assumption until
    // replacing them is seen to change nothing.
    float3 material = lerp(AuxTex1.Sample(BaseSamp, i.uv).rgb, MaterialFallback, saturate(MaterialOverride));
#else
    // No specular map: fall back to the scalars the caller supplies, sampled from real world geometry.
    float3 material = MaterialFallback;
#endif

    // Held below the ceiling because the top of rtv1's range is a mode, not a value: red at 1.0 turns the
    // reflection green, and a specular map reaches 1.0 in places, which showed up as green blotches across an
    // injected object that the game's own copy of the same model does not have.
    o.material = float4(min(material, MaterialCeiling.xxx), 0.0);

    o.albedo    = float4(albedo, 1.0);
    o.misc      = MiscChannels;
    o.geoNormal = float4(encodedGeo, 0.0);
    return o;
}
