using NoireLib.Draw3D.Core;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.Draw3D;

/// <summary>
/// The runtime diagnostics toolkit behind <c>/noire3d</c>, exposed programmatically so any consumer can
/// run it even when another plugin owns the command name. Results go to the plugin log.<br/>
/// <c>RunValidate</c>: projection parity vs the game's own WorldToScreen (the wobble-class killer).<br/>
/// <c>RunProbe</c>: reads actual depth-buffer values back and compares them against expectation.<br/>
/// <c>RunCameraPhaseTrace</c>: measures the overlay-vs-world camera drift under motion (the "swim" investigation).<br/>
/// Each of these arms a window and then samples from inside the render body, on the render thread. The game reads they
/// take there are best-effort by design: answering them off-thread would return them against a camera that has already
/// moved, which destroys the frame-coherent comparison each one is built on. Their terminal chat lines are marshalled
/// to the framework thread (<see cref="Core.DiagnosticChat"/>); the full findings always go to the plugin log.<br/>
/// The visual showcase (the showcase scene, world-geometry preview, glTF import) lives in the separate
/// <c>NoireDraw3DDemoPlugin</c>, built entirely on the public Draw3D API.
/// </summary>
public sealed unsafe class Draw3DDiagnostics
{
    private int validateFramesRemaining;
    private float validateMaxDelta;
    private double validateDeltaSum;
    private int validateSamples;
    private float validateMaxMatrixDelta;
    private bool probePending;

    // Camera-phase trace ("swim" investigation): over an armed window, sweeps the recorded camera history to find the
    // frame-lag between the CPU camera the overlay projects with and the GPU-rasterized pixels already in the present
    // buffer (see RunCameraPhaseTrace / OnCameraTrace). The legacy proj-vs-live drift is kept as a secondary signal.
    private const int LagSweepMax = 8;
    private const int DepthReadbackEvery = 6; // whole-texture depth copy is heavy; sample the depth-anchored residual on a subset of frames
    private int camTraceFramesRemaining;
    private int camTraceInjectFrames;
    private int camTraceFallbackFrames;
    private int camTraceSnapshotFrames;
    private int camTraceMainPassFrames;
    private float camTraceMaxScreenDelta;
    private double camTraceScreenSum;
    private int camTraceScreenSamples;
    private float camTraceMaxMatrixDelta;

    // Frame-lag sweep accumulators: per candidate lag k (0 = the camera this frame projected with, 1 = last frame's
    // snapshot, …), how well that camera reprojects independent world anchors onto THIS frame's rendered image. The
    // depth residual (predicted depth-buffer sample vs the actual texel) is anchored to the pixels themselves and is
    // authoritative; the screen residual (anchor reprojected vs where the game shows it) corroborates. The k with the
    // smallest residual names the camera the pixels were drawn with - the lag the injected overlay must project with.
    private readonly double[] camTraceDepthResidual = new double[LagSweepMax];
    private readonly int[] camTraceDepthResidualN = new int[LagSweepMax];
    private readonly double[] camTraceScreenResidual = new double[LagSweepMax];
    private readonly int[] camTraceScreenResidualN = new int[LagSweepMax];
    private int camTraceDepthReadbacks;

    internal Draw3DDiagnostics() { }

    /// <summary>Arms the projection parity validator for the next 10 rendered frames (results logged). Gate: max ≤ 1 px.</summary>
    public void RunValidate()
    {
        NoireDraw3D.EnsureInitialized();
        validateFramesRemaining = 10;
        validateMaxDelta = 0f;
        validateDeltaSum = 0;
        validateSamples = 0;
        validateMaxMatrixDelta = 0f;
    }

    /// <summary>
    /// Arms the ground-truth depth probe for the next rendered frame (results logged): the analytic depth
    /// map rendering uses, a diagnostic-only raycast fit, a per-point depth table for both candidate
    /// buffers, and the UI-mask alpha health. Read-only - it never disturbs the live depth state.
    /// Gate: ≥ 90 % of hit points within 1e-3.
    /// </summary>
    public void RunProbe()
    {
        NoireDraw3D.EnsureInitialized();
        probePending = true;
    }

