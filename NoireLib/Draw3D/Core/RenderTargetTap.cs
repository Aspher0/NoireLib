using NoireLib.Hooking;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Render-thread hook on the game's D3D11 immediate context, serving the bind-sequence diagnostic, the pre-UI
/// and G-buffer injection points, and the main-scene-pass camera phase. Installed on first use only, and each
/// hook stays disabled until a capture is armed or an injection is enabled.
/// </summary>
internal sealed unsafe class RenderTargetTap : IDisposable
{
    // ID3D11DeviceContext vtable slots.
    private const int SlotDrawIndexed = 12;
    private const int SlotDraw = 13;
    private const int SlotDrawIndexedInstanced = 20;
    private const int SlotDrawInstanced = 21;
    private const int SlotOmSetRenderTargets = 33;
    private const int SlotRsSetViewports = 44;
    private const int MaxBinds = 640; // a full frame including the late UI stage
    private const int MaxMultiBinds = 32;
    private const int MaxTargetsPerBind = 8; // D3D11's simultaneous render target limit
    // The measured G-buffer pass binds five. A lower floor also matches a three-target bind on the same scene
    // depth whose slots carry a different set, so injected geometry would write albedo into a half-float target.
    private const int GBufferMinTargets = 5;
    private const int CaptureWarmupFrames = 6; // let the swapchain flip through all its buffers first
    private const int InjectOrdinal = 2; // present-buffer bind #: 1 = world copy, 2 = after world / before UI

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void OmSetRenderTargetsFn(nint context, uint numViews, nint ppRenderTargetViews, nint pDepthStencilView);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawIndexedFn(nint context, uint indexCount, uint startIndex, int baseVertex);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawFn(nint context, uint vertexCount, uint startVertex);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawIndexedInstancedFn(nint context, uint indexCountPerInstance, uint instanceCount, uint startIndex, int baseVertex, uint startInstance);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawInstancedFn(nint context, uint vertexCountPerInstance, uint instanceCount, uint startVertex, uint startInstance);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void RsSetViewportsFn(nint context, uint numViewports, nint pViewports);

    /// <summary>One render-target bind, whose <see cref="Format"/> is the view's format rather than the texture's.</summary>
    private readonly record struct Bind(uint NumViews, nint Rtv0Resource, DXGI_FORMAT Format, uint Width, uint Height, bool HasDsv, bool IsBackbuffer, int DrawCount);

    /// <summary>One target of a multi-target bind, for reading a G-buffer's layout.</summary>
    private readonly record struct TargetInfo(nint Resource, DXGI_FORMAT Format, uint Width, uint Height);

    private NoireHook<OmSetRenderTargetsFn>? omHook;
    private NoireHook<DrawIndexedFn>? drawIndexedHook;
    private NoireHook<DrawFn>? drawHook;
    private NoireHook<DrawIndexedInstancedFn>? drawIndexedInstancedHook;
    private NoireHook<DrawInstancedFn>? drawInstancedHook;
    private NoireHook<RsSetViewportsFn>? rsSetViewportsHook;
    // Only the frame dump needs a device: it copies a target mid-frame. Every other job reads binds alone.
    private RenderDevice? device;
    private OmSetRenderTargetsFn? omDetour;
    private DrawIndexedFn? drawIndexedDetour;
    private DrawFn? drawDetour;
    private DrawIndexedInstancedFn? drawIndexedInstancedDetour;
    private DrawInstancedFn? drawInstancedDetour;
    private RsSetViewportsFn? rsSetViewportsDetour;
    private nint gameContext;

    private readonly Bind[] binds = new Bind[MaxBinds];
    private int bindCount;

    // Created on first arm, disposed with the tap.
    private ShadowProbe? shadowProbe;

    // Whole target sets of multi-target binds, sized flat and up front so the render thread never allocates.
    private readonly int[] multiBindAt = new int[MaxMultiBinds];
    private readonly TargetInfo[] multiBindTargets = new TargetInfo[MaxMultiBinds * MaxTargetsPerBind];
    private readonly int[] multiBindCounts = new int[MaxMultiBinds];
    private int multiBindCount;
    private int drawCounter;
    private volatile int state; // 0 = idle, 1 = warming up, 2 = capturing
    private int warmupLeft;

    // Each dump is a full-resolution copy plus a synchronous map, so the frame it runs on stalls badly.
    private const int MaxFrameDumps = 16;
    private int dumpFrom = -1;
    private int dumpCount;
    private int dumpStride; // > 0 = spread across the whole frame instead of a contiguous span
    private int dumpsWritten;
    private string dumpFolder = string.Empty;

    /// <summary>Bind count of the last captured frame; a frame is not a fixed length, so bind indices do not carry across runs.</summary>
    private int lastFrameBindCount;

