// Depth-only pass into the game's shadow maps. The light transform is copied on the GPU from g_CameraParameter and applied row by row, like the game's dp4s.

// Rows 0..2: m_ViewMatrix (3x4). Rows 3..6: m_ProjectionMatrix (4x4), or the near-field map's whole world-to-clip.
Buffer<float4> LightMat : register(t0);

cbuffer ShadowCB : register(b1)
{
    float4x4 World; // transposed like every Draw3D matrix
    float4 Mode;    // x > 0.5: rows 3..6 are one world-to-clip transform
};

struct VsIn
{
    float3 pos : POSITION;
};

struct VsOut
{
    float4 svPos : SV_Position;
};

VsOut vs(VsIn v)
{
    float4 wp = mul(float4(v.pos, 1.0), World);

    float4 clip;
    if (Mode.x < 0.5)
    {
        float4 view = float4(
            dot(LightMat.Load(0), wp),
            dot(LightMat.Load(1), wp),
            dot(LightMat.Load(2), wp),
            1.0);

        clip.x = dot(LightMat.Load(3), view);
        clip.y = dot(LightMat.Load(4), view);
        clip.z = max(dot(LightMat.Load(5), view), 0.00001); // the game's own depth shader clamps exactly so
        clip.w = dot(LightMat.Load(6), view);
    }
    else
    {
        // By columns every vertex would leave the frustum.
        clip.x = dot(LightMat.Load(3), wp);
        clip.y = dot(LightMat.Load(4), wp);
        clip.z = max(dot(LightMat.Load(5), wp), 0.00001);
        clip.w = dot(LightMat.Load(6), wp);
    }

    VsOut o;
    o.svPos = clip;
    return o;
}

// Unused. Every pipeline compiles both entry points.
void ps()
{
}
