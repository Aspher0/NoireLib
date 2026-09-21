// Layer composite: one fullscreen triangle, premultiplied. Over everything, UiBefore and UiAfter differ where the native UI drew.
Texture2D LayerTex : register(t0);
Texture2D UiBefore : register(t1);
Texture2D UiAfter  : register(t2);
SamplerState PointClamp  : register(s0); // the UI-mask difference compares bit-identical pixels
SamplerState LinearClamp : register(s1); // box-downsamples an exact-2x supersampled layer

cbuffer CompositeCB : register(b0)
{
    float4 OpacityProtect;       // x = LayerOpacity, y = ui mask enabled, z = rect count, w = difference gain
    float4 ProtectRects[128];    // nameplate policy rects, display uv: xy = min, zw = max
    float4 ProtectFactors[128];  // x = UI visibility inside the rect (1 = UI on top)
}

void vs(uint id : SV_VertexID, out float4 pos : SV_Position, out float2 uv : TEXCOORD0)
{
    uv  = float2((id << 1) & 2, id & 2);                  // one triangle covers the screen
    pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 ps(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    // The gain saturates on a single 8-bit step. A gentler one bleeds through semi-transparent HUD panels.
    float ui = 0.0;
    if (OpacityProtect.y > 0.5)
    {
        float3 before = UiBefore.Sample(PointClamp, uv).rgb;
        float3 after  = UiAfter.Sample(PointClamp, uv).rgb;
        ui = saturate(length(after - before) * OpacityProtect.w);
    }

    // The most UI-protective overlapping rect wins.
    float f = 1.0;
    int n = (int)OpacityProtect.z;
    bool inAny = false;
    float fMax = 0.0;
    for (int i = 0; i < n; i++)
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
    // At 1x a linear sample reads the exact texel.
    return LayerTex.Sample(LinearClamp, uv) * k;
}
