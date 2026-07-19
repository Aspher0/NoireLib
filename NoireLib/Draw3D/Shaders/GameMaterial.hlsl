// NoireLib Draw3D - shading for materials loaded out of the game's archives.
//
// The colour map's alpha channel is a dyeable mask, not coverage. Where it is high the texture is
// authored near-neutral so a colour can be applied to it; where it is low the texture already carries
// its final colour and must be left alone. Tinting the whole surface darkens that fixed detail, which
// on a piece of furniture is most of what you see, and blending on this alpha erases it outright.
// So the surface is drawn opaque and the tint is confined to the masked area.
//
// Unlike the stylized Lit shader, this one is trying to match the game rather than to look good on its
// own, so it works in linear light and spends a fixed light budget instead of summing terms freely.
//
// Params0 : xyz = dye colour applied to the masked area, w = how strongly to apply it (0 = none).
// Params2 : x = normal map strength (0 = geometric normal only), y = specular strength (0 = matte),
//           z = dye reference white (0 = the dye multiplies the authored colour instead),
//           w = 1 to ignore this renderer's lighting entirely (the surface keeps its own colours).
// AuxTex0 = normal map, AuxTex1 = specular/mask map. A strength of 0 means the map was not bound.
#include "Common.hlsli"

// The game stores colour maps sRGB-encoded and this renderer uploads them as UNORM, so a sample returns
// the encoded value rather than a linear one. Multiplying light into an encoded value brightens midtones,
// which is why an asset lit here reads paler than the same asset in game. Lighting therefore happens in
// linear space and the result is re-encoded on the way out, because the layer this shader writes into
// holds encoded values too. At full light the pair is an exact round trip, so an unlit-looking surface
// keeps the texture's own colours.
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

// Tangent frame recovered from screen-space derivatives rather than from the vertex buffer. The game's
// models do carry tangents, but reading them would mean a vertex format that varies per model, and the
// derivative frame is exact enough for normal mapping on a static prop. It costs nothing at rest: with a
// zero-strength normal map the geometric normal is returned untouched.
float3 ApplyNormalMap(float3 geometricNormal, float3 worldPos, float2 uv, float3 tangentNormal, float strength)
{
    float3 n = normalize(geometricNormal);
    if (strength <= 0.0)
        return n;

    float3 dp1 = ddx(worldPos);
    float3 dp2 = ddy(worldPos);
    float2 duv1 = ddx(uv);
    float2 duv2 = ddy(uv);

    // Degenerate uv derivatives (a face with no uv variation across the quad) leave the frame undefined,
    // so the geometric normal stands rather than a normalize() of zero.
    float det = (duv1.x * duv2.y) - (duv2.x * duv1.y);
    if (abs(det) < 1e-12)
        return n;

    float3 t = ((dp1 * duv2.y) - (dp2 * duv1.y)) / det;
    t = normalize(t - (n * dot(n, t)));          // Gram-Schmidt against the interpolated normal
    float3 b = cross(n, t);

    // Strength scales the tangent-space tilt rather than blending toward flat, so it stays meaningful above
    // 1 (an exaggerated surface) instead of clamping there.
    float3 m = normalize(float3(tangentNormal.xy * strength, max(tangentNormal.z, 1e-4)));
    return normalize((t * m.x) + (b * m.y) + (n * m.z));
}

struct VsIn
{
    float3 pos    : POSITION;
    float3 normal : NORMAL;
    float2 uv     : TEXCOORD0;
    float4 color  : COLOR0;
};

struct PsIn
{
    float4 svPos       : SV_Position;
    float2 uv          : TEXCOORD0;
    float4 color       : COLOR0;
    float2 clipZW      : TEXCOORD1;
    float3 worldNormal : TEXCOORD2;
    float3 worldPos    : TEXCOORD3;
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
    return o;
}

