using NoireLib.Hooking;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// The render-thread hook on the game's D3D11 immediate context. It serves three jobs on the
/// <c>ID3D11DeviceContext::OMSetRenderTargets</c> vtable slot:
/// <list type="bullet">
/// <item><b>Diagnostics</b> (<c>/noire3d rtlog</c>): records one frame's render-target bind sequence and
/// the draw counts between binds, so the pre-UI injection point can be identified from real data.</item>
/// <item><b>Pre-UI injection</b> (<c>/noire3d ontop</c>): the game composites the final world image into a
/// "present buffer" and then draws its native UI (nameplates, HUD) into that same buffer before blitting
/// to the swapchain. By learning that present buffer (the render target bound right before the swapchain
/// backbuffer) and firing a callback at its 2nd bind of the frame - after the world copy, before the UI
/// burst - Draw3D can composite its layer UNDER the native UI.</item>
/// <item><b>Camera frame phase</b>: the main scene pass is fingerprinted here (the bind whose depth-stencil
/// is RTM.DepthStencil), which snapshots the struct camera and drives <see cref="CameraConstantCapture"/> -
/// the per-frame commit of the exact camera constants the GPU rasterizes with.</item>
/// </list>
/// Opt-in (installed only on first use); the OM hook stays disabled unless a capture is armed or injection
/// is enabled; the four draw-count hooks are enabled only for the single frame of a capture.
/// </summary>
internal sealed unsafe class RenderTargetTap : IDisposable
{
    // ID3D11DeviceContext vtable slots.
    private const int SlotDrawIndexed = 12;
    private const int SlotDraw = 13;
    private const int SlotDrawIndexedInstanced = 20;
    private const int SlotDrawInstanced = 21;
    private const int SlotOmSetRenderTargets = 33;
    private const int MaxBinds = 640; // a full frame including the late UI stage
    private const int MaxMultiBinds = 32;    // multi-target binds in a frame: the G-buffer plus the post-process ones
    private const int MaxTargetsPerBind = 8; // D3D11's simultaneous render target limit
    // The measured G-buffer pass binds five. A lower floor is actively dangerous rather than merely loose: the
    // frame also contains a THREE-target bind on the same scene depth, carrying a different target set - its
    // third slot is the half-float buffer, where the five-target pass has albedo. Arming on that one and then
    // firing on a draw issued while it is still bound would write albedo into a half-float target and the
    // material scalars into the normal buffer.
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

    /// <summary>
    /// One render-target bind. <see cref="Format"/> is the VIEW's format, not the texture's: a typeless
    /// texture is viewed as UNORM by one pass and SRGB by another, and it is recorded for single-target binds
    /// too because that is the only way a pass is identifiable by what it writes rather than by its size. The
    /// velocity buffer a temporal resolve consumes is exactly that case - one target, half resolution, and
    /// nothing but its format distinguishes it from any other half-resolution post-process step.
    /// </summary>
    private readonly record struct Bind(uint NumViews, nint Rtv0Resource, DXGI_FORMAT Format, uint Width, uint Height, bool HasDsv, bool IsBackbuffer, int DrawCount);

    /// <summary>One target of a multi-target bind, for reading a G-buffer's layout.</summary>
    private readonly record struct TargetInfo(nint Resource, DXGI_FORMAT Format, uint Width, uint Height);

    private HookWrapper<OmSetRenderTargetsFn>? omHook;
    private HookWrapper<DrawIndexedFn>? drawIndexedHook;
    private HookWrapper<DrawFn>? drawHook;
    private HookWrapper<DrawIndexedInstancedFn>? drawIndexedInstancedHook;
    private HookWrapper<DrawInstancedFn>? drawInstancedHook;
    // Kept for the frame walker, which copies a target mid-frame and therefore needs a device of its own; every
    // other job here reads the bind sequence and needs none.
    private RenderDevice? device;
    private OmSetRenderTargetsFn? omDetour;
    private DrawIndexedFn? drawIndexedDetour;
    private DrawFn? drawDetour;
    private DrawIndexedInstancedFn? drawIndexedInstancedDetour;
    private DrawInstancedFn? drawInstancedDetour;
    private nint gameContext;

