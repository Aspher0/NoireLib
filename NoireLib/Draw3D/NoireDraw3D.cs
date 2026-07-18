using Dalamud.Game.ClientState.Conditions;
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
    private static RenderTarget? worldHeightRt; // top-down collision height-map (R32F); read only by HighestOnly decals
    private const uint WorldHeightResolution = 1024; // height-map texels per side (over the ~80m cache region)
    private static SceneDepth? sceneDepth;
    private static SceneStencil? sceneStencil; // the game depth-stencil's STENCIL plane (marks characters) for silhouette-exact decal exclusion

    // A cached mesh of the real collision world near the player, rebuilt on the framework thread when they leave the
    // cached region. Rendered top-down into worldHeightRt so a DecalProjection.HighestOnly decal can find its column's
    // topmost surface - see GroundDecal.hlsl. Also what Scene3D.SpawnWorldGeometry hands out. Swapped atomically.
    private sealed class WorldCollisionCache { public NoireLib.Draw3D.Geometry.Mesh Mesh = null!; public Vector3 Center; }
    private static volatile WorldCollisionCache? worldCollision;
    private static Vector3 worldCollisionBuiltAt;
    private static bool worldCollisionEverBuilt;
    private static volatile bool lastFrameNeededHeightMap; // gates the framework-thread collision rebuild to frames with a HighestOnly decal
    private const float WorldCollisionRadius = 40f;         // half-size of the collected region
    private const float WorldCollisionRebuildDistance = 8f; // rebuild once the player is this far from the region centre
    private const int WorldCollisionMaxTriangles = 60000;
    private static RenderStats? renderStats;
    private static RenderTargetTap? renderTargetTap;
    private static CameraConstantCapture? cameraCapture; // exact GPU camera constants (see TryGetInjectCamera)
    private static DepthProbe? depthProbe; // cached, non-blocking depth readback for the obstacle-occlusion hover test
    private static bool stencilDebug;      // /noire3d stencil: log the game stencil values in view (discover the object-category convention)
    private static DateTime lastStencilLog = DateTime.MinValue;

    private static ComPtr<ID3D11RenderTargetView> backbufferRtv;
    private static nint backbufferPtr;

    // Pre-UI injection: an RTV over the game's present-composition buffer, and the "did we inject this
    // frame" flag that tells the present-time path to skip its own swapchain composite (fallback otherwise).
    private static ComPtr<ID3D11RenderTargetView> presentRtv;
    private static nint presentRtvPtr;
    private static uint presentRtvWidth, presentRtvHeight;
    private static Draw3DLayering layering = Draw3DLayering.UnderGameUi; // the layer reads under the game HUD/nameplates
    private static bool injectionInitialized;       // one-shot: arm the tap on the first frame that has a device
    private static volatile bool injectedSinceLastPresent;

    // Nameplate occlusion: a writable DSV over the game's scene depth, stamped before the game's plate pass so plates
    // behind 3D objects are hidden by the game's own depth test (needs the under-UI injection; waives Law 5).
    private static GameDepthTarget? gameDepthTarget;
    private static NameplateOcclusion nameplateOcclusion = NameplateOcclusion.DepthAware;

    // Over-everything UI masking: the present buffer snapshotted either side of the game's UI pass, differenced at
    // composite time to recover the UI's own pixels. The render-thread hook is armed for the "before" snapshot even
    // when the layer itself is not injected.
    private static UiDiffMask? uiDiffMask;
    private static UiDiffMaskHealth? uiDiffMaskHealth;
    private static bool keepUiOnTop = true;
    private static float nameplateDimFactor;

    private static Scene3D? mainScene;
    private static ImDraw3D? im;
    private static readonly List<Scene3D> Scenes = new();
    private static readonly List<RenderView> Views = new();

    // Reusable snapshots of the two registries above, for the frame body. It must iterate them outside their lock,
    // because the user code it invokes is free to create or drop a scene or a view mid-loop, but a fresh snapshot
    // array every frame is precisely the steady-state garbage Law 9 forbids. Refilled under the lock at the top of
    // each frame and read only from the render thread, which is the sole caller and never re-enters itself.
    private static readonly List<Scene3D> SceneScratch = new();
    private static readonly List<RenderView> ViewScratch = new();
    private static readonly Vector4[] ProtectRects = new Vector4[128];
    private static readonly float[] ProtectFactors = new float[128];
    private static readonly float[] PlateDistances = new float[128];
    private static readonly float[] PlateCoveredBy = new float[128];  // diagnostics: far distance of the item that covered each plate
    private static readonly float[] PlateRawDistance = new float[128]; // diagnostics: the game's own (squared) plate distance field
    private static int lastPlateCount; // last frame's nameplate rect count (the '/noire3d plates' report)

    private static long frameId;
    private static FrameContext lastFrame;
    private static bool lastFrameValid;
    private static GameRenderSources.CameraData lastCameraData;
    private static Vector4 lastDepthMap; // last frame's analytic depth map, kept raw so DescribeDepthSource can format it on demand

    // A short ring of the camera each of the most recent presented frames projected its overlay with, newest reachable
    // via TryGetCameraHistory(0). Entirely inert: nothing in the render path reads it. It exists only for the
    // camera-phase trace (/noire3d camtrace), which sweeps it to find the frame-lag between the CPU camera snapshot the
    // overlay uses and the GPU-rasterized pixels already in the present buffer - the residual "swim" source.
    private const int CameraHistoryLength = 8;
    private static readonly GameRenderSources.CameraData[] cameraHistory = new GameRenderSources.CameraData[CameraHistoryLength];
    private static int cameraHistoryCursor; // index the NEXT push lands at; newest entry is (cursor - 1)
    private static int cameraHistoryCount;

    // The present-time path projects with a camera sampled on the sim thread each Framework.Update, which matches
    // the presented backbuffer more closely than a live read at present time.
    private static GameRenderSources.CameraData frameworkCamera;
    private static volatile bool frameworkCameraValid;
    private static bool frameworkHooked;

    // The injected layer must be projected with the same camera the game rasterized the world in the present buffer
    // with, or it drifts relative to world geometry under camera motion. Two sources provide that camera, best first:
    // the camera-constant capture (CameraConstantCapture - the exact uploaded GPU bytes, committed at the main scene
    // pass; immune to the sim-vs-render skew that made every struct-timed read swim under load), then the render-thread
    // struct snapshot taken at the main pass (RenderTargetTap.TryGetWorldCamera). See TryGetInjectCamera.

    private static bool keepDrawingWhenUiHidden = true;
    private static bool forcedAutoHide, forcedUserHide, forcedCutsceneHide, forcedGposeHide;

    private static int passFailStreak;
    private static bool passFaultLogged;
    internal static bool Wireframe;

    /// <summary>
    /// Traces every decal's painted shape as an outline, on top of normal rendering: the retained ones and the immediate
    /// layer's grounded shapes alike. The global answer to "where are my decals actually landing", where
    /// <see cref="Scene.SceneNode.ShowDecalShape"/> is the per-node one - an immediate-mode shape has no node to opt in
    /// with, so it can only be reached from here. Implied by <see cref="Wireframe"/>.
    /// </summary>
    internal static bool DecalShapeOutlines;

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

    /// <summary>
    /// Renders the cached collision world top-down into a height-map (the highest collision Y per XZ column) on frames
    /// that have ground decals. Default true.
    /// <br/>
    /// <b>Its only consumer is <see cref="DecalProjection.HighestOnly"/></b>, which needs that height-map to tell a
    /// tabletop from the floor beneath it. Turning this off makes a <c>HighestOnly</c> decal degrade to
    /// <see cref="DecalProjection.AllSurfaces"/> - it paints the floor under the table again. Every other decal is
    /// unaffected, so with no <c>HighestOnly</c> decal on screen this switch changes nothing visible and only saves the
    /// height-map pass.
    /// <br/>
    /// It has <b>nothing to do with cutting characters out of decals</b>: that is
    /// <see cref="Scene.SceneNode.ExcludeObjects(System.Func{Dalamud.Game.ClientState.Objects.Types.IGameObject, bool}, float)"/>
    /// plus <see cref="CharacterStencilValue"/>, which cut along the game's stencil silhouette and work regardless of this.
    /// </summary>
    public static bool CollisionHeightMap { get; set; } = true;

    /// <summary>
    /// The game depth-stencil value that marks characters, used to occlude ground decals along an excluded character's
    /// exact silhouette (see <see cref="Scene.SceneNode.ExcludeObjects(System.Func{Dalamud.Game.ClientState.Objects.Types.IGameObject, bool}, float)"/>).
    /// Discovered via <c>/noire3d stencil</c>; default <c>0x08</c>. Set to 0 to disable stencil exclusion (decals paint over characters).
    /// </summary>
    public static uint CharacterStencilValue { get; set; } = 0x08;

    /// <summary>
    /// The elevation band (world units) used by <see cref="DecalProjection.HighestOnly"/>: a surface more than this far
    /// below its column's highest collision surface is skipped. Larger tolerates coarser collision; smaller is tighter but
    /// can nibble genuine ground where collision sits slightly off the visual floor. Default 0.1 m.
    /// <br/>
    /// Like <see cref="CollisionHeightMap"/>, this is <b>only</b> read by <c>HighestOnly</c> decals - it does nothing to
    /// any other decal. <b>0 disables <c>HighestOnly</c> entirely</b> (it is the same value the shader tests the feature's
    /// availability with), so keep it above zero to keep that projection working.
    /// </summary>
    public static float TopSurfaceThreshold { get; set; } = 0.1f;

    /// <summary>Applies the layering state (used by <see cref="NativeUi"/>.<c>Layering</c>).</summary>
    internal static void SetLayering(Draw3DLayering value)
    {
        layering = value;
        ApplyInjectionState();
    }

    /// <summary>Applies the keep-UI-on-top state (used by <see cref="NativeUi"/>.<c>KeepUiOnTop</c>).</summary>
    internal static void SetKeepUiOnTop(bool value)
    {
        keepUiOnTop = value;
        ApplyInjectionState(); // over everything, the mask's pre-UI snapshot rides the same render-thread hook
    }

    /// <summary>
    /// Whether the render-thread hook needs to fire this frame's injection point at all: to composite the layer
    /// under the native UI, or - over everything - to take the pre-UI snapshot the UI mask differences against.
    /// </summary>
    private static bool NeedsInjectionPoint => layering == Draw3DLayering.UnderGameUi || keepUiOnTop;

    /// <summary>
    /// Arms or disarms the render-thread hook to match <see cref="NeedsInjectionPoint"/>. When the device isn't ready
    /// yet (very first frames) the tap can't install; the desired state is kept and the frame loop retries once a
    /// device exists, so the default under-UI state comes up on its own.
    /// </summary>
    private static void ApplyInjectionState()
    {
        if (NeedsInjectionPoint)
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

    /// <summary>
    /// Keep the 3D layer rendering when the game's UI is hidden (cutscenes, GPose, user UI-hide).
    /// <b>Default true</b>: a world overlay should survive the UI-hide toggle - otherwise the whole scene
    /// vanishes when the player hides the HUD.<br/>
    /// This affects <b>only the 3D layer</b>. It does not decide anything about the host plugin's own windows: NoireDraw3D
    /// holds Dalamud's UI-hide overrides for the layer's lifetime either way (they are the only way to keep
    /// <c>UiBuilder.Draw</c> firing, which the layer renders inside), and makes the drawing choice itself.<br/>
    /// <b>Consequence for the host:</b> because those overrides are held, Dalamud will not auto-hide your windows.
    /// Deciding that is yours - gate them on <see cref="IsGameUiHidden"/>, which a Dalamud <c>Window</c> does in one line:
    /// <code>public override bool DrawConditions() =&gt; !NoireDraw3D.IsGameUiHidden;</code>
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
    /// Whether the game's UI is hidden right now, for any of the reasons <see cref="KeepDrawingWhenUiHidden"/> overrides:
    /// the player's UI-hide toggle, a cutscene, or GPose.
    /// <br/>
    /// This reads the <b>game's</b> state, so it stays truthful while <see cref="KeepDrawingWhenUiHidden"/> is on -
    /// Dalamud's hide overrides only suppress its own per-plugin hiding, they do not mask the underlying state. That is
    /// what lets the two be separated: keep the 3D rendering, and still hide your own windows by returning
    /// <c>!NoireDraw3D.IsGameUiHidden</c> from a window's <c>DrawConditions()</c>.
    /// </summary>
    public static bool IsGameUiHidden
        => NoireService.GameGui.GameUiHidden
           || NoireService.ClientState.IsGPosing
           || NoireService.Condition.Any(ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78, ConditionFlag.OccupiedInCutSceneEvent);

    /// <summary>
    /// Registers the <c>/noire3d</c> diagnostics command (validate, probe, camtrace, cbprobe, gpucam, stencil, wire,
    /// decalshapes, heightmap, reset, rtlog, ontop, platedepth, uimask, plates; anything else prints the stats).
    /// <b>Opt-in:</b> it is not registered automatically - call this once (e.g. in your
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
    /// The single interaction front door: the genuinely-global input knobs (gestures, obstacle-occlusion, deselect,
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

    /// <summary>Performance knobs: automatic model level-of-detail, and optional distance / screen-size culling.</summary>
    public static Draw3DPerformance Performance { get; } = new();

    /// <summary>A snapshot of the renderer's counters (see <see cref="Draw3DStats"/>).</summary>
    public static Draw3DStats Stats => BuildStats();

    /// <summary>
    /// Whether the layer has a usable frame: the game's camera was readable on the last one, so world points can be
    /// projected and picked. False before the very first frame, and wherever there is no camera to read (a loading
    /// screen, the title screen). <see cref="Pick"/> returns nothing while this is false, so it is the honest answer to
    /// "why is my content not on screen, and why does clicking it do nothing".
    /// </summary>
    public static bool HasValidFrame => lastFrameValid;

    /// <summary>Programmatic access to the diagnostics toolkit (validate/probe/camtrace/stats/wireframe) - command-independent.</summary>
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
        // a blocking map here (this runs while hovering with obstacle occlusion on) churned GPU memory and stalled the
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

    /// <summary>
    /// <c>/noire3d stencil</c> diagnostic (render thread). Samples the game depth-stencil on a screen grid and logs the
    /// distinct stencil values in view with their grid-hit counts, so the game's object-category convention (which
    /// stencil value sits on characters vs furniture vs terrain vs the world) can be discovered in-game - the basis for
    /// silhouette-occluding decals off excluded objects. Throttled to ~2/s; whole-texture readback, debug-only.
    /// </summary>
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
            NoireLogger.LogInfo($"[StencilDebug] no readable stencil plane ({desc}).", "Draw3D");
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

        NoireLogger.LogInfo($"[StencilDebug] {desc} - stencil in view (value=grid-hits): {summary}", "Draw3D");
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
            lock (Scenes)
            {
                foreach (var scene in Scenes)
                    scene.DisposeContentsInternal();
                Scenes.Clear();
                SceneScratch.Clear(); // the reused frame snapshots must not keep scenes or views alive past teardown
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
        if (NeedsInjectionPoint && !injectionInitialized)
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

        // Classic / fallback path: render + composite over the backbuffer at present time. The backbuffer holds
        // this frame's world, so the frame's committed GPU camera constants are the right projection here too
        // (the boundary above already advanced, hence the present-time freshness rule inside TryGetCommitted).
        Matrix4x4? presentGpuVp = null;
        if (cameraCapture != null && cameraCapture.TryGetCommitted(presentTimePath: true, out var presentCaptured))
            presentGpuVp = presentCaptured;

        var ctx = device.Context;
        if (renderTargetTap != null)
            renderTargetTap.SuppressSelf = true; // our own binds must not pollute an armed rtlog capture
        stateGuard!.Capture(ctx);
        try
        {
            var result = RenderMainScene(device, ctx, in backBuffer, stats, cameraOverride: null, presentGpuVp);
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
    /// <paramref name="gpuViewProj"/> is the frame's committed GPU camera constants when the capture has them - the
    /// preferred projection source (exact uploaded bytes; kills the camera swim). The struct camera still supplies
    /// the frame parameters (near plane, convention flags, eye) either way.<br/>
    /// Returns <see cref="SceneRenderResult.HasContent"/> = false on empty/skipped frames - the caller must NOT
    /// composite then, so a cleared scene leaves no stale content stamped on the present buffer.
    /// </summary>
    private static SceneRenderResult RenderMainScene(RenderDevice device, ID3D11DeviceContext* ctx, in GameRenderSources.BackBufferInfo backBuffer, RenderStats stats, GameRenderSources.CameraData? cameraOverride, Matrix4x4? gpuViewProj)
    {
        // KeepDrawingWhenUiHidden is decided here, not by Dalamud's hide flags (see RefreshUiHideOverrides): those stay
        // held so the callback keeps firing, which is what leaves the host's windows free to make their own choice. Both
        // render paths funnel through here and skip their composite on empty content, so one gate covers both. It sits
        // after the render-target tap's frame boundary in PresentTimeFrame, which must run every present regardless.
        if (!keepDrawingWhenUiHidden && IsGameUiHidden)
        {
            stats.FramesSkippedUiHidden++;
            return default;
        }

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
        var usedGpuCamera = false;
        if (cam.HasRenderCamera)
        {
            camView = cam.View;
            camProj = cam.Proj;

            // The captured GPU constants are the bytes the world pixels were rasterized from - projecting with them
            // makes the overlay-vs-world camera error exactly zero at any load (jitter included). The struct-composed
            // product is the fallback, and the A/B lever ('/noire3d gpucam') for verifying the difference in-game.
            usedGpuCamera = gpuViewProj.HasValue && Diagnostics.PreferCapturedCamera;
            viewProj = usedGpuCamera ? gpuViewProj!.Value : camView * camProj;
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

        sceneStencil ??= new SceneStencil();
        sceneStencil.Update(device); // the stencil plane of the same texture; null SRV when the format has none (decal then paints as before)

        if (stencilDebug)
            ProbeStencilGrid(device, in backBuffer);
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
        if (usedGpuCamera)
            stats.GpuCameraFrames++;
        RecordCameraHistory(in cam); // inert ring for the camera-phase trace's lag sweep (see cameraHistory)

        // Prepare phase: render-thread user code runs FIRST so its mutations and Im calls land this frame.
        var scenes = SceneScratch;
        lock (Scenes)
        {
            scenes.Clear();
            scenes.AddRange(Scenes);
        }

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

        // Decal outlines. Wireframe needs them because a decal has no geometry to rasterize - its box only bounds the
        // volume the SDF runs in, and the shape lives in the pixel shader, so wire-rasterizing it paints fragments where
        // the box's triangle edges cross the paint, not the shape. DecalShapeOutlines asks for the same trace on its own,
        // over normally rendered decals. Either way the trace runs here, before the immediate layer is consumed.
        if (Wireframe || DecalShapeOutlines)
        {
            foreach (var scene in scenes)
                scene.TraceDecalShapes(im!);
        }

        Diagnostics.OnFrame(in frame, in cam, hasDepth);
        var capVpForTrace = gpuViewProj ?? Matrix4x4.Identity; // the committed capture, whether applied or not (the trace scores it either way)
        Diagnostics.OnCameraTrace(device, in frame, in cam, cameraOverride.HasValue, injectUsedWorldSnapshot, injectUsedMainPass, usedGpuCamera, in capVpForTrace, gpuViewProj.HasValue); // "swim" investigation (armed by /noire3d camtrace)
        Diagnostics.OnFrameRendered(device, in frame, sceneDepth); // probe runs even on empty frames

        // Anything to do at all?
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
            return default; // nothing to draw - caller composites nothing, so a cleared scene leaves no residue
        }

        // GPU objects (lazy, amortized).
        stateCache ??= new StateCache();
        shaderLibrary ??= new ShaderLibrary();
        scenePass ??= new ScenePass();
        compositor ??= new Compositor();
        sceneRt ??= new RenderTarget();
        privateDepth ??= new DepthTarget();

        // Supersampling: render the scene layer at a multiple of the display resolution and box-downsample at composite
        // (the composite samples linearly, so an exact 2x is a perfect 2x2 box). Fail-soft - fall back to 1x if the
        // larger target cannot be allocated. The composite still targets the present buffer at 1x, so only sceneRt (and
        // the private depth / outline targets, which follow its size) grow. RTT views are unaffected (their own size).
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
            return default; // compile failure already logged (rung 1); nothing can reach the screen

        stats.BeginFrameCounters();
        stats.DepthAvailable = hasDepth;
        stats.UsedFallbackCamera = usedFallback;
        stats.UsedGpuCamera = usedGpuCamera;
        lastDepthMap = depthMap; // the description is composed on read (DescribeDepthSource); a frame must not format strings (Law 9)

        // Nameplate policy rects (fail-soft: any error means none this frame). These are invisible - they only gate
        // WHERE the per-pixel UI mask applies (composite shader), so plates can be covered by nearer 3D content
        // without ever cutting a visible rectangle. Only the over-everything path has a mask for them to gate; under
        // the game UI, nameplate layering is the depth stamp instead and no rect is ever needed.
        // Layout: [0..plateCount) = nameplates (visibility factors decided after collection),
        //         [plateCount..rectCount) = HUD addon rects (factor 1: HUD wins inside plate regions).
        var plateCount = 0;
        if (UiMaskActive && nameplateOcclusion != NameplateOcclusion.AlwaysVisible)
            plateCount = GameRenderSources.CollectNamePlateRects(ProtectRects, PlateDistances, 64, frame.ViewportSize, PlateRawDistance);

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
            scenePass.Execute(device, ctx, in viewFrame, view.Target, view.Depth, null, null, null, 0u, 0f, Vector4.Zero, Vector4.Zero, shaderLibrary!, stateCache!, stats, Wireframe, Lighting);
        }

        // Main pass.
        scenePass!.BeginCollect(in frame, mainPass: true);
        im!.Consume(scenePass, in frame, stats, hasDepth, Wireframe, DecalShapeOutlines);
        foreach (var scene in scenes)
            scenePass.AddScene(scene, stats, hasDepth);

        // Collision height-map: the cached collision world rendered top-down, giving the highest surface per XZ column.
        // Only DecalProjection.HighestOnly reads it (to tell a tabletop from the floor under it), so it runs only on frames
        // that actually have such a decal. Needs the game depth too, since decals reconstruct their surface from it.
        // Fail-soft: no cache / no target -> null SRV -> HighestOnly degrades to AllSurfaces.
        ID3D11ShaderResourceView* worldHeightSrv = null;
        var worldHeightRegion = Vector4.Zero;
        var needHeightMap = CollisionHeightMap && scenePass.AnyTopSurfaceDecals();
        lastFrameNeededHeightMap = needHeightMap;
        if (needHeightMap && hasDepth && worldCollision is { } wc && wc.Mesh is { } wcMesh)
        {
            worldHeightRt ??= new RenderTarget(DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT);
            if (worldHeightRt.EnsureSize(device, WorldHeightResolution, WorldHeightResolution))
            {
                var minX = wc.Center.X - WorldCollisionRadius;
                var minZ = wc.Center.Z - WorldCollisionRadius;
                var size = 2f * WorldCollisionRadius;
                var heightMatrix = BuildHeightMapMatrix(minX, minZ, size);
                // Cap the height-map at the tallest decal box top (+ the elevation band) so a covered room's roof/upper
                // floor above every decal never masks the ground; each decal bounds the search to its own box in-shader.
                var heightCeiling = scenePass.MaxTopSurfaceDecalBoxTopY() + TopSurfaceThreshold;
                scenePass.RenderWorldHeight(device, ctx, wcMesh, wc.Center, heightMatrix, heightCeiling, worldHeightRt, shaderLibrary!, stateCache!, stats);
                worldHeightSrv = worldHeightRt.Srv;
                worldHeightRegion = new Vector4(minX, minZ, 1f / size, 1f);
            }
        }

        var sceneStencilSrv = hasDepth ? sceneStencil.Srv : null; // t3: stencil plane for silhouette-exact character exclusion (null = decal paints as before)
        scenePass.Execute(device, ctx, in frame, sceneRt!, privateDepth!, hasDepth ? sceneDepth.Srv : null,
            worldHeightSrv, sceneStencilSrv, CharacterStencilValue, TopSurfaceThreshold, worldHeightRegion, depthMap, shaderLibrary!, stateCache!, stats, Wireframe, Lighting);

        // Optional selection-outline pass: only when something is outlined, so the default path is untouched. Draws
        // the outlined silhouettes into a coverage mask, then dilates it into a real screen-space rim on the layer.
        // Fail-soft: a mask/shader/target hiccup skips the outline this frame, never the scene.
        RenderOutlinePass(device, ctx, in frame, hasDepth ? sceneDepth.Srv : null, depthMap, stats);

        stats.MarkSceneDone(ctx);

        // Nameplate visibility factors: 1 = letters on top, dim = covered by your content.
        var behindFactor = Math.Clamp(nameplateDimFactor, 0f, 1f);
        if (plateCount > 0)
        {
            if (nameplateOcclusion == NameplateOcclusion.DepthAware)
                scenePass.ComputeRectOcclusion(in frame, ProtectRects, PlateDistances, ProtectFactors, plateCount, behindFactor, PlateCoveredBy);
            else // Covered: the layer always covers plates
                for (var i = 0; i < plateCount; i++)
                    ProtectFactors[i] = behindFactor;
        }

        // HUD addon rects: the HUD keeps reading on top even inside covered plate regions.
        for (var i = plateCount; i < rectCount; i++)
            ProtectFactors[i] = 1f;

        lastPlateCount = plateCount;

        stats.FramesRendered++;
        return new SceneRenderResult(true, rectCount);
    }

    /// <summary>
    /// Whether the over-everything UI mask is configured to run: the layering mode that composites after the game's
    /// UI, with masking asked for. Says nothing about whether a snapshot actually landed this frame.
    /// </summary>
    private static bool UiMaskActive => layering == Draw3DLayering.OverEverything && keepUiOnTop;

    /// <summary>One-line UI-mask state for stats/probe.</summary>
    private static string UiMaskDescription
        => !UiMaskActive ? (layering == Draw3DLayering.UnderGameUi ? "n/a (game draws its UI over the layer)" : "off")
            : uiDiffMaskHealth?.Description ?? "pending";

    /// <summary>
    /// Present-time composite of the offscreen layer over the backbuffer (<see cref="Draw3DLayering.OverEverything"/>),
    /// and the fallback whenever the pre-UI injection could not run this frame. The game has already drawn its UI by
    /// this point, so the layer would cover it; with <see cref="NativeUiConfig.KeepUiOnTop"/> the composite masks
    /// itself per-pixel by the difference between the present buffer before and after the UI was drawn into it.
    /// Assumes <see cref="RenderMainScene"/> just rendered content into <see cref="sceneRt"/>.
    /// </summary>
    private static void CompositeOverBackbuffer(RenderDevice device, ID3D11DeviceContext* ctx, in GameRenderSources.BackBufferInfo backBuffer, int rectCount)
    {
        var composite = shaderLibrary!.GetComposite(device);
        if (composite == null)
            return;

        // The "after" half of the mask: the same present buffer the injection point photographed pre-UI, now carrying
        // the game's UI. Health check self-disables the mask if the two snapshots stop being comparable at all -
        // fail-visible (the layer covers the UI) beats fail-invisible (the layer disappears).
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

            mask.EndFrame(); // the next frame must photograph its own "before" or go unmasked
        }

        compositor!.Blit(device, ctx, composite, stateCache!, sceneRt!.Srv, beforeSrv, afterSrv, backbufferRtv.Get(), backBuffer.Width, backBuffer.Height, LayerOpacity, ProtectRects, ProtectFactors, rectCount);
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
        if (disposed || !NeedsInjectionPoint || !enabled || !deviceObjectsReady)
            return false;

        var stats = renderStats;
        var device = renderDevice;
        var guard = stateGuard;
        if (stats == null || device == null || guard == null)
            return false;

        // Over everything, this point exists only to photograph the present buffer before the game draws its UI into
        // it; the layer itself composites later, at present time, differencing this snapshot against the same buffer
        // with the UI in it. Returning false leaves the present-time path to render as usual.
        if (layering == Draw3DLayering.OverEverything)
        {
            uiDiffMask ??= new UiDiffMask();
            uiDiffMask.CaptureBefore(device, device.Context, presentBufferResource);
            return false;
        }

        if (!GameRenderSources.TryGetBackBuffer(out var backBuffer) || !EnsurePresentRtv(device, presentBufferResource))
            return false;

        // Project with the exact camera the world in the present buffer was rasterized with: the captured GPU
        // constants when committed this frame, else the tap's world-pass struct snapshot (see TryGetInjectCamera).
        // This locks the layer to the world at any frame-rate; there is no delay/timing estimation.
        if (!TryGetInjectCamera(out var injectCam, out var injectGpuVp))
            return false; // no camera available - let the present-time path handle this frame

        var ctx = device.Context;
        guard.Capture(ctx);
        try
        {
            // Render this frame's scene into the offscreen target with the world-pass camera (the exact camera the
            // world in the present buffer used), then blit. Nothing to mask against: the real native UI has not been
            // drawn yet and paints itself over the layer a moment later, at its own pixel granularity.
            var result = RenderMainScene(device, ctx, in backBuffer, stats, injectCam, injectGpuVp);
            if (result.HasContent)
            {
                var composite = shaderLibrary!.GetComposite(device);
                if (composite != null && sceneRt!.Srv != null)
                    compositor!.Blit(device, ctx, composite, stateCache!, sceneRt.Srv, null, null, presentRtv.Get(), presentRtvWidth, presentRtvHeight, LayerOpacity, ProtectRects, ProtectFactors, 0);

                // Stamp our opaque depth into the game buffer so the coming nameplate pass is occluded by 3D objects
                // standing in front of characters. The other modes skip it, leaving the plate pass nothing to test:
                // Covered cannot be honoured here at all (the game draws the plates after us), so it reads as
                // AlwaysVisible, which is what NameplateOcclusion.Covered documents.
                if (nameplateOcclusion == NameplateOcclusion.DepthAware)
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
    /// The camera to project the injected layer with. <paramref name="gpuViewProj"/> receives the frame's committed
    /// GPU camera constants when the capture has them - the exact uploaded bytes the world pixels were rasterized
    /// from, the source that eliminates the camera swim at any load. <paramref name="cam"/> is the render-thread
    /// world-pass struct snapshot (camera parameters, and the projection fallback when no capture is fresh), falling
    /// back to a live read only on a frame with no world pass (menu/loading, no world-anchored content).
    /// False only when no camera is available at all.
    /// </summary>
    private static bool TryGetInjectCamera(out GameRenderSources.CameraData cam, out Matrix4x4? gpuViewProj)
    {
        gpuViewProj = null;
        if (cameraCapture != null && cameraCapture.TryGetCommitted(presentTimePath: false, out var captured))
            gpuViewProj = captured;

        if (renderTargetTap != null && renderTargetTap.TryGetWorldCamera(out cam))
        {
            injectUsedWorldSnapshot = true;
            injectUsedMainPass = renderTargetTap.WorldCameraIsMainPass;
            return true;
        }

        injectUsedWorldSnapshot = false;
        injectUsedMainPass = false;
        return GameRenderSources.TryGetCamera(out cam);
    }

    // camtrace diagnostic: whether the last inject frame projected with the render-thread world-pass snapshot (true) or
    // fell back to a live camera read (false). Only meaningful on inject frames; read by Draw3DDiagnostics.OnCameraTrace.
    private static bool injectUsedWorldSnapshot;

    // camtrace diagnostic: whether that world snapshot came from the main scene pass (the swim fix) vs the first-depth
    // fallback. 0 main-pass frames in a trace means the RTM.DepthStencil fingerprint is not matching in-game.
    private static bool injectUsedMainPass;

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
        worldHeightRt?.Release();
        sceneDepth?.Invalidate();
        gameDepthTarget?.Invalidate();
        uiDiffMask?.Release();
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

    /// <summary>
    /// Builds the affine world→clip map for the top-down collision height-map: world XZ maps linearly to the R32F target
    /// (no perspective, Y ignored) so a world point at (X,Z) is sampled at UV = (X-minX, Z-minZ)/size - matching the
    /// <c>WorldHeightRegion</c> the decal shader uses. Row-vector convention (clip = wp · M); transposed on upload.
    /// </summary>
    private static Matrix4x4 BuildHeightMapMatrix(float minX, float minZ, float size)
    {
        var s = 2f / size;
        return new Matrix4x4(
            s, 0f, 0f, 0f,   // X -> clip.x
            0f, 0f, 0f, 0f,   // Y ignored
            0f, -s, 0f, 0f,   // Z -> clip.y
            -minX * s - 1f, 1f + minZ * s, 0.5f, 1f); // translation + constant clip.z/w
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

    /// <summary>
    /// Framework-thread only: rebuilds the cached collision-world mesh near the player when they leave the cached region
    /// (the collision scene is safe to read only here). Fail-soft - a fault leaves the last cache in place, and a
    /// <see cref="DecalProjection.HighestOnly"/> decal degrades to <see cref="DecalProjection.AllSurfaces"/> until it
    /// recovers. Skipped entirely unless the last frame actually needed the height-map, so a scene without a
    /// <c>HighestOnly</c> decal never pays for the collision read.
    /// </summary>
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
            return; // still inside the cached region

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
                worldCollision = null; // moved into an empty/collision-less area
            }

            old?.Mesh?.Dispose(); // safe to dispose in use: the render path null-checks the vertex buffer
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: world-collision rebuild failed; decals fall back to the cylinder exclusion.", "Draw3D");
        }
    }

    /// <summary>
    /// Holds Dalamud's four UI-hide overrides for the layer's whole lifetime, and restores them (only the ones it forced)
    /// on disposal.
    /// <br/>
    /// They are deliberately <b>not</b> tied to <see cref="KeepDrawingWhenUiHidden"/>. These flags are the only way to
    /// keep <c>UiBuilder.Draw</c> firing at all, and both the 3D layer and the host's windows draw from it - so tying them
    /// to the switch would make the switch silently decide the host's window visibility too. Held always, the switch is
    /// free to mean only what it says: <see cref="RenderMainScene"/> makes the drawing decision instead, leaving the host
    /// to decide its windows separately via <see cref="IsGameUiHidden"/>.
    /// </summary>
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

    private static void RegisterCommand()
    {
        // Commands are global and NoireLib is statically linked per plugin - registration is best-effort;
        // the Diagnostics façade keeps the toolkit reachable regardless of who won the name.
        commandRegistered = NoireService.CommandManager.AddHandler(CommandName, new CommandInfo(HandleCommand)
        {
            HelpMessage = "Draw3D diagnostics: validate | probe | camtrace [frames] | cbprobe [frames] | gpucam | stats | wire | decalshapes | stencil | heightmap | reset | rtlog | ontop | platedepth | uimask | plates",
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
            case "validate":
                Diagnostics.RunValidate();
                Print("Draw3D: projection parity validator armed for the next 10 frames - results go to the log.");
                break;
            case "probe":
                Diagnostics.RunProbe();
                Print("Draw3D: depth probe armed for the next frame - results go to the log.");
                break;
            case "camtrace":
                var camTraceFrames = int.TryParse(rest, out var parsedFrames) && parsedFrames > 0 ? parsedFrames : 120;
                Diagnostics.RunCameraPhaseTrace(camTraceFrames);
                Print($"Draw3D: camera-phase trace armed for {camTraceFrames} frames - pan/zoom/orbit the camera vigorously; the overlay-vs-world drift goes to the log.");
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
            case "gpucam":
                Diagnostics.PreferCapturedCamera = !Diagnostics.PreferCapturedCamera;
                Print(Diagnostics.PreferCapturedCamera
                    ? $"Draw3D: GPU camera capture ON - the layer projects with the exact uploaded camera constants when available. State: {cameraCapture?.Describe() ?? "not installed"}."
                    : "Draw3D: GPU camera capture OFF (A/B) - the layer projects with the struct snapshot; expect the old load-dependent swim under fast camera motion.");
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
            case "heightmap":
                CollisionHeightMap = !CollisionHeightMap;
                Print(CollisionHeightMap
                    ? "Draw3D: collision height-map ON - DecalProjection.HighestOnly decals paint only the topmost surface per column. Nothing else reads it, so with no HighestOnly decal on screen there is nothing to see."
                    : "Draw3D: collision height-map off - HighestOnly decals paint every surface in their box (they degrade to AllSurfaces). Character cut-outs are unaffected: those are ExcludeObjects + the game stencil.");
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
                NativeUi.Layering = NativeUi.Layering == Draw3DLayering.UnderGameUi ? Draw3DLayering.OverEverything : Draw3DLayering.UnderGameUi;
                Print(NativeUi.Layering == Draw3DLayering.UnderGameUi
                    ? "Draw3D: layering = under the game UI - the layer injects before the game draws its UI, so HUD, addons and nameplates read on top of it."
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
            {
                renderTargetTap = tap;

                // The camera-constant capture rides the tap's frame phase. Fail-soft: without it the layer
                // projects with the struct snapshot, exactly as before.
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
                FramesSkippedNoCamera = 0, FramesSkippedZeroSize = 0, FramesSkippedEmpty = 0, FramesSkippedUiHidden = 0, DepthOffFrames = 0,
                DisposedAssetDraws = 0, ImCommandsDropped = 0, DrawCalls = 0, Instances = 0, Triangles = 0, Batches = 0,
                CulledItems = 0, VisibleItems = 0, ProtectRects = 0, DepthAvailable = false, UsedFallbackCamera = false,
                DepthSource = "none", SceneGpuMs = 0, CompositeGpuMs = 0,
                CameraCapture = "not installed", GpuCameraFrames = 0, UsedGpuCamera = false,
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
            DepthSource = DescribeDepthSource(s.DepthAvailable),
            SceneGpuMs = s.SceneGpuMs,
            CompositeGpuMs = s.CompositeGpuMs,
            CameraCapture = cameraCapture?.Describe() ?? "not installed",
            GpuCameraFrames = s.GpuCameraFrames,
            UsedGpuCamera = s.UsedGpuCamera,
        };
    }

    /// <summary>One-line camera-constant capture state, for the camtrace report and <see cref="Draw3DStats"/>.</summary>
    internal static string DescribeCameraCapture() => cameraCapture?.Describe() ?? "not installed";

    /// <summary>
    /// Human-readable state of the over-everything UI mask: whether it is configured, whether the render-thread
    /// snapshot is landing, and what fraction of the sampled grid the native UI actually changed. This is the
    /// answer to "is keeping the UI on top doing anything", which the mask it replaced could never be asked.
    /// </summary>
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
               + "\n  The mask is the difference between the present buffer before and after the game drew its UI, so a"
               + "\n  non-zero sample means the UI covers that grid point. All zeroes with the HUD on screen means the"
               + "\n  pre-UI snapshot is not landing where the UI is drawn; all non-zero means the snapshots are not"
               + "\n  comparable and the mask disables itself.";
    }

    /// <summary>
    /// Per-nameplate report for the over-everything path: the policy factor each plate was given last frame, and the
    /// distances that decided it. This separates the two ways nameplate layering can look broken, which are otherwise
    /// identical on screen: a factor of 1 on a plate the layer still covers means the UI mask never found the plate's
    /// pixels, whereas a factor of 0 on a plate that should read on top means the occlusion test decided wrongly.
    /// </summary>
    internal static string PlateReport()
    {
        if (layering != Draw3DLayering.OverEverything)
            return "Draw3D plates: nameplate policy rects are an over-everything mechanism; under the game UI the plate pass "
                   + "tests the depth Draw3D stamps instead, so there is nothing to report. '/noire3d ontop' switches.";

        if (!keepUiOnTop)
            return "Draw3D plates: no rects collected - NativeUi.KeepUiOnTop is off, so there is no UI mask for them to gate "
                   + "and the layer covers every plate.";

        if (nameplateOcclusion == NameplateOcclusion.AlwaysVisible)
            return "Draw3D plates: no rects collected - Nameplates = AlwaysVisible needs no policy (the mask protects every plate).";

        if (lastPlateCount == 0)
            return "Draw3D plates: 0 nameplates collected last frame. With plates on screen this means the collection itself is "
                   + "failing (the NamePlate addon or UI3DModule read), so every plate falls back to reading on top.";

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

    /// <summary>
    /// Full description of the frame's depth source: the buffer in use, the analytic mapping applied to it, and the
    /// UI-mask health. Composed here, on read, from the raw values the frame recorded: only <c>/noire3d stats</c> ever
    /// asks for it, so a rendered frame must not pay for formatting it (Law 9).
    /// </summary>
    /// <param name="hasDepth">Whether the frame being described had a usable depth source.</param>
    private static string DescribeDepthSource(bool hasDepth)
        => $"{sceneDepth?.Description ?? "none"}; map: {DepthMapDescription(in lastDepthMap, hasDepth)}; uiMask: {UiMaskDescription}";

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
    /// <br/>
    /// The render thread is stricter than "not the framework thread": on the default under-UI path this fires
    /// <b>mid-frame, from inside one of the game's own D3D calls</b>. Subscribers emit geometry and read their own
    /// state only - no game state, no chat, no Dalamud game service.
    /// </summary>
    internal static event Action<FrameContext>? OnRenderOverlay;

    internal static GameRenderSources.CameraData LastCameraData => lastCameraData;

    /// <summary>Records the camera this frame's overlay was projected with, for the camera-phase trace's lag sweep. Inert to rendering.</summary>
    private static void RecordCameraHistory(in GameRenderSources.CameraData cam)
    {
        cameraHistory[cameraHistoryCursor] = cam;
        cameraHistoryCursor = (cameraHistoryCursor + 1) % CameraHistoryLength;
        if (cameraHistoryCount < CameraHistoryLength)
            cameraHistoryCount++;
    }

    /// <summary>
    /// The camera <paramref name="framesBack"/> presented frames ago (0 = the camera the current frame's overlay was
    /// projected with, 1 = the previous frame's, …). False when the ring has not yet recorded that many frames.
    /// Used only by the camera-phase trace to test whether the pixels in the present buffer match an earlier snapshot.
    /// </summary>
    internal static bool TryGetCameraHistory(int framesBack, out GameRenderSources.CameraData cam)
    {
        cam = default;
        if (framesBack < 0 || framesBack >= cameraHistoryCount)
            return false;

        var idx = (cameraHistoryCursor - 1 - framesBack) % CameraHistoryLength;
        if (idx < 0)
            idx += CameraHistoryLength;
        cam = cameraHistory[idx];
        return true;
    }

    /// <summary>The number of frame-lags the camera-phase trace can sweep (bounded by the history ring).</summary>
    internal static int CameraHistoryDepth => CameraHistoryLength;

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

        // Transform the ray into model space (direction normalized - t comes back in model units, rescaled below).
        var localOrigin = Vector3.Transform(origin, invWorld);
        var localDir = Vector3.TransformNormal(direction, invWorld);
        var dirScale = localDir.Length();
        if (dirScale < 1e-12f)
            return false;
        localDir /= dirScale;

        // BVH-accelerated: O(log triangles) per pick instead of a per-triangle scan every hover frame (the mesh builds
        // the tree once, lazily, from its retained CPU geometry).
        if (!mesh.RayCastLocal(localOrigin, localDir, out var localT, out bestTriangle))
            return false;

        // Convert the model-space hit distance back to world-space distance.
        var hitWorld = Vector3.Transform(localOrigin + localDir * localT, world);
        bestT = Vector3.Distance(origin, hitWorld);
        return true;
    }
}
