// Depth-only pass drawing Draw3D meshes into the GAME's shadow maps, inside the game's own shadow passes,
// so injected geometry casts shadows the way it already receives them.
//
// The light's transform is not uploaded from the CPU: it is copied on the GPU out of the g_CameraParameter
// block the game's own shadow draws consume, read at the end of the pass when its values are settled (see
// ShadowInject). A shadow pass fills m_ViewMatrix (world to the pass's view space) and m_ProjectionMatrix,
// and the game's own shaders compose positions the same two steps - an instance transform into view space,
// then the projection rows - so this shader does exactly that for world-space geometry, with the tiny clip
// z floor the game's depth-only shader applies. Rows are applied by dot product, matching its dp4s.

// Rows 0..2: m_ViewMatrix (world to pass view, 3x4). Rows 3..6: m_ProjectionMatrix (4x4).
// For the near-field map's single-matrix constants, rows 3..6 hold the whole world-to-clip transform instead.
Buffer<float4> LightMat : register(t0);

cbuffer ShadowCB : register(b1)
{
    float4x4 World; // the mesh's world transform, uploaded transposed like every Draw3D matrix
    float4 Mode;    // x > 0.5: rows 3..6 are one whole world-to-clip transform (the near-field map's layout)
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
        // The same dot-product application as above, against world space directly: this buffer holds one
        // whole world-to-clip transform rather than a view/projection pair. Both layouts come from the same
        // engine and are stored the same way, so applying this one by columns instead (mul(wp, m), the
        // transpose) sends every vertex outside the frustum and silently contributes nothing.
        clip.x = dot(LightMat.Load(3), wp);
        clip.y = dot(LightMat.Load(4), wp);
        clip.z = max(dot(LightMat.Load(5), wp), 0.00001);
        clip.w = dot(LightMat.Load(6), wp);
    }

    VsOut o;
    o.svPos = clip;
    return o;
}

// The pass is depth-only and a null pixel shader is bound at draw time; this exists because every
// pipeline in the library compiles both entry points.
void ps()
{
}