    private readonly Bind[] binds = new Bind[MaxBinds];
    private int bindCount;

    // The shadow-pass probe, created on first arm. Owned here because the tap is the one component that sees
    // the binds and draws it needs; disposed with the tap.
    private ShadowProbe? shadowProbe;

    // Multi-target binds get their whole target set recorded, not just the first one. A deferred renderer's
    // G-buffer is only legible as a set: which channel carries the normal, and in what precision, is the
    // difference between writing into it correctly and writing plausible nonsense. Sized flat and up front so
    // the render thread never allocates.
    private readonly int[] multiBindAt = new int[MaxMultiBinds];
    private readonly TargetInfo[] multiBindTargets = new TargetInfo[MaxMultiBinds * MaxTargetsPerBind];
    private readonly int[] multiBindCounts = new int[MaxMultiBinds];
    private int multiBindCount;
    private int drawCounter;
    private volatile int state; // 0 = idle, 1 = warming up, 2 = capturing
    private int warmupLeft;

    // Frame walker: write out what a span of binds produced. Each one is a full-resolution copy plus a
    // synchronous map, so the frame it runs on stalls badly - it is a one-shot diagnostic and the span is
    // capped rather than left to a caller's arithmetic.
    private const int MaxFrameDumps = 16;
    private int dumpFrom = -1;
    private int dumpCount;
    private int dumpStride; // > 0 = spread across the whole frame instead of a contiguous span
    private int dumpsWritten;
    private string dumpFolder = string.Empty;

    /// <summary>
    /// Binds in the last captured frame. The frame is <b>not</b> a fixed length - it moves with what is on
    /// screen - so an index read from one run does not name the same pass in the next, and a span picked from
    /// an earlier log can land on an entirely different stage. This is what a sweep sizes itself against.
    /// </summary>
    private int lastFrameBindCount;

    // The swapchain rotates through several backbuffer textures (flip model); a bind is "to the
    // backbuffer" if its target matches ANY of them. Accumulated from the per-present current buffer.
    private readonly nint[] knownBackbuffers = new nint[8];
    private int knownBackbufferCount;

    // Injection state.
    private nint presentBuffer;             // committed present-composition buffer (learned last frame)
    private nint candidatePresentBuffer;    // RTV seen right before a swapchain bind this frame
    private nint lastNonBackbufferRtv;      // running previous RTV (candidate source)
    private int presentBufferBinds;         // present-buffer binds so far this frame
    private volatile bool injecting;        // re-entrancy guard around the injection callback

    // World-camera snapshot. The injected overlay must be projected with the same camera the game rasterized the
    // world with, or it drifts relative to world geometry under camera motion. The player camera is snapshotted here on
    // the render thread at the MAIN scene pass (the bind whose depth-stencil is RenderTargetManager.DepthStencil) - not
    // merely the frame's first depth-bound bind, which is a shadow-map pass. Under load the game advances the camera
    // between the shadow passes and the main pass, so a shadow-pass snapshot still differs from the main-pass camera by
    // a small sub-pixel amount even with no frame lag, and the layer swims. The snapshot is taken at the FIRST main-scene
    // bind (the opaque pass) and locked, and falls back to the first depth bind on a frame with no main pass.
    private GameRenderSources.CameraData worldCamera;
    private volatile bool hasWorldCamera;
    private bool mainDepthSeen;      // the main-scene depth (RTM.DepthStencil) was captured this frame - locked, later binds ignored
    private nint frameSceneDepthTex; // RTM.DepthStencil texture pointer, cached per present for the main-pass fingerprint

    /// <summary>
    /// Fired inside the game's G-buffer pass, at its FIRST draw, with the pass's targets already bound.<br/>
    /// Firing at the bind instead would be too early: the game clears and sets up between binding the targets
    /// and issuing the first draw, so anything written at bind time can be wiped. Firing at the first draw also
    /// puts the callback after the frame's camera-constant commit, which is what the injected geometry needs to
    /// be projected with.<br/>
    /// The callback must leave the pipeline exactly as it found it. It draws into the game's own targets, so
    /// unrestored state corrupts the game's frame rather than Draw3D's.
    /// </summary>
    public Action? GBufferInjector { get; set; }

