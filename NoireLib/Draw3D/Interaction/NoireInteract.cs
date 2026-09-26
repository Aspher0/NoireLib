using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using NoireLib.Draw3D.Scene;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// Turns mouse input into hover, click and drag events on <see cref="SceneNode"/>s and <see cref="IPointerInteractor"/>s.<br/>
/// A gesture's owner is latched at press. A camera pan is never a click.
/// </summary>
public static class NoireInteract
{
    private const string DisposeKey = "NoireLib.Draw3D.NoireInteract";

    private static readonly object SyncRoot = new();
    private static readonly List<IPointerInteractor> Interactors = new();

    // Render thread only.
    private static readonly List<IPointerInteractor> OverlayScratch = new();
    private static readonly InteractionArbiter Arbiter = new();
    private static readonly Dispatcher Sink = new();
    private static readonly DragContext DragScratch = new();

    private static bool drawHookRegistered;
    private static bool overlayHookRegistered;
    private static int interactableCount;
    private static bool enabled = true;
    private static bool autoRun = true;

    // UI thread only.
    private static FrameContext frame;
    private static Vector2 mousePos;
    private static Vector3 rayOrigin, rayDirection;
    private static bool rayValid;
    private static SelectionModifiers modifiers;

    private static object? hoverToken;
    private static IPointerInteractor? hoverInteractor;
    private static SceneNode? hoverNode;
    private static Vector3 hoverWorldPoint;
    private static int? hoverTriangle;
    private static float hoverDistance;
    private static bool hoverDraggable;

    private static Vector2 pressScreen;
    private static Vector3 pressRayOrigin, pressRayDirection, pressWorldPoint;
    private static SceneNode? pressNode;
    private static IPointerInteractor? pressInteractor;

    private static bool showedCaptureLastFrame;
    private static bool captureWindowHovered;
    private static bool foreignCapturing;
    private static bool captureRequested;

    private static bool debugPrevLeft;
    private static object? debugPrevHover;
    private static string? debugPrevGateSig;

    private static bool deselectKeyWasDown;

    private const int DepthProbeMinFrames = 2;
    private const int DepthProbeMaxFrames = 4;
    private const float DepthProbeMovePixels = 2f;
    private static Vector2 depthProbeScreen;
    private static Vector3 depthProbeWorld;
    private static bool depthProbeValid;
    private static long depthProbeFrame = FrameThrottler.Never;

    private const int VkLButton = 0x01, VkRButton = 0x02, VkMButton = 0x04;

    /// <summary>Whether the click pipeline (button state, hover, capture and every gesture event) is logged to the plugin log (default false).</summary>
    public static bool DebugLog { get; set; }

    /// <summary>Master switch. When false no capture happens and the game keeps every click (default true).</summary>
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

    /// <summary>Whether interaction drives itself from <c>UiBuilder.Draw</c> every frame (default true). When false, call <see cref="Update"/> inside an ImGui frame.</summary>
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

    /// <summary>Screen-pixel movement that turns a left press into a drag (default 4).</summary>
    public static float DragThresholdPixels
    {
        get => Arbiter.DragThresholdPx;
        set => Arbiter.DragThresholdPx = MathF.Max(0f, value);
    }

    /// <summary>Whether hovering a non-draggable interactable claims the mouse from the game (default false).</summary>
    public static bool BlockGameMouseOnHover { get; set; }

    /// <summary>Whether a left click on a node updates its scene's <see cref="InteractSelection"/> (default true).</summary>
    public static bool SelectOnClick { get; set; } = true;

    /// <summary>Whether native game UI under the cursor blocks picking behind it (default true). Empty space around HUD elements stays clickable.</summary>
    public static bool GameUiBlocksInteraction { get; set; } = true;

    /// <summary>How every scene's <see cref="InteractSelection"/> is cleared (default <see cref="DeselectMode.ClickEmpty"/>).</summary>
    public static DeselectMode DeselectOn { get; set; } = DeselectMode.ClickEmpty;

