// Writes a mesh into the game's G-buffer in the measured layout its lighting pass expects. ViewProj is the game's own.
#include "Common.hlsli"

// Defaults live in Draw3DGameLit.
#define MaterialFallback  Params0.xyz   // rtv1 rgb when the material carries no specular map
#define MaterialOverride  Params0.w     // how much MaterialFallback replaces a sampled specular map
#define MiscChannels      Params1       // rtv3 rgba, written verbatim
#define NormalStrength    Params2.x
#define ShadingModelId    Params2.y     // rtv0 alpha, divided by 255 on the CPU
#define DyeReference      Params2.z     // 0 = the dye multiplies the authored colour
#define MaterialCeiling   Params2.w     // rtv1's top of range selects a mode
#define DyeColorStrength  Params3       // rgb = dye colour, w = strength (0 = undyed)
#define AlbedoOverride    OutlineColor  // rgb = flat albedo, a = how much it replaces the sampled albedo

// The albedo target holds encoded values. Only a dye goes through linear light. The colour map's alpha is a dye mask.
float3 ApplyDye(float3 encodedAlbedo, float maskSource)
{
    float mask = saturate(maskSource) * saturate(DyeColorStrength.w);
    if (mask <= 0.0)
        return encodedAlbedo;

    // Reference 0 multiplies the authored colour like the game. Above 0 it divides by the reference first.
    float3 dyeMul = DyeColorStrength.rgb;
    if (DyeReference > 0.0)
        dyeMul /= max(SrgbToLinear(DyeReference.xxx).r, 1e-4);

    float3 linearAlbedo = SrgbToLinear(encodedAlbedo) * lerp(float3(1.0, 1.0, 1.0), dyeMul, mask);
    return LinearToSrgb(linearAlbedo);
}

// A game normal map stores XY. Blue carries other data.
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

// The five targets of the game's geometry pass, in bind order.
struct GBufferOut
{
    float4 normalId  : SV_Target0;   // rgb = shading normal (world, n*0.5+0.5), a = shading-model id
    float4 material  : SV_Target1;   // packed material scalars, a = 0
    float4 albedo    : SV_Target2;   // rgb = albedo, a = 1
    float4 misc      : SV_Target3;   // rg = 0 on the game's furniture, b is occlusion-shaped, a = 1
    float4 geoNormal : SV_Target4;   // rgb = geometric normal, a ~ 0
};

PsIn vs(VsIn v)
{
    PsIn o;

    float4 wp = mul(float4(v.pos, 1.0), World);
    o.svPos = mul(wp, ViewProj);
    o.uv    = v.uv;
    o.color = v.color * BaseColor;

    // Exact for rotation and uniform scale only.
    o.worldNormal = mul(float4(v.normal, 0.0), World).xyz;
    o.worldPos    = wp.xyz;

    // w == 0 signals no authored frame.
    o.worldTangent = float4(mul(float4(v.tangent.xyz, 0.0), World).xyz, v.tangent.w);
    return o;
}

// Fallback for meshes with no authored frame. About ten degrees off the game's normal on strong relief.
float3 ApplyNormalMapCotangent(float3 n, float2 uv, float3 worldPos, float3 tangentNormal, float strength)
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
    float4 texel = BaseTex.Sample(BaseSamp, i.uv);
    albedo = ApplyDye(albedo * texel.rgb, texel.a);
#endif

#ifdef GBUFFER_MAPS
    float3 tn = NormalizeTangentNormal(AuxTex0.Sample(BaseSamp, i.uv));
    n = i.worldTangent.w != 0.0
        ? ApplyNormalMapAuthored(n, i.worldTangent, tn, NormalStrength)
        : ApplyNormalMapCotangent(n, i.uv, i.worldPos, tn, NormalStrength);
#endif

    albedo = lerp(albedo, AlbedoOverride.rgb, saturate(AlbedoOverride.a));

    // A flat floor reads back (126, 254, 125).
    float3 encoded = n * 0.5 + 0.5;

    // The game writes both normals.
    float3 encodedGeo = normalize(i.worldNormal) * 0.5 + 0.5;

    GBufferOut o;
    o.normalId = float4(encoded, ShadingModelId);

#ifdef GBUFFER_MAPS
    float3 material = lerp(AuxTex1.Sample(BaseSamp, i.uv).rgb, MaterialFallback, saturate(MaterialOverride));
#else
    float3 material = MaterialFallback;
#endif

    // Red at 1.0 turns the reflection green.
    o.material = float4(min(material, MaterialCeiling.xxx), 0.0);

    o.albedo    = float4(albedo, 1.0);

    // rtv3 blue: baked vertex occlusion times a sky visibility.
    float4 misc = MiscChannels;
    misc.z = saturate(misc.z * i.color.a);

    o.misc      = misc;
    o.geoNormal = float4(encodedGeo, 0.0);
    return o;
}