    /// <summary>
    /// Arms the camera-phase trace for the next <paramref name="frames"/> rendered frames (results logged): the tool that
    /// pins down the residual "swim". Each frame it takes independent world anchors under a screen grid (the game's own
    /// collision raycast) plus this frame's rendered depth texels, and for each candidate <b>frame-lag</b> - the camera
    /// the overlay projected 0, 1, 2, … frames ago, from the history ring - measures how well that camera reprojects the
    /// anchors onto THIS frame's rendered image. The pixels were drawn with exactly one camera, so the lag with the
    /// smallest residual names it. It also reports the inject-vs-present-time-fallback frame split, and the old
    /// proj-vs-live drift as a secondary signal. Read-only - no projection behavior changes.<br/>
    /// Run it while panning / zooming / orbiting the camera <b>vigorously</b> at high frame-rate under load: the log's
    /// lag table calls out the best-fit lag k, which is the exact correction (project the injected overlay with the
    /// snapshot k frames back). A best-fit of 0 means the overlay is already in phase and any residual is not a frame-lag
    /// (look at the fallback count instead). The whole-texture depth readback is throttled, so the trace itself costs
    /// some frames while armed.
    /// </summary>
    /// <param name="frames">How many rendered frames to trace (clamped to 1..6000; default 120 ≈ a couple seconds).</param>
    public void RunCameraPhaseTrace(int frames = 120)
    {
        NoireDraw3D.EnsureInitialized();
        camTraceFramesRemaining = Math.Clamp(frames, 1, 6000);
        camTraceInjectFrames = 0;
        camTraceFallbackFrames = 0;
        camTraceSnapshotFrames = 0;
        camTraceMainPassFrames = 0;
        camTraceMaxScreenDelta = 0f;
        camTraceScreenSum = 0;
        camTraceScreenSamples = 0;
        camTraceMaxMatrixDelta = 0f;
        Array.Clear(camTraceDepthResidual);
        Array.Clear(camTraceDepthResidualN);
        Array.Clear(camTraceScreenResidual);
        Array.Clear(camTraceScreenResidualN);
        camTraceDepthReadbacks = 0;
    }

    /// <summary>
    /// Wireframe rasterization of the scene pass. Ground decals have no mesh of their own to wireframe (their shape
    /// lives in the pixel shader), so they trace <see cref="DecalShapeOutlines"/> instead while this is on.
    /// </summary>
    public bool Wireframe
    {
        get => NoireDraw3D.Wireframe;
        set => NoireDraw3D.Wireframe = value;
    }

    /// <summary>Flips <see cref="Wireframe"/>. Returns the new state.</summary>
    public bool ToggleWireframe() => Wireframe = !Wireframe;

    /// <summary>
    /// Traces every decal's painted shape as an outline, over normal rendering - retained decals and the immediate
    /// layer's grounded shapes alike. Answers "where is this decal actually landing" globally, including for shapes drawn
    /// through <see cref="Im.ImDraw3D"/>, which have no node to call
    /// <see cref="Scene.SceneNode.ShowDecalShape"/> on. Always on while wireframe is.
    /// </summary>
    public bool DecalShapeOutlines
    {
        get => NoireDraw3D.DecalShapeOutlines;
        set => NoireDraw3D.DecalShapeOutlines = value;
    }

    /// <summary>Formats the current stats snapshot.</summary>
    public string GetStatsText() => NoireDraw3D.Stats.ToString();

    // ---------------------------------------------------------------- frame hooks (called by the hub)

