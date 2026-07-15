using Dalamud.Bindings.ImGui;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// The interaction spine for Draw3D: the half of picking the renderer deliberately refuses to own (Law 11).
/// It runs on the UI thread inside the ImGui frame, reads the mouse, tracks gesture state across frames, and turns
/// raw input into hover / click / drag events on <see cref="SceneNode"/>s and registered <see cref="IPointerInteractor"/>s
/// (the gizmo among them).<br/>
/// Two behaviours it exists to guarantee, both of which a bare <see cref="NoireDraw3D.Pick"/> cannot provide:
/// <list type="bullet">
/// <item><b>A click is a click, not a camera pan.</b> Moving the camera in-game is a click-and-drag; that must never
/// register as a click on a 3D object. A gesture is bound to its owner at press time (press on an object is ours,
/// press on empty world is the game's pan), and a left press that moves past the drag threshold is a drag, never a click.</item>
/// <item><b>A drag takes the lead of input.</b> Pressing a draggable target (a depth gizmo handle, a movable node)
/// claims the mouse from the game on the first frame, so the camera does not pan underneath the drag.</item>
/// </list>
/// Starts itself the moment a node becomes <see cref="SceneNode.Interactable"/> or an interactor is registered; drives
/// itself from <c>UiBuilder.Draw</c> unless <see cref="AutoRun"/> is turned off (then call <see cref="Update"/> yourself).
/// </summary>
public static class NoireInteract
{
    private const string DisposeKey = "NoireLib.Draw3D.NoireInteract";

    private static readonly object SyncRoot = new();
    private static readonly List<IPointerInteractor> Interactors = new();
    private static readonly InteractionArbiter Arbiter = new();
    private static readonly Dispatcher Sink = new();
    private static readonly DragContext DragScratch = new();

    private static bool drawHookRegistered;
    private static bool overlayHookRegistered;
    private static int interactableCount;
    private static bool enabled = true;
    private static bool autoRun = true;

    // Per-frame working state (UI thread only).
    private static FrameContext frame;
    private static Vector2 mousePos;
    private static Vector3 rayOrigin, rayDirection;
    private static bool rayValid;
    private static SelectionModifiers modifiers;

    // Current hover candidate for the frame.
    private static object? hoverToken;
    private static IPointerInteractor? hoverInteractor;
    private static SceneNode? hoverNode;
    private static Vector3 hoverWorldPoint;
    private static int? hoverTriangle;
    private static float hoverDistance;
    private static bool hoverDraggable;

    // Press snapshot, captured on the press edge and reused for the whole drag.
    private static Vector2 pressScreen;
    private static Vector3 pressRayOrigin, pressRayDirection, pressWorldPoint;
    private static SceneNode? pressNode;
    private static IPointerInteractor? pressInteractor;

    // Mouse-capture bookkeeping (one authority; see DrawCaptureWindow).
    private static bool showedCaptureLastFrame;
    private static bool captureWindowHovered;
    private static bool foreignCapturing;
    private static bool captureRequested;

    // Diagnostics (opt-in via DebugLog); edge-tracked so the log is not spammed every frame.
    private static bool debugPrevLeft;
    private static object? debugPrevHover;
    private static string? debugPrevGateSig;

    // Depth-buffer occluder probe for wall occlusion (opt-in): the whole-texture readback is throttled, the surface cached.
    private const int DepthProbeMinFrames = 2;
    private const int DepthProbeMaxFrames = 4;
    private const float DepthProbeMovePixels = 2f;
    private static Vector2 depthProbeScreen;
    private static Vector3 depthProbeWorld;
    private static bool depthProbeValid;
    private static long depthProbeFrame = long.MinValue;

    private const int VkLButton = 0x01, VkRButton = 0x02, VkMButton = 0x04;

    private static readonly uint CurrentProcessId = (uint)Environment.ProcessId;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    /// <summary>Physical button state straight from the OS, independent of who ImGui/Dalamud routed the click to.</summary>
    private static bool PhysicalDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// When true, logs the click pipeline (raw button state, hover, capture, and every press/click/drag event) to the
    /// plugin log for in-game diagnosis. Off by default; the reference scene turns it on.
    /// </summary>
    public static bool DebugLog { get; set; }