    // A bind counts as a backbuffer bind if its target matches any of the flip-model buffers seen at present.
    private readonly nint[] knownBackbuffers = new nint[8];
    private int knownBackbufferCount;

    // Injection state.
    private nint presentBuffer;             // committed present-composition buffer (learned last frame)
    private nint candidatePresentBuffer;    // RTV seen right before a swapchain bind this frame
    private nint lastNonBackbufferRtv;      // running previous RTV (candidate source)
    private int presentBufferBinds;         // present-buffer binds so far this frame
    private volatile bool injecting;        // re-entrancy guard around the injection callback

    // The camera the world was rasterized with, snapshotted at the FIRST main-scene bind (depth-stencil ==
    // RenderTargetManager.DepthStencil) and then locked. The game advances the camera between the shadow passes and
    // the main pass, so a shadow-pass snapshot is sub-pixel wrong and the overlay swims; it is only the fallback.
    private GameRenderSources.CameraData worldCamera;
    private volatile bool hasWorldCamera;
    private bool mainDepthSeen;      // locked once the main-scene depth is captured this frame
    private nint frameSceneDepthTex; // RTM.DepthStencil texture, cached per present for the main-pass fingerprint

    /// <summary>
    /// Fired inside the game's G-buffer pass at its first draw, with the pass's targets already bound. The
    /// callback draws into the game's own targets and must restore every pipeline state it changes.
    /// </summary>
    public Action? GBufferInjector { get; set; }

    /// <summary>
    /// Whether the G-buffer injection is wanted this frame. Setting it keeps the four per-draw hooks enabled,
    /// which costs a managed callback on every draw the game makes, so it must follow queued work rather than latch.
    /// </summary>
    public bool GBufferInjectionEnabled
    {
        get => gbufferInjectionEnabled;
        set
        {
            if (gbufferInjectionEnabled == value)
                return;

            gbufferInjectionEnabled = value;
            RefreshOmHookState();
        }
    }

    private bool gbufferInjectionEnabled;

    // Set at the G-buffer bind, consumed at that pass's first draw.
    private bool gbufferPassArmed;
    private bool gbufferDoneThisFrame;

    /// <summary>
    /// Fired with the game's context at the end of every shadow draw group that received draws, while the group's
    /// map, viewport, raster state and settled constants are all still bound. Groups with no draws are cached maps
    /// that already carry the geometry, and drawing into one again stamps a second silhouette.
    /// </summary>
    public Action<nint>? ShadowInjector { get; set; }

    /// <summary>
    /// Whether the shadow injection is wanted this frame; it costs the same per-draw callback the G-buffer
    /// injection does, so it follows queued work rather than latching.
    /// </summary>
    public bool ShadowInjectionEnabled
    {
        get => shadowInjectionEnabled;
        set
        {
            if (shadowInjectionEnabled == value)
                return;

            shadowInjectionEnabled = value;
            RefreshOmHookState();
        }
    }

    private bool shadowInjectionEnabled;

    /// <summary>Fired once per frame on the render thread, so the shadow queue can flip its frame boundary.</summary>
    public Action? ShadowFrameBoundary { get; set; }

    // Whether the current bind is a shadow-map bind, and whether the current draw group has drawn into it.
    private bool shadowBindActive;
    private bool shadowBindSawDraw;

    /// <summary>When true the detours skip their work, set around Draw3D's own binds so they are never observed.</summary>
    public bool SuppressSelf;

    /// <summary>Whether an injection callback is running right now, so Draw3D's own D3D calls inside it are not observed.</summary>
    public bool IsInjecting => injecting;

    /// <summary>
    /// The camera-constant capture riding this tap's frame phase, signalled from here at the frame boundary and the
    /// main-pass bind. Null when not installed.
    /// </summary>
    public CameraConstantCapture? Capture;

    /// <summary>Enables the pre-UI injection path (the OM hook must be installed and stays enabled while set).</summary>
    public bool InjectionEnabled { get; private set; }

    /// <summary>Callback fired on the render thread at the injection point with the present-buffer resource, returning whether it rendered.</summary>
    public Func<nint, bool>? Injector { get; set; }

    /// <summary>
    /// The committed present-composition buffer, or 0 before one has been learned. The same resource the
    /// <see cref="Injector"/> is handed, and still readable at present time, by which point the game has drawn its
    /// native UI into it.
    /// </summary>
    public nint PresentBuffer => presentBuffer;

    /// <summary>
    /// The view and projection the world currently in the present buffer was rasterized with, captured on the render
    /// thread.
    /// </summary>
    /// <param name="camera">Receives the captured camera.</param>
    /// <returns>False before this frame's first depth pass is seen, for instance on a menu or loading frame.</returns>
    public bool TryGetWorldCamera(out GameRenderSources.CameraData camera)
    {
        camera = worldCamera;
        return hasWorldCamera;
    }

