using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using NoireLib.Draw3D.Core;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using NoireLib.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D;

/// <summary>
/// The Draw3D hub: a lazily initialized D3D11 world renderer composited into the game's frame under every plugin
/// window, with retained content on <see cref="MainScene"/> and per-frame markers on <see cref="Im"/>.
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

    private static Core.GBufferInject? gbufferInject;

    private static Core.ShadowInject? shadowInject;

    private static int gbufferIdleFrames;

    private static int shadowIdleFrames;

    private const int GBufferIdleFramesBeforeOff = 3;
    private static ScenePass? scenePass;
    private static Compositor? compositor;
    private static TemporalResolver? temporalResolver;
    private const int JitterGraceFrames = 16; // an 8-phase cycle can land on its centre
    private const float MaxJitterPixels = 4f;
    private const float MinJitterNdc = 1e-5f; // float noise without jitter measured at 5e-8; a real jitter is ~1e-3
    private static int framesSinceJitter = int.MaxValue / 2;
    private static RenderTarget? sceneRt;
    private static RenderTarget? outlineMaskRt;
    private static RenderTarget? outlineVisRt;  // r = worldVisible per silhouette pixel
    private static DepthTarget? privateDepth;
    private static DepthTarget? layerDepth; // private depth plus translucent items, for the temporal resolve
    private static RenderTarget? worldHeightRt; // R32F, read only by HighestOnly decals
    private const uint WorldHeightResolution = 1024; // texels per side over the ~80m region
    private static SceneDepth? sceneDepth;
    private static SceneStencil? sceneStencil; // its stencil plane marks characters

    // The scene depth as the opaque pass left it, before water and translucent passes write their surfaces.
    private static OpaqueDepthSnapshot? opaqueDepth;
    private static TranslucentOcclusion translucentOcclusion = TranslucentOcclusion.SeeThrough;
    private static int opaqueDepthKeepAlive;
    private static bool frameOpaqueDepthUsable;
    // Kept this long without see-through content: content that blinks never misses a snapshot.
    private const int OpaqueDepthKeepAliveFrames = 120;

    // Rebuilt on the framework thread once the player leaves the cached region, swapped atomically.
    private sealed class WorldCollisionCache { public NoireLib.Draw3D.Geometry.Mesh Mesh = null!; public Vector3 Center; }
    private static volatile WorldCollisionCache? worldCollision;
    private static Vector3 worldCollisionBuiltAt;
    private static bool worldCollisionEverBuilt;
    private static volatile bool lastFrameNeededHeightMap;

    private static volatile int lastTopSurfaceDecals;
    private static volatile bool lastHeightMapRendered;
    private static float lastHeightCeiling;
    private const float WorldCollisionRadius = 40f;
    private const float WorldCollisionRebuildDistance = 8f;
    private const int WorldCollisionMaxTriangles = 60000;
    private static RenderStats? renderStats;
    private static RenderTargetTap? renderTargetTap;

    internal static RenderTargetTap? RenderTargetTapForDiagnostics => renderTargetTap;

    // This frame's snapshot, 0 when the frame had none.
    internal static nint OpaqueDepthTextureForDiagnostics => frameOpaqueDepthUsable && opaqueDepth != null ? opaqueDepth.Texture : 0;
    private static CameraConstantCapture? cameraCapture;
    private static DepthProbe? depthProbe;
    private static bool stencilDebug;
    private static DateTime lastStencilLog = DateTime.MinValue;

    private static ComPtr<ID3D11RenderTargetView> backbufferRtv;
    private static nint backbufferPtr;

    // Set when the frame was injected. The present-time path then skips its own composite.
    private static ComPtr<ID3D11RenderTargetView> presentRtv;
    private static nint presentRtvPtr;
    private static uint presentRtvWidth, presentRtvHeight;
    private static Draw3DLayering layering = Draw3DLayering.UnderGameUi;
    private static bool injectionInitialized;
    private static volatile bool injectedSinceLastPresent;

    // Stamped before the game's plate pass. The one place Draw3D writes into the game's depth buffer.
    private static GameDepthTarget? gameDepthTarget;
    private static NameplateOcclusion nameplateOcclusion = NameplateOcclusion.DepthAware;

    // The present buffer is snapshotted either side of the game's UI pass and differenced at composite time.
    private static UiDiffMask? uiDiffMask;
    private static UiDiffMaskHealth? uiDiffMaskHealth;
    private static bool keepUiOnTop = true;
    private static float nameplateDimFactor;

    private static Scene3D? mainScene;
    private static ImDraw3D? im;
    private static readonly List<Scene3D> Scenes = new();
    private static readonly List<RenderView> Views = new();

    // Iterated outside the lock. Callbacks may add or drop a scene or view mid-loop.
    private static readonly List<Scene3D> SceneScratch = new();
    private static readonly List<RenderView> ViewScratch = new();
    private static readonly Vector4[] ProtectRects = new Vector4[128];
    private static readonly float[] ProtectFactors = new float[128];
    private static readonly float[] PlateDistances = new float[128];
    private static readonly float[] PlateCoveredBy = new float[128];
    private static readonly float[] PlateRawDistance = new float[128]; // the game's own plate distance field, squared
    private static int lastPlateCount;

    private static long frameId;
    private static FrameContext lastFrame;
    private static bool lastFrameValid;
    private static GameRenderSources.CameraData lastCameraData;
    private static Vector4 lastDepthMap;

    private static GameRenderSources.CameraData frameworkCamera;
    private static volatile bool frameworkCameraValid;
    private static bool frameworkHooked;

    private static bool keepDrawingWhenUiHidden = true;
    private static bool forcedAutoHide, forcedUserHide, forcedCutsceneHide, forcedGposeHide;

    private static int passFailStreak;
    private static bool passFaultLogged;
    internal static bool Wireframe;

    internal static bool DecalShapeOutlines;

    internal static bool DecalVolumeOutlines;

    /// <summary>Gets the main retained scene, initializing the renderer on first access.</summary>
    /// <exception cref="InvalidOperationException">NoireLib is not initialized.</exception>
    public static Scene3D MainScene
    {
        get
        {
            EnsureInitialized();
            return mainScene!;
        }
    }

    /// <summary>Gets the immediate-mode drawing layer, initializing the renderer on first access.</summary>
    public static ImDraw3D Im
    {
        get
        {
            EnsureInitialized();
            return im!;
        }
    }

    /// <summary>Gets or sets the master switch, setting it back to true re-arming the renderer after a fault.</summary>
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

    /// <summary>
    /// Gets or sets whether water and surfaces drawn after the game's opaque pass hide Draw3D content. SeeThrough by default.
    /// Materials and shape styles override it.
    /// </summary>
    public static TranslucentOcclusion TranslucentOcclusion
    {
        get => translucentOcclusion;
        set => translucentOcclusion = value;
    }

    /// <summary>Gets or sets the 0-1 opacity applied to the whole 3D layer at composite time.</summary>
    public static float LayerOpacity { get; set; } = 1f;

    /// <summary>Gets or sets whether the collision world is rendered into the height map <see cref="DecalProjection.HighestOnly"/> reads. Off falls back to <see cref="DecalProjection.AllSurfaces"/>.</summary>
    public static bool CollisionHeightMap { get; set; } = true;

    /// <summary>Gets or sets the end-of-frame game stencil value marking characters, for cutting them out of ground decals. 0 disables it.</summary>
    public static uint CharacterStencilValue { get; set; } = 0x08;

    /// <summary>
    /// Gets or sets the elevation band in world units below a column's highest collision surface that
    /// <see cref="DecalProjection.HighestOnly"/> still paints, 0 disabling that projection entirely.
    /// </summary>
    public static float TopSurfaceThreshold { get; set; } = 0.1f;

    internal static void SetLayering(Draw3DLayering value)
    {
        layering = value;
        ApplyInjectionState();
    }

    internal static void SetKeepUiOnTop(bool value)
    {
        keepUiOnTop = value;
        ApplyInjectionState();
    }

    private static bool NeedsInjectionPoint => layering == Draw3DLayering.UnderGameUi || keepUiOnTop;

    private static void ApplyInjectionState()
    {
        if (NeedsInjectionPoint)
        {
            var tap = EnsureRenderTargetTap();
            if (tap == null)
                return;

            tap.Injector = InjectComposite;
            tap.SetInjection(true);
        }
        else
        {
            renderTargetTap?.SetInjection(false);
        }
    }

    // Every present. The snapshot is copied only while see-through content drew recently.
    private static void SyncOpaqueDepthSnapshot()
    {
        if (opaqueDepthKeepAlive > 0)
            opaqueDepthKeepAlive--;

        if (!enabled || opaqueDepthKeepAlive <= 0)
        {
            if (renderTargetTap is { OpaqueDepthEnabled: true } idle)
                idle.OpaqueDepthEnabled = false;
            return;
        }

        if (renderTargetTap is { OpaqueDepthEnabled: true } or { OpaqueDepthFaulted: true })
            return;

        var tap = EnsureRenderTargetTap();
        if (tap == null)
            return;

        tap.OpaqueDepthSnapshotter ??= TakeOpaqueDepth;
        tap.OpaqueDepthEnabled = true;
    }

    // Render thread, from inside the tap, between two of the game's passes.
    private static bool TakeOpaqueDepth(nint sceneDepthTexture)
    {
        var device = renderDevice;
        if (disposed || device == null)
            return false;

        opaqueDepth ??= new OpaqueDepthSnapshot();
        return opaqueDepth.Take(device, sceneDepthTexture);
    }

    /// <summary>Gets or sets whether the 3D layer keeps rendering while the game UI is hidden by a cutscene, GPose or the user.</summary>
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
    /// Gets a value indicating whether the game's UI is hidden by the player's UI-hide toggle, a cutscene or
    /// GPose, read from the game's own state so it stays truthful while Dalamud's per-plugin overrides are held.
    /// </summary>
    public static bool IsGameUiHidden
        => NoireService.GameGui.GameUiHidden
           || NoireService.ClientState.IsGPosing
           || NoireService.Condition.Any(ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78, ConditionFlag.OccupiedInCutSceneEvent);

    /// <summary>
    /// Registers the <c>/noire3d</c> diagnostics command, a no-op once registered, exposing the same toolkit
    /// <see cref="Diagnostics"/> offers programmatically.
    /// </summary>
    public static void EnableDiagnosticsCommand()
    {
        EnsureInitialized();
        if (!commandRegistered)
            RegisterCommand();
    }

    /// <summary>
    /// Gets the global input settings: gestures, obstacle-occlusion, deselect, multi-select modifiers and debug.
    /// </summary>
    public static Draw3DInteraction Interaction { get; } = new();

    /// <summary>Gets or sets the input gate for <see cref="Pick"/>. Pick returns false when it says UI claims the mouse.</summary>
    public static Func<bool>? PickInputGate { get; set; }

    /// <summary>Nearby game objects as exclusion cylinders. Framework or draw thread, never <see cref="Scene.Scene3D.OnPrepareFrame"/>.</summary>
    /// <param name="filter">The objects to include, or null for characters, monsters and NPCs.</param>
    /// <param name="radiusScale">The multiplier on each object's hitbox radius.</param>
    /// <returns>The exclusion volumes.</returns>
    public static IReadOnlyList<ExcludeVolume> GetActorExclusions(Func<IGameObject, bool>? filter = null, float radiusScale = 1f)
    {
        var list = new List<ExcludeVolume>();
        GameRenderSources.CollectActorExclusions(list, ScenePass.MaxActorVolumes, filter, radiusScale <= 0f ? 1f : radiusScale);
        return list;
    }

    /// <summary>Gets the lighting parameters for <see cref="Materials.MaterialDomain.Lit"/> materials.</summary>
    public static Draw3DLighting Lighting { get; } = new();

    internal static Vector4 DepthSampleJitterUv;

    /// <summary>Gets the performance settings: automatic model level-of-detail, and distance and screen-size culling.</summary>
    public static Draw3DPerformance Performance { get; } = new();

    /// <summary>Gets what <see cref="DrawGameLit(Scene.SceneNode)"/> writes into the game's G-buffer.</summary>
    public static Draw3DGameLit GameLit { get; } = new();

    /// <summary>Reads every channel of the game's G-buffer at one screen point. Needs a <c>/noire3d rtlog</c> capture first.</summary>
    /// <param name="screenPosition">Where to sample, in display pixels.</param>
    /// <param name="samples">Receives one RGBA value per target, in bind order, cleared first.</param>
    /// <param name="patch">The square patch averaged around the point.</param>
    /// <returns>True when every target was read.</returns>
    public static bool TrySampleGameGBuffer(Vector2 screenPosition, List<Vector4> samples, int patch = 4)
    {
        samples.Clear();
        if (renderDevice is null || renderTargetTap is not { } tap)
            return false;

        var targets = tap.GBufferTargets();
        return targets.Count != 0
            && GBufferProbe.TrySampleAt(renderDevice, targets, (int)screenPosition.X, (int)screenPosition.Y, patch, samples);
    }

    /// <summary>Gets a snapshot of the renderer's counters.</summary>
    public static Draw3DStats Stats => BuildStats();

    /// <summary>
    /// Gets a value indicating whether the game's camera was readable on the last frame, without which
    /// <see cref="Pick"/> returns nothing.
    /// </summary>
    public static bool HasValidFrame => lastFrameValid;

    /// <summary>Gets the diagnostics toolkit, independently of the <c>/noire3d</c> command.</summary>
    public static Draw3DDiagnostics Diagnostics { get; } = new();

    /// <summary>Raised whenever the self-disable ladder trips (a pipeline, feature, depth, pass, or the renderer was disabled).</summary>
    public static event Action<Draw3DFault>? OnFault;

    /// <summary>
    /// Creates an extra retained scene, rendered after <see cref="MainScene"/>, whose
    /// <see cref="Scene3D.Dispose"/> frees its nodes, owned meshes and editors and unregisters it.
    /// </summary>
    /// <param name="name">The scene name, or null for none.</param>
    /// <returns>The new scene.</returns>
    public static Scene3D CreateScene(string? name = null)
    {
        EnsureInitialized();
        var scene = new Scene3D(name);
        lock (Scenes)
            Scenes.Add(scene);
        return scene;
    }

    /// <summary>
    /// Unregisters an extra scene without freeing its content, unlike <see cref="Scene3D.Dispose"/>, and never removes
    /// <see cref="MainScene"/>.
    /// </summary>
    /// <param name="scene">The scene to remove.</param>
    /// <returns>True when the scene was registered.</returns>
    public static bool RemoveScene(Scene3D scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ReferenceEquals(scene, mainScene))
            return false;

        lock (Scenes)
            return Scenes.Remove(scene);
    }

    internal static void ClearAllSelections()
    {
        lock (Scenes)
        {
            foreach (var scene in Scenes)
                scene.Selection.Clear();
        }
    }

    /// <summary>
    /// Creates a render-to-texture view of a scene, rendered before the main pass each frame until disposed.
    /// </summary>
    /// <param name="scene">The scene to render.</param>
    /// <param name="camera">The virtual camera.</param>
    /// <param name="width">The output width in pixels.</param>
    /// <param name="height">The output height in pixels.</param>
    /// <returns>The new view.</returns>
    public static RenderView CreateRenderView(Scene3D scene, Camera3D camera, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(scene);
        EnsureInitialized();
        var view = new RenderView(scene, camera, width, height);
        lock (Views)
            Views.Add(view);
        return view;
    }

    /// <summary>Registers a custom pipeline. Its HLSL may include <c>Common.hlsli</c> and exposes <c>vs</c> and <c>ps</c>.</summary>
    /// <param name="name">The pipeline name materials reference.</param>
    /// <param name="hlslSource">The full HLSL source.</param>
    /// <returns>Whether it compiled. A compile error is logged.</returns>
    public static bool RegisterPipeline(string name, string hlslSource)
    {
        EnsureInitialized();
        return shaderLibrary?.RegisterCustom(name, hlslSource) ?? false;
    }

    /// <summary>
    /// Builds the world-space ray through a screen position from last frame's camera, the same ray
    /// <see cref="Pick"/> uses.
    /// </summary>
    /// <param name="screenPx">The screen position in pixels.</param>
    /// <param name="origin">Receives the ray's start, at the camera.</param>
    /// <param name="direction">Receives its normalised direction.</param>
    /// <returns>True when a frame has rendered and the position unprojects.</returns>
    public static bool TryScreenToRay(Vector2 screenPx, out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;
        return lastFrameValid && lastFrame.TryScreenToRay(screenPx, out origin, out direction);
    }

    /// <summary>
    /// Picks scene nodes under a screen position using last frame's camera, by bounding sphere and refined to
    /// exact triangles for meshes created with <c>keepCpuData</c>.
    /// </summary>
    /// <param name="screenPx">The screen position in pixels.</param>
    /// <returns>The hits nearest first, empty when <see cref="PickInputGate"/> claims input or no frame has rendered.</returns>
    public static PickHit[] Pick(Vector2 screenPx)
    {
        if (!lastFrameValid || (PickInputGate != null && !PickInputGate()))
            return Array.Empty<PickHit>();

        var frame = lastFrame;
        if (!frame.TryScreenToRay(screenPx, out var origin, out var direction))
            return Array.Empty<PickHit>();

        Vector3? groundSurface = null;
        if (NoireService.IsInitialized() && NoireService.GameGui.ScreenToWorld(screenPx, out var gw))
            groundSurface = gw;

        var t0 = Stopwatch.GetTimestamp();
        var nodes = 0;
        var refined = 0;
        PickHit[] result;
        lock (Scene3D.GraphLock)
        {
            var hits = pickScratch;
            hits.Clear();
            lock (Scenes)
            {
                foreach (var scene in Scenes)
                {
                    if (!scene.Visible)
                        continue;

                    foreach (var root in scene.Roots)
                        PickNode(root, origin, direction, groundSurface, hits, ref nodes, ref refined);
                }
            }

            hits.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            result = hits.Count == 0 ? Array.Empty<PickHit>() : hits.ToArray();
        }

        if (renderStats is { } s)
        {
            s.PickMicros = (int)((Stopwatch.GetTimestamp() - t0) * 1_000_000 / Stopwatch.Frequency);
            s.PickNodes = nodes;
            s.PickRefined = refined;
        }

        return result;
    }

    private static readonly List<PickHit> pickScratch = new(16);

    /// <summary>The world point of the nearest rendered surface at a pixel, collision or not. Render or draw thread.</summary>
    /// <param name="screenPx">The screen position, in framebuffer pixels.</param>
    /// <param name="world">The surface point.</param>
    /// <returns>False on a fallback camera, open sky or a read fault.</returns>
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

        // A blocking map every hover frame stalls the device.
        depthProbe ??= new DepthProbe();
        if (!depthProbe.TrySample(renderDevice, in info, screenPx, frame.ViewportSize, out var sample) || float.IsNaN(sample))
            return false;

        var vp = frame.ViewportSize;
        if (vp.X <= 0f || vp.Y <= 0f)
            return false;

        // Sky or unwritten depth drives clip.w to zero.
        var ndc = new Vector4(screenPx.X / vp.X * 2f - 1f, 1f - screenPx.Y / vp.Y * 2f, sample, 1f);
        var c = Vector4.Transform(ndc, frame.InvViewProj);
        if (!float.IsFinite(c.W) || MathF.Abs(c.W) < 1e-6f)
            return false;

        world = new Vector3(c.X, c.Y, c.Z) / c.W;
        return float.IsFinite(world.X) && float.IsFinite(world.Y) && float.IsFinite(world.Z);
    }

    private static void ProbeStencilGrid(RenderDevice device, in GameRenderSources.BackBufferInfo backBuffer)
    {
        var now = DateTime.UtcNow;
        if ((now - lastStencilLog).TotalMilliseconds < 500)
            return;
        lastStencilLog = now;

        if (backBuffer.Width == 0 || backBuffer.Height == 0 || !GameRenderSources.TryGetDepthTexture(out var info))
            return;

        var display = new Vector2(backBuffer.Width, backBuffer.Height);
        const int cols = 24, rows = 14;
        var pts = new List<Vector2>(cols * rows);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                pts.Add(new Vector2((c + 0.5f) / cols * display.X, (r + 0.5f) / rows * display.Y));

        var values = DepthReadback.TryReadStencilAtPoints(device, in info, pts, display, out var desc);
        if (values == null)
        {
            NoireLogger.LogInfo($"[StencilDebug] no readable stencil plane ({desc}).", "[Draw3D] ");
            return;
        }

        var counts = new SortedDictionary<int, int>();
        foreach (var v in values)
            if (v >= 0)
                counts[v] = counts.GetValueOrDefault(v) + 1;

        var summary = new System.Text.StringBuilder();
        foreach (var kv in counts)
        {
            if (summary.Length > 0)
                summary.Append(", ");
            summary.Append($"0x{kv.Key:X2}={kv.Value}");
        }

        NoireLogger.LogInfo($"[StencilDebug] {desc} - stencil in view (value=grid-hits): {summary}", "[Draw3D] ");
    }

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

            NoireService.PluginInterface.UiBuilder.RunWhenUiPrepared(() =>
            {
                if (disposed)
                    return true; // dev-reload before first Present would leak device objects

                deviceObjectsReady = true;
                return true;
            });

            NoireService.PluginInterface.UiBuilder.Draw += OnDraw;
            NoireService.PluginInterface.UiBuilder.ResizeBuffers += OnResizeBuffers;

            if (!NoireLibMain.IsRegisteredOnDispose(DisposeKey))
                NoireLibMain.RegisterOnDispose(DisposeKey, Cleanup);

            initialized = true;
            UpdateFrameworkHook();
            RefreshUiHideOverrides();
            NoireLogger.LogInfo("NoireDraw3D initialized (device objects deferred to first Present).", "[Draw3D] ");
        }
    }

    // D3D11 devices are free-threaded.
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

    // Deferred to the next frame. In-flight frames never bind freed objects.
    internal static void EnqueueRelease(Action release)
    {
        if (disposed)
        {
            try { release(); }
            catch (Exception ex) { NoireLogger.LogError(ex, "Draw3D deferred release failed.", "[Draw3D] "); }
            return;
        }

        ReleaseQueue.Enqueue(release);
    }

    internal static void UnregisterView(RenderView view)
    {
        lock (Views)
            Views.Remove(view);
    }

    internal static void RaiseFault(Draw3DFaultKind kind, Exception? ex, string message)
    {
        try
        {
            OnFault?.Invoke(new Draw3DFault(kind, message, ex));
        }
        catch (Exception handlerEx)
        {
            NoireLogger.LogError(handlerEx, "A Draw3D OnFault handler threw.", "[Draw3D] ");
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

            // No new frames first.
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

            lock (Scenes)
            {
                foreach (var scene in Scenes)
                    scene.DisposeContentsInternal();
                Scenes.Clear();
                SceneScratch.Clear();
            }

            RenderView[] views;
            lock (Views)
            {
                views = Views.ToArray();
                ViewScratch.Clear();
            }

            foreach (var view in views)
                view.Dispose();

            im?.DisposeResources();

            DrainReleaseQueue();

            // Passes, caches, targets and stats last. The releases above reference them.
            scenePass?.Dispose();
            scenePass = null;
            compositor?.Dispose();
            compositor = null;
            temporalResolver?.Dispose();
            temporalResolver = null;
            shaderLibrary?.Dispose();
            shaderLibrary = null;
            gbufferInject?.Dispose();
            gbufferInject = null;
            shadowInject?.Dispose();
            shadowInject = null;
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
            layerDepth?.Dispose();
            layerDepth = null;
            worldHeightRt?.Dispose();
            worldHeightRt = null;
            worldCollision?.Mesh?.Dispose();
            worldCollision = null;
            worldCollisionEverBuilt = false;
            sceneDepth?.Dispose();
            sceneDepth = null;
            sceneStencil?.Dispose();
            sceneStencil = null;
            uiDiffMask?.Dispose();
            uiDiffMask = null;
            uiDiffMaskHealth?.Dispose();
            uiDiffMaskHealth = null;
            renderStats?.Dispose();
            renderStats = null;
            cameraCapture?.Dispose();
            cameraCapture = null;
            renderTargetTap?.Dispose();
            renderTargetTap = null;
            opaqueDepth?.Dispose();
            opaqueDepth = null;
            opaqueDepthKeepAlive = 0;
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

            // The device last.
#if DEBUG
            renderDevice?.ReportLiveObjects();
#endif
            renderDevice?.Dispose();
            renderDevice = null;

            NoireLogger.LogInfo("NoireDraw3D disposed.", "[Draw3D] ");
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
                NoireLogger.LogError(ex, "Draw3D deferred release failed.", "[Draw3D] ");
            }
        }
    }

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

    private static void HandlePassFailure(Exception ex)
    {
        passFailStreak++;
        if (!passFaultLogged)
        {
            passFaultLogged = true;
            NoireLogger.LogError(ex, "Draw3D frame failed (self-disable ladder rung 4: layer skipped).", "[Draw3D] ");
            RaiseFault(Draw3DFaultKind.Pass, ex, "Scene pass failed; layer skipped this frame.");
        }

        if (passFailStreak >= 3)
        {
            enabled = false;
            NoireLogger.LogError("Draw3D disabled after 3 consecutive frame failures (rung 5). Set NoireDraw3D.Enabled = true to re-arm.", "[Draw3D] ");
            RaiseFault(Draw3DFaultKind.Renderer, ex, "Renderer disabled after repeated failures.");
        }
    }

    private readonly record struct SceneRenderResult(bool HasContent, int RectCount);

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

        // The render-thread hook needs a device.
        if (NeedsInjectionPoint && !injectionInitialized)
        {
            injectionInitialized = true;
            ApplyInjectionState();
        }

        // Must run every present, UI hidden or not, or injection stalls.
        renderTargetTap?.OnPresent((nint)backBuffer.Texture);

        SyncOpaqueDepthSnapshot();

        // Idle injection lapses, releasing its per-draw managed callback.
        if (renderTargetTap is { GBufferInjectionEnabled: true } gbufTap && ++gbufferIdleFrames > GBufferIdleFramesBeforeOff)
        {
            gbufTap.GBufferInjectionEnabled = false;
            gbufferInject?.Clear();
        }

        // Cleared with the lapse. Resuming would otherwise cast one frame of shadow from the old positions.
        if (renderTargetTap is { ShadowInjectionEnabled: true } shadowTap && ++shadowIdleFrames > GBufferIdleFramesBeforeOff)
        {
            shadowTap.ShadowInjectionEnabled = false;
            shadowInject?.Clear();
        }

        // InjectComposite already composited under the native UI.
        if (injectedSinceLastPresent)
        {
            injectedSinceLastPresent = false;
            return;
        }

        Matrix4x4? presentGpuVp = null;
        if (cameraCapture != null && cameraCapture.TryGetCommitted(presentTimePath: true, out var presentCaptured))
            presentGpuVp = presentCaptured;

        var ctx = device.Context;
        if (renderTargetTap != null)
            renderTargetTap.SuppressSelf = true; // Draw3D's own binds must not pollute an rtlog capture
        stateGuard!.Capture(ctx);
        try
        {
            var result = RenderMainScene(device, ctx, in backBuffer, stats, cameraOverride: null, presentGpuVp, insideFrame: false);
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
            stateGuard.Restore(ctx);
            if (renderTargetTap != null)
                renderTargetTap.SuppressSelf = false;
        }
    }

    // insideFrame: composited at the injection point, before the frame boundary. Otherwise at present time, after it.
    private static SceneRenderResult RenderMainScene(RenderDevice device, ID3D11DeviceContext* ctx, in GameRenderSources.BackBufferInfo backBuffer, RenderStats stats, GameRenderSources.CameraData? cameraOverride, Matrix4x4? gpuViewProj, bool insideFrame)
    {
        frameOpaqueDepthUsable = false;

        // Dalamud's hide flags stay held so the draw callback keeps firing.
        if (!keepDrawingWhenUiHidden && IsGameUiHidden)
        {
            stats.FramesSkippedUiHidden++;
            return default;
        }

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
        var usedGpuCamera = false;
        if (cam.HasRenderCamera)
        {
            camView = cam.View;
            camProj = cam.Proj;

            // Without captured constants the control's matrix is used. The struct is one frame ahead.
            usedGpuCamera = gpuViewProj.HasValue && Diagnostics.PreferCapturedCamera;
            var fallbackViewProj = cam.HasControlViewProj ? cam.ControlViewProj : camView * camProj;
            viewProj = usedGpuCamera ? gpuViewProj!.Value : fallbackViewProj;
            if (!usedGpuCamera && cam.HasControlViewProj)
                stats.ControlCameraFrames++;

            // Composited after the game's TAA resolve.
            var removedJitter = Vector2.Zero;
            if (usedGpuCamera && Diagnostics.RemoveTemporalJitter)
            {
                var centred = Core.CameraConstantCapture.RemoveTemporalJitter(in viewProj, out removedJitter);

                // TAA jitter is sub-pixel. More than a few pixels means the matrix is not the main camera.
                var maxJitter = MaxJitterPixels * 2f / Math.Max(Math.Min(backBuffer.Width, backBuffer.Height), 1f);
                if (!float.IsFinite(removedJitter.X) || !float.IsFinite(removedJitter.Y)
                    || MathF.Abs(removedJitter.X) > maxJitter || MathF.Abs(removedJitter.Y) > maxJitter)
                {
                    stats.CameraRejectedAtUse++;
                    usedGpuCamera = false;
                    removedJitter = Vector2.Zero;
                    viewProj = fallbackViewProj;
                    if (cam.HasControlViewProj)
                        stats.ControlCameraFrames++;
                }
                else
                {
                    viewProj = centred;
                }
            }

            viewProj = Core.CameraConstantCapture.ApplyPixelOffset(in viewProj, Diagnostics.GamePixelOffset, new Vector2(backBuffer.Width, backBuffer.Height));

            // The game's depth is still jittered.
            Diagnostics.LastRemovedJitter = removedJitter;
            framesSinceJitter = removedJitter.LengthSquared() > MinJitterNdc * MinJitterNdc ? 0 : Math.Min(framesSinceJitter + 1, int.MaxValue / 2);
            DepthSampleJitterUv = new Vector4(removedJitter.X * 0.5f, -removedJitter.Y * 0.5f, 0f, 0f);
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

        // The game's matrices do not reproduce the buffer's near/w depth. The Z column is rebuilt as reversed-Z infinite-far.
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

        // A depth failure means depth-off mode. The mapping is analytic from near, far and the Z flags.
        sceneDepth ??= new SceneDepth();
        var depthSrvOk = sceneDepth.Update(device);

        sceneStencil ??= new SceneStencil();
        sceneStencil.Update(device);

        if (stencilDebug)
            ProbeStencilGrid(device, in backBuffer);
        var hasDepth = depthSrvOk && cam.HasRenderCamera;
        var depthMap = hasDepth
            ? DepthCalibration.AnalyticMap(near, cam.FarPlane, cam.StandardZ, cam.FiniteFarPlane)
            : Vector4.Zero;

        if (!hasDepth)
            stats.DepthOffFrames++;

        // Only a copy of this frame's own depth texture. A stale one would occlude against last frame's camera.
        frameOpaqueDepthUsable = hasDepth && opaqueDepth is { Srv: not null } od && renderTargetTap != null
            && renderTargetTap.HasOpaqueDepth(afterFrameBoundary: !insideFrame) && od.Source == sceneDepth.Texture;

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
        if (usedGpuCamera)
            stats.GpuCameraFrames++;

        // Render-thread callbacks run first so their mutations land this frame.
        var scenes = SceneScratch;
        lock (Scenes)
        {
            scenes.Clear();
            scenes.AddRange(Scenes);
        }

        foreach (var scene in scenes)
            scene.FirePrepare(in frame);

        // A throwing subscriber must never fault the render.
        var overlay = OnRenderOverlay;
        if (overlay != null)
        {
            try
            {
                overlay(frame);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "A NoireDraw3D render-overlay handler threw; overlay skipped this frame.", "[Draw3D] ");
            }
        }

        // A decal's shape lives in its pixel shader.
        if (Wireframe || DecalShapeOutlines)
        {
            foreach (var scene in scenes)
                scene.TraceDecalShapes(im!);
        }

        if (DecalVolumeOutlines)
        {
            foreach (var scene in scenes)
                scene.TraceDecalVolumes(im!);
        }

        Diagnostics.OnFrame(in frame, in cam, hasDepth);
        Diagnostics.OnFrameRendered(device, in frame, sceneDepth); // runs even on empty frames

        var views = ViewScratch;
        lock (Views)
        {
            views.Clear();
            views.AddRange(Views);
        }

        var anyScene = false;
        foreach (var scene in scenes)
            anyScene |= scene.Visible && scene.NodeCount > 0;
        var anyView = false;
        foreach (var v in views)
            anyView |= v.Enabled && !v.IsDisposed;

        if (!anyScene && !anyView && !im!.HasPending)
        {
            stats.FramesSkippedEmpty++;
            return default;
        }

        stateCache ??= new StateCache();
        shaderLibrary ??= new ShaderLibrary();
        scenePass ??= new ScenePass();
        compositor ??= new Compositor();
        sceneRt ??= new RenderTarget();
        privateDepth ??= new DepthTarget();

        // Supersampling grows only sceneRt and the targets sized from it, falling back to 1x when it cannot allocate.
        var supersample = Performance.SupersampleFactor;
        var renderW = supersample > 1f ? (uint)MathF.Round(backBuffer.Width * supersample) : backBuffer.Width;
        var renderH = supersample > 1f ? (uint)MathF.Round(backBuffer.Height * supersample) : backBuffer.Height;

        if (!EnsureBackbufferRtv(device, backBuffer)
            || (!sceneRt.EnsureSize(device, renderW, renderH) && !sceneRt.EnsureSize(device, backBuffer.Width, backBuffer.Height)))
        {
            stats.FramesSkippedZeroSize++;
            return default;
        }

        if (shaderLibrary.GetComposite(device) == null)
            return default; // compile failure already logged

        stats.BeginFrameCounters();
        stats.DepthAvailable = hasDepth;
        stats.UsedFallbackCamera = usedFallback;
        stats.UsedGpuCamera = usedGpuCamera;
        lastDepthMap = depthMap;

        // [0..plateCount) are nameplates, [plateCount..rectCount) HUD addon rects at factor 1.
        var plateCount = 0;
        if (UiMaskActive && nameplateOcclusion != NameplateOcclusion.AlwaysVisible)
            plateCount = GameRenderSources.CollectNamePlateRects(ProtectRects, PlateDistances, 64, frame.ViewportSize, PlateRawDistance);

        var rectCount = plateCount;
        if (plateCount > 0)
            rectCount += GameRenderSources.CollectVisibleAddonRects(ProtectRects, plateCount, ProtectRects.Length - plateCount, frame.ViewportSize);

        stats.ProtectRects = rectCount;

        stats.BeginGpuTiming(device, ctx);

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
            scenePass.Execute(device, ctx, in viewFrame, view.Target, view.Depth, null, null, translucentOcclusion, null, null, 0u, 0f, Vector4.Zero, Vector4.Zero, shaderLibrary!, stateCache!, stats, Wireframe, Lighting);
        }

        scenePass!.BeginCollect(in frame, mainPass: true);
        im!.Consume(scenePass, in frame, stats, hasDepth, Wireframe, DecalShapeOutlines, DecalVolumeOutlines);
        foreach (var scene in scenes)
            scenePass.AddScene(scene, stats, hasDepth);

        // A null SRV degrades HighestOnly to AllSurfaces.
        ID3D11ShaderResourceView* worldHeightSrv = null;
        var worldHeightRegion = Vector4.Zero;
        var topSurfaceDecals = scenePass.CountTopSurfaceDecals();
        var needHeightMap = CollisionHeightMap && topSurfaceDecals > 0;
        lastFrameNeededHeightMap = needHeightMap;
        lastTopSurfaceDecals = topSurfaceDecals;
        lastHeightMapRendered = false;
        if (needHeightMap && hasDepth && worldCollision is { } wc && wc.Mesh is { } wcMesh)
        {
            worldHeightRt ??= new RenderTarget(DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT);
            if (worldHeightRt.EnsureSize(device, WorldHeightResolution, WorldHeightResolution))
            {
                var minX = wc.Center.X - WorldCollisionRadius;
                var minZ = wc.Center.Z - WorldCollisionRadius;
                var size = 2f * WorldCollisionRadius;
                var heightMatrix = BuildHeightMapMatrix(minX, minZ, size);
                // A roof above every decal cannot mask the ground.
                var heightCeiling = scenePass.MaxTopSurfaceDecalBoxTopY() + TopSurfaceThreshold;
                lastHeightCeiling = heightCeiling;
                // The target is only cleared on the drawing path.
                if (scenePass.RenderWorldHeight(device, ctx, wcMesh, wc.Center, heightMatrix, heightCeiling, worldHeightRt, shaderLibrary!, stateCache!, stats))
                {
                    worldHeightSrv = worldHeightRt.Srv;
                    worldHeightRegion = new Vector4(minX, minZ, 1f / size, 1f);
                    lastHeightMapRendered = true;
                }
            }
        }

        var sceneStencilSrv = hasDepth ? sceneStencil.Srv : null; // t3
        var opaqueDepthSrv = frameOpaqueDepthUsable ? opaqueDepth!.Srv : null;

        scenePass.Execute(device, ctx, in frame, sceneRt!, privateDepth!, hasDepth ? sceneDepth.Srv : null, opaqueDepthSrv, translucentOcclusion,
            worldHeightSrv, sceneStencilSrv, CharacterStencilValue, TopSurfaceThreshold, worldHeightRegion, depthMap, shaderLibrary!, stateCache!, stats, Wireframe, Lighting);

        if (scenePass.LastWantedOpaqueDepth)
        {
            opaqueDepthKeepAlive = OpaqueDepthKeepAliveFrames;
            if (scenePass.LastUsedOpaqueDepth)
                stats.OpaqueDepthFrames++;
            else if (hasDepth)
                stats.OpaqueDepthMissedFrames++;
        }

        RenderOutlinePass(device, ctx, in frame, hasDepth ? sceneDepth.Srv : null, opaqueDepthSrv, depthMap, stats);

        stats.MarkSceneDone(ctx);

        // 1 leaves the letters on top.
        var behindFactor = Math.Clamp(nameplateDimFactor, 0f, 1f);
        if (plateCount > 0)
        {
            if (nameplateOcclusion == NameplateOcclusion.DepthAware)
                scenePass.ComputeRectOcclusion(in frame, ProtectRects, PlateDistances, ProtectFactors, plateCount, behindFactor, PlateCoveredBy);
            else
                for (var i = 0; i < plateCount; i++)
                    ProtectFactors[i] = behindFactor;
        }

        // The HUD stays on top inside covered plate regions.
        for (var i = plateCount; i < rectCount; i++)
            ProtectFactors[i] = 1f;

        lastPlateCount = plateCount;

        stats.FramesRendered++;
        return new SceneRenderResult(true, rectCount);
    }

    private static bool UiMaskActive => layering == Draw3DLayering.OverEverything && keepUiOnTop;

    private static string UiMaskDescription
        => !UiMaskActive ? (layering == Draw3DLayering.UnderGameUi ? "n/a (game draws its UI over the layer)" : "off")
            : uiDiffMaskHealth?.Description ?? "pending";

    private static void CompositeOverBackbuffer(RenderDevice device, ID3D11DeviceContext* ctx, in GameRenderSources.BackBufferInfo backBuffer, int rectCount)
    {
        var composite = shaderLibrary!.GetComposite(device);
        if (composite == null)
            return;

        ID3D11ShaderResourceView* beforeSrv = null;
        ID3D11ShaderResourceView* afterSrv = null;
        if (UiMaskActive && uiDiffMask is { } mask && renderTargetTap?.PresentBuffer is { } present && present != 0)
        {
            if (mask.CaptureAfter(device, ctx, present))
            {
                uiDiffMaskHealth ??= new UiDiffMaskHealth();
                uiDiffMaskHealth.Update(device, ctx, mask, lastFrame.FrameId);
                if (uiDiffMaskHealth.DiffUsable)
                {
                    beforeSrv = mask.BeforeSrv;
                    afterSrv = mask.AfterSrv;
                }
            }

            mask.EndFrame(); // the next frame must take its own "before" snapshot
        }

        compositor!.Blit(device, ctx, composite, stateCache!, LayerForComposite(device, ctx), beforeSrv, afterSrv, backbufferRtv.Get(), backBuffer.Width, backBuffer.Height, LayerOpacity, ProtectRects, ProtectFactors, rectCount);
    }

    private static ID3D11ShaderResourceView* LayerForComposite(RenderDevice device, ID3D11DeviceContext* ctx)
    {
        // Without jitter, accumulation only adds lag.
        var raw = sceneRt!.Srv;
        if (!Diagnostics.TemporalStabilization || !lastFrameValid || scenePass == null || framesSinceJitter > JitterGraceFrames)
        {
            temporalResolver?.Reset();
            return raw;
        }

        var pipeline = shaderLibrary!.GetTemporalResolve(device);
        if (pipeline == null)
            return raw;

        // Translucent items write no private depth and would otherwise reproject through the surface behind them.
        layerDepth ??= new DepthTarget();
        if (!scenePass.RenderLayerDepth(device, ctx, layerDepth, sceneRt.Width, sceneRt.Height, lastFrame.ViewProj, shaderLibrary, stateCache!)
            || layerDepth.Srv == null)
            return raw;

        temporalResolver ??= new TemporalResolver();
        // Decal-only pixels reproject through the surface they were painted on. The renderer-wide setting names it.
        var gameDepthSrv = lastFrame.HasDepth && sceneDepth != null
            ? frameOpaqueDepthUsable && translucentOcclusion == TranslucentOcclusion.SeeThrough ? opaqueDepth!.Srv : sceneDepth.Srv
            : null;
        var gameDepthMap = new Vector4(lastDepthMap.X, lastDepthMap.Y, lastFrame.NearPlane, 1f);
        var gameDepthUv = new Vector4(lastFrame.DepthUvScale.X, lastFrame.DepthUvScale.Y, DepthSampleJitterUv.X, DepthSampleJitterUv.Y);
        var resolved = temporalResolver.Resolve(device, ctx, pipeline, stateCache!, raw, layerDepth.Srv, gameDepthSrv, gameDepthMap, gameDepthUv,
            sceneRt.Width, sceneRt.Height, lastFrame.ViewProj, lastFrame.InvViewProj, Diagnostics.TemporalWeight);
        return resolved != null ? resolved : raw;
    }

    private static void RenderOutlinePass(RenderDevice device, ID3D11DeviceContext* ctx, in FrameContext frame, ID3D11ShaderResourceView* sceneDepthSrv, ID3D11ShaderResourceView* opaqueDepthSrv, Vector4 depthMap, RenderStats stats)
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
            return;

        outlineMaskRt ??= new RenderTarget();
        outlineVisRt ??= new RenderTarget();
        if (!outlineMaskRt.EnsureSize(device, scene.Width, scene.Height) || !outlineVisRt.EnsureSize(device, scene.Width, scene.Height))
            return;

        // The rim hides where world geometry stands in front.
        var privateDepthValid = privateDepth != null && pass.LastPrivateDepthWritten;
        pass.RenderOutlineMask(device, ctx, in frame, outlineMaskRt, outlineVisRt, privateDepth!, privateDepthValid, sceneDepthSrv, opaqueDepthSrv, translucentOcclusion, depthMap, shaders, cache, stats);
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

    private static bool InjectComposite(nint presentBufferResource)
    {
        if (disposed || !NeedsInjectionPoint || !enabled || !deviceObjectsReady)
            return false;

        var stats = renderStats;
        var device = renderDevice;
        var guard = stateGuard;
        if (stats == null || device == null || guard == null)
            return false;

        // Over everything, this point only takes the UI mask's pre-UI snapshot.
        if (layering == Draw3DLayering.OverEverything)
        {
            uiDiffMask ??= new UiDiffMask();
            uiDiffMask.CaptureBefore(device, device.Context, presentBufferResource);
            return false;
        }

        if (!GameRenderSources.TryGetBackBuffer(out var backBuffer) || !EnsurePresentRtv(device, presentBufferResource))
            return false;

        if (!TryGetInjectCamera(out var injectCam, out var injectGpuVp))
            return false;

        var ctx = device.Context;
        guard.Capture(ctx);
        try
        {
            // The native UI has not drawn yet and paints over the layer.
            var result = RenderMainScene(device, ctx, in backBuffer, stats, injectCam, injectGpuVp, insideFrame: true);
            if (result.HasContent)
            {
                var composite = shaderLibrary!.GetComposite(device);
                if (composite != null && sceneRt!.Srv != null)
                    compositor!.Blit(device, ctx, composite, stateCache!, LayerForComposite(device, ctx), null, null, presentRtv.Get(), presentRtvWidth, presentRtvHeight, LayerOpacity, ProtectRects, ProtectFactors, 0);

                // Covered behaves as AlwaysVisible here.
                if (nameplateOcclusion == NameplateOcclusion.DepthAware)
                    ProjectOpaqueDepthToGameBuffer(device, ctx);

                stats.EndGpuTiming(ctx);
            }
            else
            {
                temporalResolver?.Reset();
            }

            // The present-time path stands down, even for an empty frame.
            injectedSinceLastPresent = true;
            passFailStreak = 0;
            passFaultLogged = false;
            return true;
        }
        catch (Exception ex)
        {
            if (!passFaultLogged)
            {
                passFaultLogged = true;
                NoireLogger.LogError(ex, "Draw3D: inject render failed; falling back to the present-time composite.", "[Draw3D] ");
            }

            return false;
        }
        finally
        {
            guard.Restore(ctx);
        }
    }

    /// <summary>
    /// Draws a node opaque into the game's G-buffer for this frame only, in place of its own draw and without outline,
    /// fade, ground decal or draw-above. The game's lighting pass lights it.
    /// </summary>
    /// <param name="node">The node to inject, ignored when it has no mesh.</param>
    /// <returns>True when the node was queued.</returns>
    public static bool DrawGameLit(Scene.SceneNode node)
    {
        if (node?.Renderer?.Mesh is not { } mesh)
            return false;

        var material = node.Renderer.Material;
        var texture = material.Texture;
        var textured = texture is { IsDisposed: false };

        // A game material's normal map is AuxTexture0 and its specular map AuxTexture1.
        var normal = material.AuxTexture0;
        var specular = material.AuxTexture1;
        var hasMaps = normal is { IsDisposed: false } && specular is { IsDisposed: false };

        EnqueueGameLit(
            mesh,
            node.WorldMatrix,
            material.Color,
            textured,
            textured ? texture!.SrvPointer : 0,
            hasMaps ? normal!.SrvPointer : 0,
            hasMaps ? specular!.SrvPointer : 0,
            material.SurfaceParams.X > 0f ? material.SurfaceParams.X : 1f,
            // ShapeParams as Params0, SurfaceParams as Params2, like the scene pass.
            material.ShapeParams,
            material.SurfaceParams.Z);

        // Suppresses the node's own draw for this frame only.
        node.GameLitFrameId = frameId;
        return true;
    }

    internal static void EnqueueGameLit(Geometry.Mesh mesh, in Matrix4x4 world, Vector4 color, bool textured = false, nint srv = 0, nint normalSrv = 0, nint specularSrv = 0, float normalStrength = 1f, Vector4 dyeColorStrength = default, float dyeReference = 0f)
    {
        if (renderTargetTap is not { } tap || shaderLibrary is null || renderDevice is null)
            return;

        gbufferInject ??= new Core.GBufferInject();

        tap.GBufferInjector ??= RunGBufferInjection;

        // Enabled means a managed callback on every game draw.
        tap.GBufferInjectionEnabled = true;
        gbufferIdleFrames = 0;

        gbufferInject.Enqueue(new Core.GBufferInject.Item(
            mesh, world, color, textured, srv, normalSrv, specularSrv, normalStrength, dyeColorStrength, dyeReference));

        if (GameLit.CastShadows)
        {
            shadowInject ??= new Core.ShadowInject();
            tap.ShadowInjector ??= RunShadowInjection;
            tap.ShadowFrameBoundary ??= shadowInject.OnFrameBoundary;
            tap.ShadowInjectionEnabled = true;
            shadowIdleFrames = 0;
            shadowInject.Enqueue(new Core.ShadowInject.Item(mesh, world));
        }
    }

    /// <summary>
    /// Gets last frame's shadow-cast counters, all zero while casting is off: groups entered with work, groups drawn
    /// into, groups skipped for an unknown constant layout, drawn near-field groups, and meshes drawn at the last group.
    /// </summary>
    public static (int Entered, int Drawn, int Skipped, int NearField, int Meshes) ShadowCastStats
        => shadowInject is { } inject
            ? (inject.LastEnteredCount, inject.LastBindCount, inject.LastSkippedCount, inject.LastNearFieldCount, inject.LastInjectedCount)
            : default;

    // Reads the light's own constants.
    private static void RunShadowInjection(nint context)
    {
        if (shadowInject is not { HasWork: true } inject || renderDevice is null || shaderLibrary is null)
            return;

        inject.Execute(renderDevice, shaderLibrary, (TerraFX.Interop.DirectX.ID3D11DeviceContext*)context);
    }

    // Only with the committed capture. Nothing corrects a camera mismatch in the game's own pixels.
    private static void RunGBufferInjection()
    {
        if (gbufferInject is not { HasWork: true } inject || renderDevice is null || shaderLibrary is null)
            return;

        if (cameraCapture is null || !cameraCapture.TryGetCommitted(presentTimePath: false, out var viewProj))
        {
            inject.Clear();
            return;
        }

        if (lastFrameValid)
            viewProj = Core.CameraConstantCapture.ApplyPixelOffset(in viewProj, Diagnostics.GamePixelOffset, lastFrame.ViewportSize);

        inject.Execute(renderDevice, shaderLibrary, Matrix4x4.Transpose(viewProj), GameLit);
    }

    private static bool TryGetInjectCamera(out GameRenderSources.CameraData cam, out Matrix4x4? gpuViewProj)
    {
        gpuViewProj = null;
        if (cameraCapture != null && cameraCapture.TryGetCommitted(presentTimePath: false, out var captured))
            gpuViewProj = captured;

        if (renderTargetTap != null && renderTargetTap.TryGetWorldCamera(out cam))
            return true;

        return GameRenderSources.TryGetCamera(out cam);
    }

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
        // A lingering backbuffer reference fails the game's ResizeBuffers.
        backbufferRtv.Dispose();
        backbufferRtv = default;
        presentRtv.Dispose();
        presentRtv = default;
        presentRtvPtr = 0;
        backbufferPtr = 0;

        sceneRt?.Release();
        outlineMaskRt?.Release();
        outlineVisRt?.Release();
        privateDepth?.Release();
        worldHeightRt?.Release();
        sceneDepth?.Invalidate();
        gameDepthTarget?.Invalidate();
        uiDiffMask?.Release();
        depthProbe?.Release();
    }

    private static Vector3 UnprojectEye(in Matrix4x4 invViewProj)
    {
        // The near-plane centre, z = 1 under reversed Z.
        var p = Vector4.Transform(new Vector4(0f, 0f, 1f, 1f), invViewProj);
        return Math.Abs(p.W) > 1e-9f ? new Vector3(p.X, p.Y, p.Z) / p.W : Vector3.Zero;
    }

    // Must match the decal shader's WorldHeightRegion. Row-vector, transposed on upload.
    private static Matrix4x4 BuildHeightMapMatrix(float minX, float minZ, float size)
    {
        var s = 2f / size;
        return new Matrix4x4(
            s, 0f, 0f, 0f,
            0f, 0f, 0f, 0f,
            0f, -s, 0f, 0f,
            -minX * s - 1f, 1f + minZ * s, 0.5f, 1f);
    }

    private static void PrintTopSurfaceReport()
    {
        var decals = lastTopSurfaceDecals;
        var cache = worldCollision;
        var tris = cache is { Mesh: { } cm } ? cm.IndexCount / 3 : 0;

        Print("Draw3D top-surface (HighestOnly) report:");
        Print($"  decals asking for it (last frame): {decals}");
        Print($"  CollisionHeightMap: {(CollisionHeightMap ? "on" : "OFF")}");
        Print($"  TopSurfaceThreshold: {TopSurfaceThreshold:0.###} m");
        Print(cache != null
            ? $"  collision cache: {tris} triangles around ({cache.Center.X:0.#}, {cache.Center.Y:0.#}, {cache.Center.Z:0.#}), radius {WorldCollisionRadius:0} m, analytic colliders excluded"
            : "  collision cache: NONE collected near you yet");
        Print($"  height-map drawn last frame: {(lastHeightMapRendered ? $"yes (ceiling {lastHeightCeiling:0.##} m)" : "no")}");

        if (decals == 0)
            Print("  => No decal is set to HighestOnly - check Material.Projection on the one you spawned.");
        else if (!CollisionHeightMap)
            Print("  => Turn the collision height-map on (/noire3d heightmap).");
        else if (TopSurfaceThreshold <= 0f)
            Print("  => TopSurfaceThreshold is 0, which disables HighestOnly outright. Raise it (0.1 is the default).");
        else if (cache == null)
            Print("  => No collision is cached here. There is no height to compare against. It builds a frame after the "
                  + "first HighestOnly decal appears, and only where the area actually has collision.");
        else if (!lastHeightMapRendered)
            Print("  => The height-map pass did not draw - see /xllog for a pipeline or target fault.");
        else
            Print($"  => The chain is complete. HighestOnly only changes a pixel where a surface you can SEE sits at least "
                  + $"{TopSurfaceThreshold:0.###} m below the highest collision in the same column. It is a no-op on flat "
                  + "ground and on anything whose cover has no collision mesh.");
    }

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
        UpdateWorldCollision();
    }

    // Framework thread only, where the collision scene is safe to read.
    private static void UpdateWorldCollision()
    {
        if (!CollisionHeightMap || !lastFrameNeededHeightMap || !initialized || disposed || !NoireService.IsInitialized())
            return;

        Vector3 center;
        var player = NoireService.ObjectTable.LocalPlayer;
        if (player != null)
            center = player.Position;
        else if (frameworkCameraValid)
            center = frameworkCamera.Origin;
        else
            return;

        if (worldCollisionEverBuilt && Vector3.Distance(center, worldCollisionBuiltAt) < WorldCollisionRebuildDistance)
            return;

        try
        {
            worldCollisionBuiltAt = center;
            worldCollisionEverBuilt = true;

            var geo = World.WorldGeometry.Collect(center, WorldCollisionRadius, WorldCollisionMaxTriangles, includeAnalytic: false);
            var old = worldCollision;
            if (geo is { } g && g.Indices.Length > 0)
            {
                var mesh = new NoireLib.Draw3D.Geometry.Mesh(g.Vertices, g.Indices, keepCpuData: false, "worldcollision");
                worldCollision = new WorldCollisionCache { Mesh = mesh, Center = g.Center };
            }
            else
            {
                worldCollision = null;
            }

            old?.Mesh?.Dispose(); // the render path null-checks the vertex buffer
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: world-collision rebuild failed; decals fall back to the cylinder exclusion.", "[Draw3D] ");
        }
    }

    // Restores the overrides it forced on disposal.
    private static void RefreshUiHideOverrides()
    {
        if (!NoireService.IsInitialized())
            return;

        var wanted = !disposed;
        var uiBuilder = NoireService.PluginInterface.UiBuilder;
        ApplyOverride(ref forcedAutoHide, wanted, uiBuilder.DisableAutomaticUiHide, v => uiBuilder.DisableAutomaticUiHide = v);
        ApplyOverride(ref forcedUserHide, wanted, uiBuilder.DisableUserUiHide, v => uiBuilder.DisableUserUiHide = v);
        ApplyOverride(ref forcedCutsceneHide, wanted, uiBuilder.DisableCutsceneUiHide, v => uiBuilder.DisableCutsceneUiHide = v);
        ApplyOverride(ref forcedGposeHide, wanted, uiBuilder.DisableGposeUiHide, v => uiBuilder.DisableGposeUiHide = v);
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

    private static IReadOnlyList<Core.ConstantSnapshot>? markedConstants;

    private static IReadOnlySet<(nint Pointer, int Offset)>? volatileConstantRows;

    private static IReadOnlyList<byte[]>? writeLogBaseline;

    private static int writeLogBaselineSize;

    private static void HandleLightsCommand(string rest)
    {
        if (cameraCapture is not { Installed: true } capture)
        {
            Print("Draw3D: the constant capture is not installed (no device / hook failure) - see the log.");
            return;
        }

        if (!capture.IsLocked)
        {
            Print($"Draw3D: the constant capture has not locked yet. The frame buffers are not identified. State: {capture.Describe()}. Move the camera for a few seconds and retry.");
            return;
        }

        var mode = rest.Trim().ToLowerInvariant();

        // Payloads freeze once the capture locks. The window must span the whole comparison.
        const int ArmedFrames = 36000;

        if (mode == "off")
        {
            capture.ArmFullCapture(0);
            capture.ArmWriteLog(0);
            markedConstants = null;
            volatileConstantRows = null;
            writeLogBaseline = null;
            writeLogBaselineSize = 0;
            Print("Draw3D lights: constant capture disarmed.");
            return;
        }

        if (!capture.FullCaptureArmed)
        {
            capture.ArmFullCapture(ArmedFrames);

            if (mode is "mark" or "diff" or "baseline" || mode.StartsWith("writes", StringComparison.Ordinal) || mode.StartsWith("log", StringComparison.Ordinal))
            {
                Print("Draw3D lights: the capture had stopped storing whole buffers once it locked. It has just been armed. Give it a second, then run the same command again.");
                return;
            }
        }

        // The lighting is not in the camera's size class.
        if (mode == "mark")
        {
            markedConstants = capture.SnapshotConstants();
            volatileConstantRows = null;
            Print($"Draw3D: marked {markedConstants.Count} constant buffer(s). Wait a few seconds without changing anything and run /noire3d lights baseline to measure what moves on its own, then change the lighting and run diff or candidates.");
            return;
        }

        var current = capture.SnapshotConstants();

        if (mode == "baseline")
        {
            if (markedConstants is null)
            {
                Print("Draw3D lights: nothing marked yet - run /noire3d lights mark first.");
                return;
            }

            volatileConstantRows = Core.LightConstantProbe.VolatileRows(markedConstants, current);
            markedConstants = current;
            Print($"Draw3D lights: {volatileConstantRows.Count} row(s) change on their own and will be ignored from now on. Change the lighting, then run /noire3d lights candidates.");
            return;
        }
        var sb = new StringBuilder();

        if (mode == "diff")
        {
            if (markedConstants is null)
            {
                Print("Draw3D: nothing marked yet - run /noire3d lights mark first, change the lighting, then diff.");
                return;
            }

            sb.AppendLine("Draw3D lights diff: rows that moved between the mark and now. A light's direction or colour is in here; anything that did not move is not one.");
            var compared = 0;
            foreach (var after in current)
            {
                foreach (var before in markedConstants)
                {
                    if (before.Pointer != after.Pointer)
                        continue;

                    sb.AppendLine(Core.LightConstantProbe.DescribeChanges(before, after));
                    compared++;
                    break;
                }
            }

            if (compared == 0)
                sb.AppendLine("  (no buffer from the mark is still tracked - the game rotates its buffers; mark and diff closer together)");

            sb.AppendLine();
            sb.AppendLine("A buffer reported as NOT RE-CAPTURED holds the same bytes at both ends. Its rows say nothing either way.");
            sb.AppendLine("Rotation rows are filtered out: a view matrix is three perpendicular unit vectors, and without that test every one of them reads as a possible light direction.");
            sb.AppendLine("If nearly every buffer still changed, the camera moved between the mark and the diff - repeat it standing still. Only the light differs.");

            Print($"Draw3D lights: compared {compared} buffer(s) against the mark - details in the log.");
            NoireLogger.LogInfo(sb.ToString(), "[Draw3D] ");
            return;
        }

        // Several lights can share one buffer.
        if (mode.StartsWith("writes", StringComparison.Ordinal) || mode.StartsWith("log", StringComparison.Ordinal))
        {
            if (capture.WriteLogArmed)
            {
                Print($"Draw3D lights: still recording ({capture.WriteLogCount} write(s) so far) - run this again in a second.");
                return;
            }

            var parts = mode.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var argument = parts.Length > 1 ? parts[1] : string.Empty;

            if (argument is "base" or "diff")
            {
                if (capture.WriteLogCount == 0)
                {
                    Print($"Draw3D lights: nothing recorded to take a {argument} from. Run '/noire3d lights writes 512' first, wait a second, then run this.");
                    return;
                }

                var payloads = capture.WriteLogPayloads();
                var recordedSize = capture.WriteLogSize;

                if (argument == "base")
                {
                    writeLogBaseline = payloads;
                    writeLogBaselineSize = recordedSize;
                    capture.ArmWriteLog(0);
                    Print($"Draw3D lights: kept {payloads.Count} distinct payload(s) from the {recordedSize} B class as the baseline. "
                        + "Now change ONE light in the room - switching a lamp off reads far more clearly than dimming it - then record again "
                        + "with the same size and run '/noire3d lights writes diff'.");
                    return;
                }

                if (writeLogBaseline is null)
                {
                    Print("Draw3D lights: no baseline kept yet - record a run, take '/noire3d lights writes base', change a light, record again, then diff.");
                    return;
                }

                if (writeLogBaselineSize != recordedSize)
                {
                    Print($"Draw3D lights: the baseline covers {writeLogBaselineSize} B buffers and this run covers {recordedSize} B. They cannot be compared. Record again with '/noire3d lights writes {writeLogBaselineSize}'.");
                    return;
                }

                Print($"Draw3D lights: compared {payloads.Count} payload(s) against the baseline - details in the log.");
                NoireLogger.LogInfo(Core.ConstantWriteLog.DescribeDiff(writeLogBaseline, payloads), "[Draw3D] ");
                capture.ArmWriteLog(0);
                return;
            }

            if (argument == "list")
            {
                if (capture.WriteLogCount == 0)
                {
                    capture.ArmWriteLog(2, Core.GameLightHarvest.RecordBytes);
                    Print("Draw3D lights: recording for 2 frames. Run '/noire3d lights writes list' again to read the lights out of it.");
                    return;
                }

                var payloads = capture.WriteLogPayloads();
                var lights = Core.GameLightHarvest.FromPayloads(payloads);

                Print($"Draw3D lights: {lights.Count} light record(s) from {payloads.Count} payload(s) - details in the log.");
                NoireLogger.LogInfo(Core.GameLightHarvest.Describe(lights, payloads.Count), "[Draw3D] ");
                capture.ArmWriteLog(0);
                return;
            }

            if (capture.WriteLogCount == 0)
            {
                // Early per-object writes would exhaust the budget before the lighting pass.
                var width = int.TryParse(argument, out var parsedWidth) ? parsedWidth : 0;

                capture.ArmWriteLog(2, width);
                Print(width > 0
                    ? $"Draw3D lights: recording writes to {width} B buffers for 2 frames. Run '/noire3d lights writes' to read it, or '/noire3d lights writes base' to keep it for a comparison."
                    : $"Draw3D lights: recording every constant write for 2 frames. Sizes tracked: {string.Join(", ", capture.TrackedSizes())} B - pass one (for example '/noire3d lights writes 512') if this truncates. Run the command again to read it.");
                return;
            }

            Print(capture.WriteLogTruncated
                ? $"Draw3D lights: {capture.WriteLogCount} write(s) recorded but the cap was hit. The END of the frame is missing - and that is where lighting runs. Re-run restricted to one size, e.g. /noire3d lights writes 512."
                : $"Draw3D lights: {capture.WriteLogCount} recorded write(s) - details in the log. A buffer rewritten many times with different contents is a per-item list.");

            NoireLogger.LogInfo(capture.DescribeWriteLog(), "[Draw3D] ");
            capture.ArmWriteLog(0);
            return;
        }

        if (mode == "candidates")
        {
            Print(markedConstants is null
                ? $"Draw3D lights: ranked {current.Count} buffer(s) by shape alone - details in the log. Mark, baseline, change the lighting, then run this again for a ranking with evidence behind it."
                : volatileConstantRows is null
                    ? $"Draw3D lights: ranked {current.Count} buffer(s) against the mark, with NO control - rows that change every frame are still in the list. Run baseline next time."
                    : $"Draw3D lights: ranked {current.Count} buffer(s) against the mark, ignoring {volatileConstantRows.Count} self-changing row(s) - details in the log.");

            NoireLogger.LogInfo(Core.LightConstantProbe.DescribeCandidates(current, markedConstants, volatileConstantRows), "[Draw3D] ");
            return;
        }

        sb.AppendLine("Draw3D lights: rows of a light-like shape in the game's constant buffers, with rotation rows filtered out.");
        sb.AppendLine("A unit vector may be a light direction; a value in 0..1 may be a colour. Neither is proof - use mark/diff across a lighting change to narrow it.");

        var sizes = new SortedDictionary<int, int>();
        foreach (var snapshot in current)
        {
            sizes.TryGetValue(snapshot.ByteWidth, out var n);
            sizes[snapshot.ByteWidth] = n + 1;
        }

        var sizeList = new List<string>(sizes.Count);
        foreach (var pair in sizes)
            sizeList.Add($"{pair.Key} B x{pair.Value}");

        sb.AppendLine($"Size classes tracked: {string.Join(", ", sizeList)}.");
        sb.AppendLine();

        foreach (var snapshot in current)
            sb.AppendLine(Core.LightConstantProbe.Describe(snapshot));

        Print($"Draw3D lights: dumped {current.Count} constant buffer(s) to the log. Run /noire3d lights mark, change the lighting, then /noire3d lights diff to find which rows follow it.");
        NoireLogger.LogInfo(sb.ToString(), "[Draw3D] ");
    }

    private static void HandleFrameDumpCommand(string rest)
    {
        if (EnsureRenderTargetTap() is not { } dumpTap)
        {
            Print("Draw3D: the render-target tap could not be installed (see the log).");
            return;
        }

        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NoireLib_FrameDump");

        // Bind indices shift with what is on screen.
        if (parts.Length == 0 || parts[0].Equals("sweep", StringComparison.OrdinalIgnoreCase))
        {
            var sweepCount = parts.Length > 1 && int.TryParse(parts[1], out var wanted) ? wanted : 12;
            var stride = dumpTap.ArmFrameSweep(sweepCount, folder);
            Print(stride > 0
                ? $"Draw3D: sweeping the whole frame on the next one - every {stride}th bind, images in {folder}. Find the first image where the object is already wrong, then narrow with /noire3d framedump <from> <count>."
                : "Draw3D: the render-target tap could not be installed (see the log).");
            return;
        }

        if (!int.TryParse(parts[0], out var from))
        {
            Print("Draw3D: /noire3d framedump [sweep [count] | <from> [count]] - sweep covers the whole frame and is the one to start with. Each dump stalls the frame.");
            return;
        }

        var count = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? parsed : 4;
        dumpTap.ArmFrameDump(from, count, folder);
        Print($"Draw3D: dumping binds {from}..{from + count - 1} on the next frame - images in {folder}. Indices only mean anything within one frame. Read them off the bind table this same run prints.");
    }

    private static void RegisterCommand()
    {
        // Commands are global across every plugin linking NoireLib.
        commandRegistered = NoireService.CommandManager.AddHandler(CommandName, new CommandInfo(HandleCommand)
        {
            HelpMessage = "Draw3D diagnostics: validate | probe | cbprobe [frames] | calibrate [compare] | temporal | lights [mark|baseline|diff|candidates|writes [size|base|diff|list]|off] | gpucam | batchcb | stats | wire | decalshapes | decalvolumes | stencil | heightmap | topsurface | reset | rtlog | depthwrites | water | framedump [sweep [count]|<from> [count]] | gbuffer | ontop | platedepth | uimask | plates",
        });

        if (!commandRegistered)
            NoireLogger.LogDebug($"'{CommandName}' already registered by another plugin - use NoireDraw3D.Diagnostics instead.", "[Draw3D] ");
    }

    private static void HandleCommand(string command, string args)
    {
        // A path argument keeps its case.
        var trimmed = args.Trim();
        var sp = trimmed.IndexOf(' ');
        var verb = (sp < 0 ? trimmed : trimmed[..sp]).ToLowerInvariant();
        var rest = sp < 0 ? string.Empty : trimmed[(sp + 1)..].Trim();
        switch (verb)
        {
            case "validate":
                Diagnostics.RunValidate();
                Print("Draw3D: projection parity validator armed for the next 10 frames - results go to the log.");
                break;
            case "probe":
                Diagnostics.RunProbe();
                Print("Draw3D: depth probe armed for the next frame - results go to the log.");
                break;
            case "cbprobe":
                if (cameraCapture is { Installed: true } probeCapture)
                {
                    var probeFrames = int.TryParse(rest, out var parsedProbe) && parsedProbe > 0 ? parsedProbe : 120;
                    probeCapture.ArmProbe(probeFrames);
                    Print($"Draw3D: camera-constant discovery probe armed for {probeFrames} world frames - keep playing; the observation table goes to the log.");
                }
                else
                {
                    Print("Draw3D: the camera-constant capture is not installed (no device / hook failure) - see the log.");
                }

                break;
            case "lights":
                HandleLightsCommand(rest);
                break;
            case "calibrate":
                if (rest.Equals("compare", StringComparison.OrdinalIgnoreCase))
                {
                    Diagnostics.CompareDepthCalibration();
                    Print("Draw3D: pixel-convention calibration compared - results go to the log.");
                }
                else
                {
                    Diagnostics.SnapshotDepthCalibration();
                    Print("Draw3D: pixel-convention calibration snapshot taken - turn the camera, then /noire3d calibrate compare.");
                }

                break;
            case "temporal":
                Diagnostics.TemporalStabilization = !Diagnostics.TemporalStabilization;
                Print($"Draw3D: temporal stabilization {(Diagnostics.TemporalStabilization ? "on" : "off")}.");
                break;
            case "gpucam":
                Diagnostics.PreferCapturedCamera = !Diagnostics.PreferCapturedCamera;
                Print(Diagnostics.PreferCapturedCamera
                    ? $"Draw3D: GPU camera capture ON - the layer projects with the exact uploaded camera constants when available. State: {cameraCapture?.Describe() ?? "not installed"}."
                    : "Draw3D: GPU camera capture OFF (A/B) - the layer projects with the control's view-projection, which can lag the drawn camera by a frame.");
                break;
            case "batchcb":
                Performance.BatchedObjectConstants = !Performance.BatchedObjectConstants;
                Print(Performance.BatchedObjectConstants
                    ? "Draw3D: batched object constants ON (A/B) - standard single draws ride the instanced route and the object CB re-uploads only on material-param changes. Compare fps and 'objectCb updates' in /noire3d stats against OFF in the same scene."
                    : "Draw3D: batched object constants OFF - every single draw uploads the object CB (the classic path).");
                break;
            case "stencil":
                stencilDebug = !stencilDebug;
                Print(stencilDebug
                    ? "Draw3D: stencil debug ON - aim the camera at your character, a piece of furniture, terrain, etc.; the game stencil values in view are logged (~2/s). Tell me which value sits on each thing and I'll key decal exclusion off it."
                    : "Draw3D: stencil debug off.");
                break;
            case "wire":
                Print($"Draw3D: wireframe {(Diagnostics.ToggleWireframe() ? "on" : "off")}.");
                break;
            case "decalshapes":
                Diagnostics.DecalShapeOutlines = !Diagnostics.DecalShapeOutlines;
                Print($"Draw3D: decal shape outlines {(Diagnostics.DecalShapeOutlines ? "on" : "off")} - every decal traces what it paints, immediate-layer shapes included.");
                break;
            case "decalvolumes":
                Diagnostics.DecalVolumeOutlines = !Diagnostics.DecalVolumeOutlines;
                Print($"Draw3D: decal volume boxes {(Diagnostics.DecalVolumeOutlines ? "on" : "off")} - every decal draws the projection box its SDF is evaluated in, immediate-layer shapes included.");
                break;
            case "heightmap":
                CollisionHeightMap = !CollisionHeightMap;
                Print(CollisionHeightMap
                    ? "Draw3D: collision height-map ON - DecalProjection.HighestOnly decals paint only the topmost surface per column. Nothing else reads it. With no HighestOnly decal on screen there is nothing to see."
                    : "Draw3D: collision height-map off - HighestOnly decals paint every surface in their box (they degrade to AllSurfaces). Character cut-outs are unaffected: those are ExcludeObjects + the game stencil.");
                break;
            case "topsurface":
                PrintTopSurfaceReport();
                break;
            case "reset":
                renderStats?.ResetCounters();
                Enabled = true;
                Print("Draw3D: counters reset, renderer re-armed.");
                break;
            case "gbuffer":
                // The game clears the G-buffer at the next frame's first pass.
                if (EnsureRenderTargetTap() is not { } gbufTap)
                {
                    Print("Draw3D: the render-target tap could not be installed (see the log).");
                    break;
                }

                if (renderDevice is null)
                {
                    Print("Draw3D: no render device.");
                    break;
                }

                var gbufTargets = gbufTap.GBufferTargets();
                if (gbufTargets.Count == 0)
                {
                    Print("Draw3D: no G-buffer identified yet - run /noire3d rtlog first, then this.");
                    break;
                }

                var gbufFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NoireLib_GBuffer");
                Print($"Draw3D: reading back {gbufTargets.Count} G-buffer target(s) - images in {gbufFolder}, details in the log.");
                NoireLogger.LogInfo(Core.GBufferProbe.Describe(renderDevice, gbufTargets, gbufFolder), "[Draw3D] ");
                break;
            case "rtlog":
                if (EnsureRenderTargetTap() is { } tap)
                {
                    tap.ArmCapture();
                    Print("Draw3D: capturing the next frame's render-target bind sequence.");
                }
                else
                {
                    Print("Draw3D: the render-target tap could not be installed (see the log).");
                }

                break;
            case "depthwrites":
                if (EnsureRenderTargetTap() is { } censusTap)
                {
                    censusTap.ArmDepthCensus();
                    Print("Draw3D: capturing the next frame's bind sequence and every bind that changes the scene depth. Report in the log. Expect one stalled frame.");
                }
                else
                {
                    Print("Draw3D: the render-target tap could not be installed (see the log).");
                }

                break;
            case "water":
                TranslucentOcclusion = TranslucentOcclusion == TranslucentOcclusion.SeeThrough ? TranslucentOcclusion.Occlude : TranslucentOcclusion.SeeThrough;
                Print(TranslucentOcclusion == TranslucentOcclusion.SeeThrough
                    ? "Draw3D: translucent surfaces = see through - content occludes against the depth the opaque pass left, so it shows under water."
                    : "Draw3D: translucent surfaces = occlude (A/B) - water and later surfaces hide content like solid geometry.");
                break;
            case "framedump":
                HandleFrameDumpCommand(rest);
                break;
            case "shadowprobe":
                if (EnsureRenderTargetTap() is { } shadowTap)
                {
                    shadowTap.ArmShadowProbe();
                    Print("Draw3D: probing the next frame's shadow passes - every depth-only bind and the VS constants at its first draw. Report in the log. Stand where something visibly casts a shadow, and expect one stalled frame.");
                }
                else
                {
                    Print("Draw3D: the render-target tap could not be installed (see the log).");
                }

                break;
            case "ontop":
                NativeUi.Layering = NativeUi.Layering == Draw3DLayering.UnderGameUi ? Draw3DLayering.OverEverything : Draw3DLayering.UnderGameUi;
                Print(NativeUi.Layering == Draw3DLayering.UnderGameUi
                    ? "Draw3D: layering = under the game UI - the layer injects before the game draws its UI. HUD, addons and nameplates read on top of it."
                    : "Draw3D: layering = over everything - the layer composites at present time and covers the game UI. Nameplate occlusion does nothing in this mode.");
                break;
            case "platedepth":
                NativeUi.Nameplates = NativeUi.Nameplates == NameplateOcclusion.DepthAware ? NameplateOcclusion.AlwaysVisible : NameplateOcclusion.DepthAware;
                Print(NativeUi.Nameplates == NameplateOcclusion.DepthAware
                    ? "Draw3D: nameplates = depth-aware - plates standing behind your 3D objects get covered by them."
                    : "Draw3D: nameplates = always visible - plates read on top of the layer at any distance.");
                break;
            case "uimask":
                Print(UiMaskReport());
                break;
            case "plates":
                Print(PlateReport());
                break;
            default:
                Print(Diagnostics.GetStatsText());
                break;
        }
    }

    private static void Print(string message)
    {
        NoireService.ChatGui.Print(message);
        NoireLogger.LogInfo(message, "[Draw3D] ");
    }

    private static RenderTargetTap? EnsureRenderTargetTap()
    {
        if (renderTargetTap != null)
            return renderTargetTap;

        try
        {
            var device = RequireDevice();
            var tap = new RenderTargetTap();
            if (tap.Install(device))
            {
                renderTargetTap = tap;

                var capture = new CameraConstantCapture();
                if (capture.Install(device, tap))
                {
                    cameraCapture = capture;
                    tap.Capture = capture;
                }
                else
                {
                    capture.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: could not initialize the render-target tap.", "[Draw3D] ");
        }

        return renderTargetTap;
    }

    private static Draw3DStats BuildStats()
    {
        var s = renderStats;
        if (s == null)
        {
            return new Draw3DStats
            {
                FramesRendered = 0, FramesSkippedDisabled = 0, FramesSkippedInitPending = 0, FramesSkippedNoDevice = 0,
                FramesSkippedNoCamera = 0, FramesSkippedZeroSize = 0, FramesSkippedEmpty = 0, FramesSkippedUiHidden = 0, DepthOffFrames = 0,
                OpaqueDepthFrames = 0, OpaqueDepthMissedFrames = 0, OpaqueDepthSource = "off",
                DisposedAssetDraws = 0, ImCommandsDropped = 0, DrawCalls = 0, Instances = 0, Triangles = 0, Batches = 0,
                ObjectCbUpdates = 0, CulledItems = 0, VisibleItems = 0, ProtectRects = 0, DepthAvailable = false, UsedFallbackCamera = false,
                DepthSource = "none", SceneGpuMs = 0, CompositeGpuMs = 0,
                CameraCapture = "not installed", GpuCameraFrames = 0, ControlCameraFrames = 0, CameraRejectedAtUse = 0, UsedGpuCamera = false,
                LastPickMicros = 0, LastPickNodes = 0, LastPickRefined = 0,
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
            FramesSkippedUiHidden = s.FramesSkippedUiHidden,
            DepthOffFrames = s.DepthOffFrames,
            OpaqueDepthFrames = s.OpaqueDepthFrames,
            OpaqueDepthMissedFrames = s.OpaqueDepthMissedFrames,
            OpaqueDepthSource = DescribeOpaqueDepth(),
            DisposedAssetDraws = s.DisposedAssetDraws,
            ImCommandsDropped = s.ImCommandsDropped,
            DrawCalls = s.DrawCalls,
            Instances = s.Instances,
            Triangles = s.Triangles,
            Batches = s.Batches,
            ObjectCbUpdates = s.ObjectCbUpdates,
            CulledItems = s.CulledItems,
            VisibleItems = s.VisibleItems,
            ProtectRects = s.ProtectRects,
            DepthAvailable = s.DepthAvailable,
            UsedFallbackCamera = s.UsedFallbackCamera,
            DepthSource = DescribeDepthSource(s.DepthAvailable),
            SceneGpuMs = s.SceneGpuMs,
            CompositeGpuMs = s.CompositeGpuMs,
            CameraCapture = cameraCapture?.Describe() ?? "not installed",
            GpuCameraFrames = s.GpuCameraFrames,
            ControlCameraFrames = s.ControlCameraFrames,
            CameraRejectedAtUse = s.CameraRejectedAtUse,
            UsedGpuCamera = s.UsedGpuCamera,
            LastPickMicros = s.PickMicros,
            LastPickNodes = s.PickNodes,
            LastPickRefined = s.PickRefined,
        };
    }

    internal static string DescribeCameraCapture() => cameraCapture?.Describe() ?? "not installed";

    internal static string UiMaskReport()
    {
        if (layering == Draw3DLayering.UnderGameUi)
            return "Draw3D UI mask: not used - under the game UI the game draws its own UI over the layer, letter-exact and for free. "
                   + "The mask only exists for the over-everything path ('/noire3d ontop' switches).";

        if (!keepUiOnTop)
            return "Draw3D UI mask: off (NativeUi.KeepUiOnTop = false) - the layer covers the game UI.";

        var tapState = renderTargetTap == null ? "not installed"
            : renderTargetTap.PresentBuffer == 0 ? "installed, present buffer not learned yet"
            : $"installed, present buffer 0x{renderTargetTap.PresentBuffer:X}";

        var samples = uiDiffMaskHealth?.LastSamples is { } s
            ? "\n  grid difference: " + string.Join(" ", Array.ConvertAll(s, d => d.ToString("F2")))
            : "\n  grid difference: not sampled yet (give it ~2 seconds)";

        return $"Draw3D UI mask (over everything, keep UI on top):\n  render-thread hook: {tapState}\n  health: {UiMaskDescription}{samples}"
               + "\n  The mask is the difference between the present buffer before and after the game drew its UI. A"
               + "\n  non-zero sample means the UI covers that grid point. All zeroes with the HUD on screen means the"
               + "\n  pre-UI snapshot is not landing where the UI is drawn; all non-zero means the snapshots are not"
               + "\n  comparable and the mask disables itself.";
    }

    internal static string PlateReport()
    {
        if (layering != Draw3DLayering.OverEverything)
            return "Draw3D plates: nameplate policy rects are an over-everything mechanism; under the game UI the plate pass "
                   + "tests the depth Draw3D stamps instead. There is nothing to report. '/noire3d ontop' switches.";

        if (!keepUiOnTop)
            return "Draw3D plates: no rects collected - NativeUi.KeepUiOnTop is off. There is no UI mask for them to gate "
                   + "and the layer covers every plate.";

        if (nameplateOcclusion == NameplateOcclusion.AlwaysVisible)
            return "Draw3D plates: no rects collected - Nameplates = AlwaysVisible needs no policy (the mask protects every plate).";

        if (lastPlateCount == 0)
            return "Draw3D plates: 0 nameplates collected last frame. With plates on screen this means the collection itself is "
                   + "failing (the NamePlate addon or UI3DModule read). Every plate falls back to reading on top.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Draw3D plates: {lastPlateCount} collected, mode {nameplateOcclusion}, dim {nameplateDimFactor:F2}. "
                      + "factor 1 = plate reads on top, dim = layer covers it.");
        sb.AppendLine($"  camera at {lastFrame.EyePos.X:F1},{lastFrame.EyePos.Y:F1},{lastFrame.EyePos.Z:F1}");
        for (var i = 0; i < lastPlateCount && i < 12; i++)
        {
            var r = ProtectRects[i];
            var by = PlateCoveredBy[i] > 0f ? $"covered by content whose far side is {PlateCoveredBy[i]:F1}m out" : "not covered";
            sb.AppendLine($"  [{i}] factor {ProtectFactors[i]:F2} | plate {PlateDistances[i]:F1}m (rooted from the game's squared {PlateRawDistance[i]:F1}) | {by} | rect uv ({r.X:F3},{r.Y:F3})-({r.Z:F3},{r.W:F3})");
        }

        sb.Append("A plate is covered when its distance is the larger of the two. If the plate distance does not match how "
                  + "far that character actually looks, the comparison is being fed a bad number and the mode cannot work.");
        return sb.ToString();
    }

    private static string DescribeOpaqueDepth()
        => $"{renderTargetTap?.OpaqueDepthDescription ?? "off"}; copy {opaqueDepth?.Description ?? "none"}; default {translucentOcclusion}";

    private static string DescribeDepthSource(bool hasDepth)
        => $"{sceneDepth?.Description ?? "none"}; map: {DepthMapDescription(in lastDepthMap, hasDepth)}; uiMask: {UiMaskDescription}";

    internal static string DepthMapDescription(in Vector4 map, bool hasDepth)
        => !hasDepth
            ? "depth-off"
            : $"z={map.X:E2}{(map.Y >= 0 ? "+" : "")}{map.Y:F5}/w ({(map.Y > 0 ? "reversed" : "standard")}-Z, analytic)";

    internal static FrameContext LastFrame => lastFrame;

    internal static bool LastFrameValid => lastFrameValid;

    // Under the UI it fires inside a game D3D call. Subscribers may emit geometry and read their own state only.
    internal static event Action<FrameContext>? OnRenderOverlay;

    internal static GameRenderSources.CameraData LastCameraData => lastCameraData;

    private static void PickNode(SceneNode node, Vector3 origin, Vector3 direction, Vector3? groundSurface, List<PickHit> hits, ref int nodes, ref int refined)
    {
        if (!node.Visible || node.Destroyed)
            return;

        nodes++;
        var renderer = node.Renderer;
        if (renderer != null && !renderer.Mesh.IsDisposed)
        {
            if (renderer.Material.Domain == MaterialDomain.GroundDecal)
            {
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
                        refined++;
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
            PickNode(child, origin, direction, groundSurface, hits, ref nodes, ref refined);
    }

    private static bool TryPickDecal(SceneNode node, MeshRenderer renderer, Vector3 origin, Vector3 direction, Vector3? groundSurface, out float t)
    {
        t = 0f;

        var world = node.ResolveWorld();
        if (!Matrix4x4.Invert(world, out var invWorld))
            return false;

        // The decal's own plane is exact on flat ground. The game's raycast catches decals on terrain.
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

    private static bool TryDecalFootprint(Material material, in Matrix4x4 invWorld, Vector3 origin, Vector3 direction, Vector3 worldHit, out float t)
    {
        t = 0f;

        var lp = Vector3.Transform(worldHit, invWorld);
        // Y spans the whole volume, tolerating a collision surface off the rendered one.
        if (MathF.Abs(lp.X) > 0.5f || MathF.Abs(lp.Z) > 0.5f || MathF.Abs(lp.Y) > 0.5f)
            return false;
        if (!InsideDecalShape(material, lp))
            return false;

        var dd = Vector3.Dot(direction, direction);
        t = dd > 1e-12f ? Vector3.Dot(worldHit - origin, direction) / dd : 0f;
        return t >= 0f;
    }

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

    // GroundDecal.hlsl's footprint SDF, edge at |p| = 1.
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
            case DecalShape.Chevron:
                {
                    Span<Vector2> corners = stackalloc Vector2[6];
                    Geometry.DecalOutline.ChevronOutline(sp.X, corners); // x = half stroke ratio
                    sd = InsidePolygon(p, corners) ? -1f : 1f;
                    break;
                }
            default:                                             // Rect and Texture
                sd = MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y)) - 1f;
                break;
        }

        return sd <= 0f;
    }

    private static bool InsidePolygon(Vector2 point, ReadOnlySpan<Vector2> corners)
    {
        var inside = false;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i, i++)
        {
            if (corners[i].Y > point.Y != corners[j].Y > point.Y
                && point.X < (corners[j].X - corners[i].X) * (point.Y - corners[i].Y) / (corners[j].Y - corners[i].Y) + corners[i].X)
                inside = !inside;
        }

        return inside;
    }

    private static bool RaySphere(Vector3 origin, Vector3 direction, in Geometry.BoundingSphere sphere, out float t)
        => Geometry3DHelper.RaySphere(origin, direction, sphere.Center, sphere.Radius, out t);

    private static bool RayMesh(Vector3 origin, Vector3 direction, Geometry.Mesh mesh, in Matrix4x4 world, out float bestT, out int bestTriangle)
    {
        bestT = float.MaxValue;
        bestTriangle = -1;

        if (!Matrix4x4.Invert(world, out var invWorld))
            return false;

        var localOrigin = Vector3.Transform(origin, invWorld);
        var localDir = Vector3.TransformNormal(direction, invWorld);
        var dirScale = localDir.Length();
        if (dirScale < 1e-12f)
            return false;
        localDir /= dirScale;

        if (!mesh.RayCastLocal(localOrigin, localDir, out var localT, out bestTriangle))
            return false;

        var hitWorld = Vector3.Transform(localOrigin + localDir * localT, world);
        bestT = Vector3.Distance(origin, hitWorld);
        return true;
    }
}
