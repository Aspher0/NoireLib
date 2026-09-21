// Temporal resolve of the layer, settling edges the game's jittered depth moves. History reprojects through the layer's depth,
// or the game's depth for decal-only pixels, clamped to the 3 x 3 neighbourhood, and is dropped where the surface changed.
Texture2D    Layer        : register(t0);
Texture2D    History      : register(t1);
Texture2D    LayerDepth   : register(t2); // reversed-Z device z, 0 = nothing drawn
Texture2D    GameDepth    : register(t3); // jittered
Texture2D    PrevGameW    : register(t4); // last frame's game surface clip w per layer pixel (1e30 = none)
SamplerState LinearClamp  : register(s0);

cbuffer ResolveCB : register(b0)
{
    float4x4 InvViewProj;   // this frame, transposed
    float4x4 PrevViewProj;  // last frame, transposed
    float4   Params;        // x = current weight, y = 1 when history is valid, zw = 1 / target size
    float4   GameDepthMap;  // game sample = x + y / w, z = near, w = 1 when valid
    float4   GameDepthUv;   // xy = display uv to game depth uv scale, zw = jitter uv offset
}

struct ResolveOut
{
    float4 colour : SV_Target0;
    float  gameW  : SV_Target1;
};

void vs(uint id : SV_VertexID, out float4 pos : SV_Position, out float2 uv : TEXCOORD0)
{
    uv  = float2((id << 1) & 2, id & 2);
    pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

// 1e30 = sky, unwritten or no game depth.
float GameSurfaceW(float2 uv)
{
    if (GameDepthMap.w < 0.5)
        return 1e30;

    uint gw, gh;
    GameDepth.GetDimensions(gw, gh);
    int2 g = clamp(int2((uv + GameDepthUv.zw) * GameDepthUv.xy * float2(gw, gh)), int2(0, 0), int2(int(gw) - 1, int(gh) - 1));
    float denom = GameDepth.Load(int3(g, 0)).r - GameDepthMap.x;
    return denom * GameDepthMap.y > 1e-12 ? GameDepthMap.y / denom : 1e30;
}

float3 WorldAt(float2 uv, float deviceZ)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(float4(ndc, deviceZ, 1.0), InvViewProj);
    return world.xyz / world.w;
}

// One pixel of tolerance for the jitter.
void PrevGameRange(float2 uv, int2 hi, out float lo, out float up)
{
    int2 c = int2(uv / Params.zw);
    lo = 1e30;
    up = 0.0;
    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    {
        [unroll]
        for (int dx = -1; dx <= 1; dx++)
        {
            float w = PrevGameW.Load(int3(clamp(c + int2(dx, dy), int2(0, 0), hi), 0)).r;
            lo = min(lo, w);
            up = max(up, w);
        }
    }
}

ResolveOut ps(float4 pos : SV_Position, float2 uv : TEXCOORD0)
{
    ResolveOut o;
    int2 p = int2(pos.xy);
    float4 current = Layer.Load(int3(p, 0));
    float gameW = GameSurfaceW(uv);
    o.gameW = gameW;
    o.colour = current;
    if (Params.y < 0.5)
        return o;

    uint w, h;
    Layer.GetDimensions(w, h);
    int2 hi = int2(int(w) - 1, int(h) - 1);

    // Reversed-Z: nearer is larger.
    float4 lo = current;
    float4 up = current;
    float nearest = 0.0;
    int2 nearestAt = p;
    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    {
        [unroll]
        for (int dx = -1; dx <= 1; dx++)
        {
            int2 q = clamp(p + int2(dx, dy), int2(0, 0), hi);
            float4 c = Layer.Load(int3(q, 0));
            lo = min(lo, c);
            up = max(up, c);
            float d = LayerDepth.Load(int3(q, 0)).r;
            if (d > nearest)
            {
                nearest = d;
                nearestAt = q;
            }
        }
    }

    if (up.a <= 0.0)
        return o; // no trail may be left

    // A decal lies on the game's surface. That is its depth.
    bool ours = nearest > 0.0;
    if (!ours)
    {
        if (gameW >= 1e29)
            return o;
        nearest = GameDepthMap.z / gameW;
        nearestAt = p;
    }

    float2 at = (float2(nearestAt) + 0.5) * Params.zw;
    float3 world = WorldAt(at, nearest);
    float4 prevClip = mul(float4(world, 1.0), PrevViewProj);
    if (prevClip.w <= 1e-4)
        return o;

    float2 prevNdc = prevClip.xy / prevClip.w;
    float2 prevUv = float2(prevNdc.x * 0.5 + 0.5, 0.5 - prevNdc.y * 0.5) + (uv - at);
    if (any(prevUv < 0.0) || any(prevUv > 1.0))
        return o;

    // A character stepping over the layer, or anything disoccluded, drops the history.
    if (gameW < 1e29)
    {
        float4 sPrev = mul(float4(WorldAt(uv, GameDepthMap.z / gameW), 1.0), PrevViewProj);
        if (sPrev.w > 1e-4)
        {
            float2 sUv = float2(sPrev.x / sPrev.w * 0.5 + 0.5, 0.5 - sPrev.y / sPrev.w * 0.5);
            if (all(sUv >= 0.0) && all(sUv <= 1.0))
            {
                float sLo, sUp;
                PrevGameRange(sUv, hi, sLo, sUp);
                if (sPrev.w < sLo * 0.98 || sPrev.w > sUp * 1.02)
                    return o;
            }
        }
    }

    // The jitter's hesitating edge stays accumulated. A real crossing of an occluder does not.
    if (ours)
    {
        float wNow = GameDepthMap.z / nearest;
        float cLo = gameW;
        float cUp = gameW;
        [unroll]
        for (int dy2 = -1; dy2 <= 1; dy2++)
        {
            [unroll]
            for (int dx2 = -1; dx2 <= 1; dx2++)
            {
                float n = GameSurfaceW((float2(clamp(p + int2(dx2, dy2), int2(0, 0), hi)) + 0.5) * Params.zw);
                cLo = min(cLo, n);
                cUp = max(cUp, n);
            }
        }

        float pLo, pUp;
        PrevGameRange(prevUv, hi, pLo, pUp);
        int nowState = wNow <= cLo ? 1 : (wNow > cUp ? -1 : 0);
        int prevState = prevClip.w <= pLo ? 1 : (prevClip.w > pUp ? -1 : 0);
        if (nowState * prevState < 0)
            return o;
    }

    float4 history = clamp(History.SampleLevel(LinearClamp, prevUv, 0), lo, up);
    o.colour = lerp(history, current, Params.x);
    return o;
}