    /// <summary>
    /// Whether the deselect key for <see cref="DeselectMode.Key"/> is held (default Escape).<br/>
    /// Read non-modifier keys from the OS (<see cref="KeybindsHelper.IsAsyncKeyDown"/>). Dalamud forwards them to ImGui only while a text field is focused.
    /// </summary>
    public static Func<bool> DeselectKeyHeld { get; set; } = static () => KeybindsHelper.IsAsyncKeyDown((int)VirtualKey.ESCAPE);

    /// <summary>While this returns true, a left-click toggles a node in a <see cref="SelectionMode.Multi"/> selection (default Ctrl).</summary>
    public static Func<bool> ToggleSelectionHeld { get; set; } = static () => ImGui.GetIO().KeyCtrl;

    /// <summary>While this returns true, a left-click adds a node to a <see cref="SelectionMode.Multi"/> selection (default Shift).</summary>
    public static Func<bool> AddSelectionHeld { get; set; } = static () => ImGui.GetIO().KeyShift;

    /// <summary>How what the game draws in front of an object affects picking it (default <see cref="ObstacleOcclusion.Off"/>). The ground can occlude at grazing angles.</summary>
    public static ObstacleOcclusion ObstacleOcclusionMode { get; set; } = ObstacleOcclusion.Off;

    /// <summary>While this returns true under <see cref="ObstacleOcclusion.HoldToClickThrough"/>, obstacles are ignored (default Alt).</summary>
    public static Func<bool> ClickThroughHeld { get; set; } = static () => ImGui.GetIO().KeyAlt;

    /// <summary>Slack in world units added to the obstacle distance before an object counts as occluded (default 0.3).</summary>
    public static float ObstacleOcclusionBias { get; set; } = 0.3f;

    /// <summary>The node currently under the cursor (null when none, or when the mouse is over other UI).</summary>
    public static SceneNode? HoveredNode { get; private set; }

    /// <summary>Whether a gesture NoireInteract owns is in progress (a click being resolved or a drag).</summary>
    public static bool IsInteracting => Arbiter.HasActiveInteraction;

    /// <summary>Whether NoireInteract is claiming the mouse from the game this frame.</summary>
    public static bool IsCapturingMouse => showedCaptureLastFrame;

    /// <summary>Whether another UI surface owns the mouse this frame or the cursor is outside the game viewport. Draw3D then neither hovers nor picks.</summary>
    public static bool ForeignUiHasMouse => foreignCapturing;

    /// <summary>Claims the mouse from the game for the rest of this frame. Only during <see cref="Update"/>.</summary>
    public static void RequestCapture() => captureRequested = true;

