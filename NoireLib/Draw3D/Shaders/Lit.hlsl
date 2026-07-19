// NoireLib Draw3D - half-Lambert lit shader (variants: LIT_TEXTURED, LIT_INSTANCED, OPAQUE_DOMAIN).
// Deliberately not trying to match the game's lighting - a clean stylized look beats an uncanny mismatch.
#include "Common.hlsli"

struct VsIn
{
    float3 pos    : POSITION;
    float3 normal : NORMAL;
    float2 uv     : TEXCOORD0;
    float4 color  : COLOR0;
#ifdef LIT_INSTANCED
    // Instance world rows are UNtransposed (see Unlit.hlsl note).
    float4 i0 : IWORLD0; float4 i1 : IWORLD1; float4 i2 : IWORLD2; float4 i3 : IWORLD3;
    float4 iColor : ICOLOR;
#endif
};

struct PsIn
{
    float4 svPos       : SV_Position;
    float2 uv          : TEXCOORD0;
    float4 color       : COLOR0;
    float2 clipZW      : TEXCOORD1;
    float3 worldNormal : TEXCOORD2;
};

PsIn vs(VsIn v)
{
    PsIn o;
#ifdef LIT_INSTANCED
    float4x4 world = float4x4(v.i0, v.i1, v.i2, v.i3);
    float4 tint = v.iColor;
#else
    float4x4 world = World;
    float4 tint = float4(1, 1, 1, 1);
#endif
    float4 wp = mul(float4(v.pos, 1.0), world);
    o.svPos       = mul(wp, ViewProj);
    o.uv          = v.uv;
    o.color       = v.color * BaseColor * tint;
    // Exact for rotation + uniform scale; non-uniform scale skews lighting (accepted core limitation).
    o.worldNormal = mul(float4(v.normal, 0.0), world).xyz;
    o.clipZW      = o.svPos.zw;
    return o;
}

float4 ps(PsIn i) : SV_Target
{
    float4 c = i.color;
#ifdef LIT_TEXTURED
    c *= BaseTex.Sample(BaseSamp, i.uv);
#endif
    float3 n = normalize(i.worldNormal);
    float ndl = dot(n, normalize(LightDirIntensity.xyz)) * 0.5 + 0.5;   // half-Lambert
    c.rgb = c.rgb * (Ambient.rgb * Ambient.a + LightColor.rgb * (ndl * ndl) * LightDirIntensity.w);

    float vis = DepthVisibility(DisplayUv(i.svPos), i.clipZW.y, Params1.x);
#ifdef OPAQUE_DOMAIN
    if (vis < 0.5) discard;
    return float4(c.rgb, 1.0);
#else
    c.a *= vis;
    return float4(c.rgb * c.a, c.a);                     // premultiplied output: rgb already scaled by alpha
#endif
}