    /// <summary>
    /// Whether the G-buffer injection is wanted this frame.<br/>
    /// Setting it keeps the four per-draw hooks enabled, and those fire a managed callback on EVERY draw the
    /// game makes - hundreds to thousands per frame. It must therefore track whether there is actually work
    /// queued, not latch on at the first use and stay on.
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

    /// <summary>When true the detour skips its work - set around Draw3D's OWN binds so they never interfere.</summary>
    public bool SuppressSelf;

    /// <summary>Whether the injection callback is running right now - Draw3D's own D3D calls made inside it must not be observed.</summary>
    public bool IsInjecting => injecting;

    /// <summary>
    /// The camera-constant capture riding this tap's frame phase: the frame boundary and the main-pass commit are
    /// signalled from here, and its upload-path taps follow the injection enable state. Null when not installed.
    /// </summary>
    public CameraConstantCapture? Capture;

    /// <summary>Enables the pre-UI injection path (the OM hook must be installed and stays enabled while set).</summary>
    public bool InjectionEnabled { get; private set; }

    /// <summary>Callback fired (render thread) at the injection point with the present-buffer resource. Returns true if it rendered.</summary>
    public Func<nint, bool>? Injector { get; set; }

    /// <summary>
    /// The committed present-composition buffer, or 0 before one has been learned. The same resource the
    /// <see cref="Injector"/> is handed, still readable at present time - by then the game has drawn its native UI
    /// into it, which is what lets the over-everything composite difference the two states to find the UI.
    /// </summary>
    public nint PresentBuffer => presentBuffer;

    /// <summary>
    /// The player camera captured on the render thread at this frame's first depth pass - the exact view/projection
    /// the world currently in the present buffer was rasterized with. The inject callback projects the overlay with
    /// this so it stays locked to the world at any frame-rate. False until this frame's first depth pass is seen
    /// (e.g. a menu/loading frame with no world pass); the caller then falls back to the live camera.
    /// </summary>
    public bool TryGetWorldCamera(out GameRenderSources.CameraData camera)
    {
        camera = worldCamera;
        return hasWorldCamera;
    }

    /// <summary>
    /// Whether this frame's world-camera snapshot came from the main scene pass (RTM.DepthStencil fingerprint matched)
    /// rather than the first-depth-bind fallback. False on every frame means the fingerprint is not matching in-game,
    /// so the camera silently degrades to the less accurate shadow-pass fallback until this is investigated.
    /// </summary>
    public bool WorldCameraIsMainPass => mainDepthSeen;

    /// <summary>True once the hooks have been installed (they may still be disabled).</summary>
    public bool Installed => omHook != null;

    /// <summary>Installs the hooks (disabled) by reading the immediate context's vtable slots. One-time.</summary>
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
            omHook = new HookWrapper<OmSetRenderTargetsFn>((nint)vtable[SlotOmSetRenderTargets], omDetour, autoEnable: false, name: "Draw3D.OMSetRenderTargets");
            drawIndexedDetour = DrawIndexedDetour;
            drawIndexedHook = new HookWrapper<DrawIndexedFn>((nint)vtable[SlotDrawIndexed], drawIndexedDetour, autoEnable: false, name: "Draw3D.DrawIndexed");
            drawDetour = DrawDetour;
            drawHook = new HookWrapper<DrawFn>((nint)vtable[SlotDraw], drawDetour, autoEnable: false, name: "Draw3D.Draw");
            drawIndexedInstancedDetour = DrawIndexedInstancedDetour;
            drawIndexedInstancedHook = new HookWrapper<DrawIndexedInstancedFn>((nint)vtable[SlotDrawIndexedInstanced], drawIndexedInstancedDetour, autoEnable: false, name: "Draw3D.DrawIndexedInstanced");
            drawInstancedDetour = DrawInstancedDetour;
            drawInstancedHook = new HookWrapper<DrawInstancedFn>((nint)vtable[SlotDrawInstanced], drawInstancedDetour, autoEnable: false, name: "Draw3D.DrawInstanced");
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

