// NoireLib Draw3D - layer composite: one fullscreen triangle, premultiplied blend onto the target.
// The entire visible output of Draw3D reaches the screen through this pass, without ImGui involvement.
//
// Game-UI-on-top happens HERE, per pixel, and only on the over-everything path (the under-UI path needs none of
// this: it composites before the game draws its UI, so the game paints over the layer by itself). UiBefore and
// UiAfter are two snapshots of the game's present buffer, taken before and after the native UI was drawn into it.
// Wherever they differ, the UI painted something - so their difference is the UI's own shape, letter granularity
// included, with no coverage channel to trust and no rectangle ever cut.
//
// The rects are invisible POLICY regions only (nameplate layering): their factor scales how strongly the UI mask
// applies there, so the visible boundary is always the UI's own pixels, never a rectangle.
Texture2D LayerTex : register(t0);
Texture2D UiBefore : register(t1);
Texture2D UiAfter  : register(t2);
SamplerState PointClamp  : register(s0); // UI-mask difference must be point-exact (bit-identical pixels; see below)
SamplerState LinearClamp : register(s1); // the layer is sampled linearly so an exact-2x supersampled layer box-downsamples here

cbuffer CompositeCB : register(b0)
{
    float4 OpacityProtect;       // x = LayerOpacity, y = ui mask enabled, z = rect count, w = difference gain
    float4 ProtectRects[128];    // nameplate policy rects, display-uv space: xy = min, zw = max
    float4 ProtectFactors[128];  // x = UI visibility inside the rect (1 = UI on top, 0 = layer covers the UI)
}

void vs(uint id : SV_VertexID, out float4 pos : SV_Position, out float2 uv : TEXCOORD0)
{
    uv  = float2((id << 1) & 2, id & 2);                  // (0,0) (2,0) (0,2) - one triangle covers the screen
    pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 ps(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    // UI coverage by difference. Every pixel the UI did not draw on is bit-identical between the two snapshots (they
    // are copies of one texture taken either side of the UI pass), so the test is effectively "did this pixel change
    // at all" - the gain saturates on a single 8-bit step. Anything gentler under-masks: a semi-transparent HUD panel
    // over dark scenery moves the image only a few percent, and a proportional mask would let the layer bleed through
    // it at half strength. A UI pixel that blends to exactly the colour beneath it still reads as no-UI, which is
    // correct - it is invisible either way.
    float ui = 0.0;
    if (OpacityProtect.y > 0.5)
    {
        float3 before = UiBefore.Sample(PointClamp, uv).rgb;
        float3 after  = UiAfter.Sample(PointClamp, uv).rgb;
        ui = saturate(length(after - before) * OpacityProtect.w);
    }

    // f = how much the UI keeps reading on top at this pixel. Default 1 (always on top).
    // Inside policy rects the most UI-protective overlapping rect wins (max), so a HUD window
    // overlapping a "covered" nameplate region still reads on top.
    float f = 1.0;
    int n = (int)OpacityProtect.z;
    bool inAny = false;
    float fMax = 0.0;
    for (int i = 0; i < n; i++)                           // <= 128 - trivially cheap at composite resolution
    {
        float4 r = ProtectRects[i];
        if (all(uv >= r.xy) && all(uv <= r.zw))
        {
            inAny = true;
            fMax = max(fMax, ProtectFactors[i].x);
        }
    }
    if (inAny) f = fMax;

    float k = OpacityProtect.x * (1.0 - f * ui);
    // Linear: at 1x it reads the exact texel (== point); a supersampled layer downsamples here (an exact 2x is a 2x2 box).
    return LayerTex.Sample(LinearClamp, uv) * k;           // scaling a premultiplied color by k stays premultiplied (linear operation)
}