    /// <summary>Registers a pointer client (a gizmo or custom widget) into the shared arbitration. Idempotent.</summary>
    /// <param name="interactor">The interactor to add.</param>
    public static void RegisterInteractor(IPointerInteractor interactor)
    {
        ArgumentNullException.ThrowIfNull(interactor);
        lock (SyncRoot)
        {
            if (Interactors.Contains(interactor))
                return;

            Interactors.Add(interactor);
            Interactors.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
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

    /// <summary>Advances interaction by one frame. Call it inside an ImGui frame only when <see cref="AutoRun"/> is off.</summary>
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

        // Last frame's flag, discounting our own capture window.
        var foreignImGui = io.WantCaptureMouse && !(showedCaptureLastFrame && captureWindowHovered);

        if (!NoireDraw3D.LastFrameValid)
        {
            ReleaseCapture();
            return;
        }

        frame = NoireDraw3D.LastFrame;
        mousePos = ImGui.GetMousePos();

        var insideWindow = CursorWithinGameWindow(mousePos, io.DisplaySize);

        // WantCaptureMouse never reflects native addons.
        var uiAddon = insideWindow && (GameUiBlocksInteraction || DebugLog) ? AddonHelper.HitTest(mousePos, io.DisplaySize) : default;
        var overGameUi = uiAddon.IsValid;
        var uiAddonName = overGameUi ? uiAddon.Name : null;
        var nativeUi = GameUiBlocksInteraction && overGameUi;
        var otherUiOwnsMouse = foreignImGui || nativeUi || !insideWindow;

        // Their mouse ownership gates picking this frame.
        var selfDrivenOwnsMouse = DrawSelfDrivenInteractors();
        foreignCapturing = otherUiOwnsMouse || selfDrivenOwnsMouse;

        rayValid = insideWindow && frame.TryScreenToRay(mousePos, out rayOrigin, out rayDirection);

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
                    "[Draw3D] ");
            }
        }

        modifiers = SelectionModifiers.None;
        if (SafePredicate(ToggleSelectionHeld, nameof(ToggleSelectionHeld)))
            modifiers |= SelectionModifiers.Toggle;
        if (SafePredicate(AddSelectionHeld, nameof(AddSelectionHeld)))
            modifiers |= SelectionModifiers.Add;

        // Polled every frame. A press made elsewhere does not clear when the cursor returns.
        var deselectKeyDown = (DeselectOn & DeselectMode.Key) != 0 && SafePredicate(DeselectKeyHeld, nameof(DeselectKeyHeld));
        if (deselectKeyDown && !deselectKeyWasDown && insideWindow)
            NoireDraw3D.ClearAllSelections();
        deselectKeyWasDown = deselectKeyDown;

        ResolveHover();
        HoveredNode = hoverNode;

        // Without capture, Dalamud may route a world click to the game without setting io.MouseDown.
        var imguiLeft = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var leftDown = insideWindow && (imguiLeft || KeybindsHelper.IsAsyncKeyDown(VkLButton));
        var rightDown = insideWindow && (ImGui.IsMouseDown(ImGuiMouseButton.Right) || KeybindsHelper.IsAsyncKeyDown(VkRButton));
        var middleDown = insideWindow && (ImGui.IsMouseDown(ImGuiMouseButton.Middle) || KeybindsHelper.IsAsyncKeyDown(VkMButton));

        if (DebugLog && (leftDown != debugPrevLeft || !ReferenceEquals(hoverToken, debugPrevHover)))
        {
            NoireLogger.LogInfo(
                $"[Interact] left={leftDown} (imgui={imguiLeft} phys={KeybindsHelper.IsAsyncKeyDown(VkLButton)}) hover={DescribeToken(hoverToken)} " +
                $"draggable={hoverDraggable} foreign={foreignCapturing} inside={insideWindow} rayValid={rayValid} " +
                $"pos=({mousePos.X:F0},{mousePos.Y:F0})",
                "[Draw3D] ");
            debugPrevLeft = leftDown;
            debugPrevHover = hoverToken;
        }

        var sample = new PointerSample(
            mousePos, leftDown, rightDown, middleDown,
            hoverToken, hoverDraggable, foreignCapturing, BlockGameMouseOnHover);

        var wantCapture = Arbiter.Update(in sample, Sink);

        DrawInteractors();

        DrawCaptureWindow(wantCapture || captureRequested);
    }

    private static bool CursorWithinGameWindow(Vector2 mouse, Vector2 displaySize)
    {
        if (displaySize.X <= 0f || displaySize.Y <= 0f)
            return false;

        if (mouse.X < 0f || mouse.Y < 0f || mouse.X >= displaySize.X || mouse.Y >= displaySize.Y)
            return false;

        return WindowHelper.IsGameWindowFocused();
    }

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

        var occlude = ObstacleOcclusionMode switch
        {
            ObstacleOcclusion.Off => false,
            ObstacleOcclusion.Always => true,
            _ => !SafePredicate(ClickThroughHeld, nameof(ClickThroughHeld)),
        };
        var obstacleDepth = float.PositiveInfinity;
        if (occlude && TryGetOccluderSurface(out var obstacleWorld))
        {
            var depth = Vector3.Dot(obstacleWorld - rayOrigin, rayDirection);
            if (depth > 0.01f)
                obstacleDepth = depth; // degenerate or behind-camera surface
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
                    if (occlude && it.OccludesBehindObstacles(token) &&
                        Vector3.Dot(hp - rayOrigin, rayDirection) > obstacleDepth + ObstacleOcclusionBias)
                        continue;

                    hoverToken = token;
                    hoverInteractor = it;
                    hoverDraggable = true;
                    hoverDistance = dist;
                    hoverWorldPoint = hp;
                    return;
                }
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"An interactor {it.GetType().Name} threw during hit-testing.", "[Draw3D] ");
            }
        }

        var hits = NoireDraw3D.Pick(mousePos);
        foreach (var h in hits)
        {
            var node = h.Node;
            if (!node.Interactable || !node.Visible || node.Destroyed)
                continue;

            if (occlude && h.Distance > obstacleDepth + ObstacleOcclusionBias)
                continue;

            hoverToken = node;
            hoverNode = node;
            hoverDraggable = node.Draggable;
            hoverDistance = h.Distance;
            hoverTriangle = h.TriangleIndex;
            hoverWorldPoint = rayOrigin + rayDirection * h.Distance;
            return;
        }
    }

    // The depth buffer, or the collision raycast when depth is unreadable.
    private static bool TryGetOccluderSurface(out Vector3 world)
    {
        if (frame.HasDepth && !frame.UsedFallbackCamera)
            return TryGetDepthOccluder(out world); // a miss is open sky

        return NoireService.GameGui.ScreenToWorld(mousePos, out world);
    }

    private static bool TryGetDepthOccluder(out Vector3 world)
    {
        var fid = frame.FrameId;
        var minElapsed = FrameThrottler.HasElapsed(fid, depthProbeFrame, DepthProbeMinFrames);
        if (minElapsed && (depthProbeFrame == FrameThrottler.Never
                           || Vector2.Distance(mousePos, depthProbeScreen) > DepthProbeMovePixels
                           || FrameThrottler.HasElapsed(fid, depthProbeFrame, DepthProbeMaxFrames)))
        {
            depthProbeFrame = fid;
            depthProbeScreen = mousePos;
            depthProbeValid = NoireDraw3D.TryReadDepthWorld(mousePos, out depthProbeWorld);
        }

        world = depthProbeWorld;
        return depthProbeValid;
    }

    private static bool SafePredicate(Func<bool>? predicate, string name)
    {
        try
        {
            return predicate?.Invoke() ?? false;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"The {name} predicate threw.", "[Draw3D] ");
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
                continue;

            try
            {
                it.Draw(in frame, ReferenceEquals(it, hoverInteractor) ? hoverToken : null);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"An interactor {it.GetType().Name} threw while drawing.", "[Draw3D] ");
            }
        }
    }

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
                NoireLogger.LogError(ex, $"An interactor {it.GetType().Name} threw in its self-driven pre-pass.", "[Draw3D] ");
            }
        }

        return owns;
    }

    // Render thread. Reads no input.
    private static void DrawOverlayInteractors(FrameContext overlayFrame)
    {
        if (!enabled)
            return;

        lock (SyncRoot)
        {
            OverlayScratch.Clear();
            OverlayScratch.AddRange(Interactors);
        }

        foreach (var it in OverlayScratch)
        {
            if (!it.Active || it.SelfDriven)
                continue;

            try
            {
                it.DrawOverlay(in overlayFrame);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"An interactor {it.GetType().Name} threw while drawing its render overlay.", "[Draw3D] ");
            }
        }
    }

    // A fullscreen invisible window makes ImGui set WantCaptureMouse, and Dalamud withholds the input from the game. The InvisibleButton holds capture through a fast drag.
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

        // On its first frame IsWindowHovered() reads a spurious false.
        var firstShow = !showedCaptureLastFrame;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var hoveredNow = false;
        var buttonActive = false;
        if (ImGui.Begin("##NoireInteractCapture", flags))
        {
            ImGui.SetCursorPos(Vector2.Zero);
            ImGui.InvisibleButton("##NoireInteractHit", viewport.Size);
            buttonActive = ImGui.IsItemActive();
            hoveredNow = ImGui.IsWindowHovered();
        }

        ImGui.End();
        ImGui.PopStyleVar();

        captureWindowHovered = hoveredNow || buttonActive || firstShow;
        showedCaptureLastFrame = true;
    }

    private static void ReleaseCapture()
    {
        showedCaptureLastFrame = false;
        captureWindowHovered = false;
        foreignCapturing = false;
        HoveredNode = null;
    }

    private static InteractHit BuildHit(SceneNode node, MouseButton button, bool current)
    {
        if (current && ReferenceEquals(node, hoverNode))
            return new InteractHit(node, button, hoverWorldPoint, hoverTriangle, hoverDistance, mousePos, rayOrigin, rayDirection);

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
        _ => token.GetType().Name,
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
            NoireLogger.LogError(ex, $"A NoireInteract node {what} handler threw.", "[Draw3D] ");
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
            NoireLogger.LogError(ex, $"A NoireInteract node {what} handler threw.", "[Draw3D] ");
        }
    }

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

        NoireDraw3D.PickInputGate = static () => !foreignCapturing;

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
            NoireLogger.LogError(ex, "NoireInteract.Update threw; interaction skipped this frame.", "[Draw3D] ");
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
            OverlayScratch.Clear(); // must not keep interactors alive past teardown
            interactableCount = 0;
        }

        Arbiter.Reset();
        NoireDraw3D.ClearAllSelections();
        HoveredNode = null;
        showedCaptureLastFrame = false;
        captureWindowHovered = false;
        deselectKeyWasDown = false;
        NoireDraw3D.PickInputGate = null;
    }

    private sealed class Dispatcher : IArbiterSink
    {
        public void HoverEnter(object token)
        {
            if (token is SceneNode node)
            {
                node.IsHovered = true;
                Raise(node.OnHoverEnter, BuildHit(node, MouseButton.Left, current: true), "OnHoverEnter");
                node.ApplyHoverHighlight();
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
                node.RemoveHoverHighlight();
            }
            else
            {
                SafeInteractor(token, static (it, t) => it.OnHoverExit(t));
            }
        }

        public void Press(object token, MouseButton button)
        {
            if (DebugLog)
                NoireLogger.LogInfo($"[Interact] PRESS {button} on {DescribeToken(token)}", "[Draw3D] ");

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
                NoireLogger.LogInfo($"[Interact] CLICK {button} on {DescribeToken(token)}", "[Draw3D] ");

            if (token is SceneNode node)
            {
                var hit = BuildHit(node, button, current: true);
                switch (button)
                {
                    case MouseButton.Left:
                        // The pick may redirect to a SelectionProxy. The hit and OnClick stay on the clicked node.
                        if (SelectOnClick && node.Selectable)
                            node.Scene?.Selection.Pick(node.ResolveSelectionTarget(), modifiers);
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
                NoireLogger.LogInfo("[Interact] BACKGROUND CLICK (empty world)", "[Draw3D] ");

            if ((DeselectOn & DeselectMode.ClickEmpty) != 0)
                NoireDraw3D.ClearAllSelections();
        }

        public void DragStart(object token)
        {
            if (DebugLog)
                NoireLogger.LogInfo($"[Interact] DRAGSTART on {DescribeToken(token)}", "[Draw3D] ");

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
                NoireLogger.LogError(ex, $"An interactor {it.GetType().Name} threw handling a pointer event.", "[Draw3D] ");
            }
        }
    }
}