    internal void OnFrame(in FrameContext frame, in GameRenderSources.CameraData cam, bool hasDepth)
    {
        if (validateFramesRemaining <= 0)
            return;

        validateFramesRemaining--;

        // Sample world points: around the player, plus points pushed forward through screen rays.
        // The object-table and GameGui reads below happen on the render thread, deliberately. This measurement is a
        // parity check between our projection and the game's own, and it is only meaningful when both project the same
        // points against the same camera at the same instant. Marshalling the reads to the framework thread would
        // answer them a frame later against a camera that has since moved, turning every sample into inter-frame
        // camera drift (what RunCameraPhaseTrace measures on purpose) and hiding the projection error being looked for.
        // They stay here as best-effort diagnostic reads: armed by hand, never on a normal frame, and read-only.
        Span<Vector3> points = stackalloc Vector3[24];
        var count = 0;

        var player = NoireService.ObjectTable.LocalPlayer?.Position;
        if (player is { } p)
        {
            for (var i = 0; i < 8; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / 8f);
                points[count++] = p + new Vector3(cos * 3f, (i % 3) * 0.8f, sin * 3f);
            }
        }

        for (var gy = 0; gy < 4; gy++)
        {
            for (var gx = 0; gx < 4; gx++)
            {
                var screen = new Vector2(frame.ViewportSize.X * (0.2f + 0.2f * gx), frame.ViewportSize.Y * (0.2f + 0.2f * gy));
                if (frame.TryScreenToRay(screen, out var origin, out var dir))
                    points[count++] = origin + dir * (5f + gx * 12f + gy * 3f);
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (!frame.TryWorldToScreen(points[i], out var ours))
                continue;
            if (!NoireService.GameGui.WorldToScreen(points[i], out var theirs))
                continue;

            var delta = Vector2.Distance(ours, theirs);
            validateMaxDelta = MathF.Max(validateMaxDelta, delta);
            validateDeltaSum += delta;
            validateSamples++;
        }

        // Cross-check View·Proj against the game's own combined matrix.
        if (cam.HasRenderCamera && cam.HasControlViewProj)
        {
            var ours = cam.View * cam.Proj;
            var theirs = cam.ControlViewProj;
            var maxDelta = 0f;
            var a = ours;
            var b = theirs;
            Span<float> av = stackalloc float[16] { a.M11, a.M12, a.M13, a.M14, a.M21, a.M22, a.M23, a.M24, a.M31, a.M32, a.M33, a.M34, a.M41, a.M42, a.M43, a.M44 };
            Span<float> bv = stackalloc float[16] { b.M11, b.M12, b.M13, b.M14, b.M21, b.M22, b.M23, b.M24, b.M31, b.M32, b.M33, b.M34, b.M41, b.M42, b.M43, b.M44 };
            for (var i = 0; i < 16; i++)
                maxDelta = MathF.Max(maxDelta, MathF.Abs(av[i] - bv[i]));
            validateMaxMatrixDelta = MathF.Max(validateMaxMatrixDelta, maxDelta);
        }

        if (validateFramesRemaining == 0)
        {
            var mean = validateSamples > 0 ? validateDeltaSum / validateSamples : 0;
            var verdict = validateMaxDelta <= 1.0f ? "PASS" : "FAIL";
            var report = $"Draw3D validate [{verdict}]: {validateSamples} samples over 10 frames - max {validateMaxDelta:F3} px, mean {mean:F3} px (gate: max ≤ 1 px). " +
                         $"VP cross-check max element delta: {validateMaxMatrixDelta:E2}. Camera fallback active: {frame.UsedFallbackCamera}. " +
                         "Repeat in the §7.5 poses: orbit, side-on grazing, wall-collision camera, first-person, max zoom.";
            DiagnosticChat.Print($"Draw3D validate: {verdict} - max {validateMaxDelta:F3} px (details in log).");
            NoireLogger.LogInfo(report, "Draw3D");
        }
    }

