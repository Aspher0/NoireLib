using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Scene;
using System;

namespace NoireLib.Draw3D;

/// <summary>
/// The one front door for the genuinely-global interaction knobs, reached via <see cref="NoireDraw3D.Interaction"/>.
/// A dev building things never types <see cref="NoireInteract"/>: hover / click / selection live on <c>scene</c> /
/// <c>node</c> / <c>editor</c>; this facade groups the process-wide input tuning (gestures, wall-occlusion, deselect
/// rules, multi-select modifiers, debug) and the custom-interactor registry on the class you already use. Every member
/// forwards to the <see cref="NoireInteract"/> engine, which remains available as the advanced alias.
/// </summary>
public sealed class Draw3DInteraction
{
    internal Draw3DInteraction() { }

    /// <summary>Master switch. When false, no capture happens and the game keeps every click (default true).</summary>
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

    /// <summary>Screen-pixel movement that turns a left press into a drag (and rules it out as a click). Default 4.</summary>
    public float DragThresholdPixels
    {
        get => NoireInteract.DragThresholdPixels;
        set => NoireInteract.DragThresholdPixels = value;
    }

    /// <summary>Whether merely hovering a plain (non-draggable) interactable claims the mouse from the game. Default false (a world-overlay-friendly choice).</summary>
    public bool BlockGameMouseOnHover
    {
        get => NoireInteract.BlockGameMouseOnHover;
        set => NoireInteract.BlockGameMouseOnHover = value;
    }

    /// <summary>Whether a left click on a selectable node updates its scene selection (Ctrl-toggle / Shift-add). Default true.</summary>
    public bool SelectOnClick
    {
        get => NoireInteract.SelectOnClick;
        set => NoireInteract.SelectOnClick = value;
    }

    /// <summary>Whether native game UI under the cursor blocks Draw3D from hovering / picking a 3D object behind it. Default true.</summary>
    public bool GameUiBlocksInteraction
    {
        get => NoireInteract.GameUiBlocksInteraction;
        set => NoireInteract.GameUiBlocksInteraction = value;
    }

    /// <summary>How selections are cleared (empty-world click and/or a key). Default <see cref="DeselectMode.ClickEmpty"/>. Clearing acts on every scene's selection.</summary>
    public DeselectMode DeselectOn
    {
        get => NoireInteract.DeselectOn;
        set => NoireInteract.DeselectOn = value;
    }

    /// <summary>The deselect key edge for <see cref="DeselectMode.Key"/> (default Escape).</summary>
    public Func<bool> DeselectKey
    {
        get => NoireInteract.DeselectKey;
        set => NoireInteract.DeselectKey = value;
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

    /// <summary>How game-world geometry (walls, terrain) in front of a 3D object affects hovering/clicking it. Default <see cref="WallOcclusion.Off"/>.</summary>
    public WallOcclusion WallOcclusion
    {
        get => NoireInteract.WallOcclusionMode;
        set => NoireInteract.WallOcclusionMode = value;
    }

    /// <summary>The click-through override for <see cref="WallOcclusion.HoldToClickThrough"/> (default Alt held).</summary>
    public Func<bool> ClickThroughHeld
    {
        get => NoireInteract.ClickThroughHeld;
        set => NoireInteract.ClickThroughHeld = value;
    }

    /// <summary>Slack (world units) added to the wall distance before an object counts as occluded. Default 0.3.</summary>
    public float WallOcclusionBias
    {
        get => NoireInteract.WallOcclusionBias;
        set => NoireInteract.WallOcclusionBias = value;
    }

    /// <summary>When true, logs the click / hover / capture pipeline to the plugin log for in-game diagnosis. Off by default.</summary>
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

    /// <summary>Registers a pointer client (a custom widget or gizmo) into the shared arbitration. Idempotent - the deepest interaction floor.</summary>
    /// <param name="interactor">The interactor to add.</param>
    public void RegisterInteractor(IPointerInteractor interactor) => NoireInteract.RegisterInteractor(interactor);

    /// <summary>Unregisters a pointer client. Returns whether it was registered.</summary>
    /// <param name="interactor">The interactor to remove.</param>
    public bool UnregisterInteractor(IPointerInteractor interactor) => NoireInteract.UnregisterInteractor(interactor);

    /// <summary>Asks the interaction layer to claim the mouse from the game for the remainder of this frame (custom self-managed widgets).</summary>
    public void RequestCapture() => NoireInteract.RequestCapture();

    /// <summary>Advances interaction by one frame - call it yourself only when <see cref="AutoRun"/> is off.</summary>
    public void Update() => NoireInteract.Update();
}
