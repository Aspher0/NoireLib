// NoireLib Draw3D - selection outline composite. Turns the coverage mask (rgb = outline colour, a = coverage of the
// object's FULL silhouette) into a real screen-space silhouette rim, then OCCLUDES that finished rim: each rim pixel
// takes the world-visibility of the nearest silhouette pixel (VisTex), so the outline is drawn around the whole object
// and only the parts whose silhouette is behind a wall are hidden - never an outline around each fragment poking
// through an occluder. Premultiplied output, blended over the scene layer.
Texture2D    MaskTex    : register(t0);   // rgb = outline colour, a = coverage (full silhouette)
Texture2D    VisTex     : register(t1);   // r   = worldVisible at each silhouette pixel (1 = in front of the world)
SamplerState PointClamp : register(s0);

cbuffer OutlineCB : register(b0)
{
    float4 OutlineParams;   // x = width (px), yz = 1/viewport, w unused
};

// Compile-time cap on the dilation radius (px). The runtime width is clamped to this so the kernel stays bounded.
#define OUTLINE_MAX_RADIUS 8

void vs(uint id : SV_VertexID, out float4 pos : SV_Position, out float2 uv : TEXCOORD0)
{
    uv  = float2((id << 1) & 2, id & 2);                  // one triangle covers the screen
    pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 ps(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float  width = clamp(OutlineParams.x, 1.0, (float)OUTLINE_MAX_RADIUS);
    float2 texel = OutlineParams.yz;

    float4 center = MaskTex.SampleLevel(PointClamp, uv, 0);

    // Dilate with a DENSE disk (every whole-pixel offset within `width`), so the rim is gap-free at every silhouette
    // orientation and stays accurate no matter how many objects are outlined. Keep the nearest covered sample.
    float  bestA    = 0.0;
    float3 bestRgb  = 0.0;
    float  bestDist = 1e9;
    float2 bestUv   = uv;
    [loop] for (int dy = -OUTLINE_MAX_RADIUS; dy <= OUTLINE_MAX_RADIUS; dy++)
    {
        [loop] for (int dx = -OUTLINE_MAX_RADIUS; dx <= OUTLINE_MAX_RADIUS; dx++)
        {
            if (dx == 0 && dy == 0)
                continue;
            float dist = sqrt((float)(dx * dx + dy * dy));
            if (dist > width)
                continue;
            float2 suv = uv + float2(dx, dy) * texel;
            float4 s = MaskTex.SampleLevel(PointClamp, suv, 0);
            if (s.a > 0.001 && dist < bestDist)
            {
                bestDist = dist;
                bestA    = s.a;
                bestRgb  = s.rgb;
                bestUv   = suv;
            }
        }
    }

    // Rim = a covered neighbour where this pixel is itself (mostly) uncovered → an outer outline in the neighbour's colour.
    float rim = bestA * saturate(1.0 - center.a);
    // Occlude the finished outline: hide the rim where the nearest silhouette pixel is behind the world (a wall /
    // character). The outline shape itself was computed ignoring occlusion, so it still traces the whole object.
    rim *= VisTex.SampleLevel(PointClamp, bestUv, 0).r;

    return float4(bestRgb * rim, rim);                    // premultiplied over the scene layer
}
