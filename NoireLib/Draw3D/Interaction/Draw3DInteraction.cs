using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Scene;
using System;

namespace NoireLib.Draw3D;

/// <summary>Process-wide interaction settings and the interactor registry, forwarding to <see cref="NoireInteract"/>.</summary>
public sealed class Draw3DInteraction
{
    internal Draw3DInteraction() { }

    /// <summary>Master switch. When false no capture happens and the game keeps every click (default true).</summary>
    public bool Enabled
    {
        get => NoireInteract.Enabled;
        set => NoireInteract.Enabled = value;
    }

    /// <summary>Whether interaction drives itself from <c>UiBuilder.Draw</c> every frame (default true). Turn off to call <see cref="Update"/> yourself.</summary>
    public bool AutoRun
    {
        get => NoireInteract.AutoRun;
        set => NoireInteract.AutoRun = value;
    }

    /// <summary>Screen-pixel movement that turns a left press into a drag (default 4).</summary>
    public float DragThresholdPixels
    {
        get => NoireInteract.DragThresholdPixels;
        set => NoireInteract.DragThresholdPixels = value;
    }

    /// <summary>Whether merely hovering a non-draggable interactable claims the mouse from the game (default false).</summary>
    public bool BlockGameMouseOnHover
    {
        get => NoireInteract.BlockGameMouseOnHover;
        set => NoireInteract.BlockGameMouseOnHover = value;
    }

    /// <summary>Whether a left click on a selectable node updates its scene selection (default true).</summary>
    public bool SelectOnClick
    {
        get => NoireInteract.SelectOnClick;
        set => NoireInteract.SelectOnClick = value;
    }

    /// <summary>Whether native game UI under the cursor blocks picking a 3D object behind it (default true).</summary>
    public bool GameUiBlocksInteraction
    {
        get => NoireInteract.GameUiBlocksInteraction;
        set => NoireInteract.GameUiBlocksInteraction = value;
    }

    /// <summary>How every scene's selection is cleared (default <see cref="DeselectMode.ClickEmpty"/>).</summary>
    public DeselectMode DeselectOn
    {
        get => NoireInteract.DeselectOn;
        set => NoireInteract.DeselectOn = value;
    }

    /// <summary>Whether the deselect key for <see cref="DeselectMode.Key"/> is held (default Escape). The press edge is detected internally.</summary>
    public Func<bool> DeselectKeyHeld
    {
        get => NoireInteract.DeselectKeyHeld;
        set => NoireInteract.DeselectKeyHeld = value;
    }

    /// <summary>While this returns true, a left-click toggles a node in/out of a multi-selection (default Ctrl).</summary>
    public Func<bool> ToggleSelectionHeld
    {
        get => NoireInteract.ToggleSelectionHeld;
        set => NoireInteract.ToggleSelectionHeld = value;
    }

    /// <summary>While this returns true, a left-click adds a node to a multi-selection (default Shift).</summary>
    public Func<bool> AddSelectionHeld
    {
        get => NoireInteract.AddSelectionHeld;
        set => NoireInteract.AddSelectionHeld = value;
    }

    /// <summary>How anything the game draws in front of a 3D object affects picking it (default <see cref="ObstacleOcclusion.Off"/>).</summary>
    public ObstacleOcclusion ObstacleOcclusion
    {
        get => NoireInteract.ObstacleOcclusionMode;
        set => NoireInteract.ObstacleOcclusionMode = value;
    }

    /// <summary>The click-through override for <see cref="ObstacleOcclusion.HoldToClickThrough"/> (default Alt held).</summary>
    public Func<bool> ClickThroughHeld
    {
        get => NoireInteract.ClickThroughHeld;
        set => NoireInteract.ClickThroughHeld = value;
    }

    /// <summary>Slack in world units added to the obstacle distance before an object counts as occluded (default 0.3).</summary>
    public float ObstacleOcclusionBias
    {
        get => NoireInteract.ObstacleOcclusionBias;
        set => NoireInteract.ObstacleOcclusionBias = value;
    }

    /// <summary>Whether the click, hover and capture pipeline is logged to the plugin log (default false).</summary>
    public bool DebugLog
    {
        get => NoireInteract.DebugLog;
        set => NoireInteract.DebugLog = value;
    }

    /// <summary>The node currently under the cursor (null when none, or when the mouse is over other UI).</summary>
    public SceneNode? HoveredNode => NoireInteract.HoveredNode;

    /// <summary>True while any gesture the interaction layer owns is in progress (a click being resolved or a drag).</summary>
    public bool IsInteracting => NoireInteract.IsInteracting;

    /// <summary>True while a drag the interaction layer owns is claiming the mouse from the game this frame.</summary>
    public bool IsCapturingMouse => NoireInteract.IsCapturingMouse;

    /// <summary>True when another UI surface owns the mouse this frame, or the cursor is outside the game viewport.</summary>
    public bool ForeignUiHasMouse => NoireInteract.ForeignUiHasMouse;

    /// <summary>Registers a pointer client (a custom widget or gizmo) into the shared arbitration. Idempotent.</summary>
    /// <param name="interactor">The interactor to add.</param>
    public void RegisterInteractor(IPointerInteractor interactor) => NoireInteract.RegisterInteractor(interactor);

    /// <summary>Unregisters a pointer client. Returns whether it was registered.</summary>
    /// <param name="interactor">The interactor to remove.</param>
    public bool UnregisterInteractor(IPointerInteractor interactor) => NoireInteract.UnregisterInteractor(interactor);

    /// <summary>Asks the interaction layer to claim the mouse from the game for the rest of this frame.</summary>
    public void RequestCapture() => NoireInteract.RequestCapture();

    /// <summary>Advances interaction by one frame. Call it only when <see cref="AutoRun"/> is off.</summary>
    public void Update() => NoireInteract.Update();
}