    /// <summary>
    /// Whether this frame's world-camera snapshot came from the main scene pass rather than the less accurate
    /// first-depth-bind fallback.
    /// </summary>
    public bool WorldCameraIsMainPass => mainDepthSeen;

    /// <summary>True once the hooks have been installed (they may still be disabled).</summary>
    public bool Installed => omHook != null;

    /// <summary>Installs the hooks, disabled, by reading the immediate context's vtable slots.</summary>
    /// <param name="device">The render device whose immediate context is hooked.</param>
    /// <returns>True when the hooks are installed or were already installed.</returns>
    public bool Install(RenderDevice device)
    {
        if (omHook != null)
            return true;

        var ctx = device.Context;
        if (ctx == null)
            return false;

        gameContext = (nint)ctx;
        this.device = device;
        var vtable = *(void***)ctx;

        try
        {
            omDetour = OmDetour;
            omHook = new NoireHook<OmSetRenderTargetsFn>((nint)vtable[SlotOmSetRenderTargets], omDetour, DeviceHookOptions("Draw3D.OMSetRenderTargets"));
            drawIndexedDetour = DrawIndexedDetour;
            drawIndexedHook = new NoireHook<DrawIndexedFn>((nint)vtable[SlotDrawIndexed], drawIndexedDetour, DeviceHookOptions("Draw3D.DrawIndexed"));
            drawDetour = DrawDetour;
            drawHook = new NoireHook<DrawFn>((nint)vtable[SlotDraw], drawDetour, DeviceHookOptions("Draw3D.Draw"));
            drawIndexedInstancedDetour = DrawIndexedInstancedDetour;
            drawIndexedInstancedHook = new NoireHook<DrawIndexedInstancedFn>((nint)vtable[SlotDrawIndexedInstanced], drawIndexedInstancedDetour, DeviceHookOptions("Draw3D.DrawIndexedInstanced"));
            drawInstancedDetour = DrawInstancedDetour;
            drawInstancedHook = new NoireHook<DrawInstancedFn>((nint)vtable[SlotDrawInstanced], drawInstancedDetour, DeviceHookOptions("Draw3D.DrawInstanced"));
            rsSetViewportsDetour = RsSetViewportsDetour;
            rsSetViewportsHook = new NoireHook<RsSetViewportsFn>((nint)vtable[SlotRsSetViewports], rsSetViewportsDetour, DeviceHookOptions("Draw3D.RSSetViewports"));
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: failed to install the render-thread hook (pre-UI features unavailable).", "Draw3D");
            Dispose();
            return false;
        }

        NoireLogger.LogInfo("Draw3D: render-thread hook installed (disabled until armed/enabled).", "Draw3D");
        return true;
    }

    /// <summary>Turns the pre-UI injection path on or off, keeping the OM hook enabled while it is on.</summary>
    /// <param name="enabled">Whether the injection path runs.</param>
    public void SetInjection(bool enabled)
    {
        InjectionEnabled = enabled;
        RefreshOmHookState();
    }

    /// <summary>
    /// Arms the <see cref="ShadowProbe"/> for the next frame, recording every depth-only bind's target and the
    /// vertex-shader constants at its first draw into the log.
    /// </summary>
    public void ArmShadowProbe()
    {
        if (omHook == null)
            return;

        shadowProbe ??= new ShadowProbe();
        shadowProbe.Arm();
    }

    /// <summary>Arms a one-frame diagnostic capture after a short warm-up, so every swapchain buffer is learned first.</summary>
    public void ArmCapture()
    {
        if (omHook == null)
            return;

        bindCount = 0;
        multiBindCount = 0;
        warmupLeft = CaptureWarmupFrames;
        state = 1;
        dumpFrom = -1;
        dumpCount = 0;
        RefreshOmHookState();
    }

    /// <summary>Arms a capture that also writes out what a span of binds produced, as images.</summary>
    /// <param name="from">First bind index to write out.</param>
    /// <param name="count">How many consecutive binds to write out.</param>
    /// <param name="folder">Destination folder for the images.</param>
    public void ArmFrameDump(int from, int count, string folder)
    {
        if (omHook == null)
            return;

        ArmCapture();
        dumpFrom = Math.Max(0, from);
        dumpCount = Math.Clamp(count, 1, MaxFrameDumps);
        dumpStride = 0;
        dumpsWritten = 0;
        dumpFolder = folder;
    }

