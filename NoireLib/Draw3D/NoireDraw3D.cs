using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using NoireLib.Draw3D.Core;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D;

/// <summary>
/// The Draw3D hub: a real D3D11 world renderer that draws after the game's frame is complete -
/// glowless, color-exact, hardware-clipped, under every plugin window, with zero hooks and zero ImGui.<br/>
/// Lazy-initialized on first access (NoireLib must be initialized first); disposal is wired through
/// <see cref="NoireLibMain.RegisterOnDispose"/> automatically.<br/>
/// Draw retained content via <see cref="MainScene"/>, per-frame markers via <see cref="Im"/>.
/// </summary>
public static unsafe partial class NoireDraw3D
{
    private const string DisposeKey = "NoireLib.Draw3D.NoireDraw3D";
    private const string CommandName = "/noire3d";

    private static readonly object InitLock = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly ConcurrentQueue<Action> ReleaseQueue = new();

    private static bool initialized;
    private static bool disposed;
    private static bool deviceObjectsReady;
    private static bool commandRegistered;

    private static RenderDevice? renderDevice;
    private static StateGuard? stateGuard;
    private static StateCache? stateCache;
    private static ShaderLibrary? shaderLibrary;
    private static ScenePass? scenePass;
    private static Compositor? compositor;
    private static RenderTarget? sceneRt;
    private static RenderTarget? outlineMaskRt; // RGBA silhouette-coverage mask for the selection outline pass
    private static RenderTarget? outlineVisRt;  // per-silhouette-pixel worldVisible flag (r) for occluding the outline
    private static DepthTarget? privateDepth;
    private static SceneDepth? sceneDepth;
    private static UiMaskSource? uiMaskSource;
    private static UiMaskHealth? uiMaskHealth;
    private static RenderStats? renderStats;
    private static RenderTargetTap? renderTargetTap;
    private static DepthProbe? depthProbe; // cached, non-blocking depth readback for the wall-occlusion hover test

    private static ComPtr<ID3D11RenderTargetView> backbufferRtv;
    private static nint backbufferPtr;

    // Pre-UI injection: an RTV over the game's present-composition buffer, and the "did we inject this
    // frame" flag that tells the present-time path to skip its own swapchain composite (fallback otherwise).
    private static ComPtr<ID3D11RenderTargetView> presentRtv;
    private static nint presentRtvPtr;
    private static uint presentRtvWidth, presentRtvHeight;
    private static bool renderUnderNativeUi = true; // on by default - the layer reads under the game HUD/nameplates
    private static bool injectionInitialized;       // one-shot: arm the tap on the first frame that has a device
    private static volatile bool injectedSinceLastPresent;

    // Native-UI depth-write: a writable DSV over the game's scene depth, so nameplates occlude against 3D objects
    // standing in front of them (needs RenderUnderNativeUi; waives Law 5). On by default alongside the injection.
    private static GameDepthTarget? gameDepthTarget;
    private static bool nativeUiDepthWrite = true;

    // Native-UI protection backing (grouped under the NativeUi config; the old flat properties are obsolete forwarders).
    private static bool protectGameUi = true;
    private static NativeUiProtectionMode nativeUiProtectionMode = NativeUiProtectionMode.DepthAware;
    private static float nativeUiProtectionDimFactor;

    private static Scene3D? mainScene;
    private static ImDraw3D? im;
    private static readonly List<Scene3D> Scenes = new();
    private static readonly List<RenderView> Views = new();
    private static readonly Vector4[] ProtectRects = new Vector4[128];
    private static readonly float[] ProtectFactors = new float[128];
    private static readonly float[] PlateDistances = new float[128];

    private static long frameId;
    private static FrameContext lastFrame;
    private static bool lastFrameValid;
    private static GameRenderSources.CameraData lastCameraData;

    // The present-time path projects with a camera sampled on the sim thread each Framework.Update, which matches
    // the presented backbuffer more closely than a live read at present time.
    private static GameRenderSources.CameraData frameworkCamera;
    private static volatile bool frameworkCameraValid;
    private static bool frameworkHooked;

    // The injected layer must be projected with the same camera the game rasterized the world in the present buffer
    // with, or it drifts relative to world geometry under camera motion. The render-thread tap snapshots that camera
    // at the frame's first depth pass and the inject path projects with exactly it (see TryGetInjectCamera /
    // RenderTargetTap.TryGetWorldCamera), so the layer stays locked to the world independent of frame-rate.

    private static bool keepDrawingWhenUiHidden = true;
    private static bool forcedAutoHide, forcedUserHide, forcedCutsceneHide, forcedGposeHide;

    private static int passFailStreak;
    private static bool passFaultLogged;
    internal static bool Wireframe;

    // ---------------------------------------------------------------- public surface

    /// <summary>The main retained scene. First access initializes the renderer.<br/>Throws <see cref="InvalidOperationException"/> when NoireLib is not initialized.</summary>
    public static Scene3D MainScene
    {
        get
        {
            EnsureInitialized();
            return mainScene!;
        }
    }

    /// <summary>The immediate-mode drawing layer. First access initializes the renderer.</summary>
    public static ImDraw3D Im
    {
        get
        {
            EnsureInitialized();
            return im!;
        }
    }

    /// <summary>Master switch. Setting it back to true after a renderer fault re-arms the renderer.</summary>
    public static bool Enabled
    {
        get => enabled;
        set
        {
            enabled = value;
            if (value)
            {
                passFailStreak = 0;
                passFaultLogged = false;
            }
        }
    }

    private static bool enabled = true;

    /// <summary>0–1 opacity applied to the whole 3D layer at composite time (linear under premultiplication - true layer transparency).</summary>
    public static float LayerOpacity { get; set; } = 1f;

    /// <summary>Deprecated: use <see cref="NativeUi"/>.<c>Protection</c>.</summary>
    [Obsolete("Grouped under NoireDraw3D.NativeUi.Protection.")]
    public static NativeUiProtectionMode NativeUiProtection
    {
        get => nativeUiProtectionMode;
        set => nativeUiProtectionMode = value;
    }

    /// <summary>Deprecated: use <see cref="NativeUi"/>.<c>DimFactor</c>.</summary>
    [Obsolete("Grouped under NoireDraw3D.NativeUi.DimFactor.")]
    public static float NativeUiProtectionDimFactor
    {
        get => nativeUiProtectionDimFactor;
        set => nativeUiProtectionDimFactor = value;
    }

    /// <summary>Deprecated: use <see cref="NativeUi"/>.<c>Protect</c>.</summary>
    [Obsolete("Grouped under NoireDraw3D.NativeUi.Protect.")]
    public static bool ProtectGameUi
    {
        get => protectGameUi;
        set => protectGameUi = value;
    }

    /// <summary>Deprecated: use <see cref="NativeUi"/>.<c>RenderUnder</c>.</summary>
    [Obsolete("Grouped under NoireDraw3D.NativeUi.RenderUnder.")]
    public static bool RenderUnderNativeUi
    {
        get => renderUnderNativeUi;
        set => SetRenderUnderNativeUi(value);
    }

    /// <summary>Applies the render-under-native-UI state (used by <see cref="NativeUi"/> and the obsolete flat property).</summary>
    internal static void SetRenderUnderNativeUi(bool value)
    {
        renderUnderNativeUi = value;
        ApplyInjectionState();
    }

    /// <summary>
    /// Arms or disarms the render-thread injection to match <see cref="renderUnderNativeUi"/>. When the device
    /// isn't ready yet (very first frames) the tap can't install; the desired state is kept and the frame loop
    /// retries once a device exists, so the default-on state comes up on its own.
    /// </summary>
    private static void ApplyInjectionState()
    {
        if (renderUnderNativeUi)
        {
            var tap = EnsureRenderTargetTap();
            if (tap == null)
                return; // no device yet - retried from the frame loop

            tap.Injector = InjectComposite;
            tap.SetInjection(true);
        }
        else
        {
            renderTargetTap?.SetInjection(false);
        }
    }

    /// <summary>Deprecated: use <see cref="NativeUi"/>.<c>DepthWrite</c>.</summary>
    [Obsolete("Grouped under NoireDraw3D.NativeUi.DepthWrite.")]
    public static bool NativeUiDepthWrite
    {
        get => nativeUiDepthWrite;
        set => nativeUiDepthWrite = value;
    }

    /// <summary>
    /// Keep the 3D layer rendering when the plugin's UI is hidden (cutscenes, GPose, user UI-hide).
    /// <b>Default true</b>: a world overlay should survive the UI-hide toggle - otherwise the whole scene
    /// vanishes when the player hides the HUD.<br/>
    /// <b>Plugin-wide side effect:</b> the switch lives on the shared UiBuilder, so it also keeps the host
    /// plugin's own windows drawing in those states; set it false (or hide those windows yourself on
    /// cutscene/GPose) if that isn't wanted.
    /// </summary>
    public static bool KeepDrawingWhenUiHidden
    {
        get => keepDrawingWhenUiHidden;
        set
        {
            keepDrawingWhenUiHidden = value;
            RefreshUiHideOverrides();
        }
    }

