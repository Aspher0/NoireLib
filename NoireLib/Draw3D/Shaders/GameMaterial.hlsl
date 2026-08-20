// NoireLib Draw3D: shading for materials loaded out of the game's archives.
//
// The colour map's alpha channel is a dyeable mask, not coverage. Where it is high the texture is authored
// near-neutral and takes a colour; where it is low it already carries its final colour. The surface is therefore
// drawn opaque and the tint is confined to the masked area.
//
// Params0 : xyz = dye colour applied to the masked area, w = how strongly to apply it (0 = none).
// Params2 : x = normal map strength (0 = geometric normal only), y = specular strength (0 = matte),
//           z = dye reference white (0 = the dye multiplies the authored colour instead),
//           w = 1 to ignore this renderer's lighting entirely (the surface keeps its own colours).
// AuxTex0 = normal map, AuxTex1 = specular/mask map. A strength of 0 means the map was not bound.
#include "Common.hlsli"

// Colour maps are sRGB-encoded and uploaded as UNORM, so a sample returns the encoded value. Lighting runs in linear
// space and the result is re-encoded, since the layer this writes into holds encoded values too; at full light the
// pair is an exact round trip.
//
// Normal mapping prefers the authored tangent frame, matching the game, and falls back to the
// screen-space derivative frame when the mesh carries none.

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

    // Handedness rides through untransformed: it is a convention, not a direction, and w == 0 is the
    // "no authored frame" signal the pixel shader keys on.
    o.worldTangent = float4(mul(float4(v.tangent.xyz, 0.0), World).xyz, v.tangent.w);
    return o;
}

float4 ps(PsIn i) : SV_Target
{
    // Alpha carries no coverage here, so an occluded pixel must be discarded rather than blended away.
    float vis = DepthVisibility(DisplayUv(i.svPos), i.clipZW.y, Params1.x);
    if (vis < 0.5)
        discard;

    float4 texel = BaseTex.Sample(BaseSamp, i.uv);

    // The authored alpha is effectively two-valued, so this recovers the mask while tolerating filtered edges.
    float mask = saturate(texel.a) * saturate(Params0.w);

    float3 albedo = SrgbToLinear(texel.rgb) * SrgbToLinear(i.color.rgb);

    // Two readings of how a dye meets the masked area:
    //   reference 0   the dye multiplies the authored colour, as the game does (three stains sampled
    //                 from its own G-buffer land within 0.004 per channel).
    //   reference > 0 the authored colour is divided by that reference first, so an area authored at the reference
    //                 lands on the dye exactly. An authoring aid, not a model of the game.
    // Params0.rgb arrives in LINEAR light and is used as it comes, matching GameGBuffer.hlsl so the injected and
    // ordinary paths cannot land on different colours. Only the CPU knows which encoding a colour came in.
    float3 dyeMul = Params0.rgb;
    if (Params2.z > 0.0)
        dyeMul /= max(SrgbToLinear(Params2.zzz).r, 1e-4);

    albedo *= lerp(float3(1.0, 1.0, 1.0), dyeMul, mask);

    // Red and green carry the tangent-space normal, so z is reconstructed; blue's meaning varies by shader package.
    float2 nxy = (AuxTex0.Sample(BaseSamp, i.uv).rg * 2.0) - 1.0;
    float3 tangentNormal = float3(nxy, sqrt(saturate(1.0 - dot(nxy, nxy))));
    float3 n = i.worldTangent.w != 0.0
        ? ApplyNormalMapAuthored(i.worldNormal, i.worldTangent, tangentNormal, Params2.x)
        : ApplyNormalMap(i.worldNormal, i.worldPos, i.uv, tangentNormal, Params2.x);

    float3 lightDir = normalize(LightDirIntensity.xyz);
    float  ndl = dot(n, lightDir) * 0.5 + 0.5;   // half-Lambert

    // Ambient and directional may not sum past unity, or a lit surface reads brighter than the same asset in game.
    // The divisor engages only above one, so turning both intensities down still dims instead of normalizing away.
    float  ambient = Ambient.a;
    float  direct  = LightDirIntensity.w;
    float  budget  = max(ambient + direct, 1.0);
    float3 light   = ((Ambient.rgb * ambient) + (LightColor.rgb * direct * ndl * ndl)) / budget;

    // Params2.w removes this renderer's lighting entirely, leaving the surface at the colours the texture and dye
    // give it. That is the absence of lighting, not the game's own.
    light = lerp(light, float3(1.0, 1.0, 1.0), saturate(Params2.w));

    float3 shaded = albedo * light;

    // Green is roughness and red a specular mask, per the community shader reference, which marks the mask
    // channels uncertain; the game leaves these surfaces matte, so the term is off unless asked for. Sampled
    // unconditionally, since a zero strength makes it vanish without an unbound slot reaching the arithmetic.
    float4 spec = AuxTex1.Sample(BaseSamp, i.uv);
    float  roughness = saturate(spec.g);

    // Roughness, not gloss: a higher value spreads the highlight wider and dims it rather than tightening it.
    float  gloss = lerp(96.0, 4.0, roughness);
    float  energy = (gloss + 8.0) / 104.0;
    float3 view = normalize(EyePosTime.xyz - i.worldPos);
    float3 halfway = normalize(view + lightDir);
    float  facing = pow(saturate(dot(n, halfway)), gloss);
    shaded += LightColor.rgb * (facing * energy * saturate(spec.r) * max(Params2.y, 0.0) * direct / budget);

    return float4(LinearToSrgb(shaded), 1.0);
}