    /// <summary>
    /// Arms a dump spread evenly across the whole frame rather than over a chosen span, so it does not depend on a
    /// bind index staying stable across runs.
    /// </summary>
    /// <param name="count">How many binds to write out, spread across the frame.</param>
    /// <param name="folder">Destination folder for the images.</param>
    /// <returns>The stride chosen, or 0 when the hooks are not installed.</returns>
    public int ArmFrameSweep(int count, string folder)
    {
        if (omHook == null)
            return 0;

        ArmFrameDump(0, count, folder);

        // Length assumed for a frame that has never been measured; the stride only sets sample spacing, so
        // guessing high costs a sparser sweep rather than a failed one.
        const int AssumedFrameBinds = 128;
        var length = lastFrameBindCount > 0 ? lastFrameBindCount : AssumedFrameBinds;
        dumpStride = Math.Max(1, length / dumpCount);
        return dumpStride;
    }

    /// <summary>
    /// Writes out the target that has just finished being drawn into, when it falls in the armed span. Runs before
    /// the game's new bind is applied, the only moment the previous target's contents are final.
    /// </summary>
    private void DumpFinishedBind()
    {
        var finished = bindCount - 1;
        if (dumpFrom < 0 || finished < 0 || dumpsWritten >= dumpCount)
            return;

        var wanted = dumpStride > 0
            ? finished % dumpStride == 0
            : finished >= dumpFrom && finished < dumpFrom + dumpCount;

        if (!wanted)
            return;

        var resource = binds[finished].Rtv0Resource;
        if (resource == 0 || device is not { } dev)
            return;

        dumpsWritten++;

        try
        {
            var path = System.IO.Path.Combine(dumpFolder, $"frame_bind{finished:D3}.bmp");
            var note = GBufferProbe.Dump(dev, resource, path);
            NoireLogger.LogInfo($"[FrameDump] bind {finished}: {note}", "Draw3D");

            // The stencil mark the game's light volumes test exists only between the geometry and lighting passes,
            // so nothing at the end of the frame can read it.
            if (GameRenderSources.TryGetDepthTexture(out var depth) && depth.Texture != 0)
            {
                var stencilPath = System.IO.Path.Combine(dumpFolder, $"frame_bind{finished:D3}_stencil.bmp");
                NoireLogger.LogInfo($"[FrameDump] bind {finished}: {GBufferProbe.DumpStencil(dev, depth.Texture, stencilPath)}", "Draw3D");
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Draw3D: frame dump of bind {finished} failed.", "Draw3D");
        }
    }

    /// <summary>
    /// Per-present bookkeeping on the render thread: learns the swapchain backbuffers, commits the present buffer
    /// learned this frame, resets per-frame counters, and drives the diagnostic-capture state machine.
    /// </summary>
    /// <param name="backbufferTexture">The backbuffer texture being presented.</param>
    public void OnPresent(nint backbufferTexture)
    {
        RememberBackbuffer(backbufferTexture);
        Capture?.OnFrameBoundary();
        shadowProbe?.OnFrameBoundary();
        if (shadowInjectionEnabled)
            ShadowFrameBoundary?.Invoke();

        if (candidatePresentBuffer != 0)
            presentBuffer = candidatePresentBuffer;
        candidatePresentBuffer = 0;
        lastNonBackbufferRtv = 0;
        presentBufferBinds = 0;
        hasWorldCamera = false; // re-snapshot at the next frame's main scene pass
        mainDepthSeen = false;

        gbufferPassArmed = false;
        gbufferDoneThisFrame = false;
        shadowBindActive = false;
        shadowBindSawDraw = false;
        // Scene-depth texture for next frame's main-pass fingerprint; a resize costs one frame of first-depth fallback.
        frameSceneDepthTex = GameRenderSources.TryGetDepthTexture(out var sceneDepth) ? sceneDepth.Texture : 0;

        switch (state)
        {
            case 1:
                if (--warmupLeft <= 0)
                {
                    bindCount = 0;
                    multiBindCount = 0;
                    drawCounter = 0;
                    state = 2;
                    RefreshOmHookState();
                }

                break;
            case 2:
                Flush();
                state = 0;
                RefreshOmHookState();
                break;
        }
    }

    private void RefreshOmHookState()
    {
        var wanted = InjectionEnabled || state == 2 || shadowInjectionEnabled;
        if (omHook != null && omHook.IsEnabled != wanted)
            omHook.SetEnabled(wanted);

        // The viewport hook exists solely for the shadow injection's group boundaries.
        rsSetViewportsHook?.SetEnabled(shadowInjectionEnabled);

        // The camera-constant capture only has a main-pass signal while the injection point is running.
        Capture?.SetActive(InjectionEnabled);
        RefreshDrawHookState();
    }

    /// <summary>Enables the draw hooks for whichever consumer wants them: the one-frame capture, an injection, or the camera-constant commit.</summary>
    private void RefreshDrawHookState()
        => SetDrawHooksEnabled(state == 2 || GBufferInjectionEnabled || shadowInjectionEnabled || (Capture?.WantsDrawSignal ?? false));

    private void SetDrawHooksEnabled(bool enabled)
    {
        drawIndexedHook?.SetEnabled(enabled);
        drawHook?.SetEnabled(enabled);
        drawIndexedInstancedHook?.SetEnabled(enabled);
        drawInstancedHook?.SetEnabled(enabled);
    }

    private void RememberBackbuffer(nint texture)
    {
        if (texture == 0)
            return;

        for (var i = 0; i < knownBackbufferCount; i++)
        {
            if (knownBackbuffers[i] == texture)
                return;
        }

        if (knownBackbufferCount < knownBackbuffers.Length)
            knownBackbuffers[knownBackbufferCount++] = texture;
    }

    private bool IsBackbuffer(nint resource)
    {
        for (var i = 0; i < knownBackbufferCount; i++)
        {
            if (knownBackbuffers[i] == resource)
                return true;
        }

        return false;
    }

    private bool Counting(nint context) => state == 2 && !SuppressSelf && !injecting && context == gameContext;

    private void OmDetour(nint context, uint numViews, nint ppRtvs, nint pDsv)
    {
        // The target about to be replaced has just received its last draw, the only point its contents are readable.
        if (state == 2 && dumpFrom >= 0 && !injecting && !SuppressSelf && context == gameContext)
            DumpFinishedBind();

        // Everything a shadow draw group ran with stays bound until the Original call below applies the new targets.
        TryInjectShadowAtGroupEnd(context);

        if (!injecting && context == gameContext)
        {
            shadowBindActive = false;
            shadowBindSawDraw = false;
        }

        omHook!.Original(context, numViews, ppRtvs, pDsv);

        if (injecting || SuppressSelf || context != gameContext)
            return;

        var rtv0 = ResolveRtv0Resource(numViews, ppRtvs);

        // Depth-only binds cover both shadow maps and the scene's own depth pre-pass; the probe records which is which
        // rather than filtering here.
        if (shadowProbe is { Armed: true } && rtv0 == 0 && pDsv != 0)
            shadowProbe.OnDepthOnlyBind(pDsv, IsMainSceneDepth(pDsv));

        shadowBindActive = shadowInjectionEnabled && rtv0 == 0 && pDsv != 0 && !IsMainSceneDepth(pDsv);

        // The present-composition buffer is the RTV bound right before a swapchain backbuffer bind.
        if (rtv0 != 0)
        {
            if (IsBackbuffer(rtv0))
            {
                if (lastNonBackbufferRtv != 0)
                    candidatePresentBuffer = lastNonBackbufferRtv;
            }
            else
            {
                lastNonBackbufferRtv = rtv0;
            }
        }

        // The camera is snapshotted at the FIRST main-scene-depth bind, never a later one: transparency, water and
        // depth-reading post-fx re-bind scene depth with a newer camera that overshoots the already-drawn pixels.
        if (InjectionEnabled && !mainDepthSeen && pDsv != 0 && rtv0 != 0 && !IsBackbuffer(rtv0))
        {
            if (IsMainSceneDepth(pDsv) && GameRenderSources.TryGetCamera(out var mainSnap))
            {
                worldCamera = mainSnap;
                hasWorldCamera = true;
                mainDepthSeen = true;

                // The game binds and uploads its camera block between this bind and the pass's first draw, so the
                // commit is armed rather than taken here. The struct snapshot above stays the fallback.
                Capture?.OnMainPassBind();
            }
            else if (!hasWorldCamera && GameRenderSources.TryGetCamera(out var provisionalSnap))
            {
                worldCamera = provisionalSnap; // shadow-pass fallback until the main pass replaces it
                hasWorldCamera = true;
            }
        }

        // Post-process passes also bind multiple targets, but never with the scene's depth-stencil.
        if (GBufferInjectionEnabled && !gbufferDoneThisFrame && numViews >= GBufferMinTargets && pDsv != 0 && IsMainSceneDepth(pDsv))
            gbufferPassArmed = true;

        if (state == 2 && bindCount < MaxBinds)
            Record(numViews, rtv0, pDsv, ppRtvs);

        if (InjectionEnabled && presentBuffer != 0 && rtv0 == presentBuffer && Injector != null)
        {
            presentBufferBinds++;
            if (presentBufferBinds == InjectOrdinal)
            {
                injecting = true;
                try
                {
                    Injector(presentBuffer);
                }
                catch (Exception ex)
                {
                    NoireLogger.LogError(ex, "Draw3D: native-UI injection callback threw.", "Draw3D");
                }
                finally
                {
                    injecting = false;
                }
            }
        }
    }

    /// <summary>
    /// Runs at every game draw, taking the camera commit before the G-buffer injection so injected geometry is
    /// projected with the camera the commit establishes.
    /// </summary>
    /// <param name="context">The device context the draw was issued on.</param>
    private void OnDraw(nint context)
    {
        Capture?.OnGameDraw(context); // a no-op except at the main pass's first draw

        if (shadowProbe is { Armed: true } && !injecting && !SuppressSelf && context == gameContext)
            shadowProbe.OnGameDraw((TerraFX.Interop.DirectX.ID3D11DeviceContext*)context);

        if (shadowBindActive && !injecting && !SuppressSelf && context == gameContext)
            shadowBindSawDraw = true;

        if (!gbufferPassArmed || GBufferInjector is not { } injector)
            return;

        // Disarmed before the call, so a callback that throws is not retried against every remaining draw of the pass.
        gbufferPassArmed = false;
        gbufferDoneThisFrame = true;

        injecting = true;
        try
        {
            injector();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: G-buffer injection callback threw - injection disabled for safety.", "Draw3D");
            GBufferInjectionEnabled = false;
        }
        finally
        {
            injecting = false;
        }
    }

    /// <summary>
    /// Injects the queued shadow geometry if a shadow draw group with draws behind it is ending right now. Called
    /// before the state change that ends the group, which is a new target bind or a viewport change inside the same
    /// bind, since an atlas map renders each slice as its own viewport group with its own constants.
    /// </summary>
    /// <param name="context">The device context the state change was issued on.</param>
    private void TryInjectShadowAtGroupEnd(nint context)
    {
        if (!shadowBindActive || !shadowBindSawDraw || !shadowInjectionEnabled || injecting || SuppressSelf
            || context != gameContext || ShadowInjector is not { } shadowInjector)
            return;

        // The next group must see its own draws before it earns an injection.
        shadowBindSawDraw = false;

        injecting = true;
        try
        {
            shadowInjector(context);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: shadow injection callback threw - shadow casting disabled for safety.", "Draw3D");
            ShadowInjectionEnabled = false;
        }
        finally
        {
            injecting = false;
        }
    }

    private void RsSetViewportsDetour(nint context, uint numViewports, nint pViewports)
    {
        // A viewport change inside a shadow bind ends the current slice's draw group, while its constants still bind.
        TryInjectShadowAtGroupEnd(context);

        rsSetViewportsHook!.Original(context, numViewports, pViewports);
    }

    private void DrawIndexedDetour(nint context, uint indexCount, uint startIndex, int baseVertex)
    {
        if (Counting(context))
            drawCounter++;
        OnDraw(context);

        drawIndexedHook!.Original(context, indexCount, startIndex, baseVertex);
    }

    private void DrawDetour(nint context, uint vertexCount, uint startVertex)
    {
        if (Counting(context))
            drawCounter++;
        OnDraw(context);

        drawHook!.Original(context, vertexCount, startVertex);
    }

    private void DrawIndexedInstancedDetour(nint context, uint indexCountPerInstance, uint instanceCount, uint startIndex, int baseVertex, uint startInstance)
    {
        if (Counting(context))
            drawCounter++;
        OnDraw(context);

        drawIndexedInstancedHook!.Original(context, indexCountPerInstance, instanceCount, startIndex, baseVertex, startInstance);
    }

    private void DrawInstancedDetour(nint context, uint vertexCountPerInstance, uint instanceCount, uint startVertex, uint startInstance)
    {
        if (Counting(context))
            drawCounter++;
        OnDraw(context);

        drawInstancedHook!.Original(context, vertexCountPerInstance, instanceCount, startVertex, startInstance);
    }

    private nint ResolveRtv0Resource(uint numViews, nint ppRtvs)
    {
        if (numViews == 0 || ppRtvs == 0)
            return 0;

        var rtv = ((ID3D11RenderTargetView**)ppRtvs)[0];
        if (rtv == null)
            return 0;

        ID3D11Resource* resource = null;
        rtv->GetResource(&resource);
        if (resource == null)
            return 0;

        var res = (nint)resource;
        resource->Release(); // used for comparison only; the RTV keeps the resource alive
        return res;
    }

    /// <summary>
    /// Whether a bound depth-stencil view targets the main scene depth, distinguishing the main world pass from the
    /// shadow-map passes that render first. False when the per-frame scene-depth cache is unset.
    /// </summary>
    /// <param name="pDsv">The bound depth-stencil view.</param>
    /// <returns>True when the view's resource is the cached scene-depth texture.</returns>
    private bool IsMainSceneDepth(nint pDsv)
    {
        if (frameSceneDepthTex == 0 || pDsv == 0)
            return false;

        ID3D11Resource* resource = null;
        ((ID3D11DepthStencilView*)pDsv)->GetResource(&resource);
        if (resource == null)
            return false;

        var match = (nint)resource == frameSceneDepthTex;
        resource->Release(); // used for comparison only; the DSV keeps the resource alive
        return match;
    }

    private void Record(uint numViews, nint rtv0, nint pDsv, nint ppRtvs)
    {
        uint w = 0, h = 0;
        if (rtv0 != 0 && ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)rtv0, out var tex))
        {
            D3D11_TEXTURE2D_DESC desc;
            tex.Get()->GetDesc(&desc);
            w = desc.Width;
            h = desc.Height;
            tex.Dispose();
        }