    /// <summary>
    /// Registers the <c>/noire3d</c> diagnostics command (validate, probe, stats, wire, smoke, clear, reset,
    /// rtlog, ontop, platedepth). <b>Opt-in:</b> it is not registered automatically - call this once (e.g. in your
    /// plugin constructor) to expose the command. No-op if it is already registered (by you or another plugin using
    /// NoireLib). The full toolkit is also available programmatically via <see cref="Diagnostics"/> without it.
    /// </summary>
    public static void EnableDiagnosticsCommand()
    {
        EnsureInitialized();
        if (!commandRegistered)
            RegisterCommand();
    }

    /// <summary>
    /// The single interaction front door: the genuinely-global input knobs (gestures, wall-occlusion, deselect,
    /// multi-select modifiers, debug) grouped on the one class you already use. The everyday path never touches it -
    /// hover / click / select live on <c>scene</c> / <c>node</c> / <c>editor</c>. This is the advanced surface for
    /// tuning gestures or registering a custom interactor.
    /// </summary>
    public static Draw3DInteraction Interaction { get; } = new();

    /// <summary>
    /// Consumer-supplied input arbitration for <see cref="Pick"/>: return false when the mouse is already
    /// claimed by UI. Draw3D reads no input itself (Law 11) - NoireUI or the host plugin wires this.
    /// </summary>
    public static Func<bool>? PickInputGate { get; set; }

    /// <summary>
    /// Convenience for <see cref="Im.ImShapeStyle.ExcludeVolumes"/>: nearby game objects as exclusion cylinders,
    /// so "cut this decal around the characters standing in it" is one call. <paramref name="filter"/> null =
    /// characters, monsters and NPCs (players, battle NPCs, event NPCs); <paramref name="radiusScale"/> widens
    /// (&gt;1) or tightens (&lt;1) each cylinder. Reads the object table - call it on the framework/draw thread
    /// (not from <see cref="Scene.Scene3D.OnPrepareFrame"/>), then hand the result to whichever decals should honor it.
    /// </summary>
    /// <param name="filter">Which objects to include; null uses the default character/monster/NPC set.</param>
    /// <param name="radiusScale">Multiplier on each object's hitbox radius (default 1).</param>
    public static IReadOnlyList<ExcludeVolume> GetActorExclusions(Func<IGameObject, bool>? filter = null, float radiusScale = 1f)
    {
        var list = new List<ExcludeVolume>();
        GameRenderSources.CollectActorExclusions(list, ScenePass.MaxActorVolumes, filter, radiusScale <= 0f ? 1f : radiusScale);
        return list;
    }

    /// <summary>Lighting parameters for <see cref="Materials.MaterialDomain.Lit"/> materials.</summary>
    public static Draw3DLighting Lighting { get; } = new();

    /// <summary>A snapshot of the renderer's counters (see <see cref="Draw3DStats"/>).</summary>
    public static Draw3DStats Stats => BuildStats();

    /// <summary>Programmatic access to the diagnostics toolkit (validate/probe/stats/wireframe/smoke) - command-independent.</summary>
    public static Draw3DDiagnostics Diagnostics { get; } = new();

    /// <summary>Raised whenever the self-disable ladder trips (a pipeline, feature, depth, pass, or the renderer was disabled).</summary>
    public static event Action<Draw3DFault>? OnFault;

    /// <summary>
    /// Creates an extra retained scene, rendered after <see cref="MainScene"/>. The returned scene is a self-contained
    /// ownership unit (<see cref="Scene3D.Dispose"/> frees its nodes, owned meshes and editors, and removes it from the
    /// renderer). Create as many as you like.
    /// </summary>
    /// <param name="name">Optional scene name.</param>
    public static Scene3D CreateScene(string? name = null)
    {
        EnsureInitialized();
        var scene = new Scene3D(name);
        lock (Scenes)
            Scenes.Add(scene);
        return scene;
    }

    /// <summary>
    /// Removes an extra scene from the renderer so it stops drawing. Prefer <see cref="Scene3D.Dispose"/>, which calls
    /// this <i>and</i> frees the scene's owned content; this bare form only unregisters. No-op for <see cref="MainScene"/>
    /// (permanent). Returns whether the scene was registered.
    /// </summary>
    /// <param name="scene">The scene to remove.</param>
    public static bool RemoveScene(Scene3D scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ReferenceEquals(scene, mainScene))
            return false;

