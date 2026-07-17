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

    // Camera-phase trace ("swim" investigation): compares the camera the overlay is projected with against a fresh live
    // read taken later in the same frame, over an armed window (see RunCameraPhaseTrace / OnCameraTrace).
    private int camTraceFramesRemaining;
    private int camTraceInjectFrames;
    private int camTraceFallbackFrames;
    private int camTraceSnapshotFrames;
    private float camTraceMaxScreenDelta;
    private double camTraceScreenSum;
    private int camTraceScreenSamples;
    private float camTraceMaxMatrixDelta;

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
    /// Arms the camera-phase trace for the next <paramref name="frames"/> rendered frames (results logged): each frame it
    /// compares the camera the overlay was projected with against a fresh live camera read taken later in the same frame,
    /// and reports the resulting screen drift (the visible "swim" magnitude), the View·Proj element delta, and how many
    /// frames went through the pre-UI inject path (world-pass snapshot) vs the present-time fallback. Read-only.<br/>
    /// Run it while panning / zooming / orbiting the camera vigorously at high frame-rate to characterize the swim; the
    /// numbers say whether the drift is intra-frame camera advance (large delta on inject frames) or a fallback-path
    /// camera-source mismatch (many fallback frames). No projection behavior changes - this only measures.
    /// </summary>
    /// <param name="frames">How many rendered frames to trace (clamped to 1..6000; default 120 ≈ a couple seconds).</param>
    public void RunCameraPhaseTrace(int frames = 120)
    {
        NoireDraw3D.EnsureInitialized();
        camTraceFramesRemaining = Math.Clamp(frames, 1, 6000);
        camTraceInjectFrames = 0;
        camTraceFallbackFrames = 0;
        camTraceSnapshotFrames = 0;
        camTraceMaxScreenDelta = 0f;
        camTraceScreenSum = 0;
        camTraceScreenSamples = 0;
        camTraceMaxMatrixDelta = 0f;
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
    /// Per-rendered-frame camera-phase sampling (called from the shared render body with the camera the overlay was
    /// actually projected with). Compares it against a fresh live read taken now - later in the same frame than the
    /// projection camera was captured - and accumulates the screen drift over the armed window. See <see cref="RunCameraPhaseTrace"/>.
    /// </summary>
    /// <param name="frame">This frame's context (viewport + projection cross-check).</param>
    /// <param name="projCam">The camera the overlay was projected with (inject = world-pass snapshot; fallback = framework snapshot).</param>
    /// <param name="viaInject">True when the frame rendered through the pre-UI inject path.</param>
    /// <param name="usedWorldSnapshot">True when the inject path used the render-thread world-pass camera snapshot (vs a live fallback).</param>
    internal void OnCameraTrace(in FrameContext frame, in GameRenderSources.CameraData projCam, bool viaInject, bool usedWorldSnapshot)
    {
        if (camTraceFramesRemaining <= 0)
            return;

        camTraceFramesRemaining--;
        if (viaInject)
        {
            camTraceInjectFrames++;
            if (usedWorldSnapshot)
                camTraceSnapshotFrames++;
        }
        else
        {
            camTraceFallbackFrames++;
        }

        // A fresh live camera read, sampled now - later in the frame than projCam was captured. The delta between the two
        // IS the phase error the overlay is projected with this frame.
        if (projCam.HasRenderCamera && GameRenderSources.TryGetCamera(out var live) && live.HasRenderCamera)
        {
            var projVp = projCam.View * projCam.Proj;
            var liveVp = live.View * live.Proj;

            var md = 0f;
            Span<float> pv = stackalloc float[16] { projVp.M11, projVp.M12, projVp.M13, projVp.M14, projVp.M21, projVp.M22, projVp.M23, projVp.M24, projVp.M31, projVp.M32, projVp.M33, projVp.M34, projVp.M41, projVp.M42, projVp.M43, projVp.M44 };
            Span<float> lv = stackalloc float[16] { liveVp.M11, liveVp.M12, liveVp.M13, liveVp.M14, liveVp.M21, liveVp.M22, liveVp.M23, liveVp.M24, liveVp.M31, liveVp.M32, liveVp.M33, liveVp.M34, liveVp.M41, liveVp.M42, liveVp.M43, liveVp.M44 };
            for (var i = 0; i < 16; i++)
                md = MathF.Max(md, MathF.Abs(pv[i] - lv[i]));
            camTraceMaxMatrixDelta = MathF.Max(camTraceMaxMatrixDelta, md);

            // Screen drift: for a grid of world points the projection camera "sees" across the view, measure how far the
            // live camera would place each one. That gap is exactly what the eye reads as swim.
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

        if (camTraceFramesRemaining == 0)
        {
            var traced = camTraceInjectFrames + camTraceFallbackFrames;
            var meanDrift = camTraceScreenSamples > 0 ? camTraceScreenSum / camTraceScreenSamples : 0;
            var report =
                $"Draw3D camtrace: {traced} frames - inject {camTraceInjectFrames} (world-snapshot {camTraceSnapshotFrames}), present-time fallback {camTraceFallbackFrames}. " +
                $"Overlay-vs-world screen drift: max {camTraceMaxScreenDelta:F2} px, mean {meanDrift:F2} px over {camTraceScreenSamples} samples. " +
                $"View·Proj max element delta: {camTraceMaxMatrixDelta:E2}. " +
                "Read: near-zero drift = in phase; large drift on inject frames = intra-frame camera advance; many fallback frames = camera-source mismatch under load.";
            DiagnosticChat.Print($"Draw3D camtrace: max drift {camTraceMaxScreenDelta:F2} px, fallback {camTraceFallbackFrames}/{traced} frames (details in log).");
            NoireLogger.LogInfo(report, "Draw3D");
        }
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