    /// <summary>
    /// Per-rendered-frame camera-phase sampling (called from the shared render body, render thread). Runs the frame-lag
    /// sweep that measures the overlay-vs-pixels error directly, plus the legacy proj-vs-live drift as a secondary
    /// signal. See <see cref="RunCameraPhaseTrace"/>.
    /// </summary>
    /// <param name="device">The render device (for the throttled depth-buffer readback that anchors the sweep to the pixels).</param>
    /// <param name="frame">This frame's context (viewport + projection).</param>
    /// <param name="projCam">The camera the overlay was projected with (inject = world-pass snapshot; fallback = framework snapshot).</param>
    /// <param name="viaInject">True when the frame rendered through the pre-UI inject path.</param>
    /// <param name="usedWorldSnapshot">True when the inject path used the render-thread world-pass camera snapshot (vs a live fallback).</param>
    /// <param name="usedMainPass">True when that snapshot came from the main scene pass (the swim fix) vs the first-depth fallback.</param>
    internal void OnCameraTrace(RenderDevice device, in FrameContext frame, in GameRenderSources.CameraData projCam, bool viaInject, bool usedWorldSnapshot, bool usedMainPass)
    {
        if (camTraceFramesRemaining <= 0)
            return;

        camTraceFramesRemaining--;
        if (viaInject)
        {
            camTraceInjectFrames++;
            if (usedWorldSnapshot)
                camTraceSnapshotFrames++;
            if (usedMainPass)
                camTraceMainPassFrames++;
        }
        else
        {
            camTraceFallbackFrames++;
        }

        // Secondary signal: the projection camera vs a fresh live read taken now (later in the same frame). Kept because
        // a large value flags intra-frame camera advance and the fallback count flags source mismatch - but this is
        // BLIND to the real swim, since BOTH cameras are ahead of the pixels already in the present buffer. The lag
        // sweep below is what measures the actual pixels-vs-overlay error.
        if (projCam.HasRenderCamera && GameRenderSources.TryGetCamera(out var live) && live.HasRenderCamera)
        {
            var projVp = projCam.View * projCam.Proj;
            var liveVp = live.View * live.Proj;
            camTraceMaxMatrixDelta = MathF.Max(camTraceMaxMatrixDelta, MaxElementDelta(in projVp, in liveVp));

            for (var gy = 0; gy < 4; gy++)
            {
                for (var gx = 0; gx < 4; gx++)
                {
                    var screen = new Vector2(frame.ViewportSize.X * (0.15f + 0.2f * gx), frame.ViewportSize.Y * (0.15f + 0.2f * gy));
                    if (!frame.TryScreenToRay(screen, out var origin, out var dir))
                        continue;

                    var wp = origin + dir * 20f;
                    if (!TryProjectToScreen(in projVp, wp, frame.ViewportSize, out var s1))
                        continue;
                    if (!TryProjectToScreen(in liveVp, wp, frame.ViewportSize, out var s2))
                        continue;

                    var d = Vector2.Distance(s1, s2);
                    camTraceMaxScreenDelta = MathF.Max(camTraceMaxScreenDelta, d);
                    camTraceScreenSum += d;
                    camTraceScreenSamples++;
                }
            }
        }

        SweepFrameLags(device, in frame);

        if (camTraceFramesRemaining == 0)
            ReportCameraTrace();
    }