    /// <summary>Turns the pre-UI injection path on or off. Keeps the OM hook enabled while on.</summary>
    public void SetInjection(bool enabled)
    {
        InjectionEnabled = enabled;
        RefreshOmHookState();
    }

    /// <summary>Arms a one-frame diagnostic capture after a short warm-up (so every swapchain buffer is learned first).</summary>
    /// <summary>
    /// Arms the shadow-pass probe for the next frame: every depth-only bind's target, and the VS constants at
    /// each one's first draw. The report lands in the log. See <see cref="ShadowProbe"/> for why this exists.
    /// </summary>
    public void ArmShadowProbe()
    {
        if (omHook == null)
            return;

        shadowProbe ??= new ShadowProbe();
        shadowProbe.Arm();
    }

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

    /// <summary>
    /// Arms a capture that also writes out what a span of binds actually produced, as images.<br/>
    /// <b>Why this exists.</b> A wrong pixel in the final image says nothing about which pass made it wrong, and
    /// naming a suspect pass and toggling its game setting only ever rules out the passes a setting exposes.
    /// Reading the intermediate targets walks the frame in order and finds the first one where the pixel is
    /// already wrong, which identifies the pass by observation rather than by elimination.
    /// </summary>
    /// <param name="from">First bind index to write out (indices come from a <c>rtlog</c> run).</param>
    /// <param name="count">How many consecutive binds to write out.</param>
    /// <param name="folder">Where the images go.</param>
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
    /// Arms a dump spread evenly across the whole frame rather than over a chosen span.<br/>
    /// This is the one to reach for first. A bind index is not a stable name for a pass - the frame grows and
    /// shrinks with what is on screen, so a span chosen from an earlier log can land nowhere near the pass it
    /// named. A sweep covers the frame end to end in a single run, which brackets where a pixel changes without
    /// depending on any index surviving between runs.
    /// </summary>
    /// <param name="count">How many binds to write out, spread across the frame.</param>
    /// <param name="folder">Where the images go.</param>
    /// <returns>The stride chosen, or 0 when the hooks are not installed.</returns>
    public int ArmFrameSweep(int count, string folder)
    {
        if (omHook == null)
            return 0;

        ArmFrameDump(0, count, folder);

        // Works on the first run, with no capture needed beforehand: a frame that has never been measured is
        // assumed to be about this long, and the stride only decides the spacing of the samples. Guessing high
        // costs a sparser sweep, never a failed one, and the next run sizes itself against the real length.
        const int AssumedFrameBinds = 128;
        var length = lastFrameBindCount > 0 ? lastFrameBindCount : AssumedFrameBinds;
        dumpStride = Math.Max(1, length / dumpCount);
        return dumpStride;
    }

    /// <summary>
    /// Writes out the target that has just finished being drawn into, when it falls in the armed span.<br/>
    /// Runs before the game's new bind is applied, which is the only moment the previous target's contents are
    /// final: once the next pass starts drawing, whatever it produced is gone.
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

            // The stencil plane alongside it: the game's light volumes test a mark written during the geometry
            // pass, so it exists only between those two passes and nothing at the end of the frame can read it.
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
    /// Per-present bookkeeping (render thread): learns the swapchain backbuffers, commits the present buffer
    /// learned this frame, resets per-frame counters, and drives the diagnostic-capture state machine.
    /// </summary>
    public void OnPresent(nint backbufferTexture)
    {
        RememberBackbuffer(backbufferTexture);
        Capture?.OnFrameBoundary();
        shadowProbe?.OnFrameBoundary();

        // Commit the present buffer observed this frame for next frame's injection; reset per-frame state.
        if (candidatePresentBuffer != 0)
            presentBuffer = candidatePresentBuffer;
        candidatePresentBuffer = 0;
        lastNonBackbufferRtv = 0;
        presentBufferBinds = 0;
        hasWorldCamera = false; // re-snapshot at the next frame's main scene pass
        mainDepthSeen = false;

        // Once per frame: the pass is re-armed at its next bind, so a frame that never runs one injects nothing.
        gbufferPassArmed = false;
        gbufferDoneThisFrame = false;
        // Cache the main scene-depth texture for next frame's main-pass fingerprint (stable across frames; a resize just
        // costs one frame of first-depth fallback until it is refreshed here).
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
                    RefreshOmHookState(); // also enables the draw hooks for the capture frame
                }