    /// <summary>Master switch. When false, no capture happens and the game keeps every click (default true).</summary>
    public static bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value)
                return;

            enabled = value;
            if (!value)
                Arbiter.Reset();
        }
    }

    /// <summary>
    /// Whether NoireInteract drives itself from <c>UiBuilder.Draw</c> every frame (default true). Set false to call
    /// <see cref="Update"/> from your own ImGui draw code instead, when you want explicit control of ordering, since
    /// interaction must run inside an ImGui frame to read input and claim the mouse.
    /// </summary>
    public static bool AutoRun
    {
        get => autoRun;
        set
        {
            autoRun = value;
            if (value)
                EnsureRunning();
            else
                RemoveDrawHook();
        }
    }

    /// <summary>Screen-pixel movement that turns a left press into a drag (and rules it out as a click). Default 4.</summary>
    public static float DragThresholdPixels
    {
        get => Arbiter.DragThresholdPx;
        set => Arbiter.DragThresholdPx = MathF.Max(0f, value);
    }

    /// <summary>
    /// Whether merely hovering a plain (non-draggable) interactable claims the mouse. Default <b>false</b>: the
    /// playable choice for a world overlay, where hovering an object never steals the game's mouse, so the camera
    /// still pans/zooms and the world is still clickable straight through a highlighted object; only an actual drag
    /// of a draggable target (a gizmo handle) takes the lead of input. A plain left-click still selects and fires
    /// <c>OnClick</c>, but coexists with the game (the click also reaches the world behind). Set <b>true</b> for the
    /// aggressive, ImGui-consistent mode where hovering claims the mouse and consumes the click from the game: tidy
    /// for a modal editor, but it blocks camera/zoom while the cursor rests on an object.
    /// </summary>
    public static bool BlockGameMouseOnHover { get; set; }

    /// <summary>Whether a left click on a node updates <see cref="Selection"/> (with Ctrl-toggle / Shift-add). Default true.</summary>
    public static bool SelectOnClick { get; set; } = true;

    /// <summary>
    /// Whether native game UI under the cursor blocks Draw3D from hovering / picking a 3D object behind it. Default
    /// <b>true</b>: a click on a game HUD window belongs to the game, not to an object behind it. Detection is by the
    /// game's own collision nodes (not an addon's padded bounding box), so empty space around HUD elements stays
    /// clickable. Set <b>false</b> to let clicks pass through all game UI to the 3D layer (also disables the
    /// <c>[Interact/Gate]</c> game-UI probe unless <see cref="DebugLog"/> is on).
    /// </summary>
    public static bool GameUiBlocksInteraction { get; set; } = true;

    /// <summary>
    /// How the <see cref="Selection"/> is cleared. Default <see cref="DeselectMode.ClickEmpty"/>: a left click on empty
    /// world (not a camera pan, not over UI) deselects. Set <see cref="DeselectMode.None"/> to manage it yourself, or add
    /// <see cref="DeselectMode.Key"/> to also clear on <see cref="DeselectKey"/>. Flags; combine freely.
    /// </summary>
    public static DeselectMode DeselectOn { get; set; } = DeselectMode.ClickEmpty;

    /// <summary>
    /// The deselect key edge for <see cref="DeselectMode.Key"/>: returns true on the frame the key is pressed. Default
    /// <b>Escape</b>. Point it at any key or your own input.
    /// </summary>
    public static Func<bool> DeselectKey { get; set; } = static () => ImGui.IsKeyPressed(ImGuiKey.Escape, false);

    /// <summary>
    /// While this returns true, a left-click on a node <b>toggles</b> it in and out of a <see cref="SelectionMode.Multi"/>
    /// selection. Default <b>Ctrl</b>. Point it at any key, or <c>() =&gt; false</c> to disable toggle-select.
    /// </summary>
    public static Func<bool> ToggleSelectionHeld { get; set; } = static () => ImGui.GetIO().KeyCtrl;

    /// <summary>
    /// While this returns true, a left-click on a node <b>adds</b> it to a <see cref="SelectionMode.Multi"/> selection
    /// (keeping the rest). Default <b>Shift</b>. Set <c>() =&gt; true</c> to always add on click, or point it at any key.
    /// Honoured only when <see cref="Selection"/> is in <see cref="SelectionMode.Multi"/>.
    /// </summary>
    public static Func<bool> AddSelectionHeld { get; set; } = static () => ImGui.GetIO().KeyShift;

    /// <summary>
    /// How game-world geometry (walls, terrain, houses) in front of a 3D object affects hovering/clicking it.
    /// Default <see cref="WallOcclusion.Off"/>: objects are always hoverable/clickable, so picking is reliable at every
    /// camera angle out of the box. Opt into <see cref="WallOcclusion.Always"/> or
    /// <see cref="WallOcclusion.HoldToClickThrough"/> when you want real geometry to block picking (bear in mind the
    /// ground an object rests on can then occlude it at grazing angles). Also governs native gizmo handles whose depth
    /// mode is not fully on top (see <see cref="IPointerInteractor.OccludesBehindWalls"/>).
    /// </summary>
    public static WallOcclusion WallOcclusionMode { get; set; } = WallOcclusion.Off;

    /// <summary>
    /// The click-through override for <see cref="WallOcclusion.HoldToClickThrough"/>: while it returns true, obstacles
    /// are ignored and objects behind them are clickable. Default <b>Alt</b> held (Ctrl/Shift are the selection
    /// modifiers). Point it at any key (for example <c>() =&gt; ImGui.GetIO().KeyCtrl</c>) or your own input.
    /// </summary>
    public static Func<bool> ClickThroughHeld { get; set; } = static () => ImGui.GetIO().KeyAlt;

    /// <summary>Slack (world units) added to the wall distance before an object counts as occluded, so a ground-hugging decal is not blocked by its own ground. Default 0.3.</summary>
    public static float WallOcclusionBias { get; set; } = 0.3f;

    /// <summary>The node currently under the cursor (null when none, or when the mouse is over other UI).</summary>
    public static SceneNode? HoveredNode { get; private set; }

    /// <summary>True while any gesture NoireInteract owns is in progress (a click being resolved or a drag).</summary>
    public static bool IsInteracting => Arbiter.HasActiveInteraction;

    /// <summary>True while a drag NoireInteract owns is claiming the mouse from the game this frame.</summary>
    public static bool IsCapturingMouse => showedCaptureLastFrame;

    /// <summary>
    /// True when another UI surface owns the mouse this frame, or the cursor is not over the game viewport at all.
    /// Sources: a foreign ImGui window (a different plugin's, never our own capture window), native game UI (a HUD
    /// window / addon) under the cursor, or the cursor being outside the game window. While true, Draw3D neither
    /// hovers, picks, nor captures.
    /// </summary>
    public static bool ForeignUiHasMouse => foreignCapturing;

    /// <summary>
    /// Ask NoireInteract to claim the mouse from the game for the remainder of this frame. For pointer clients that
    /// handle their own input yet still need the game camera blocked while active (a custom self-managed widget).
    /// Effective only during <see cref="Update"/> (call it from an interactor's <see cref="IPointerInteractor.Draw"/>).
    /// The built-in ImGuizmo gizmo does not use this: it is <see cref="IPointerInteractor.SelfDriven"/> and blocks the
    /// camera itself with <c>SetNextFrameWantCaptureMouse</c>.
    /// </summary>
    public static void RequestCapture() => captureRequested = true;

    /// <summary>Registers a pointer client (gizmo, custom widget) into the shared arbitration. Idempotent.</summary>
    /// <param name="interactor">The interactor to add.</param>
    public static void RegisterInteractor(IPointerInteractor interactor)
    {
        ArgumentNullException.ThrowIfNull(interactor);
        lock (SyncRoot)
        {
            if (Interactors.Contains(interactor))
                return;

            Interactors.Add(interactor);
            Interactors.Sort(static (a, b) => b.Priority.CompareTo(a.Priority)); // highest priority first
        }

        EnsureRunning();
    }

    /// <summary>Unregisters a pointer client. Returns whether it was registered.</summary>
    /// <param name="interactor">The interactor to remove.</param>
    public static bool UnregisterInteractor(IPointerInteractor interactor)
    {
        lock (SyncRoot)
            return Interactors.Remove(interactor);
    }

    /// <summary>
    /// Advances interaction by one frame. Called automatically from <c>UiBuilder.Draw</c> when <see cref="AutoRun"/>
    /// is on; call it yourself (from inside your ImGui draw code) when you turn AutoRun off. Safe to call when nothing
    /// is interactable: it early-outs cheaply and never claims the mouse.
    /// </summary>
    public static void Update()
    {
        if (!NoireService.IsInitialized())
            return;

        var shouldRun = enabled && (interactableCount > 0 || InteractorCount() > 0);
        if (!shouldRun)
        {
            ReleaseCapture();
            return;
        }

        captureRequested = false;
        var io = ImGui.GetIO();

        // Another ImGui surface (a different plugin's window, never our own capture window) owning the mouse. Reads last
        // frame's capture flag, discounting our own fullscreen capture window so we never mistake ourselves for foreign.
        var foreignImGui = io.WantCaptureMouse && !(showedCaptureLastFrame && captureWindowHovered);

        if (!NoireDraw3D.LastFrameValid)
        {
            ReleaseCapture();
            return;
        }

        frame = NoireDraw3D.LastFrame;
        mousePos = ImGui.GetMousePos();

        // The cursor must be inside the game viewport and the game window must be in front. When it is not, no click the
        // OS reports belongs to the game, so nothing in the world may hover, select, deselect, drag, or capture from it.
        var insideWindow = CursorWithinGameWindow(mousePos, io.DisplaySize);

        // Native game UI (HUD windows, inventory, friend list) under the cursor is a hard pass: the game owns that click,
        // so we neither hover nor pick a 3D object through it. Detection is by the game's own collision nodes, so only an
        // actual UI element blocks, never the transparent padding around it. Native addons are not ImGui, so
        // WantCaptureMouse never reflects them; it is a separate game-state test, folded into the one "another surface
        // owns the mouse" signal. The addon name is fetched for the [Interact/Gate] diagnostic when logging.
        string? uiAddonName = null;
        var overGameUi = insideWindow && (GameUiBlocksInteraction || DebugLog)
                         && NoireDraw3D.IsCursorOverGameUi(mousePos, io.DisplaySize, out uiAddonName);
        var nativeUi = GameUiBlocksInteraction && overGameUi;
        var otherUiOwnsMouse = foreignImGui || nativeUi || !insideWindow;

        // Self-driven pre-pass: interactors that read ImGui IO through their own window (the ImGuizmo gizmo backend) run
        // before hover resolution, so their "owns the mouse now" is known this frame, not a frame late. While one owns
        // the mouse (a handle hovered/dragged) the frame is a hard pass for scene picking; the interactor blocks the
        // game camera itself. Ownership is decided entirely by the interactor's own geometry, with no UI-detection term.
        var selfDrivenOwnsMouse = DrawSelfDrivenInteractors();
        foreignCapturing = otherUiOwnsMouse || selfDrivenOwnsMouse;

        rayValid = insideWindow && frame.TryScreenToRay(mousePos, out rayOrigin, out rayDirection);

        // Gate diagnostics: log the moment the "why nothing is interactable here" reason changes (for example the cursor
        // crossing into a region that is a hard pass), naming the game addon when it is the cause. Answers whether a dead
        // spot is game UI, a foreign ImGui window, an off-window cursor, an invalid ray, or none of these (a real pick miss).
        if (DebugLog)
        {
            var gateSig = $"{insideWindow}|{foreignImGui}|{overGameUi}:{uiAddonName}|{selfDrivenOwnsMouse}|{rayValid}";
            if (gateSig != debugPrevGateSig)
            {
                debugPrevGateSig = gateSig;
                NoireLogger.LogInfo(
                    $"[Interact/Gate] inside={insideWindow} foreignImGui={foreignImGui} " +
                    $"gameUi={overGameUi}{(overGameUi ? $"('{uiAddonName}')" : string.Empty)} blocksUi={GameUiBlocksInteraction} " +
                    $"selfDriven={selfDrivenOwnsMouse} rayValid={rayValid} foreignCapturing={foreignCapturing} " +
                    $"pos=({mousePos.X:F0},{mousePos.Y:F0})",
                    "Draw3D");
            }
        }

        modifiers = SelectionModifiers.None;
        if (SafePredicate(ToggleSelectionHeld, "ToggleSelectionHeld"))
            modifiers |= SelectionModifiers.Toggle;
        if (SafePredicate(AddSelectionHeld, "AddSelectionHeld"))
            modifiers |= SelectionModifiers.Add;

        // Keybind deselect (edge-triggered), independent of where the cursor is (but not while it is outside the window).
        // Clears every scene's selection - "deselect" is global to the pointer, selections are per-scene.
        if (insideWindow && (DeselectOn & DeselectMode.Key) != 0 && SafeDeselectKey())
            NoireDraw3D.ClearAllSelections();

        ResolveHover();
        HoveredNode = hoverNode;

        // Button state: OR the physical OS state with ImGui's. When we are not capturing, Dalamud routes a world click
        // to the game and may never set ImGui's io.MouseDown, so the arbiter would never see the press. Reading the
        // physical button makes a click on a 3D object detectable without stealing the mouse on hover. All button reads
        // are gated on the cursor being inside the window, so a click in another application never reaches the arbiter.
        var imguiLeft = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var leftDown = insideWindow && (imguiLeft || PhysicalDown(VkLButton));
        var rightDown = insideWindow && (ImGui.IsMouseDown(ImGuiMouseButton.Right) || PhysicalDown(VkRButton));
        var middleDown = insideWindow && (ImGui.IsMouseDown(ImGuiMouseButton.Middle) || PhysicalDown(VkMButton));

        if (DebugLog && (leftDown != debugPrevLeft || !ReferenceEquals(hoverToken, debugPrevHover)))
        {
            NoireLogger.LogInfo(
                $"[Interact] left={leftDown} (imgui={imguiLeft} phys={PhysicalDown(VkLButton)}) hover={DescribeToken(hoverToken)} " +
                $"draggable={hoverDraggable} foreign={foreignCapturing} inside={insideWindow} rayValid={rayValid} " +
                $"pos=({mousePos.X:F0},{mousePos.Y:F0})",
                "Draw3D");
            debugPrevLeft = leftDown;
            debugPrevHover = hoverToken;
        }

        var sample = new PointerSample(
            mousePos, leftDown, rightDown, middleDown,
            hoverToken, hoverDraggable, foreignCapturing, BlockGameMouseOnHover);

        var wantCapture = Arbiter.Update(in sample, Sink);

        DrawInteractors();                                 // ray-driven interactors' UI-thread pass (self-driven ones ran in the pre-pass)

        // Native drags block the camera with the capture window. A self-driven interactor (ImGuizmo) does not get one:
        // it blocks the game camera itself via SetNextFrameWantCaptureMouse (see DrawImGuizmo), which creates no window.
        DrawCaptureWindow(wantCapture || captureRequested);
    }

    /// <summary>
    /// Whether the cursor is over the game viewport and the game window is the foreground window. Anything outside the
    /// framebuffer, or a click while another application is in front, is not the game's to act on.
    /// </summary>
    private static bool CursorWithinGameWindow(Vector2 mouse, Vector2 displaySize)
    {
        if (displaySize.X <= 0f || displaySize.Y <= 0f)
            return false;

        if (mouse.X < 0f || mouse.Y < 0f || mouse.X >= displaySize.X || mouse.Y >= displaySize.Y)
            return false;

        var foreground = GetForegroundWindow();
        if (foreground == 0)
            return false;

        GetWindowThreadProcessId(foreground, out var pid);
        return pid == CurrentProcessId;
    }

    /// <summary>Determines the topmost grabbable target under the cursor: registered interactors first (by priority), then interactable nodes.</summary>
    private static void ResolveHover()
    {
        hoverToken = null;
        hoverInteractor = null;
        hoverNode = null;
        hoverTriangle = null;
        hoverDraggable = false;
        hoverDistance = 0f;
        hoverWorldPoint = rayValid ? rayOrigin + rayDirection : Vector3.Zero;

        if (!rayValid || foreignCapturing)
            return;

        // Optional world-geometry occlusion (disabled by default, so picking is pure geometry and reliable at any camera
        // angle). When enabled, a wall / terrain / furnishing in front of a target blocks hovering it unless the
        // consumer's click-through override is active; wallDepth is the occluding surface's depth along the pick ray,
        // directly comparable to a target's own ray depth.
        var occlude = WallOcclusionMode switch
        {
            WallOcclusion.Off => false,
            WallOcclusion.Always => true,
            _ => !SafeClickThroughHeld(),
        };
        var wallDepth = float.PositiveInfinity;
        if (occlude && NoireService.IsInitialized() && TryGetOccluderSurface(out var wallWorld))
        {
            var depth = Vector3.Dot(wallWorld - rayOrigin, rayDirection);
            if (depth > 0.01f)
                wallDepth = depth; // ignore a degenerate or behind-camera surface
        }

        IPointerInteractor[] snapshot;
        lock (SyncRoot)
            snapshot = Interactors.ToArray();

        foreach (var it in snapshot)
        {
            if (!it.Active)
                continue;

            try
            {
                if (it.HitTest(rayOrigin, rayDirection, mousePos, in frame, out var token, out var dist, out var hp) && token != null)
                {
                    // Interactors are grabbable through walls unless they opt into occlusion (the native gizmo does so
                    // when its handles are world-occluded rather than drawn fully on top).
                    if (occlude && it.OccludesBehindWalls(token) &&
                        Vector3.Dot(hp - rayOrigin, rayDirection) > wallDepth + WallOcclusionBias)
                        continue;

                    hoverToken = token;
                    hoverInteractor = it;
                    hoverDraggable = true; // interactor targets (gizmo handles) are always drag targets
                    hoverDistance = dist;
                    hoverWorldPoint = hp;
                    return;
                }
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"An interactor {it.GetType().Name} threw during hit-testing.", "Draw3D");
            }
        }

        // Fall back to scene-node picking (nearest interactable). Pick honours our PickInputGate (not foreign).
        var hits = NoireDraw3D.Pick(mousePos);
        foreach (var h in hits)
        {
            var node = h.Node;
            if (!node.Interactable || !node.Visible || node.Destroyed)
                continue;

            // h.Distance is the depth along the pick ray, directly comparable to wallDepth.
            if (occlude && h.Distance > wallDepth + WallOcclusionBias)
                continue; // a wall / terrain is in front of this object (hold the click-through key to reach it)

            hoverToken = node;
            hoverNode = node;
            hoverDraggable = node.Draggable;
            hoverDistance = h.Distance;
            hoverTriangle = h.TriangleIndex;
            hoverWorldPoint = rayOrigin + rayDirection * h.Distance;
            return;
        }
    }

    /// <summary>
    /// The nearest game surface under the cursor, for wall occlusion. Prefers the game depth buffer (every rendered
    /// surface: static meshes, fences, furniture, decorations, not only the collision meshes the raycast alone
    /// misses), reconstructing the world point and caching it since the readback copies the whole depth texture. Falls
    /// back to the game's collision raycast only on frames where the depth buffer is unreadable.
    /// </summary>
    private static bool TryGetOccluderSurface(out Vector3 world)
    {
        if (frame.HasDepth && !frame.UsedFallbackCamera)
            return TryGetDepthOccluder(out world); // a miss here is open sky (no occluder), not a reason to fall back

        world = default;
        return NoireService.GameGui.ScreenToWorld(mousePos, out world);
    }

    /// <summary>Throttled depth-buffer surface probe: re-reads at most every few frames, or when the cursor moves, and reuses the cached surface between reads.</summary>
    private static bool TryGetDepthOccluder(out Vector3 world)
    {
        var fid = frame.FrameId;
        var minElapsed = depthProbeFrame == long.MinValue || fid - depthProbeFrame >= DepthProbeMinFrames;
        if (minElapsed && (depthProbeFrame == long.MinValue
                           || Vector2.Distance(mousePos, depthProbeScreen) > DepthProbeMovePixels
                           || fid - depthProbeFrame >= DepthProbeMaxFrames))
        {
            depthProbeFrame = fid;
            depthProbeScreen = mousePos;
            depthProbeValid = NoireDraw3D.TryReadDepthWorld(mousePos, out depthProbeWorld);
        }

        world = depthProbeWorld;
        return depthProbeValid;
    }

    private static bool SafeClickThroughHeld()
    {
        try
        {
            return ClickThroughHeld?.Invoke() ?? false;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "The ClickThroughHeld predicate threw.", "Draw3D");
            return false;
        }
    }

    private static bool SafePredicate(Func<bool>? predicate, string name)
    {
        try
        {
            return predicate?.Invoke() ?? false;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"The {name} predicate threw.", "Draw3D");
            return false;
        }
    }

    private static bool SafeDeselectKey()
    {
        try
        {
            return DeselectKey?.Invoke() ?? false;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "The DeselectKey predicate threw.", "Draw3D");
            return false;
        }
    }

    private static void DrawInteractors()
    {
        IPointerInteractor[] snapshot;
        lock (SyncRoot)
            snapshot = Interactors.ToArray();

        foreach (var it in snapshot)
        {
            if (!it.Active || it.SelfDriven)
                continue; // self-driven interactors already drew in the pre-pass (DrawSelfDrivenInteractors)

            try
            {
                it.Draw(in frame, ReferenceEquals(it, hoverInteractor) ? hoverToken : null);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"An interactor {it.GetType().Name} threw while drawing.", "Draw3D");
            }
        }
    }

    /// <summary>
    /// The self-driven pre-pass: runs each self-driven interactor's own input and draw (the ImGuizmo gizmo host window)
    /// before scene hover resolution, and returns whether any of them owns the mouse right now.
    /// </summary>
    private static bool DrawSelfDrivenInteractors()
    {
        IPointerInteractor[] snapshot;
        lock (SyncRoot)
            snapshot = Interactors.ToArray();

        var owns = false;
        foreach (var it in snapshot)
        {
            if (!it.Active || !it.SelfDriven)
                continue;

            try
            {
                owns |= it.DrawSelfDriven(in frame);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"An interactor {it.GetType().Name} threw in its self-driven pre-pass.", "Draw3D");
            }
        }

        return owns;
    }

    /// <summary>
    /// Render-thread overlay (fired by <see cref="NoireDraw3D.OnRenderOverlay"/> with the live frame): draws each
    /// ray-driven interactor's zero-latency geometry (the native gizmo handles) so their screen-constant sizing tracks
    /// the camera instead of lagging a frame during zoom. Self-driven interactors (ImGuizmo) draw themselves on the UI
    /// thread and are skipped. Runs on the render thread; it only reads hover/drag state and emits geometry, no input.
    /// </summary>
    private static void DrawOverlayInteractors(FrameContext overlayFrame)
    {
        if (!enabled)
            return;

        IPointerInteractor[] snapshot;
        lock (SyncRoot)
            snapshot = Interactors.ToArray();

        foreach (var it in snapshot)
        {
            if (!it.Active || it.SelfDriven)
                continue;

            try
            {
                it.DrawOverlay(in overlayFrame);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"An interactor {it.GetType().Name} threw while drawing its render overlay.", "Draw3D");
            }
        }
    }

    // ---------------------------------------------------------------- capture window

    /// <summary>
    /// The single mouse-capture authority. When NoireInteract wants the mouse, it shows a fullscreen, invisible ImGui
    /// window under the cursor: hovering it makes ImGui set <c>WantCaptureMouse</c>, which is what tells Dalamud to
    /// withhold the input from the game, so the camera cannot pan and nothing is targeted. The window is only shown
    /// while interacting (and only when no foreign window already owns the cursor), so the game keeps the mouse the rest
    /// of the time. The InvisibleButton's active-id holds the capture through a fast drag even if the cursor outruns hover.
    /// The self-driven ImGuizmo backend does not use this: it blocks the camera with <c>SetNextFrameWantCaptureMouse</c>
    /// (no window) instead.
    /// </summary>
    /// <param name="want">Whether the window should be shown this frame.</param>
    private static void DrawCaptureWindow(bool want)
    {
        if (!want)
        {
            showedCaptureLastFrame = false;
            captureWindowHovered = false;
            return;
        }

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus;

        // First frame this window appears: ImGui's hovered-window is computed from the previous frame's window list,
        // which did not yet contain this window, so IsWindowHovered() reads a spurious false. Remember that to correct it.
        var firstShow = !showedCaptureLastFrame;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var hoveredNow = false;
        var buttonActive = false;
        if (ImGui.Begin("##NoireInteractCapture", flags))
        {
            ImGui.SetCursorPos(Vector2.Zero);
            ImGui.InvisibleButton("##NoireInteractHit", viewport.Size);
            buttonActive = ImGui.IsItemActive();        // the active-id holds capture through a fast drag past the hover
            hoveredNow = ImGui.IsWindowHovered();
        }

        ImGui.End();
        ImGui.PopStyleVar();

        // The window is fullscreen and only shown when the cursor is already over our target, so on its first appearance
        // it is the surface under the cursor even though IsWindowHovered() cannot see that yet; a genuine foreign window
        // on top self-corrects next frame. Without this optimism the frame-one false breaks the self-discount above.
        captureWindowHovered = hoveredNow || buttonActive || firstShow;
        showedCaptureLastFrame = true;
    }

    private static void ReleaseCapture()
    {
        showedCaptureLastFrame = false;
        captureWindowHovered = false;
        foreignCapturing = false;
        if (HoveredNode != null)
            HoveredNode = null;
    }

    // ---------------------------------------------------------------- dispatch helpers

    private static InteractHit BuildHit(SceneNode node, MouseButton button, bool current)
    {
        if (current && ReferenceEquals(node, hoverNode))
            return new InteractHit(node, button, hoverWorldPoint, hoverTriangle, hoverDistance, mousePos, rayOrigin, rayDirection);

        // Exit / stale: no live pick point, so anchor at the node origin.
        var origin = node.WorldMatrix.Translation;
        return new InteractHit(node, button, origin, null, Vector3.Distance(rayOrigin, origin), mousePos, rayOrigin, rayDirection);
    }

    private static DragContext BuildDragContext()
    {
        DragScratch.Button = MouseButton.Left;
        DragScratch.Node = pressNode;
        DragScratch.ScreenStart = pressScreen;
        DragScratch.ScreenNow = mousePos;
        DragScratch.PressWorldPoint = pressWorldPoint;
        DragScratch.PressRayOrigin = pressRayOrigin;
        DragScratch.PressRayDirection = pressRayDirection;
        DragScratch.RayOrigin = rayValid ? rayOrigin : pressRayOrigin;
        DragScratch.RayDirection = rayValid ? rayDirection : pressRayDirection;
        DragScratch.Frame = frame;
        return DragScratch;
    }

    private static string DescribeToken(object? token) => token switch
    {
        null => "null",
        SceneNode n => $"node '{n.Name ?? "(unnamed)"}'",
        _ => token.GetType().Name,   // for example GizmoHandleRef for a gizmo handle
    };

    private static IPointerInteractor? ResolveInteractor(object token)
    {
        if (pressInteractor != null && pressInteractor.OwnsToken(token))
            return pressInteractor;
        if (hoverInteractor != null && hoverInteractor.OwnsToken(token))
            return hoverInteractor;

        lock (SyncRoot)
        {
            foreach (var it in Interactors)
            {
                if (it.OwnsToken(token))
                    return it;
            }
        }

        return null;
    }

    private static void Raise(Action<InteractHit>? handler, in InteractHit hit, string what)
    {
        if (handler == null)
            return;

        try
        {
            handler(hit);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"A NoireInteract node {what} handler threw.", "Draw3D");
        }
    }

    private static void Raise(Action<DragContext>? handler, DragContext ctx, string what)
    {
        if (handler == null)
            return;

        try
        {
            handler(ctx);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"A NoireInteract node {what} handler threw.", "Draw3D");
        }
    }

    // ---------------------------------------------------------------- lifecycle

    internal static void OnNodeBecameInteractable()
    {
        lock (SyncRoot)
            interactableCount++;
        EnsureRunning();
    }

    internal static void OnNodeNoLongerInteractable()
    {
        lock (SyncRoot)
        {
            if (interactableCount > 0)
                interactableCount--;
        }
    }

    private static int InteractorCount()
    {
        lock (SyncRoot)
            return Interactors.Count;
    }

    private static void EnsureRunning()
    {
        if (!NoireService.IsInitialized())
            return;

        // NoireInteract is the natural owner of the pick gate: nothing should pick while foreign UI holds the mouse.
        NoireDraw3D.PickInputGate = static () => !foreignCapturing;

        // Subscribe once to the render-thread overlay so native gizmo handles draw zero-latency (phase-locked to zoom).
        // Independent of AutoRun; the overlay is about drawing on the render thread, not how Update() is driven.
        if (!overlayHookRegistered)
        {
            NoireDraw3D.OnRenderOverlay += DrawOverlayInteractors;
            overlayHookRegistered = true;
        }

        if (autoRun)
            EnsureDrawHook();

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeKey))
            NoireLibMain.RegisterOnDispose(DisposeKey, Cleanup);
    }

    private static void EnsureDrawHook()
    {
        if (drawHookRegistered)
            return;

        NoireService.PluginInterface.UiBuilder.Draw += OnDraw;
        drawHookRegistered = true;
    }

    private static void RemoveDrawHook()
    {
        if (!drawHookRegistered)
            return;

        if (NoireService.IsInitialized())
            NoireService.PluginInterface.UiBuilder.Draw -= OnDraw;
        drawHookRegistered = false;
    }

    private static void OnDraw()
    {
        try
        {
            Update();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "NoireInteract.Update threw; interaction skipped this frame.", "Draw3D");
        }
    }

    private static void Cleanup()
    {
        RemoveDrawHook();
        if (overlayHookRegistered)
        {
            NoireDraw3D.OnRenderOverlay -= DrawOverlayInteractors;
            overlayHookRegistered = false;
        }

        lock (SyncRoot)
        {
            Interactors.Clear();
            interactableCount = 0;
        }

        Arbiter.Reset();
        NoireDraw3D.ClearAllSelections();
        HoveredNode = null;
        showedCaptureLastFrame = false;
        captureWindowHovered = false;
        if (NoireDraw3D.PickInputGate != null && NoireService.IsInitialized())
            NoireDraw3D.PickInputGate = null;
    }

    /// <summary>Routes arbiter events to the owning node or interactor, with per-callback error containment.</summary>
    private sealed class Dispatcher : IArbiterSink
    {
        public void HoverEnter(object token)
        {
            if (token is SceneNode node)
            {
                node.IsHovered = true;
                Raise(node.OnHoverEnter, BuildHit(node, MouseButton.Left, current: true), "OnHoverEnter");
            }
            else
            {
                SafeInteractor(token, static (it, t) => it.OnHoverEnter(t));
            }
        }

        public void HoverExit(object token)
        {
            if (token is SceneNode node)
            {
                node.IsHovered = false;
                Raise(node.OnHoverExit, BuildHit(node, MouseButton.Left, current: false), "OnHoverExit");
            }
            else
            {
                SafeInteractor(token, static (it, t) => it.OnHoverExit(t));
            }
        }

        public void Press(object token, MouseButton button)
        {
            if (DebugLog)
                NoireLogger.LogInfo($"[Interact] PRESS {button} on {DescribeToken(token)}", "Draw3D");

            // Snapshot the grab so the whole drag reads a stable press ray / anchor.
            pressScreen = mousePos;
            pressRayOrigin = rayOrigin;
            pressRayDirection = rayDirection;
            pressWorldPoint = hoverWorldPoint;
            pressNode = token as SceneNode;
            pressInteractor = pressNode == null ? ResolveInteractor(token) : null;
        }

        public void Click(object token, MouseButton button)
        {
            if (DebugLog)
                NoireLogger.LogInfo($"[Interact] CLICK {button} on {DescribeToken(token)}", "Draw3D");

            if (token is SceneNode node)
            {
                var hit = BuildHit(node, button, current: true);
                switch (button)
                {
                    case MouseButton.Left:
                        // Route into the node's OWN scene selection (per-scene, no global). A node opts out of
                        // selection via SceneNode.Selectable = false (MakeInteractable), staying hover/click-only.
                        if (SelectOnClick && node.Selectable)
                            node.Scene?.Selection.Pick(node, modifiers);
                        Raise(node.OnClick, hit, "OnClick");
                        break;
                    case MouseButton.Right:
                        Raise(node.OnRightClick, hit, "OnRightClick");
                        break;
                    case MouseButton.Middle:
                        Raise(node.OnMiddleClick, hit, "OnMiddleClick");
                        break;
                }
            }
            else
            {
                SafeInteractor(token, (it, t) => it.OnClick(t, button));
            }
        }

        public void BackgroundClick()
        {
            if (DebugLog)
                NoireLogger.LogInfo("[Interact] BACKGROUND CLICK (empty world)", "Draw3D");

            if ((DeselectOn & DeselectMode.ClickEmpty) != 0)
                NoireDraw3D.ClearAllSelections();
        }

        public void DragStart(object token)
        {
            if (DebugLog)
                NoireLogger.LogInfo($"[Interact] DRAGSTART on {DescribeToken(token)}", "Draw3D");

            var ctx = BuildDragContext();
            if (token is SceneNode node)
                Raise(node.OnDragStart, ctx, "OnDragStart");
            else
                SafeInteractor(token, (it, t) => it.OnDragStart(t, ctx));
        }

        public void Drag(object token)
        {
            var ctx = BuildDragContext();
            if (token is SceneNode node)
                Raise(node.OnDrag, ctx, "OnDrag");
            else
                SafeInteractor(token, (it, t) => it.OnDrag(t, ctx));
        }

        public void DragEnd(object token)
        {
            var ctx = BuildDragContext();
            if (token is SceneNode node)
                Raise(node.OnDragEnd, ctx, "OnDragEnd");
            else
                SafeInteractor(token, (it, t) => it.OnDragEnd(t, ctx));

            pressNode = null;
            pressInteractor = null;
        }

        private static void SafeInteractor(object token, Action<IPointerInteractor, object> action)
        {
            var it = ResolveInteractor(token);
            if (it == null)
                return;

            try
            {
                action(it, token);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"An interactor {it.GetType().Name} threw handling a pointer event.", "Draw3D");
            }
        }
    }
}