    /// <summary>
    /// The authoritative measurement: anchor points are independent world surfaces under a screen grid (the game's own
    /// collision raycast) plus this frame's rendered depth texels. For each candidate frame-lag it takes the camera the
    /// overlay projected that many frames ago (the history ring) and measures how well it reprojects those anchors onto
    /// THIS frame's image - a screen residual (where the anchor lands vs where the game shows it, every frame) and a
    /// depth residual (predicted depth-buffer sample vs the actual texel, on a throttled subset). The pixels were drawn
    /// with exactly one camera; the lag that minimizes the residual names it. Best-effort, read-only.
    /// </summary>
    private void SweepFrameLags(RenderDevice device, in FrameContext frame)
    {
        // Independent world anchors under a screen grid: the game's collision raycast, camera-agnostic physical points.
        Span<Vector2> screens = stackalloc Vector2[16];
        Span<Vector3> worlds = stackalloc Vector3[16];
        var n = 0;
        for (var gy = 0; gy < 4; gy++)
        {
            for (var gx = 0; gx < 4; gx++)
            {
                var s = new Vector2(frame.ViewportSize.X * (0.2f + 0.2f * gx), frame.ViewportSize.Y * (0.2f + 0.2f * gy));
                if (NoireService.GameGui.ScreenToWorld(s, out var w))
                {
                    screens[n] = s;
                    worlds[n] = w;
                    n++;
                }
            }
        }

        if (n == 0)
            return;

        // Precompute each candidate camera's View·Proj + analytic depth map once (sample = map.x + map.y / clipW).
        var lags = Math.Min(LagSweepMax, NoireDraw3D.CameraHistoryDepth);
        Span<Matrix4x4> vp = stackalloc Matrix4x4[LagSweepMax];
        Span<Vector4> map = stackalloc Vector4[LagSweepMax];
        Span<bool> ok = stackalloc bool[LagSweepMax];
        var available = 0;
        for (var k = 0; k < lags; k++)
        {
            if (NoireDraw3D.TryGetCameraHistory(k, out var camK) && camK.HasRenderCamera)
            {
                vp[k] = camK.View * camK.Proj;
                var near = camK.NearPlane > 1e-6f ? camK.NearPlane : 0.1f;
                map[k] = DepthCalibration.AnalyticMap(near, camK.FarPlane, camK.StandardZ, camK.FiniteFarPlane);
                ok[k] = true;
                available = k + 1;
            }
        }

        if (available == 0)
            return;

        // Screen residual, every frame (cheap): how far each candidate camera puts the anchor from where the game shows it.
        for (var i = 0; i < n; i++)
        {
            for (var k = 0; k < available; k++)
            {
                if (!ok[k] || !TryProjectToScreen(in vp[k], worlds[i], frame.ViewportSize, out var proj))
                    continue;
                camTraceScreenResidual[k] += Vector2.Distance(proj, screens[i]);
                camTraceScreenResidualN[k]++;
            }
        }

        // Depth residual, throttled (whole-texture readback is heavy): predicted depth-buffer sample vs the actual
        // texel. Anchored to the pixels themselves, so this - not the screen residual - is the tie-breaker.
        if (camTraceFramesRemaining % DepthReadbackEvery != 0 || !GameRenderSources.TryGetDepthTexture(out var info))
            return;

        var pts = new List<Vector2>(n);
        for (var i = 0; i < n; i++)
            pts.Add(screens[i]);

        var depth = DepthReadback.TryReadAtPoints(device, in info, pts, frame.ViewportSize, out _);
        if (depth == null)
            return;
        camTraceDepthReadbacks++;

        for (var i = 0; i < n; i++)
        {
            var actual = depth[i];
            if (float.IsNaN(actual) || actual < 0f || actual > 1f)
                continue;

            for (var k = 0; k < available; k++)
            {
                if (!ok[k])
                    continue;

                var clip = Vector4.Transform(new Vector4(worlds[i], 1f), vp[k]);
                if (clip.W <= 1e-4f)
                    continue;

                var predicted = map[k].X + map[k].Y / clip.W;
                camTraceDepthResidual[k] += MathF.Abs(predicted - actual);
                camTraceDepthResidualN[k]++;
            }
        }
    }

