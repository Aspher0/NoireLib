// NoireLib Draw3D — layer composite: one fullscreen triangle, premultiplied blend onto the backbuffer.
// Law 11 at the pixel level: the entire visible output of Draw3D reaches the screen without ImGui.
//
// Game-UI-on-top happens HERE, per pixel: UiTex is a copy of the finished game frame, whose alpha
// channel holds the accumulated native-UI coverage. Multiplying the layer by (1 - uiAlpha) puts every
// UI pixel — nameplate letters, window drop shadows, chat transparency — visually on top of the layer
// at letter granularity. The rects are invisible POLICY regions only (depth-aware nameplates): their
// factor scales how strongly the UI mask applies there, so the visible boundary is always the UI's own
// pixel shape, never a rectangle.
Texture2D LayerTex : register(t0);
Texture2D UiTex    : register(t1);
SamplerState PointClamp : register(s0);

cbuffer CompositeCB : register(b0)
{
    float4 OpacityProtect;       // x = LayerOpacity, y = ui mask enabled, z = rect count
    float4 ProtectRects[128];    // nameplate policy rects, display-uv space: xy = min, zw = max
    float4 ProtectFactors[128];  // x = UI visibility inside the rect (1 = UI on top, 0 = layer covers the UI)
}

void vs(uint id : SV_VertexID, out float4 pos : SV_Position, out float2 uv : TEXCOORD0)
{
    uv  = float2((id << 1) & 2, id & 2);                  // (0,0) (2,0) (0,2) — one triangle covers the screen
    pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 ps(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float ui = OpacityProtect.y > 0.5 ? saturate(UiTex.Sample(PointClamp, uv).a) : 0.0;

    // f = how much the UI keeps reading on top at this pixel. Default 1 (always on top).
    // Inside policy rects the most UI-protective overlapping rect wins (max), so a HUD window
    // overlapping a "covered" nameplate region still reads on top.
    float f = 1.0;
    int n = (int)OpacityProtect.z;
    bool inAny = false;
    float fMax = 0.0;
    for (int i = 0; i < n; i++)                           // <= 128 — trivially cheap at composite resolution
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
    return LayerTex.Sample(PointClamp, uv) * k;           // premultiplied x scalar is linear — Law 4
}
