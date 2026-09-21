// Metallic-roughness shading for glTF materials: one directional light plus ambient, in linear light.
// BaseColor = base color factor (linear). BaseTex = base color texture (sRGB). AuxTex0 = normal map.
// AuxTex1 = ORM texture (r = occlusion, g = roughness, b = metallic).
// Params0: x = metallic, y = roughness, z = normal-map strength, w = ORM mode (0 = none, 1 = no occlusion, 2 = with occlusion).
// Params1: x = DepthFade in world units.
// Params2: xyz = emissive factor (linear), w = -1 for blend, else the mask cutoff.
#include "Common.hlsli"

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
    o.color       = v.color * BaseColor;   // both linear per the glTF spec
    o.worldNormal = mul(float4(v.normal, 0.0), World).xyz;
    o.worldPos    = wp.xyz;
    o.clipZW      = o.svPos.zw;
    o.worldTangent = float4(mul(float4(v.tangent.xyz, 0.0), World).xyz, v.tangent.w);
    return o;
}

float4 ps(PsIn i) : SV_Target
{
    float alphaCtl = Params2.w;
    float blendMode = alphaCtl < 0.0 ? 1.0 : 0.0;

    float vis = DepthVisibility(DisplayUv(i.svPos), i.clipZW.y, Params1.x);
    if (blendMode < 0.5 && vis < 0.5)
        discard;                            // occlusion kills the pixel

    float4 texel = BaseTex.Sample(BaseSamp, i.uv);
    float alpha = texel.a * i.color.a;
    if (blendMode < 0.5 && alphaCtl > 0.0 && alpha < alphaCtl)
        discard;                            // MASK cutoff

    float3 albedo = SrgbToLinear(texel.rgb) * i.color.rgb;

    // Factors multiply the sampled channels when the texture is bound.
    float metallic = saturate(Params0.x);
    float roughness = saturate(Params0.y);
    float ao = 1.0;
    if (Params0.w > 0.5)
    {
        float4 orm = AuxTex1.Sample(BaseSamp, i.uv);
        roughness = saturate(roughness * orm.g);
        metallic = saturate(metallic * orm.b);
        if (Params0.w > 1.5)
            ao = saturate(orm.r);
    }

    float2 nxy = (AuxTex0.Sample(BaseSamp, i.uv).rg * 2.0) - 1.0;
    float3 tangentNormal = float3(nxy, sqrt(saturate(1.0 - dot(nxy, nxy))));
    float3 n = i.worldTangent.w != 0.0
        ? ApplyNormalMapAuthored(i.worldNormal, i.worldTangent, tangentNormal, Params0.z)
        : ApplyNormalMap(i.worldNormal, i.worldPos, i.uv, tangentNormal, Params0.z);

    // Metal has no diffuse and tints its specular with the albedo.
    float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 diffuseAlbedo = albedo * (1.0 - metallic);

    float3 lightDir = normalize(LightDirIntensity.xyz);
    float3 view = normalize(EyePosTime.xyz - i.worldPos);
    float3 halfway = normalize(view + lightDir);

    // Ambient plus directional may not sum past one.
    float ambient = Ambient.a;
    float direct = LightDirIntensity.w;
    float budget = max(ambient + direct, 1.0);

    float ndl = saturate(dot(n, lightDir));
    float3 diffuse = diffuseAlbedo * ((Ambient.rgb * ambient * ao) + (LightColor.rgb * direct * ndl)) / budget;

    // Blinn-Phong with GameMaterial.hlsl's energy convention.
    float gloss = lerp(96.0, 4.0, roughness);
    float energy = (gloss + 8.0) / 104.0;
    float facing = pow(saturate(dot(n, halfway)), gloss);
    float3 fresnel = f0 + ((1.0 - f0) * pow(1.0 - saturate(dot(view, n)), 5.0));
    float3 specular = LightColor.rgb * (facing * energy * direct / budget) * fresnel;

    // Stands in for an environment map.
    float3 ambientSpec = Ambient.rgb * (ambient / budget) * fresnel * (1.0 - roughness) * ao;

    float3 shaded = diffuse + specular + ambientSpec + Params2.xyz;

    if (blendMode < 0.5)
        return float4(LinearToSrgb(shaded), 1.0);

    float a = saturate(alpha) * vis;
    return float4(LinearToSrgb(shaded) * a, a);
}