    /// <summary>Formats the frame-lag sweep and names the best-fit lag (the correction the injected overlay should apply).</summary>
    private void ReportCameraTrace()
    {
        var traced = camTraceInjectFrames + camTraceFallbackFrames;
        var meanLegacy = camTraceScreenSamples > 0 ? camTraceScreenSum / camTraceScreenSamples : 0;

        var bestDepthK = -1;
        var bestDepth = double.MaxValue;
        var bestScreenK = -1;
        var bestScreen = double.MaxValue;

        var sb = new StringBuilder();
        sb.AppendLine($"Draw3D camtrace: {traced} frames - inject {camTraceInjectFrames} (world-snapshot {camTraceSnapshotFrames}, main-pass {camTraceMainPassFrames}), present-time fallback {camTraceFallbackFrames}. Depth readbacks: {camTraceDepthReadbacks}.");
        if (camTraceSnapshotFrames > 0 && camTraceMainPassFrames == 0)
            sb.AppendLine("  WARNING: 0 main-pass snapshots - the RTM.DepthStencil fingerprint is not matching, so the swim fix is inert (still on the first-depth fallback). Report this.");
        sb.AppendLine($"  Secondary proj-vs-live drift (blind to the true swim): max {camTraceMaxScreenDelta:F2} px, mean {meanLegacy:F2} px; View·Proj max element delta {camTraceMaxMatrixDelta:E2}.");
        sb.AppendLine("  Frame-lag sweep - how well the camera k frames back reprojects onto THIS frame's rendered image (lower = better fit):");
        sb.AppendLine("   lag | depth residual (sample units, n) | screen residual (px, n)");
        for (var k = 0; k < LagSweepMax; k++)
        {
            var dN = camTraceDepthResidualN[k];
            var sN = camTraceScreenResidualN[k];
            if (dN == 0 && sN == 0)
                continue;

            var dMean = dN > 0 ? camTraceDepthResidual[k] / dN : double.NaN;
            var sMean = sN > 0 ? camTraceScreenResidual[k] / sN : double.NaN;
            if (dN > 0 && dMean < bestDepth) { bestDepth = dMean; bestDepthK = k; }
            if (sN > 0 && sMean < bestScreen) { bestScreen = sMean; bestScreenK = k; }

            var dTxt = dN > 0 ? $"{dMean:E3} ({dN})" : "-";
            var sTxt = sN > 0 ? $"{sMean:F2} ({sN})" : "-";
            sb.AppendLine($"   {k,3} | {dTxt,-32} | {sTxt}");
        }

        var best = bestDepthK >= 0 ? bestDepthK : bestScreenK;
        string verdict;
        if (best < 0)
            verdict = "no usable anchors - aim at terrain/walls and keep the camera moving while tracing.";
        else if (best == 0)
            verdict = "best fit is lag 0 - the overlay is already in phase, so any residual swim is NOT a frame-lag. "
                      + "Check the fallback count above (a high count = present-time camera-source mismatch under load) or intra-frame advance.";
        else
            verdict = $"the pixels best match the camera from {best} frame(s) back. FIX: project the injected overlay with the "
                      + $"snapshot {best} frame(s) back (NoireDraw3D.TryGetCameraHistory({best})) instead of the live world-pass snapshot.";

        sb.AppendLine($"  Verdict: {verdict}");

        DiagnosticChat.Print($"Draw3D camtrace: best-fit frame-lag = {(best < 0 ? "n/a" : best.ToString())}, fallback {camTraceFallbackFrames}/{traced} (details in log).");
        NoireLogger.LogInfo(sb.ToString(), "Draw3D");
    }

    /// <summary>Largest absolute element-wise difference between two matrices (the View·Proj phase cross-check).</summary>
    private static float MaxElementDelta(in Matrix4x4 a, in Matrix4x4 b)
    {
        var m = MathF.Abs(a.M11 - b.M11);
        m = MathF.Max(m, MathF.Abs(a.M12 - b.M12));
        m = MathF.Max(m, MathF.Abs(a.M13 - b.M13));
        m = MathF.Max(m, MathF.Abs(a.M14 - b.M14));
        m = MathF.Max(m, MathF.Abs(a.M21 - b.M21));
        m = MathF.Max(m, MathF.Abs(a.M22 - b.M22));
        m = MathF.Max(m, MathF.Abs(a.M23 - b.M23));
        m = MathF.Max(m, MathF.Abs(a.M24 - b.M24));
        m = MathF.Max(m, MathF.Abs(a.M31 - b.M31));
        m = MathF.Max(m, MathF.Abs(a.M32 - b.M32));
        m = MathF.Max(m, MathF.Abs(a.M33 - b.M33));
        m = MathF.Max(m, MathF.Abs(a.M34 - b.M34));
        m = MathF.Max(m, MathF.Abs(a.M41 - b.M41));
        m = MathF.Max(m, MathF.Abs(a.M42 - b.M42));
        m = MathF.Max(m, MathF.Abs(a.M43 - b.M43));
        return MathF.Max(m, MathF.Abs(a.M44 - b.M44));
    }

