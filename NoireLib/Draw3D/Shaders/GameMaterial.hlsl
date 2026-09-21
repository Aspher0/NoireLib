// Shading for materials loaded from the game's archives. The colour map's alpha is a dye mask.
// Params0: xyz = dye colour, w = strength (0 = none).
// Params2: x = normal map strength, y = specular strength, z = dye reference white (0 = multiply), w = 1 to ignore lighting.
// AuxTex0 = normal map, AuxTex1 = specular/mask map. A strength of 0 means the map is not bound.
#include "Common.hlsli"

// Colour maps sample encoded. Lighting runs linear and is re-encoded.

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
    float2 clipZW       : TEXCOORD1;
    float3 worldNormal  : TEXCOORD2;
    float3 worldPos     : TEXCOORD3;
    float4 worldTangent : TEXCOORD4;
};

PsIn vs(VsIn v)
{
    PsIn o;
    float4 wp     = mul(float4(v.pos, 1.0), World);
    o.svPos       = mul(wp, ViewProj);
    o.uv          = v.uv;
    o.color       = v.color * BaseColor;
    o.worldNormal = mul(float4(v.normal, 0.0), World).xyz;
    o.worldPos    = wp.xyz;
    o.clipZW      = o.svPos.zw;

    // w == 0 signals no authored frame.
    o.worldTangent = float4(mul(float4(v.tangent.xyz, 0.0), World).xyz, v.tangent.w);
    return o;
}

float4 ps(PsIn i) : SV_Target
{
    // Alpha carries no coverage. An occluded pixel is discarded.
    float vis = DepthVisibility(DisplayUv(i.svPos), i.clipZW.y, Params1.x);
    if (vis < 0.5)
        discard;

    float4 texel = BaseTex.Sample(BaseSamp, i.uv);

    // The authored alpha is effectively two-valued.
    float mask = saturate(texel.a) * saturate(Params0.w);

    float3 albedo = SrgbToLinear(texel.rgb) * SrgbToLinear(i.color.rgb);

    // Same as GameGBuffer.hlsl. Reference 0 matches the game within 0.004 per channel on three stains.
    float3 dyeMul = Params0.rgb;
    if (Params2.z > 0.0)
        dyeMul /= max(SrgbToLinear(Params2.zzz).r, 1e-4);

    albedo *= lerp(float3(1.0, 1.0, 1.0), dyeMul, mask);

    // Blue's meaning varies by shader package.
    float2 nxy = (AuxTex0.Sample(BaseSamp, i.uv).rg * 2.0) - 1.0;
    float3 tangentNormal = float3(nxy, sqrt(saturate(1.0 - dot(nxy, nxy))));
    float3 n = i.worldTangent.w != 0.0
        ? ApplyNormalMapAuthored(i.worldNormal, i.worldTangent, tangentNormal, Params2.x)
        : ApplyNormalMap(i.worldNormal, i.worldPos, i.uv, tangentNormal, Params2.x);

    float3 lightDir = normalize(LightDirIntensity.xyz);
    float  ndl = dot(n, lightDir) * 0.5 + 0.5;

    // Past one, the surface reads brighter than the same asset in game.
    float  ambient = Ambient.a;
    float  direct  = LightDirIntensity.w;
    float  budget  = max(ambient + direct, 1.0);
    float3 light   = ((Ambient.rgb * ambient) + (LightColor.rgb * direct * ndl * ndl)) / budget;

    light = lerp(light, float3(1.0, 1.0, 1.0), saturate(Params2.w));

    float3 shaded = albedo * light;

    // Green is roughness, red a specular mask (uncertain per the community shader reference).
    float4 spec = AuxTex1.Sample(BaseSamp, i.uv);
    float  roughness = saturate(spec.g);

    // Roughness: a higher value spreads and dims the highlight.
    float  gloss = lerp(96.0, 4.0, roughness);
    float  energy = (gloss + 8.0) / 104.0;
    float3 view = normalize(EyePosTime.xyz - i.worldPos);
    float3 halfway = normalize(view + lightDir);
    float  facing = pow(saturate(dot(n, halfway)), gloss);
    shaded += LightColor.rgb * (facing * energy * saturate(spec.r) * max(Params2.y, 0.0) * direct / budget);

    return float4(LinearToSrgb(shaded), 1.0);
}