                break;
            case 2:
                Flush();
                state = 0;
                RefreshOmHookState(); // draw hooks stay on only if the camera capture still wants its draw signal
                break;
        }
    }

    private void RefreshOmHookState()
    {
        var wanted = InjectionEnabled || state == 2;
        if (omHook != null && omHook.IsEnabled != wanted)
            omHook.SetEnabled(wanted);

        // The camera-constant capture only means something while the injection point provides the main-pass signal;
        // its per-frame commit runs at the main pass's first draw, so the draw hooks follow its active state too.
        Capture?.SetActive(InjectionEnabled);
        RefreshDrawHookState();
    }

    /// <summary>Draw hooks serve two masters: the one-frame rtlog capture, and the camera-constant commit signal.</summary>
    private void RefreshDrawHookState()
        => SetDrawHooksEnabled(state == 2 || GBufferInjectionEnabled || (Capture?.WantsDrawSignal ?? false));

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
        // Before the game's bind is applied: the target it is about to replace has just received its last draw,
        // so this is the only point at which what that pass produced can still be read.
        if (state == 2 && dumpFrom >= 0 && !injecting && !SuppressSelf && context == gameContext)
            DumpFinishedBind();

        omHook!.Original(context, numViews, ppRtvs, pDsv); // apply the game's bind first

        if (injecting || SuppressSelf || context != gameContext)
            return;

        var rtv0 = ResolveRtv0Resource(numViews, ppRtvs);

        // The shadow probe watches depth-only binds: no color resolved, a depth-stencil present. That shape
        // covers shadow maps and the scene's own depth pre-pass; the probe records which is which rather
        // than guessing here, because a diagnostic that filters is a diagnostic that can hide the answer.
        if (shadowProbe is { Armed: true } && rtv0 == 0 && pDsv != 0)
            shadowProbe.OnDepthOnlyBind(pDsv, IsMainSceneDepth(pDsv));

        // Learn the present-composition buffer: the RTV bound right before a swapchain backbuffer bind.
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

        // Snapshot the camera the world in the present buffer was rasterized with, at the FIRST main-scene-depth bind
        // (the opaque depth pre-pass / colour pass - where the visible pixels are drawn). NOT the last such bind: later
        // passes that re-bind scene depth (transparency, water, depth-reading post-fx) run with a newer camera and
        // overshoot the pixels, reintroducing swim even though it would remove any snapshot lag. A shadow-map pass
        // binds depth first, so it seeds a provisional snapshot that the first main pass replaces; locked after that
        // so no later bind can overwrite it.
        if (InjectionEnabled && !mainDepthSeen && pDsv != 0 && rtv0 != 0 && !IsBackbuffer(rtv0))
        {
            if (IsMainSceneDepth(pDsv) && GameRenderSources.TryGetCamera(out var mainSnap))
            {
                worldCamera = mainSnap;
                hasWorldCamera = true;
                mainDepthSeen = true; // lock: the opaque camera is captured, ignore later (newer) binds

                // Arm the camera-constant commit for this pass's FIRST draw (see CameraConstantCapture.OnMainPassBind:
                // the game binds and uploads its camera block between this bind and that draw, so committing here
                // would read the previous pass's bindings). The struct snapshot above stays the fallback.
                Capture?.OnMainPassBind();
            }
            else if (!hasWorldCamera && GameRenderSources.TryGetCamera(out var provisionalSnap))
            {
                worldCamera = provisionalSnap; // first depth bind (shadow pass) - a fallback until the main pass replaces it
                hasWorldCamera = true;
            }
        }

        // The G-buffer pass: several targets bound together with the main scene depth. Post-process passes also
        // bind multiple targets, but never with the scene's depth-stencil, which is what separates them.
        if (GBufferInjectionEnabled && !gbufferDoneThisFrame && numViews >= GBufferMinTargets && pDsv != 0 && IsMainSceneDepth(pDsv))
            gbufferPassArmed = true;

        if (state == 2 && bindCount < MaxBinds)
            Record(numViews, rtv0, pDsv, ppRtvs);

        // Pre-UI injection: at the present buffer's Nth bind (after the world copy, before the UI burst).
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
    /// Runs at every game draw: the camera commit, then the G-buffer injection if this is the injected pass's
    /// first draw. Order matters - the injected geometry is projected with the camera the commit establishes.
    /// </summary>
    private void OnDraw(nint context)
    {
        Capture?.OnGameDraw(context); // no-op except at the main pass's first draw (the commit moment)

        if (shadowProbe is { Armed: true } && !injecting && !SuppressSelf && context == gameContext)
            shadowProbe.OnGameDraw((TerraFX.Interop.DirectX.ID3D11DeviceContext*)context);

        if (!gbufferPassArmed || GBufferInjector is not { } injector)
            return;

        // Once per frame regardless of outcome: a callback that throws must not be retried against every
        // remaining draw of the pass.
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
        resource->Release(); // for-comparison only; the RTV keeps the resource alive
        return res;
    }

    /// <summary>
    /// Whether a bound depth-stencil view targets the main scene depth (RTM.DepthStencil) - the fingerprint that
    /// distinguishes the main world pass from the shadow-map passes that render first. Compares the DSV's resource to
    /// the per-frame cached scene-depth texture; false (fall back to the first depth bind) when the cache is unset.
    /// </summary>
    private bool IsMainSceneDepth(nint pDsv)
    {
        if (frameSceneDepthTex == 0 || pDsv == 0)
            return false;

        ID3D11Resource* resource = null;
        ((ID3D11DepthStencilView*)pDsv)->GetResource(&resource);
        if (resource == null)
            return false;

        var match = (nint)resource == frameSceneDepthTex;
        resource->Release(); // for-comparison only; the DSV keeps the resource alive
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
    /// The G-buffer's target resources from the last capture, in bind order.<br/>
    /// Chosen as the multi-target bind with the most draws behind it: a G-buffer pass is where the world is
    /// drawn, so it carries hundreds of draws, while the post-process binds that share the frame carry one
    /// apiece. Counting draws rather than targets is what separates them - the frame's widest bind is an
    /// eight-target post-process pass at quarter resolution, not the G-buffer.
    /// </summary>
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

            // Ranked on target count first, then resolution, then draws - in that order deliberately. Draws
            // alone picked a two-target post-process bind over the five-target G-buffer, because a
            // post-process pass can carry more draws than a sparsely populated geometry pass. Target count is
            // the stable discriminator; resolution then rejects the wide half- and quarter-resolution
            // post-process binds, which have as many targets as the G-buffer but never its size.
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
    /// Reports the target set of every multi-target bind.<br/>
    /// The G-buffer pass is the one to look for: several full-resolution targets with a depth-stencil, followed
    /// by a burst of draws. Its formats say what may be written into each channel, which is what an injected
    /// draw has to match exactly - a normal written into the wrong precision is lit, just lit wrongly.
    /// </summary>
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

            // The draws that followed this bind are what separate a geometry pass from a post-process blit.
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

            // The view's own format is the one that matters. A typeless texture can be viewed as UNORM by one
            // pass and as SRGB by another, and writing through the wrong one shifts every colour.
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

            resource->Release(); // for-comparison only; the view keeps the resource alive
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
        DiagnosticChat.Print($"Draw3D: captured {bindCount} binds / {drawCounter} draws this frame.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        InjectionEnabled = false;
        Injector = null;
        Capture = null; // owned and disposed by the hub
        shadowProbe?.Dispose();
        shadowProbe = null;

        omHook?.Dispose();
        drawIndexedHook?.Dispose();
        drawHook?.Dispose();
        drawIndexedInstancedHook?.Dispose();
        drawInstancedHook?.Dispose();
        omHook = null;
        drawIndexedHook = null;
        drawHook = null;
        drawIndexedInstancedHook = null;
        drawInstancedHook = null;
        omDetour = null;
        drawIndexedDetour = null;
        drawDetour = null;
        drawIndexedInstancedDetour = null;
        drawInstancedDetour = null;
    }
}