    /// <summary>Projects a world point through a raw View·Proj to framebuffer-pixel screen space. False behind the camera.</summary>
    private static bool TryProjectToScreen(in Matrix4x4 viewProj, Vector3 world, Vector2 viewport, out Vector2 screen)
    {
        screen = default;
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 1e-4f)
            return false;

        var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
        screen = new Vector2((ndc.X * 0.5f + 0.5f) * viewport.X, (0.5f - ndc.Y * 0.5f) * viewport.Y);
        return true;
    }

    internal void OnFrameRendered(RenderDevice device, in FrameContext frame, SceneDepth? sceneDepth)
    {
        if (!probePending)
            return;

        probePending = false;

        try
        {
            RunProbeNow(device, in frame, sceneDepth);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D depth probe failed.", "Draw3D");
        }
    }

    private void RunProbeNow(RenderDevice device, in FrameContext frame, SceneDepth? sceneDepth)
    {
        // Gather ground-truth points: screen positions raycast into the world by the game itself.
        // Read on the render thread by design: these points are compared against depth texels read back from THIS
        // frame's buffer, so a raycast answered a frame later on the framework thread would be measured against a
        // different camera and a different depth image, which is exactly the comparison the probe exists to avoid.
        // Best-effort diagnostic read: armed by hand, one frame, read-only.
        var screens = new List<Vector2>();
        var worlds = new List<Vector3>();
        for (var gy = 0; gy < 4; gy++)
        {
            for (var gx = 0; gx < 4; gx++)
            {
                var screen = new Vector2(frame.ViewportSize.X * (0.2f + 0.2f * gx), frame.ViewportSize.Y * (0.2f + 0.2f * gy));
                if (NoireService.GameGui.ScreenToWorld(screen, out var world))
                {
                    screens.Add(screen);
                    worlds.Add(world);
                }
            }
        }

        if (screens.Count == 0)
        {
            Report("Draw3D probe: no raycast hits (nothing under the sampled screen points?). Aim the camera at terrain and retry.");
            return;
        }

        // Expected buffer value from the analytic depth map (what rendering actually uses), plus our
        // reconstructed device-z as a cross-check. Both come from the camera, never from a fit.
        var cam = NoireDraw3D.LastCameraData;
        var near = cam.NearPlane > 1e-6f ? cam.NearPlane : 0.1f;
        var map = DepthCalibration.AnalyticMap(near, cam.FarPlane, cam.StandardZ, cam.FiniteFarPlane);
        var expectedMap = new float[screens.Count];
        var expectedProjZ = new float[screens.Count];
        var clipWs = new float[screens.Count];
        for (var i = 0; i < screens.Count; i++)
        {
            var clip = Vector4.Transform(new Vector4(worlds[i], 1f), frame.ViewProj);
            clipWs[i] = clip.W;
            expectedMap[i] = clip.W > 1e-6f ? map.X + map.Y / clip.W : float.NaN;
            expectedProjZ[i] = clip.W > 1e-6f ? clip.Z / clip.W : float.NaN;
        }

        // Actual values from both candidate depth buffers.
        float[]? actualMain = null, actualSwap = null;
        string mainDesc = "unavailable", swapDesc = "unavailable";
        if (GameRenderSources.TryGetDepthTexture(out var mainInfo))
            actualMain = DepthReadback.TryReadAtPoints(device, in mainInfo, screens, frame.ViewportSize, out mainDesc);
        if (GameRenderSources.TryGetSwapChainDepthTexture(out var swapInfo))
            actualSwap = DepthReadback.TryReadAtPoints(device, in swapInfo, screens, frame.ViewportSize, out swapDesc);

        // An informational least-squares fit of the same raycast points (diagnostic ONLY - never used for
        // rendering). A gap between this and the analytic map is collision-vs-rendered-surface disagreement,
        // which is exactly why fitting was abandoned in favour of the analytic map.
        var fitXs = new List<float>(screens.Count);
        var fitYs = new List<float>(screens.Count);
        if (actualMain != null)
        {
            for (var i = 0; i < screens.Count; i++)
            {
                if (clipWs[i] > 1e-6f && !float.IsNaN(actualMain[i]) && actualMain[i] is >= 0f and <= 1f)
                {
                    fitXs.Add(1f / clipWs[i]);
                    fitYs.Add(actualMain[i]);
                }
            }
        }

        var fitDesc = DepthCalibration.TrySolve(fitXs, fitYs, out var fitA, out var fitB, out var fitResid, out var fitInliers)
            ? $"z={fitA:E2}{(fitB >= 0 ? "+" : "")}{fitB:F5}/w ({fitInliers} pts, resid {fitResid:E1})"
            : "unfittable this frame";

        var details = new StringBuilder();
        details.AppendLine($"Draw3D probe: {screens.Count} raycast points. Active source: {sceneDepth?.Description ?? "none"}.");
        details.AppendLine($"  analytic map (used by rendering): z={map.X:E2}{(map.Y >= 0 ? "+" : "")}{map.Y:F5}/w");
        details.AppendLine($"  raycast fit (diagnostic only):    {fitDesc}");
        details.AppendLine($"  RenderTargetManager depth: {mainDesc}");
        details.AppendLine($"  SwapChain depth:           {swapDesc}");
        details.AppendLine("  point | expected(map) expected(projZ) | actual(RTM) actual(Swap)");
        for (var i = 0; i < screens.Count; i++)
            details.AppendLine($"  {i,2}: {Fmt(expectedMap, i)} {Fmt(expectedProjZ, i)} | {Fmt(actualMain, i)} {Fmt(actualSwap, i)}");

        var mainVsMap = CountMatches(expectedMap, actualMain);
        var swapVsMap = CountMatches(expectedMap, actualSwap);
        details.AppendLine($"  matches within 1e-3: RTM×map {mainVsMap}/{screens.Count}, Swap×map {swapVsMap}/{screens.Count}");

        var gate = (int)MathF.Ceiling(screens.Count * 0.9f);
        var verdict = mainVsMap >= gate ? "PASS"
            : swapVsMap >= gate ? "FAIL - scene depth lives in the SwapChain buffer at present time (paste the log)"
            : "FAIL - the analytic map does not match the RTM buffer; paste the point table "
              + "(mismatched rows are usually collision-vs-rendered-surface disagreement, harmless if few)";

        Report($"Draw3D probe [{verdict.Split(' ')[0]}]: RTM×map {mainVsMap}/{screens.Count} (gate ≥ {gate}). {(verdict.Contains('-') ? verdict[(verdict.IndexOf('-') + 2)..] : "Analytic depth mapping confirmed against ground truth.")}");
        NoireLogger.LogInfo($"Draw3D probe details:\n{details}", "Draw3D");
    }

    private static string Fmt(float[]? values, int i)
        => values == null || float.IsNaN(values[i]) ? "   n/a  " : values[i].ToString("F6");

    private static int CountMatches(float[] expected, float[]? actual)
    {
        if (actual == null)
            return 0;

        var matches = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!float.IsNaN(expected[i]) && !float.IsNaN(actual[i]) && MathF.Abs(expected[i] - actual[i]) <= 1e-3f)
                matches++;
        }

        return matches;
    }

    private static void Report(string message)
    {
        DiagnosticChat.Print(message);
        NoireLogger.LogInfo(message, "Draw3D");
    }
}