        lock (Scenes)
            return Scenes.Remove(scene);
    }

    /// <summary>Clears every scene's selection. The interaction layer's "deselect" is global to the pointer even though selections are per-scene.</summary>
    internal static void ClearAllSelections()
    {
        lock (Scenes)
        {
            foreach (var scene in Scenes)
                scene.Selection.Clear();
        }
    }

    /// <summary>
    /// Creates a render-to-texture view of a scene (rendered before the main pass each frame).
    /// Dispose the view to stop it and release its target.
    /// </summary>
    /// <param name="scene">The scene to render.</param>
    /// <param name="camera">The virtual camera.</param>
    /// <param name="width">Output width in pixels.</param>
    /// <param name="height">Output height in pixels.</param>
    public static RenderView CreateRenderView(Scene3D scene, Camera3D camera, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(scene);
        EnsureInitialized();
        var view = new RenderView(scene, camera, width, height);
        lock (Views)
            Views.Add(view);
        return view;
    }

    /// <summary>
    /// Registers a custom shader pipeline usable via <see cref="Materials.Material.CustomPipeline"/>.<br/>
    /// The HLSL may <c>#include "Common.hlsli"</c> and must expose <c>vs</c>/<c>ps</c> entry points over the
    /// standard vertex layout. Compile errors disable only this pipeline (logged with full compiler output).
    /// </summary>
    /// <param name="name">Pipeline name referenced by materials.</param>
    /// <param name="hlslSource">Full HLSL source.</param>
    public static bool RegisterPipeline(string name, string hlslSource)
    {
        EnsureInitialized();
        return shaderLibrary?.RegisterCustom(name, hlslSource) ?? false;
    }

    /// <summary>
    /// Picks scene nodes under a screen position using last frame's camera: bounding-sphere hits, refined
    /// to exact triangles for meshes created with <c>keepCpuData</c>. Nearest first. Returns an empty array
    /// when <see cref="PickInputGate"/> says input is claimed or no frame has rendered yet.
    /// </summary>
    /// <param name="screenPx">Screen position in pixels.</param>
    public static PickHit[] Pick(Vector2 screenPx)
    {
        if (!lastFrameValid || (PickInputGate != null && !PickInputGate()))
            return Array.Empty<PickHit>();

        var frame = lastFrame;
        if (!frame.TryScreenToRay(screenPx, out var origin, out var direction))
            return Array.Empty<PickHit>();

        // The visible game surface under the cursor (terrain / wall), so ground decals pick against their real rendered
        // footprint on that surface - a projected shape, not the fat volume box. Null when aiming at open sky.
        Vector3? groundSurface = null;
        if (NoireService.IsInitialized() && NoireService.GameGui.ScreenToWorld(screenPx, out var gw))
            groundSurface = gw;

        var hits = new List<PickHit>();
        lock (Scene3D.GraphLock)
        {
            lock (Scenes)
            {
                foreach (var scene in Scenes)
                {
                    if (!scene.Visible)
                        continue;

                    foreach (var root in scene.Roots)
                        PickNode(root, origin, direction, groundSurface, hits);
                }
            }
        }

        hits.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return hits.ToArray();
    }

    /// <summary>
    /// Whether a screen point lies over visible native game UI (a HUD window, inventory, friend list, …). This is the
    /// pointer test the interaction layer uses to treat game UI as a hard pass - it never hovers or picks a 3D object
    /// through an addon the cursor is over. Native addons are not ImGui, so ImGui's <c>WantCaptureMouse</c> never
    /// reflects them; this reads game state instead. Near-fullscreen transparent overlay roots (nameplates, fly-text)
    /// are skipped so they never blanket-block. Returns false before the first frame or on any read fault.
    /// Call on the draw/framework thread.
    /// </summary>
    /// <param name="screenPx">Cursor position in framebuffer pixels (ImGui mouse space).</param>
    /// <param name="displaySize">The ImGui display size, for the near-fullscreen overlay skip.</param>
    public static bool IsCursorOverGameUi(Vector2 screenPx, Vector2 displaySize)
        => GameRenderSources.IsPointOverVisibleAddon(screenPx, displaySize);

    /// <summary>
    /// The diagnostic form of <see cref="IsCursorOverGameUi(Vector2, Vector2)"/>: also reports the name of the game
    /// addon whose collision node is under the cursor (null when none), for interaction diagnostics.
    /// </summary>
    /// <param name="screenPx">Cursor position in framebuffer pixels (ImGui mouse space).</param>
    /// <param name="displaySize">The ImGui display size, for the near-fullscreen overlay skip.</param>
    /// <param name="addonName">Receives the matching addon's name, or null.</param>
    public static bool IsCursorOverGameUi(Vector2 screenPx, Vector2 displaySize, out string? addonName)
        => GameRenderSources.IsPointOverVisibleAddon(screenPx, displaySize, out addonName);

    /// <summary>
    /// Reads the game depth buffer at a screen pixel and reconstructs the world-space point of the nearest rendered
    /// surface there. Unlike the game's collision raycast, the depth buffer contains <b>every drawn surface</b> (static
    /// meshes, fences, furniture, decorations), so this is the accurate "what is visibly in front of the cursor" source
    /// for click occlusion. Returns false when depth is unreadable this frame (depth-off / fallback camera), when the
    /// pixel is open sky, or on any fault. The depth resource is copied whole, so callers must throttle. Call on the
    /// render/draw thread (the same thread <c>UiBuilder.Draw</c> runs on).
    /// </summary>
    /// <param name="screenPx">Screen position in framebuffer pixels.</param>
    /// <param name="world">Receives the world-space surface point under the cursor.</param>
    public static bool TryReadDepthWorld(Vector2 screenPx, out Vector3 world)
    {
        world = default;
        if (!lastFrameValid)
            return false;

        var frame = lastFrame;
        if (!frame.HasDepth || frame.UsedFallbackCamera || renderDevice == null)
            return false;

        if (!GameRenderSources.TryGetDepthTexture(out var info))
            return false;

        // Cached, non-blocking readback: one reused staging texture, sampled a cycle late. A per-call staging copy plus
        // a blocking map here (this runs while hovering with wall occlusion on) churned GPU memory and stalled the
        // pipeline, which froze and eventually crashed the device.
        depthProbe ??= new DepthProbe();
        if (!depthProbe.TrySample(renderDevice, in info, screenPx, frame.ViewportSize, out var sample) || float.IsNaN(sample))
            return false;

        var vp = frame.ViewportSize;
        if (vp.X <= 0f || vp.Y <= 0f)
            return false;

        // Depth-buffer value is the surface's NDC z; unproject (ndc.xy, ndc.z) straight through InvViewProj. The far /
        // sky / unwritten value drives clip.w toward zero (infinite-far reversed-Z), which this rejects as "no surface".
        var ndc = new Vector4(screenPx.X / vp.X * 2f - 1f, 1f - screenPx.Y / vp.Y * 2f, sample, 1f);
        var c = Vector4.Transform(ndc, frame.InvViewProj);
        if (!float.IsFinite(c.W) || MathF.Abs(c.W) < 1e-6f)
            return false;

        world = new Vector3(c.X, c.Y, c.Z) / c.W;
        return float.IsFinite(world.X) && float.IsFinite(world.Y) && float.IsFinite(world.Z);
    }

    // ---------------------------------------------------------------- internals: lifecycle

    /// <summary>Lazily initializes the hub (event wiring; GPU objects wait for the game's first Present).</summary>
    internal static void EnsureInitialized()
    {
        if (initialized)
            return;

        lock (InitLock)
        {
            if (initialized)
                return;

            if (!NoireService.IsInitialized())
                throw new InvalidOperationException("NoireLib must be initialized before using NoireDraw3D.");

            mainScene = new Scene3D("Main") { IsHubOwned = true };
            Scenes.Add(mainScene);
            im = new ImDraw3D();
            stateGuard = new StateGuard();
            renderStats = new RenderStats();

            // Defer GPU-object creation until the present pipeline is confirmed alive.
            NoireService.PluginInterface.UiBuilder.RunWhenUiPrepared(() =>
            {
                if (disposed)
                    return true; // dev-reload before first Present: creating device objects now would leak them

                deviceObjectsReady = true;
                return true;
            });

            NoireService.PluginInterface.UiBuilder.Draw += OnDraw;
            NoireService.PluginInterface.UiBuilder.ResizeBuffers += OnResizeBuffers;

            if (!NoireLibMain.IsRegisteredOnDispose(DisposeKey))
                NoireLibMain.RegisterOnDispose(DisposeKey, Cleanup);

            initialized = true;
            UpdateFrameworkHook(); // start the sim-thread camera sampler for the present-time path
            RefreshUiHideOverrides(); // default KeepDrawingWhenUiHidden is true - the layer survives UI-hide
            NoireLogger.LogInfo("NoireDraw3D initialized (device objects deferred to first Present).", "Draw3D");
        }
    }

    /// <summary>Acquires the render device on demand - any thread (devices are free-threaded). Used by mesh/texture creation.</summary>
    internal static RenderDevice RequireDevice()
    {
        EnsureInitialized();
        if (renderDevice != null)
            return renderDevice;

        lock (InitLock)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NoireDraw3D));

            renderDevice ??= RenderDevice.TryCreate()
                ?? throw new InvalidOperationException("The game's D3D11 device is not available yet.");
            return renderDevice;
        }
    }

    /// <summary>Defers a GPU release to the render thread (start of the next frame) so in-flight frames never bind freed objects.</summary>
    internal static void EnqueueRelease(Action release)
    {
        if (disposed)
        {
            // Renderer gone - nothing is in flight; release immediately.
            try { release(); }
            catch (Exception ex) { NoireLogger.LogError(ex, "Draw3D deferred release failed.", "Draw3D"); }
            return;
        }

        ReleaseQueue.Enqueue(release);
    }

    internal static void UnregisterView(RenderView view)
    {
        lock (Views)
            Views.Remove(view);
    }

    /// <summary>Raises <see cref="OnFault"/> (self-disable ladder notifications).</summary>
    internal static void RaiseFault(Draw3DFaultKind kind, Exception? ex, string message)
    {
        try
        {
            OnFault?.Invoke(new Draw3DFault(kind, message, ex));
        }
        catch (Exception handlerEx)
        {
            NoireLogger.LogError(handlerEx, "A Draw3D OnFault handler threw.", "Draw3D");
        }
    }

    private static void Cleanup()
    {
        lock (InitLock)
        {
            if (!initialized || disposed)
            {
                disposed = true;
                return;
            }

            disposed = true;

            // 1. No new frames.
            NoireService.PluginInterface.UiBuilder.Draw -= OnDraw;
            NoireService.PluginInterface.UiBuilder.ResizeBuffers -= OnResizeBuffers;
            SetFrameworkHook(false);
            keepDrawingWhenUiHidden = false;
            RefreshUiHideOverrides();

            if (commandRegistered)
            {
                NoireService.CommandManager.RemoveHandler(CommandName);
                commandRegistered = false;
            }

            // 2. Scenes and views (releases references to user assets). DisposeContentsInternal frees each scene's
            // owned meshes / textures / models / editors as well as its nodes - the ownership-scope teardown.
            Diagnostics.ClearSmokeScene();
            lock (Scenes)
            {
                foreach (var scene in Scenes)
                    scene.DisposeContentsInternal();
                Scenes.Clear();
            }

            RenderView[] views;
            lock (Views)
                views = Views.ToArray();
            foreach (var view in views)
                view.Dispose();

            im?.DisposeResources();

            // 3. Drain deferred releases (they may have queued above).
            DrainReleaseQueue();

            // 4. Passes, caches, targets, stats.
            scenePass?.Dispose();
            scenePass = null;
            compositor?.Dispose();
            compositor = null;
            shaderLibrary?.Dispose();
            shaderLibrary = null;
            stateCache?.Dispose();
            stateCache = null;
            sceneRt?.Dispose();
            sceneRt = null;
            outlineMaskRt?.Dispose();
            outlineMaskRt = null;
            outlineVisRt?.Dispose();
            outlineVisRt = null;
            privateDepth?.Dispose();
            privateDepth = null;
            sceneDepth?.Dispose();
            sceneDepth = null;
            uiMaskSource?.Dispose();
            uiMaskSource = null;
            uiMaskHealth?.Dispose();
            uiMaskHealth = null;
            renderStats?.Dispose();
            renderStats = null;
            renderTargetTap?.Dispose();
            renderTargetTap = null;
            depthProbe?.Dispose();
            depthProbe = null;
            gameDepthTarget?.Dispose();
            gameDepthTarget = null;
            presentRtv.Dispose();
            presentRtv = default;
            presentRtvPtr = 0;
            backbufferRtv.Dispose();
            backbufferRtv = default;
            backbufferPtr = 0;
            DrainReleaseQueue();

            // 5. Device last; debug builds report anything we leaked.
#if DEBUG
            renderDevice?.ReportLiveObjects();
#endif
            renderDevice?.Dispose();
            renderDevice = null;

            NoireLogger.LogInfo("NoireDraw3D disposed.", "Draw3D");
        }
    }

    private static void DrainReleaseQueue()
    {
        while (ReleaseQueue.TryDequeue(out var release))
        {
            try
            {
                release();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "Draw3D deferred release failed.", "Draw3D");
            }
        }
    }

    // ---------------------------------------------------------------- internals: frame loop

    private static void OnDraw()
    {
        DrainReleaseQueue();

        var stats = renderStats;
        if (stats == null || disposed)
            return;

        if (!enabled)
        {
            stats.FramesSkippedDisabled++;
            return;
        }

        if (!deviceObjectsReady)
        {
            stats.FramesSkippedInitPending++;
            return;
        }

        try
        {
            PresentTimeFrame(stats);
        }
        catch (Exception ex)
        {
            HandlePassFailure(ex);
        }
    }

    /// <summary>Advances the self-disable ladder (rungs 4–5) after a frame threw. Shared by both render entries.</summary>
    private static void HandlePassFailure(Exception ex)
    {
        passFailStreak++;
        if (!passFaultLogged)
        {
            passFaultLogged = true;
            NoireLogger.LogError(ex, "Draw3D frame failed (self-disable ladder rung 4: layer skipped).", "Draw3D");
            RaiseFault(Draw3DFaultKind.Pass, ex, "Scene pass failed; layer skipped this frame.");
        }

        if (passFailStreak >= 3)
        {
            enabled = false;
            NoireLogger.LogError("Draw3D disabled after 3 consecutive frame failures (rung 5). Set NoireDraw3D.Enabled = true to re-arm.", "Draw3D");
            RaiseFault(Draw3DFaultKind.Renderer, ex, "Renderer disabled after repeated failures.");
        }
    }

    /// <summary>What <see cref="RenderMainScene"/> produced this frame: whether there is layer content to composite,
    /// and how many nameplate/HUD policy rects were collected (for the present-time UI-mask composite).</summary>
    private readonly record struct SceneRenderResult(bool HasContent, int RectCount);

    /// <summary>
    /// The present-time frame entry (Dalamud's Draw callback, end of frame). Always advances the render-target
    /// tap's frame boundary. When the pre-UI injection already rendered and composited this frame's scene under
    /// the native UI (zero-latency path), there is nothing left to do. Otherwise it renders the scene and
    /// composites it over the backbuffer - the classic over-everything / UI-masked path, and the fallback that
    /// keeps the layer visible on any frame the injection could not run.
    /// </summary>
    private static void PresentTimeFrame(RenderStats stats)
    {
        RenderDevice device;
        try
        {
            device = RequireDevice();
        }
        catch (InvalidOperationException)
        {
            stats.FramesSkippedNoDevice++;
            return;
        }

        if (!GameRenderSources.TryGetBackBuffer(out var backBuffer))
        {
            stats.FramesSkippedZeroSize++;
            return;
        }

        // Arm the default-on injection on the first frame that has a device (the property default couldn't install
        // the render-thread hook before the device existed). One-shot: user toggles go through the setter instead.
        if (renderUnderNativeUi && !injectionInitialized)
        {
            injectionInitialized = true;
            ApplyInjectionState();
        }

        // Frame boundary for the render-target tap: commits the present buffer learned this frame for next
        // frame's injection and resets its per-frame counters. Must run every present - this is why the layer
        // has to keep drawing while the UI is hidden (see KeepDrawingWhenUiHidden), or injection would stall.
        renderTargetTap?.OnPresent((nint)backBuffer.Texture);

        // The render-thread injection already rendered + composited this frame's scene under the native UI,
        // with the same camera the world was drawn with (zero latency). Compositing again here would just paint
        // the layer over the UI. The flag is set on the render thread by InjectComposite earlier this same
        // frame; reset it for the next one and stop.
        if (injectedSinceLastPresent)
        {
            injectedSinceLastPresent = false;
            return;
        }

        // Classic / fallback path: render + composite over the backbuffer at present time.
        var ctx = device.Context;
        if (renderTargetTap != null)
            renderTargetTap.SuppressSelf = true; // our own binds must not pollute an armed rtlog capture
        stateGuard!.Capture(ctx);
        try
        {
            var result = RenderMainScene(device, ctx, in backBuffer, stats, cameraOverride: null);
            if (result.HasContent)
            {
                CompositeOverBackbuffer(device, ctx, in backBuffer, result.RectCount);
                stats.EndGpuTiming(ctx);
                passFailStreak = 0;
                passFaultLogged = false;
            }
        }
        finally
        {
            stateGuard.Restore(ctx); // the context leaves exactly as it arrived - even on faults (Law 6)
            if (renderTargetTap != null)
                renderTargetTap.SuppressSelf = false;
        }
    }

    /// <summary>
    /// The shared render body used by both the present-time path and the pre-UI injection. Snapshots the camera,
    /// builds the frame, fires per-frame user code + diagnostics, then renders render-to-texture views and the
    /// main scene into the offscreen premultiplied target. The caller has captured the StateGuard and owns the
    /// composite to whichever target (backbuffer or the game's present buffer), plus <see cref="RenderStats.EndGpuTiming"/>.<br/>
    /// <paramref name="cameraOverride"/> is supplied by the injection path - the world-pass camera snapshot that
    /// matches the world already in the present buffer (see <see cref="TryGetInjectCamera"/>). The present-time
    /// fallback passes null and uses the sim-thread framework snapshot, falling back to a live camera read.<br/>
    /// Returns <see cref="SceneRenderResult.HasContent"/> = false on empty/skipped frames - the caller must NOT
    /// composite then, so a cleared scene leaves no stale content stamped on the present buffer.
    /// </summary>
    private static SceneRenderResult RenderMainScene(RenderDevice device, ID3D11DeviceContext* ctx, in GameRenderSources.BackBufferInfo backBuffer, RenderStats stats, GameRenderSources.CameraData? cameraOverride)
    {
        // Camera snapshot - once, at a stable point (Law 2). The injection path passes the delayed render camera
        // that matches the world already in the present buffer; the present-time path uses the sim-thread snapshot
        // (it matches the shown backbuffer better than a live read at present time), falling back to a live read.
        GameRenderSources.CameraData cam;
        if (cameraOverride.HasValue)
        {
            cam = cameraOverride.Value;
        }
        else if (frameworkCameraValid)
        {
            cam = frameworkCamera;
        }
        else if (!GameRenderSources.TryGetCamera(out cam))
        {
            stats.FramesSkippedNoCamera++;
            return default;
        }

        Matrix4x4 camView = Matrix4x4.Identity, camProj = Matrix4x4.Identity, viewProj;
        var usedFallback = false;
        if (cam.HasRenderCamera)
        {
            camView = cam.View;
            camProj = cam.Proj;
            viewProj = camView * camProj;
        }
        else if (cam.HasControlViewProj)
        {
            viewProj = cam.ControlViewProj;
            usedFallback = true;
        }
        else
        {
            stats.FramesSkippedNoCamera++;
            return default;
        }

        // The game's exposed camera matrices reproduce screen X/Y/W exactly, but their device-Z does
        // NOT match the GPU's reversed-Z depth buffer (measured with /noire3d probe: clip.z/clip.w ≈ 0
        // everywhere, while the real buffer holds near/w). That unusable clip.z was being written into
        // our private V2↔V2 depth buffer, which inverted object ordering - farther shapes drew on top.
        // Rebuild a clean reversed-Z infinite-far Z column (clip.z = near ⇒ deviceZ = near/w) while
        // leaving the X/Y/W columns untouched, so screen position and the clip-w that the world-occlusion
        // compare relies on are unchanged. InvViewProj is taken AFTER this so the depth→world round trip
        // (decal reconstruction, screen-to-ray picking) stays exact.
        var near = cam.NearPlane > 1e-6f ? cam.NearPlane : 0.1f;
        viewProj.M13 = 0f;
        viewProj.M23 = 0f;
        viewProj.M33 = 0f;
        viewProj.M43 = near;

        if (!Matrix4x4.Invert(viewProj, out var invViewProj))
        {
            stats.FramesSkippedNoCamera++;
            return default;
        }

        // Depth (per-frame; failure = depth-off mode, everything still renders). The buffer's value
        // convention is computed analytically from the camera's own near/far + reversed/standard flags
        // (sample = A + B/clipW), not fitted from collision raycasts: the raycast surface and the rendered
        // depth texel are frequently DIFFERENT surfaces, which biased the fit - fragile to acquire, easy to
        // lose, and it slid ground decals. /noire3d probe confirms the analytic map matches the buffer
        // exactly. The wholesale-VP fallback camera exposes no near/flags, so it runs depth-off by design.
        sceneDepth ??= new SceneDepth();
        var depthSrvOk = sceneDepth.Update(device);
        var hasDepth = depthSrvOk && cam.HasRenderCamera;
        var depthMap = hasDepth
            ? DepthCalibration.AnalyticMap(near, cam.FarPlane, cam.StandardZ, cam.FiniteFarPlane)
            : Vector4.Zero;
        if (!hasDepth)
            stats.DepthOffFrames++;

        var eye = cam.HasRenderCamera ? cam.Origin : UnprojectEye(invViewProj);
        var reversedZ = !cam.HasRenderCamera || !cam.StandardZ;

        var frame = new FrameContext(
            viewProj, invViewProj, camView, camProj, eye,
            (float)Clock.Elapsed.TotalSeconds,
            new Vector2(backBuffer.Width, backBuffer.Height),
            hasDepth ? sceneDepth.UvScale : Vector2.One,
            reversedZ, near, hasDepth, usedFallback, ++frameId);

        lastFrame = frame;
        lastFrameValid = true;
        lastCameraData = cam;

        // Prepare phase: render-thread user code runs FIRST so its mutations and Im calls land this frame.
        Scene3D[] scenes;
        lock (Scenes)
            scenes = Scenes.ToArray();
        foreach (var scene in scenes)
            scene.FirePrepare(in frame);

        // Render-thread overlay (native gizmo handles): same zero-latency point as FirePrepare, so its Im calls land
        // this frame with the live camera. Guarded - a throwing subscriber must never fault the render.
        var overlay = OnRenderOverlay;
        if (overlay != null)
        {
            try
            {
                overlay(frame);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "A NoireDraw3D render-overlay handler threw; overlay skipped this frame.", "Draw3D");
            }
        }

        Diagnostics.OnFrame(in frame, in cam, hasDepth);
        Diagnostics.OnFrameRendered(device, in frame, sceneDepth); // probe runs even on empty frames

        // Anything to do at all?
        RenderView[] views;
        lock (Views)
            views = Views.ToArray();

        var anyScene = false;
        foreach (var scene in scenes)
            anyScene |= scene.Visible && scene.NodeCount > 0;
        var anyView = false;
        foreach (var v in views)
            anyView |= v.Enabled && !v.IsDisposed;

        if (!anyScene && !anyView && !im!.HasPending)
        {
            stats.FramesSkippedEmpty++;
            return default; // nothing to draw - caller composites nothing, so a cleared scene leaves no residue
        }

        // GPU objects (lazy, amortized).
        stateCache ??= new StateCache();
        shaderLibrary ??= new ShaderLibrary();
        scenePass ??= new ScenePass();
        compositor ??= new Compositor();
        sceneRt ??= new RenderTarget();
        privateDepth ??= new DepthTarget();

        if (!EnsureBackbufferRtv(device, backBuffer) || !sceneRt.EnsureSize(device, backBuffer.Width, backBuffer.Height))
        {
            stats.FramesSkippedZeroSize++;
            return default;
        }

        if (shaderLibrary.GetComposite(device) == null)
            return default; // compile failure already logged (rung 1); nothing can reach the screen

        stats.BeginFrameCounters();
        stats.DepthAvailable = hasDepth;
        stats.UsedFallbackCamera = usedFallback;
        stats.DepthSourceDescription =
            $"{sceneDepth.Description}; map: {DepthMapDescription(in depthMap, hasDepth)}; uiMask: {(protectGameUi ? uiMaskHealth?.Description ?? "pending" : "off")}";

        // Nameplate policy rects (fail-soft: any error means none this frame). These are invisible -
        // they only gate WHERE the per-pixel UI mask applies (composite shader), so plates can be
        // covered by nearer 3D content without ever cutting a visible rectangle.
        // Layout: [0..plateCount) = nameplates (visibility factors decided after collection),
        //         [plateCount..rectCount) = HUD addon rects (factor 1: HUD wins inside plate regions).
        var plateCount = 0;
        if (nativeUiProtectionMode != NativeUiProtectionMode.AlwaysVisible && protectGameUi)
            plateCount = GameRenderSources.CollectNamePlateRects(ProtectRects, PlateDistances, 64, frame.ViewportSize, frame.EyePos);

        var rectCount = plateCount;
        if (plateCount > 0)
            rectCount += GameRenderSources.CollectVisibleAddonRects(ProtectRects, plateCount, ProtectRects.Length - plateCount, frame.ViewportSize);

        stats.ProtectRects = rectCount;

        stats.BeginGpuTiming(device, ctx);

        // Render-to-texture views first (own camera, no game-depth compare).
        foreach (var view in views)
        {
            if (!view.Enabled || view.IsDisposed || !view.Scene.Visible || !view.EnsureTarget(device))
                continue;

            var vp = view.Camera.BuildViewProj(view.Width / (float)view.Height);
            if (!Matrix4x4.Invert(vp, out var invVp))
                continue;

            var viewFrame = new FrameContext(
                vp, invVp, Matrix4x4.Identity, Matrix4x4.Identity, view.Camera.Position,
                frame.Time, new Vector2(view.Width, view.Height), Vector2.One,
                reversedZ: true, view.Camera.NearPlane, hasDepth: false, usedFallbackCamera: false, frame.FrameId);

            scenePass!.BeginCollect(in viewFrame, mainPass: false);
            scenePass.AddScene(view.Scene, stats, depthAvailable: false);
            scenePass.Execute(device, ctx, in viewFrame, view.Target, view.Depth, null, Vector4.Zero, shaderLibrary!, stateCache!, stats, Wireframe, Lighting);
        }

        // Main pass.
        scenePass!.BeginCollect(in frame, mainPass: true);
        im!.Consume(scenePass, in frame, stats, hasDepth);
        foreach (var scene in scenes)
            scenePass.AddScene(scene, stats, hasDepth);

        scenePass.Execute(device, ctx, in frame, sceneRt!, privateDepth!, hasDepth ? sceneDepth.Srv : null,
            depthMap, shaderLibrary!, stateCache!, stats, Wireframe, Lighting);

        // Optional selection-outline pass: only when something is outlined, so the default path is untouched. Draws
        // the outlined silhouettes into a coverage mask, then dilates it into a real screen-space rim on the layer.
        // Fail-soft: a mask/shader/target hiccup skips the outline this frame, never the scene.
        RenderOutlinePass(device, ctx, in frame, hasDepth ? sceneDepth.Srv : null, depthMap, stats);

        stats.MarkSceneDone(ctx);

        // Nameplate visibility factors: 1 = letters on top, behindFactor = covered by your content.
        var behindFactor = Math.Clamp(nativeUiProtectionDimFactor, 0f, 1f);
        if (plateCount > 0)
        {
            if (nativeUiProtectionMode == NativeUiProtectionMode.DepthAware)
                scenePass.ComputeRectOcclusion(in frame, ProtectRects, PlateDistances, ProtectFactors, plateCount, behindFactor);
            else // Off: the layer always covers plates
                for (var i = 0; i < plateCount; i++)
                    ProtectFactors[i] = behindFactor;
        }

        // HUD addon rects: the HUD keeps reading on top even inside covered plate regions.
        for (var i = plateCount; i < rectCount; i++)
            ProtectFactors[i] = 1f;

        stats.FramesRendered++;
        return new SceneRenderResult(true, rectCount);
    }

    /// <summary>
    /// Present-time composite of the offscreen layer over the backbuffer: the classic over-everything path, or -
    /// when <see cref="ProtectGameUi"/> is on - masked per-pixel by the finished frame's native-UI-coverage alpha
    /// so the HUD reads on top. Also the fallback whenever the pre-UI injection could not run this frame.
    /// Assumes <see cref="RenderMainScene"/> just rendered content into <see cref="sceneRt"/>.
    /// </summary>
    private static void CompositeOverBackbuffer(RenderDevice device, ID3D11DeviceContext* ctx, in GameRenderSources.BackBufferInfo backBuffer, int rectCount)
    {
        var composite = shaderLibrary!.GetComposite(device);
        if (composite == null)
            return;

        // Per-pixel game-UI-on-top: copy the finished frame (its alpha = native-UI coverage) and let the
        // composite mask the layer by it. Health check self-disables the mask when the alpha channel is
        // unusable (some upscalers fill it) - fail-visible beats fail-invisible.
        ID3D11ShaderResourceView* uiMaskSrv = null;
        if (protectGameUi)
        {
            uiMaskSource ??= new UiMaskSource();
            uiMaskHealth ??= new UiMaskHealth();
            if (uiMaskSource.EnsureAndCopy(device, ctx, (nint)backBuffer.Texture))
            {
                uiMaskHealth.Update(device, ctx, uiMaskSource, lastFrame.FrameId);
                if (uiMaskHealth.AlphaUsable)
                    uiMaskSrv = uiMaskSource.Srv;
            }
        }

        compositor!.Blit(device, ctx, composite, stateCache!, sceneRt!.Srv, uiMaskSrv, backbufferRtv.Get(), backBuffer.Width, backBuffer.Height, LayerOpacity, ProtectRects, ProtectFactors, rectCount);
    }

    /// <summary>
    /// The selection-outline post-process: renders the outlined items' silhouettes into a coverage mask, then dilates
    /// that mask into a real screen-space rim blended onto the scene layer. Only runs when the scene pass collected an
    /// outlined item, so the ordinary path pays nothing. Every step is guarded - a null pipeline / target skips the
    /// outline this frame without disturbing the scene.
    /// </summary>
    private static void RenderOutlinePass(RenderDevice device, ID3D11DeviceContext* ctx, in FrameContext frame, ID3D11ShaderResourceView* sceneDepthSrv, Vector4 depthMap, RenderStats stats)
    {
        var pass = scenePass;
        var scene = sceneRt;
        var comp = compositor;
        var shaders = shaderLibrary;
        var cache = stateCache;
        if (pass == null || scene == null || comp == null || shaders == null || cache == null || !pass.HasOutlinedItems)
            return;

        var outline = shaders.GetOutline(device);
        if (outline == null || shaders.GetOutlineMaskMesh(device) == null)
            return; // outline shaders self-disabled (rung 1) - no rim this frame

        outlineMaskRt ??= new RenderTarget();
        outlineVisRt ??= new RenderTarget();
        if (!outlineMaskRt.EnsureSize(device, scene.Width, scene.Height) || !outlineVisRt.EnsureSize(device, scene.Width, scene.Height))
            return;

        // The mask holds each object's FULL silhouette (ignoring occlusion) plus a per-pixel worldVisible flag from the
        // game depth; the composite outlines the whole shape and then hides the segments whose silhouette is behind a
        // wall/character. Decals GE-test the private depth (this frame's scene) so nearer 3D objects remove them.
        var privateDepthValid = privateDepth != null && pass.LastPrivateDepthWritten;
        pass.RenderOutlineMask(device, ctx, in frame, outlineMaskRt, outlineVisRt, privateDepth!, privateDepthValid, sceneDepthSrv, depthMap, shaders, cache, stats);
        comp.BlitOutline(device, ctx, outline, cache, outlineMaskRt.Srv, outlineVisRt.Srv, scene.Rtv, scene.Width, scene.Height, pass.MaxOutlineWidthPixels);
    }

    private static bool EnsureBackbufferRtv(RenderDevice device, in GameRenderSources.BackBufferInfo info)
    {
        if (info.Texture == backbufferPtr && backbufferRtv.Get() != null)
            return true;

        backbufferRtv.Dispose();
        backbufferRtv = default;
        backbufferPtr = 0;

        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)info.Texture, out var texture))
            return false;

        using (texture)
        {
            ID3D11RenderTargetView* rtv = null;
            if (device.Device->CreateRenderTargetView((ID3D11Resource*)texture.Get(), null, &rtv) < 0 || rtv == null)
                return false;

            backbufferRtv.Attach(rtv);
            backbufferPtr = info.Texture;
            return true;
        }
    }

    /// <summary>
    /// Render-thread injection callback (from <see cref="RenderTargetTap"/>): renders THIS frame's scene and
    /// composites it onto the game's present-composition buffer - after the world image lands there and before
    /// the native UI is drawn, so HUD and nameplates read on top. Projects with the world-pass camera snapshot
    /// (<see cref="TryGetInjectCamera"/>) - the exact camera the world in the present buffer was drawn with - so
    /// the layer holds still under camera motion at any frame-rate. State is saved/restored (Law 6).
    /// Returns true when it rendered; on failure the flag stays clear and the present-time path renders instead.
    /// </summary>
    private static bool InjectComposite(nint presentBufferResource)
    {
        if (disposed || !renderUnderNativeUi || !enabled || !deviceObjectsReady)
            return false;

        var stats = renderStats;
        var device = renderDevice;
        var guard = stateGuard;
        if (stats == null || device == null || guard == null)
            return false;

        if (!GameRenderSources.TryGetBackBuffer(out var backBuffer) || !EnsurePresentRtv(device, presentBufferResource))
            return false;

        // Project with the exact camera the world in the present buffer was rasterized with - the render-thread
        // tap's world-pass snapshot (see TryGetInjectCamera). This locks the layer to the world at any frame-rate;
        // there is no delay/timing estimation. Falls back to the live camera on a frame with no world pass.
        if (!TryGetInjectCamera(out var injectCam))
            return false; // no camera available - let the present-time path handle this frame

        var ctx = device.Context;
        guard.Capture(ctx);
        try
        {
            // Render this frame's scene into the offscreen target with the world-pass camera (the exact camera the
            // world in the present buffer used), then blit. No UI mask/rects - the real native UI draws on top.
            var result = RenderMainScene(device, ctx, in backBuffer, stats, injectCam);
            if (result.HasContent)
            {
                var composite = shaderLibrary!.GetComposite(device);
                if (composite != null && sceneRt!.Srv != null)
                    compositor!.Blit(device, ctx, composite, stateCache!, sceneRt.Srv, null, presentRtv.Get(), presentRtvWidth, presentRtvHeight, LayerOpacity, ProtectRects, ProtectFactors, 0);

                // Optional: stamp our opaque depth into the game buffer so the coming nameplate pass occludes
                // against 3D objects in front of characters (real depth-aware nameplates under the native UI).
                if (nativeUiDepthWrite)
                    ProjectOpaqueDepthToGameBuffer(device, ctx);

                stats.EndGpuTiming(ctx);
            }

            // The core ran this frame via injection (even on an empty frame, which simply skips the blit above
            // so a cleared scene leaves nothing stamped). Tell the present-time path to stand down for this frame.
            injectedSinceLastPresent = true;
            passFailStreak = 0;
            passFaultLogged = false;
            return true;
        }
        catch (Exception ex)
        {
            // Fail-soft: leave the flag clear so the present-time path renders this frame over the backbuffer.
            if (!passFaultLogged)
            {
                passFaultLogged = true;
                NoireLogger.LogError(ex, "Draw3D: inject render failed; falling back to the present-time composite.", "Draw3D");
            }

            return false;
        }
        finally
        {
            guard.Restore(ctx);
        }
    }

    /// <summary>
    /// The camera to project the injected layer with: the render thread's world-pass snapshot - the exact camera
    /// the world currently in the present buffer was rasterized with, captured at the frame's first depth pass, so
    /// the layer matches the world independent of frame-rate. Falls back to the live render camera only on a frame
    /// where no world pass was seen (e.g. menu/loading, which carries no world-anchored content).
    /// False only when no camera is available at all.
    /// </summary>
    private static bool TryGetInjectCamera(out GameRenderSources.CameraData cam)
    {
        if (renderTargetTap != null && renderTargetTap.TryGetWorldCamera(out cam))
            return true;

        return GameRenderSources.TryGetCamera(out cam);
    }

    /// <summary>
    /// Writes this frame's opaque 3D depth into the game's scene depth buffer (render thread, inside the inject
    /// StateGuard) so the coming nameplate pass occludes against 3D objects in front of characters. Fail-soft:
    /// a no-op when the depth buffer is unavailable or can't back a DSV. Reuses the items ScenePass just drew.
    /// </summary>
    private static void ProjectOpaqueDepthToGameBuffer(RenderDevice device, ID3D11DeviceContext* ctx)
    {
        if (scenePass == null || shaderLibrary == null || stateCache == null || renderStats == null)
            return;

        gameDepthTarget ??= new GameDepthTarget();
        var dsv = gameDepthTarget.Ensure(device);
        if (dsv == null)
            return;

        scenePass.ProjectOpaqueDepth(device, ctx, in lastFrame, dsv, gameDepthTarget.Width, gameDepthTarget.Height, shaderLibrary, stateCache, renderStats);
    }

    /// <summary>Creates and caches an RTV over the game's present-composition buffer (recreated when its pointer changes).</summary>
    private static bool EnsurePresentRtv(RenderDevice device, nint resource)
    {
        if (resource == presentRtvPtr && presentRtv.Get() != null)
            return true;

        presentRtv.Dispose();
        presentRtv = default;
        presentRtvPtr = 0;

        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)resource, out var texture))
            return false;

        using (texture)
        {
            D3D11_TEXTURE2D_DESC desc;
            texture.Get()->GetDesc(&desc);

            ID3D11RenderTargetView* rtv = null;
            if (device.Device->CreateRenderTargetView((ID3D11Resource*)texture.Get(), null, &rtv) < 0 || rtv == null)
                return false;

            presentRtv.Attach(rtv);
            presentRtvPtr = resource;
            presentRtvWidth = desc.Width;
            presentRtvHeight = desc.Height;
            return true;
        }
    }

    private static void OnResizeBuffers()
    {
        // Hard DXGI obligation: release every backbuffer-derived reference synchronously - a lazily
        // released reference would fail the *game's own* ResizeBuffers (the v1 landmine, §1.2-5d).
        backbufferRtv.Dispose();
        backbufferRtv = default;
        presentRtv.Dispose();
        presentRtv = default;
        presentRtvPtr = 0;
        backbufferPtr = 0;

        // Our own targets carry no such constraint; recreate on the next frame anyway.
        sceneRt?.Release();
        outlineMaskRt?.Release();
        outlineVisRt?.Release();
        privateDepth?.Release();
        sceneDepth?.Invalidate();
        gameDepthTarget?.Invalidate();
        uiMaskSource?.Release();
        depthProbe?.Release(); // drops the cached staging copy of the (now stale-sized) depth texture; recreated on next sample
        // Depth calibration survives resizes: the value mapping is per-value, not per-texel.
    }

    private static Vector3 UnprojectEye(in Matrix4x4 invViewProj)
    {
        // Fallback-camera eye approximation: the near-plane center (reversed-Z near = 1).
        var p = Vector4.Transform(new Vector4(0f, 0f, 1f, 1f), invViewProj);
        return Math.Abs(p.W) > 1e-9f ? new Vector3(p.X, p.Y, p.Z) / p.W : Vector3.Zero;
    }

    // ---------------------------------------------------------------- internals: camera sampler, UI-hide, command

    private static void UpdateFrameworkHook() => SetFrameworkHook(initialized && !disposed);

    private static void SetFrameworkHook(bool hook)
    {
        if (hook == frameworkHooked || !NoireService.IsInitialized())
            return;

        if (hook)
            NoireService.Framework.Update += OnFrameworkUpdate;
        else
            NoireService.Framework.Update -= OnFrameworkUpdate;
        frameworkHooked = hook;
    }

    private static void OnFrameworkUpdate(IFramework framework)
    {
        frameworkCameraValid = GameRenderSources.TryGetCamera(out frameworkCamera);
    }

    private static void RefreshUiHideOverrides()
    {
        if (!NoireService.IsInitialized())
            return;

        var uiBuilder = NoireService.PluginInterface.UiBuilder;
        ApplyOverride(ref forcedAutoHide, keepDrawingWhenUiHidden, uiBuilder.DisableAutomaticUiHide, v => uiBuilder.DisableAutomaticUiHide = v);
        ApplyOverride(ref forcedUserHide, keepDrawingWhenUiHidden, uiBuilder.DisableUserUiHide, v => uiBuilder.DisableUserUiHide = v);
        ApplyOverride(ref forcedCutsceneHide, keepDrawingWhenUiHidden, uiBuilder.DisableCutsceneUiHide, v => uiBuilder.DisableCutsceneUiHide = v);
        ApplyOverride(ref forcedGposeHide, keepDrawingWhenUiHidden, uiBuilder.DisableGposeUiHide, v => uiBuilder.DisableGposeUiHide = v);
    }

    private static void ApplyOverride(ref bool forced, bool needed, bool current, Action<bool> setter)
    {
        if (needed)
        {
            if (!current)
            {
                setter(true);
                forced = true;
            }
        }
        else if (forced)
        {
            setter(false);
            forced = false;
        }
    }

    private static void RegisterCommand()
    {
        // Commands are global and NoireLib is statically linked per plugin - registration is best-effort;
        // the Diagnostics façade keeps the toolkit reachable regardless of who won the name.
        commandRegistered = NoireService.CommandManager.AddHandler(CommandName, new CommandInfo(HandleCommand)
        {
            HelpMessage = "Draw3D diagnostics: validate | probe | stats | wire | smoke | model <path> | gizmo | clear | reset | rtlog | ontop | platedepth",
        });

        if (!commandRegistered)
            NoireLogger.LogDebug($"'{CommandName}' already registered by another plugin - use NoireDraw3D.Diagnostics instead.", "Draw3D");
    }

    private static void HandleCommand(string command, string args)
    {
        // Split the verb from the rest so a path argument (e.g. "model C:\...") keeps its case.
        var trimmed = args.Trim();
        var sp = trimmed.IndexOf(' ');
        var verb = (sp < 0 ? trimmed : trimmed[..sp]).ToLowerInvariant();
        var rest = sp < 0 ? string.Empty : trimmed[(sp + 1)..].Trim();
        switch (verb)
        {
            case "model":
                Diagnostics.SpawnSmokeModel(rest);
                break;
            case "validate":
                Diagnostics.RunValidate();
                Print("Draw3D: projection parity validator armed for the next 10 frames - results go to the log.");
                break;
            case "probe":
                Diagnostics.RunProbe();
                Print("Draw3D: depth probe armed for the next frame - results go to the log.");
                break;
            case "wire":
                Print($"Draw3D: wireframe {(Diagnostics.ToggleWireframe() ? "on" : "off")}.");
                break;
            case "smoke":
                Diagnostics.SpawnSmokeScene();
                Print("Draw3D: smoke scene spawned around you - hover the solid objects, left-click one to select it, then drag the gizmo handles (the camera stays put). '/noire3d clear' removes it.");
                break;
            case "gizmo":
                Print(Diagnostics.ToggleSmokeGizmoBackend());
                break;
            case "clear":
                Diagnostics.ClearSmokeScene();
                Print("Draw3D: smoke scene cleared.");
                break;
            case "reset":
                renderStats?.ResetCounters();
                Enabled = true;
                Print("Draw3D: counters reset, renderer re-armed.");
                break;
            case "rtlog":
                if (EnsureRenderTargetTap() is { } tap)
                {
                    tap.ArmCapture();
                    Print("Draw3D: capturing the next frame's render-target bind sequence - paste the log (/xllog).");
                }
                else
                {
                    Print("Draw3D: the render-target tap could not be installed (see the log).");
                }

                break;
            case "ontop":
                NativeUi.RenderUnder = !NativeUi.RenderUnder;
                Print(NativeUi.RenderUnder
                    ? "Draw3D: native-UI-on-top ON (experimental) - the layer now injects before the game UI; HUD/nameplates should read on top. '/noire3d ontop' again to turn off."
                    : "Draw3D: native-UI-on-top off - the layer composites over everything again (previous behaviour).");
                break;
            case "platedepth":
                NativeUi.DepthWrite = !NativeUi.DepthWrite;
                Print(NativeUi.DepthWrite
                    ? "Draw3D: native-UI depth-write ON (experimental) - 3D objects write into the game depth buffer so nameplates behind them get occluded. Needs '/noire3d ontop' on. '/noire3d platedepth' again to turn off."
                    : "Draw3D: native-UI depth-write off.");
                break;
            default:
                Print(Diagnostics.GetStatsText());
                break;
        }
    }

    private static void Print(string message)
    {
        NoireService.ChatGui.Print(message);
        NoireLogger.LogInfo(message, "Draw3D");
    }

    /// <summary>Lazily installs the render-target tap (opt-in hook) on first use. Null when the device isn't ready.</summary>
    private static RenderTargetTap? EnsureRenderTargetTap()
    {
        if (renderTargetTap != null)
            return renderTargetTap;

        try
        {
            var device = RequireDevice();
            var tap = new RenderTargetTap();
            if (tap.Install(device))
                renderTargetTap = tap;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: could not initialize the render-target tap.", "Draw3D");
        }

        return renderTargetTap;
    }

    // ---------------------------------------------------------------- internals: stats, picking

    private static Draw3DStats BuildStats()
    {
        var s = renderStats;
        if (s == null)
        {
            return new Draw3DStats
            {
                FramesRendered = 0, FramesSkippedDisabled = 0, FramesSkippedInitPending = 0, FramesSkippedNoDevice = 0,
                FramesSkippedNoCamera = 0, FramesSkippedZeroSize = 0, FramesSkippedEmpty = 0, DepthOffFrames = 0,
                DisposedAssetDraws = 0, ImCommandsDropped = 0, DrawCalls = 0, Instances = 0, Triangles = 0, Batches = 0,
                CulledItems = 0, VisibleItems = 0, ProtectRects = 0, DepthAvailable = false, UsedFallbackCamera = false,
                DepthSource = "none", SceneGpuMs = 0, CompositeGpuMs = 0,
            };
        }

        return new Draw3DStats
        {
            FramesRendered = s.FramesRendered,
            FramesSkippedDisabled = s.FramesSkippedDisabled,
            FramesSkippedInitPending = s.FramesSkippedInitPending,
            FramesSkippedNoDevice = s.FramesSkippedNoDevice,
            FramesSkippedNoCamera = s.FramesSkippedNoCamera,
            FramesSkippedZeroSize = s.FramesSkippedZeroSize,
            FramesSkippedEmpty = s.FramesSkippedEmpty,
            DepthOffFrames = s.DepthOffFrames,
            DisposedAssetDraws = s.DisposedAssetDraws,
            ImCommandsDropped = s.ImCommandsDropped,
            DrawCalls = s.DrawCalls,
            Instances = s.Instances,
            Triangles = s.Triangles,
            Batches = s.Batches,
            CulledItems = s.CulledItems,
            VisibleItems = s.VisibleItems,
            ProtectRects = s.ProtectRects,
            DepthAvailable = s.DepthAvailable,
            UsedFallbackCamera = s.UsedFallbackCamera,
            DepthSource = s.DepthSourceDescription,
            SceneGpuMs = s.SceneGpuMs,
            CompositeGpuMs = s.CompositeGpuMs,
        };
    }

    internal static UiMaskHealth? UiMaskHealthState => uiMaskHealth;

    /// <summary>One-line description of the active analytic depth mapping for stats/probe.</summary>
    internal static string DepthMapDescription(in Vector4 map, bool hasDepth)
        => !hasDepth
            ? "depth-off"
            : $"z={map.X:E2}{(map.Y >= 0 ? "+" : "")}{map.Y:F5}/w ({(map.Y > 0 ? "reversed" : "standard")}-Z, analytic)";

    internal static FrameContext LastFrame => lastFrame;

    internal static bool LastFrameValid => lastFrameValid;

    /// <summary>
    /// Render-thread overlay hook, fired each rendered frame right after the scene prepare phase with the CURRENT
    /// frame - so <see cref="Im"/> calls made from it land this frame (zero-latency), unlike calls made from
    /// <c>UiBuilder.Draw</c> which render a frame late. <see cref="NoireLib.Draw3D.Interaction.NoireInteract"/> subscribes once to
    /// draw the native gizmo handles here, so their screen-constant sizing tracks the live camera instead of lagging
    /// it by a frame during zoom (the handle "swim"). Interaction (hit-testing, drag solving) still runs on the UI
    /// thread; only the drawing moves here.
    /// </summary>
    internal static event Action<FrameContext>? OnRenderOverlay;

    internal static GameRenderSources.CameraData LastCameraData => lastCameraData;

    private static void PickNode(SceneNode node, Vector3 origin, Vector3 direction, Vector3? groundSurface, List<PickHit> hits)
    {
        if (!node.Visible || node.Destroyed)
            return;

        var renderer = node.Renderer;
        if (renderer != null && !renderer.Mesh.IsDisposed)
        {
            if (renderer.Material.Domain == MaterialDomain.GroundDecal)
            {
                // A decal is a projected footprint, not a solid - pick its actual shape on the ground surface, so
                // hovering the hole of a ring or outside a sector's arc correctly misses (not the whole volume box).
                if (TryPickDecal(node, renderer, origin, direction, groundSurface, out var dt))
                    hits.Add(new PickHit(node, dt, null));
            }
            else
            {
                var world = node.ResolveWorld();
                var bounds = renderer.Mesh.LocalBounds.Transform(world);
                if (RaySphere(origin, direction, bounds, out var sphereT))
                {
                    var mesh = renderer.Mesh;
                    if (mesh.CpuVertices != null && (mesh.CpuIndices16 != null || mesh.CpuIndices32 != null))
                    {
                        if (RayMesh(origin, direction, mesh, world, out var t, out var tri))
                            hits.Add(new PickHit(node, t, tri));
                    }
                    else
                    {
                        hits.Add(new PickHit(node, sphereT, null));
                    }
                }
            }
        }

        foreach (var child in node.Children)
            PickNode(child, origin, direction, groundSurface, hits);
    }

    /// <summary>
    /// Picks a ground-decal node by its rendered footprint (matches <c>GroundDecal.hlsl</c>): find the world surface
    /// point under the cursor (the real ground, else the ray meeting the decal's local ground plane), bring it into the
    /// unit-box local space, and reject it unless it is inside both the volume and the shape SDF (ring / sector / …).
    /// </summary>
    private static bool TryPickDecal(SceneNode node, MeshRenderer renderer, Vector3 origin, Vector3 direction, Vector3? groundSurface, out float t)
    {
        t = 0f;

        var world = node.ResolveWorld();
        if (!Matrix4x4.Invert(world, out var invWorld))
            return false;

        // A ground decal is a flat footprint projected vertically, so two candidate surface points can put the cursor
        // "on" it, and a hit on EITHER counts (whichever is nearer along the ray wins):
        //   1. The decal's own plane (ray ∩ node local +Y plane). View/zoom/angle independent and exact on flat ground -
        //      this is what fixes "some camera angles/zoom don't catch": it never depends on the game's raycast landing.
        //   2. The game's reported ground under the cursor (ScreenToWorld). Catches slope-projected decals where the
        //      terrain sits off the node plane, at the cost of the game's raycast reliability.
        var hit = false;
        t = float.MaxValue;

        if (TryRayLocalGroundPlane(origin, direction, in world, out var planeHit)
            && TryDecalFootprint(renderer.Material, in invWorld, origin, direction, planeHit, out var tPlane))
        {
            hit = true;
            t = tPlane;
        }

        if (groundSurface is { } gs
            && TryDecalFootprint(renderer.Material, in invWorld, origin, direction, gs, out var tGround)
            && tGround < t)
        {
            hit = true;
            t = tGround;
        }

        return hit;
    }

    /// <summary>Transforms a world surface point into the decal's local frame and tests it against the footprint volume + shape, returning the ray parameter of the point.</summary>
    private static bool TryDecalFootprint(Material material, in Matrix4x4 invWorld, Vector3 origin, Vector3 direction, Vector3 worldHit, out float t)
    {
        t = 0f;

        var lp = Vector3.Transform(worldHit, invWorld);
        // XZ is the real footprint gate; the vertical band is generous (the volume height) so uneven ground / a
        // collision surface reported off the rendered ground doesn't reject a spot the decal visibly covers.
        if (MathF.Abs(lp.X) > 0.5f || MathF.Abs(lp.Z) > 0.5f || MathF.Abs(lp.Y) > 0.5f)
            return false;
        if (!InsideDecalShape(material, lp))
            return false;

        var dd = Vector3.Dot(direction, direction);
        t = dd > 1e-12f ? Vector3.Dot(worldHit - origin, direction) / dd : 0f;
        return t >= 0f;
    }

    /// <summary>Intersects the ray with the decal's local ground plane (its origin, local +Y as normal).</summary>
    private static bool TryRayLocalGroundPlane(Vector3 origin, Vector3 direction, in Matrix4x4 world, out Vector3 worldHit)
    {
        worldHit = default;

        var planePoint = world.Translation;
        var planeNormal = Vector3.TransformNormal(Vector3.UnitY, world);
        var len = planeNormal.Length();
        if (len < 1e-6f)
            return false;
        planeNormal /= len;

        var denom = Vector3.Dot(direction, planeNormal);
        if (MathF.Abs(denom) < 1e-6f)
            return false;

        var tp = Vector3.Dot(planePoint - origin, planeNormal) / denom;
        if (tp < 0f)
            return false;

        worldHit = origin + direction * tp;
        return true;
    }

    /// <summary>The decal footprint SDF from <c>GroundDecal.hlsl</c> (footprint space p = lp.xz·2, edge at |p| = 1): inside when sd ≤ 0.</summary>
    internal static bool InsideDecalShape(Material mat, Vector3 lp)
    {
        var p = new Vector2(lp.X, lp.Z) * 2f;
        var sp = mat.ShapeParams;
        float sd;
        switch (mat.Shape)
        {
            case DecalShape.Circle:
                sd = p.Length() - 1f;
                break;
            case DecalShape.Ring:
            {
                var r = p.Length();
                sd = MathF.Max(r - 1f, sp.X - r);                // x = inner radius ratio
                break;
            }
            case DecalShape.Sector:
            {
                var r = p.Length();
                var an = MathF.Abs(MathF.Atan2(p.X, p.Y));       // 0 at local +Z
                sd = MathF.Max(MathF.Max(r - 1f, sp.Y - r), (an - sp.X) * r); // x = half angle, y = inner ratio
                break;
            }
            default:                                             // Rect / Texture - the footprint square
                sd = MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y)) - 1f;
                break;
        }

        return sd <= 0f;
    }

    private static bool RaySphere(Vector3 origin, Vector3 direction, in Geometry.BoundingSphere sphere, out float t)
    {
        t = 0f;
        var oc = origin - sphere.Center;
        var b = Vector3.Dot(oc, direction);
        var c = oc.LengthSquared() - sphere.Radius * sphere.Radius;
        var disc = b * b - c;
        if (disc < 0f)
            return false;

        var sq = MathF.Sqrt(disc);
        t = -b - sq;
        if (t < 0f)
            t = -b + sq;
        return t >= 0f;
    }

    private static bool RayMesh(Vector3 origin, Vector3 direction, Geometry.Mesh mesh, in Matrix4x4 world, out float bestT, out int bestTriangle)
    {
        bestT = float.MaxValue;
        bestTriangle = -1;

        if (!Matrix4x4.Invert(world, out var invWorld))
            return false;

        // Transform the ray into model space (direction unnormalized - t stays in world units after rescale).
        var localOrigin = Vector3.Transform(origin, invWorld);
        var localDir = Vector3.TransformNormal(direction, invWorld);
        var dirScale = localDir.Length();
        if (dirScale < 1e-12f)
            return false;
        localDir /= dirScale;

        var vertices = mesh.CpuVertices!;
        var triCount = mesh.IndexCount / 3;
        for (var i = 0; i < triCount; i++)
        {
            int i0, i1, i2;
            if (mesh.CpuIndices16 != null)
            {
                i0 = mesh.CpuIndices16[i * 3];
                i1 = mesh.CpuIndices16[i * 3 + 1];
                i2 = mesh.CpuIndices16[i * 3 + 2];
            }
            else
            {
                i0 = (int)mesh.CpuIndices32![i * 3];
                i1 = (int)mesh.CpuIndices32[i * 3 + 1];
                i2 = (int)mesh.CpuIndices32[i * 3 + 2];
            }

            if (RayTriangle(localOrigin, localDir, vertices[i0].Position, vertices[i1].Position, vertices[i2].Position, out var t) && t < bestT)
            {
                bestT = t;
                bestTriangle = i;
            }
        }

        if (bestTriangle < 0)
            return false;

        // Convert model-space t back to world-space distance.
        var hitLocal = localOrigin + localDir * bestT;
        var hitWorld = Vector3.Transform(hitLocal, world);
        bestT = Vector3.Distance(origin, hitWorld);
        return true;
    }

    private static bool RayTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        // Möller–Trumbore, two-sided.
        t = 0f;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(dir, e2);
        var det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f)
            return false;

        var invDet = 1f / det;
        var s = origin - a;
        var u = Vector3.Dot(s, p) * invDet;
        if (u < 0f || u > 1f)
            return false;

        var q = Vector3.Cross(s, e1);
        var v = Vector3.Dot(dir, q) * invDet;
        if (v < 0f || u + v > 1f)
            return false;

        t = Vector3.Dot(e2, q) * invDet;
        return t >= 0f;
    }
}
