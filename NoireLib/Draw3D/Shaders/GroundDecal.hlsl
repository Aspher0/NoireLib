// Projected decal (unit-box volume, CullFront, depth Disabled, Premultiplied). Variants: DECAL_TEXTURED.
#include "Common.hlsli"

struct VsIn
{
    float3 pos    : POSITION;
    float3 normal : NORMAL;
    float2 uv     : TEXCOORD0;
    float4 color  : COLOR0;
};

float4 vs(VsIn v) : SV_Position
{
    float4 wp = mul(float4(v.pos, 1.0), World);
    return mul(wp, ViewProj);
}

// Ground is never cut.
float CharacterMask(float3 wp, float2 uv)
{
    if (CharacterStencil == 0u || ActorCount == 0u)
        return 0.0;                                       // an unbound stencil reads 0
    if (SceneStencilValue(uv) != CharacterStencil)
        return 0.0;

    for (uint ai = 0; ai < ActorCount; ai++)
    {
        float2 d = wp.xz - Actors[ai].xy;
        if (dot(d, d) < Actors[ai].z * Actors[ai].z)
            return 1.0;
    }
    return 0.0;
}

// Negative inside. Corners stay sharp.
float PolygonDistance(float2 p, float2 v[6])
{
    float d = dot(p - v[0], p - v[0]);
    float s = 1.0;
    for (int i = 0, j = 5; i < 6; j = i, i++)
    {
        float2 e = v[j] - v[i];
        float2 w = p - v[i];
        float2 b = w - e * saturate(dot(w, e) / dot(e, e));
        d = min(d, dot(b, b));
        bool3 c = bool3(p.y >= v[i].y, p.y < v[j].y, e.x * w.y > e.y * w.x);
        if (all(c) || all(!c))
            s = -s;
    }
    return s * sqrt(d);
}

float4 ps(float4 svPos : SV_Position, out float outDepth : SV_Depth) : SV_Target
{
    float2 uv  = DisplayUv(svPos);
    float w = SceneSurfaceW(uv);
    bool hasSurface = w < 1e29;
    // The private-depth GE test lets nearer 3D objects occlude the decal.
    outDepth = hasSurface ? DepthUv.z + DepthUv.w / max(w, 1e-6) : 0.0;

    // A zero return keeps fwidth() defined across the quad.
    if (!hasSurface) return float4(0, 0, 0, 0);
    float3 wp  = WorldFromDepth(uv, outDepth);
    float3 lp  = mul(float4(wp, 1.0), InvWorld).xyz;
    if (any(abs(lp) > 0.5)) return float4(0, 0, 0, 0);

    // The box orientation alone decides floor or wall.
    float2 p = lp.xz * 2.0;                              // edge at |p| = 1
    float vis = 1.0 - smoothstep(0.35, 0.5, abs(lp.y)) * Params1.w; // feather near the box top and bottom

    vis *= 1.0 - CharacterMask(wp, uv);

    // HighestOnly: skip a surface more than DepthCal.w below the column's highest collision.
    if (Params2.x > 0.5 && DepthCal.w > 0.0)
    {
        float groundY = WorldGroundHeight(wp);
        float boxTopY = Params2.y;
        if (groundY > -1e29 && groundY <= boxTopY + DepthCal.w && wp.y < groundY - DepthCal.w)
            return float4(0, 0, 0, 0);
    }

#ifdef DECAL_TEXTURED
    float4 t = BaseTex.Sample(BaseSamp, p * 0.5 + 0.5);
    float4 c0 = t * BaseColor;
    c0.a *= vis;
    return float4(c0.rgb * c0.a, c0.a);
#else
    float sd;
    int kind = (int)Params1.y;
    if      (kind == 0) sd = length(p) - 1.0;                                        // Circle
    else if (kind == 1) sd = max(length(p) - 1.0, Params0.x - length(p));            // Ring (x = inner ratio)
    else if (kind == 2) {                                                            // Sector (x = half angle, y = inner ratio, oriented +Z)
        float r  = length(p);
        float an = abs(atan2(p.x, p.y));                  // 0 at +Z
        sd = max(max(r - 1.0, Params0.y - r), (an - Params0.x) * r);                 // angular edge in arc-length units
    }
    else if (kind == 5) {                                                            // Chevron (x = half stroke ratio, pointing +Z)
        float t  = Params0.x > 0.0 ? Params0.x : 0.22;
        float e  = 1.0 - t;
        float2 a = float2(-e, -0.6 * e);
        float2 b = float2(0.0, 0.6 * e);
        float2 c = float2(e, -0.6 * e);
        float2 up = normalize(b - a);
        float2 down = normalize(c - b);
        float2 ln = float2(-up.y, up.x);
        float2 rn = float2(-down.y, down.x);
        float2 mitre = (ln + rn) * (t / (1.0 + dot(ln, rn)));
        float2 v[6] = { a + ln * t, b + mitre, c + rn * t, c - rn * t, b - mitre, a - ln * t };
        sd = PolygonDistance(p, v);
    }
    else                sd = max(abs(p.x), abs(p.y)) - 1.0;                          // Rect

    float fill    = SdfCoverage(sd);
    // Constant world thickness. Params2.z keeps an immediate shape's rim proportional.
    float footprintScale = 0.5 * (length(World[0].xyz) + length(World[2].xyz));
    float outlineRef = Params2.z > 0.0 ? Params2.z : 1.0;
    float bandW = Params1.z * outlineRef / max(footprintScale, 1e-4);
    float outline = bandW > 0.0
        ? SdfCoverage(abs(sd + bandW * 0.5) - bandW * 0.5)
        : 0.0;

    // OutlineColor alpha 0 = the decal's own colour.
    float4 rim = OutlineColor.a > 0.0 ? OutlineColor : BaseColor;

    float fillA = BaseColor.a * fill * Params0.w;         // Params0.w = fill opacity
    float rimA  = rim.a * outline;
    float top   = max(fillA, rimA);
    float a     = top * vis;
    float3 rgb  = lerp(BaseColor.rgb, rim.rgb, top > 1e-6 ? rimA / top : 0.0);
    return float4(rgb * a, a);
#endif
}