float4 ps(PsIn i) : SV_Target
{
    // Opaque surface: an occluded pixel has to be killed, because alpha carries no coverage here.
    float vis = DepthVisibility(DisplayUv(i.svPos), i.clipZW.y, Params1.x);
    if (vis < 0.5)
        discard;

    float4 texel = BaseTex.Sample(BaseSamp, i.uv);

    // Alpha is read as data. The authored values are effectively two-valued, so this recovers the mask
    // while still tolerating filtered edges between the two regions.
    float mask = saturate(texel.a) * saturate(Params0.w);

    float3 albedo = SrgbToLinear(texel.rgb) * SrgbToLinear(i.color.rgb);

    // Two readings of how a dye meets the masked area, because they differ by more than a shade and only a
    // comparison against a known dye in game can decide between them.
    //   reference 0  - the dye multiplies the authored colour, so a light dye darkens a light surface.
    //   reference > 0 - the authored colour is divided by that reference first, so an area authored at the
    //                   reference lands on the dye exactly and the texture only carries relative shading.
    float3 dyeMul = SrgbToLinear(Params0.rgb);
    if (Params2.z > 0.0)
        dyeMul /= max(SrgbToLinear(Params2.zzz).r, 1e-4);

    albedo *= lerp(float3(1.0, 1.0, 1.0), dyeMul, mask);

    // Normal map: red and green carry the tangent-space normal, so z is reconstructed rather than read.
    // The blue channel is left alone here because its meaning varies by shader package.
    float2 nxy = (AuxTex0.Sample(BaseSamp, i.uv).rg * 2.0) - 1.0;
    float3 tangentNormal = float3(nxy, sqrt(saturate(1.0 - dot(nxy, nxy))));
    float3 n = ApplyNormalMap(i.worldNormal, i.worldPos, i.uv, tangentNormal, Params2.x);

    float3 lightDir = normalize(LightDirIntensity.xyz);
    float  ndl = dot(n, lightDir) * 0.5 + 0.5;   // half-Lambert

    // Ambient and directional may not sum past unity: with the default intensities a surface facing the
    // light would otherwise be multiplied by 1.2 and read visibly brighter than the same asset in game.
    // The divisor engages only when the total would exceed one, so turning both intensities down still
    // dims the surface instead of being normalized away.
    float  ambient = Ambient.a;
    float  direct  = LightDirIntensity.w;
    float  budget  = max(ambient + direct, 1.0);
    float3 light   = ((Ambient.rgb * ambient) + (LightColor.rgb * direct * ndl * ndl)) / budget;

    // Params2.w takes this renderer's lighting out of the picture entirely, leaving the surface at the
    // colours the texture and dye give it. That is not the game's lighting, it is the absence of ours:
    // its purpose is to remove one variable while judging the others, since a difference in colour and a
    // difference in light are otherwise impossible to tell apart by eye.
    light = lerp(light, float3(1.0, 1.0, 1.0), saturate(Params2.w));

    float3 shaded = albedo * light;

    // Specular map. The community shader reference names green as roughness and red as a specular mask,
    // and marks the mask channels uncertain; the game leaves these surfaces matte, so this is off unless
    // asked for. Sampled unconditionally because the strength is zero when the map is absent, which makes
    // the term vanish without an unbound slot ever reaching the arithmetic.
    float4 spec = AuxTex1.Sample(BaseSamp, i.uv);
    float  roughness = saturate(spec.g);

    // Roughness, not gloss: a higher value spreads the highlight wider and dims it, rather than tightening
    // it. Reading this channel the other way round is what made the first attempt look lacquered.
    float  gloss = lerp(96.0, 4.0, roughness);
    float  energy = (gloss + 8.0) / 104.0;
    float3 view = normalize(EyePosTime.xyz - i.worldPos);
    float3 halfway = normalize(view + lightDir);
    float  facing = pow(saturate(dot(n, halfway)), gloss);
    shaded += LightColor.rgb * (facing * energy * saturate(spec.r) * max(Params2.y, 0.0) * direct / budget);

    return float4(LinearToSrgb(shaded), 1.0);
}