        var format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        if (numViews > 0 && ppRtvs != 0)
        {
            var view = ((ID3D11RenderTargetView**)ppRtvs)[0];
            if (view != null)
            {
                D3D11_RENDER_TARGET_VIEW_DESC viewDesc;
                view->GetDesc(&viewDesc);
                format = viewDesc.Format;
            }
        }

        if (numViews > 1)
            RecordMultiTarget(numViews, ppRtvs);

        binds[bindCount++] = new Bind(numViews, rtv0, format, w, h, pDsv != 0, IsBackbuffer(rtv0), drawCounter);
    }

    /// <summary>
    /// The G-buffer's target resources from the last capture, in bind order, chosen from the multi-target binds that
    /// carry a depth-stencil.
    /// </summary>
    /// <returns>The target resources, or an empty list when no candidate bind was captured.</returns>
    public List<nint> GBufferTargets()
    {
        var result = new List<nint>();
        var best = -1;
        var bestTargets = 0;
        var bestWidth = 0u;
        var bestDraws = -1;

        for (var i = 0; i < multiBindCount; i++)
        {
            var at = multiBindAt[i];
            if (at + 1 >= bindCount || !binds[at].HasDsv)
                continue;

            var targets = multiBindCounts[i];
            var width = multiBindTargets[i * MaxTargetsPerBind].Width;
            var draws = binds[at + 1].DrawCount - binds[at].DrawCount;

            // Target count first, then resolution, then draws. Draws alone picks a two-target post-process bind over
            // the G-buffer, and resolution is what rejects the half- and quarter-resolution post-process binds.
            if (targets < bestTargets)
                continue;

            if (targets == bestTargets)
            {
                if (width < bestWidth || (width == bestWidth && draws <= bestDraws))
                    continue;
            }

            bestTargets = targets;
            bestWidth = width;
            bestDraws = draws;
            best = i;
        }

        if (best < 0)
            return result;

        for (var t = 0; t < multiBindCounts[best]; t++)
            result.Add(multiBindTargets[(best * MaxTargetsPerBind) + t].Resource);

        return result;
    }

    /// <summary>
    /// Reports the target set of every multi-target bind, whose formats an injected draw must match exactly.
    /// </summary>
    /// <param name="sb">The report being built.</param>
    private void AppendMultiTargets(StringBuilder sb)
    {
        if (multiBindCount == 0)
        {
            sb.AppendLine();
            sb.AppendLine("No multi-target binds this frame. With only one target ever bound the renderer is forward, not deferred.");
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"Multi-target binds ({multiBindCount}). The G-buffer is the full-resolution one followed by a burst of draws:");

        for (var i = 0; i < multiBindCount; i++)
        {
            var count = multiBindCounts[i];
            var at = multiBindAt[i];

            var following = at + 1 < bindCount ? binds[at + 1].DrawCount - binds[at].DrawCount : 0;

            sb.AppendLine($"  idx {at,3}: {count} target(s), {following} draw(s) follow");
            for (var t = 0; t < count; t++)
            {
                var info = multiBindTargets[(i * MaxTargetsPerBind) + t];
                sb.AppendLine($"      rtv{t} | {FormatName(info.Format),-28} | {info.Width,4}x{info.Height,-4} | 0x{info.Resource:X}");
            }
        }
    }

    /// <summary>Names a DXGI format, falling back to its numeric value so an unlisted one is still reportable.</summary>
    /// <param name="format">The format to name.</param>
    /// <returns>The format's name, or its numeric value.</returns>
    private static string FormatName(DXGI_FORMAT format) => format switch
    {
        DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM => "R8G8B8A8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB => "R8G8B8A8_UNORM_SRGB",
        DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_TYPELESS => "R8G8B8A8_TYPELESS",
        DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM => "B8G8R8A8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB => "B8G8R8A8_UNORM_SRGB",
        DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM => "R10G10B10A2_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT => "R16G16B16A16_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM => "R16G16B16A16_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R11G11B10_FLOAT => "R11G11B10_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT => "R16G16_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM => "R16G16_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM => "R8G8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT => "R32_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT => "R16_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R8_UNORM => "R8_UNORM",
        _ => $"format {(int)format}",
    };

    /// <summary>Records every target of a multi-target bind, so a G-buffer's channel layout is readable.</summary>
    /// <param name="numViews">The bind's view count.</param>
    /// <param name="ppRtvs">The bind's render-target view array.</param>
    private void RecordMultiTarget(uint numViews, nint ppRtvs)
    {
        if (multiBindCount >= MaxMultiBinds || ppRtvs == 0)
            return;

        var slot = multiBindCount;
        var written = 0;
        var views = (ID3D11RenderTargetView**)ppRtvs;

        for (var i = 0; i < numViews && i < MaxTargetsPerBind; i++)
        {
            var view = views[i];
            if (view == null)
                continue;

            // The view's format, not the texture's: a typeless texture is viewed as UNORM by one pass and SRGB by
            // another, and writing through the wrong one shifts every colour.
            D3D11_RENDER_TARGET_VIEW_DESC viewDesc;
            view->GetDesc(&viewDesc);

            ID3D11Resource* resource = null;
            view->GetResource(&resource);
            if (resource == null)
                continue;

            uint w = 0, h = 0;
            if (ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)resource, out var tex))
            {
                D3D11_TEXTURE2D_DESC desc;
                tex.Get()->GetDesc(&desc);
                w = desc.Width;
                h = desc.Height;
                tex.Dispose();
            }

            multiBindTargets[(slot * MaxTargetsPerBind) + written] = new TargetInfo((nint)resource, viewDesc.Format, w, h);
            written++;

            resource->Release(); // used for comparison only; the view keeps the resource alive
        }

        if (written == 0)
            return;

        multiBindAt[slot] = bindCount;
        multiBindCounts[slot] = written;
        multiBindCount++;
    }

    private void Flush()
    {
        lastFrameBindCount = bindCount;

        var sb = new StringBuilder();
        var bbList = new StringBuilder();
        for (var i = 0; i < knownBackbufferCount; i++)
            bbList.Append($"0x{knownBackbuffers[i]:X} ");
        sb.AppendLine($"Draw3D RT-bind sequence, one frame ({bindCount} binds, {drawCounter} draws; {knownBackbufferCount} backbuffers: {bbList}; present buffer 0x{presentBuffer:X})");

        var bbIdx = new StringBuilder();
        for (var i = 0; i < bindCount; i++)
        {
            if (binds[i].IsBackbuffer)
                bbIdx.Append(i).Append(binds[i].HasDsv ? "(+dsv) " : " ");
        }

        sb.AppendLine($"  backbuffer binds at idx: {(bbIdx.Length == 0 ? "(none learned - re-run)" : bbIdx.ToString())}");
        sb.AppendLine("  'draws' = draw calls made into the PREVIOUS row's target (1 = a blit; a burst = a real pass, e.g. the UI).");
        sb.AppendLine("  A single-target two-channel float at half the display size is the shape of a velocity buffer.");
        sb.AppendLine("  idx | draws | #rtv | backbuffer | dsv |  size    | format                       | rtv0 resource");
        for (var i = 0; i < bindCount; i++)
        {
            var b = binds[i];
            var draws = i == 0 ? b.DrawCount : b.DrawCount - binds[i - 1].DrawCount;
            sb.AppendLine($"  {i,3} | {draws,5} |  {b.NumViews,2}  |    {(b.IsBackbuffer ? "YES" : " - ")}    | {(b.HasDsv ? "yes" : " - ")} | {b.Width,4}x{b.Height,-4} | {FormatName(b.Format),-28} | 0x{b.Rtv0Resource:X}");
        }

        AppendMultiTargets(sb);

        NoireLogger.LogInfo(sb.ToString(), "Draw3D");
        NoireLogger.PrintToChat($"Draw3D: captured {bindCount} binds / {drawCounter} draws this frame.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        InjectionEnabled = false;
        Injector = null;
        ShadowInjector = null;
        ShadowFrameBoundary = null;
        Capture = null; // owned and disposed by the hub
        shadowProbe?.Dispose();
        shadowProbe = null;

        omHook?.Dispose();
        drawIndexedHook?.Dispose();
        drawHook?.Dispose();
        drawIndexedInstancedHook?.Dispose();
        drawInstancedHook?.Dispose();
        rsSetViewportsHook?.Dispose();
        omHook = null;
        drawIndexedHook = null;
        drawHook = null;
        drawIndexedInstancedHook = null;
        drawInstancedHook = null;
        rsSetViewportsHook = null;
        omDetour = null;
        drawIndexedDetour = null;
        drawDetour = null;
        drawIndexedInstancedDetour = null;
        drawInstancedDetour = null;
        rsSetViewportsDetour = null;
    }

    /// <summary>
    /// Options for a hook on a graphics device call: no fault guard, no counters and no verification, because these
    /// run thousands of times per frame and the addresses are vtable slots XIVClientStructs never describes.
    /// </summary>
    /// <param name="name">The hook name.</param>
    /// <returns>The hook options.</returns>
    private static HookOptions DeviceHookOptions(string name) => new()
    {
        Name = name,
        AutoEnable = false,
        Guard = HookGuardMode.None,
        Verification = HookVerificationPolicy.Ignore,
        CollectStats = false,
    };

}
