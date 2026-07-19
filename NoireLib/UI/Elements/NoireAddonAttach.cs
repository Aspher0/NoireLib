using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Pins one of your windows to a native game window, so it docks beside the party list, sits under the target frame,
/// or hangs off the duty finder, and follows it wherever the player drags or rescales it.
/// </summary>
/// <remarks>
/// The attachment writes the window's own <see cref="Window.Position"/> rather than drawing anything, so it composes
/// with whatever the window already does and leaves its contents alone.<br/>
/// Visibility follows too: a window attached to a game window that is not on screen has nowhere to be, so by default
/// it closes with it and reopens when it comes back. That is what turns "a panel that exists only while the Duty
/// Finder is open" into two lines of setup.
/// </remarks>
/// <example>
/// <code>
/// new NoireAddonAttach(myWindow, "_PartyList", UiSide.Right) { Gap = 8f };
/// </code>
/// </example>
public sealed class NoireAddonAttach : NoireDrawable
{
    private bool closedByAttachment;
    private bool wasAttached;
    private bool subscribed;

    // What the window looked like before the attachment first wrote to it. Dalamud reapplies a window's position and
    // size every frame it draws, and only when they are set at all, so handing them back is what actually releases a
    // window: an attachment that merely stopped writing would leave it frozen wherever it was last put.
    private Window? held;
    private Vector2? heldPosition;
    private ImGuiCond heldPositionCondition;
    private bool holdingPosition;
    private WindowSizeConstraints? heldSizeConstraints;
    private bool holdingSize;

    /// <summary>
    /// Attaches a window to a native game window and starts following it immediately.
    /// </summary>
    /// <param name="window">The window to pin.</param>
    /// <param name="addonName">The addon to pin it to, for example <c>_PartyList</c>.</param>
    /// <param name="side">Which side of the game window to sit on.</param>
    /// <param name="id">An optional unique identifier, used in log messages.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="window"/> is <see langword="null"/>.</exception>
    public NoireAddonAttach(Window window, string addonName, UiSide side = UiSide.Right, string? id = null)
        : base(id, "AddonAttach")
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        AddonName = addonName ?? string.Empty;
        Side = side;

        // An attachment nobody applied does nothing at all, and the symptom is a window that simply never moves, which
        // points nowhere near the master default. Following the game window is the entire purpose of the object.
        AutoDraw = true;

        if (NoireService.IsInitialized())
        {
            NoireService.Framework.Update += OnFrameworkUpdate;
            subscribed = true;
        }

        Register();
    }

    #region Target

    /// <summary>The window being pinned.</summary>
    public Window Window { get; set; }

    /// <summary>The native game window to pin to, for example <c>_PartyList</c>.</summary>
    public string AddonName { get; set; }

    /// <summary>Which side of the game window to sit on. Defaults to <see cref="UiSide.Right"/>.</summary>
    public UiSide Side { get; set; } = UiSide.Right;

    /// <summary>How the window lines up along that side. Defaults to <see cref="UiAlign.Start"/>.</summary>
    public UiAlign Align { get; set; } = UiAlign.Start;

    /// <summary>
    /// The gap between the two, in pixels at 100%, always measured away from the game window whichever side is used.
    /// </summary>
    public float Gap { get; set; }

    /// <summary>
    /// An additional offset applied after the placement, in pixels at 100%. Unlike <see cref="Gap"/> this is taken
    /// verbatim, so it can nudge along either axis.
    /// </summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// A position to use instead of the one built from <see cref="Side"/>, <see cref="Align"/> and <see cref="Gap"/>.<br/>
    /// Set it to place the window against a point the four sides cannot name, for example a third of the way down the
    /// right edge. It is used exactly as given, including its own addon name.
    /// </summary>
    public UiPosition? PositionOverride { get; set; }

    #endregion

    #region Behaviour

    /// <summary>
    /// Whether the attachment is doing anything. Turning it off hands the window straight back: it keeps the position
    /// and size it had, and is free to be moved and resized like any other window.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the window closes while the game window is not on screen. On by default.
    /// </summary>
    public bool FollowVisibility { get; set; } = true;

    /// <summary>
    /// Whether a window closed by <see cref="FollowVisibility"/> reopens when the game window comes back. On by default.
    /// </summary>
    /// <remarks>
    /// Without this, following visibility is a one-way trip: the first time the game window closes, the attached
    /// window is gone for the session and nothing says why. A window the user closed themselves is left closed.
    /// </remarks>
    public bool RestoreOnReappear { get; set; } = true;

    /// <summary>Whether the window is resized to the game window's width. Off by default.</summary>
    /// <remarks>Independent of <see cref="MatchHeight"/>: an axis left off stays freely resizable by hand.</remarks>
    public bool MatchWidth { get; set; }

    /// <summary>Whether the window is resized to the game window's height. Off by default.</summary>
    /// <remarks>Independent of <see cref="MatchWidth"/>: an axis left off stays freely resizable by hand.</remarks>
    public bool MatchHeight { get; set; }

    /// <summary>Whether the game window was on screen the last time the attachment ran.</summary>
    public bool IsAttached { get; private set; }

    /// <summary>
    /// Whether the game window is on screen right now, asked directly rather than remembered from the last frame.
    /// </summary>
    /// <remarks>
    /// Answers for whichever addon is actually in effect, so it stays right when a <see cref="PositionOverride"/>
    /// names one of its own. Unlike <see cref="IsAttached"/> it does not care whether the attachment is enabled, which
    /// is what makes it the thing to check before opening a window that follows visibility: a window opened while its
    /// game window is not on screen is closed again before it draws, and asking first is how you say so instead.
    /// </remarks>
    public bool IsAddonVisible => UiAddon.GetRect(EffectiveAddonName) != null;

    /// <summary>
    /// The addon actually being followed, which a <see cref="PositionOverride"/> may replace.
    /// </summary>
    /// <remarks>
    /// Read straight off the two sources rather than by building the position and asking it, because this is on the
    /// framework tick and <see cref="BuildPosition"/> allocates. It answers identically: a built position takes its
    /// addon from <see cref="AddonName"/> and nothing else.
    /// </remarks>
    private string EffectiveAddonName => PositionOverride?.AddonName ?? AddonName;

    /// <summary>Invoked when <see cref="IsAttached"/> changes, with the new value.</summary>
    public Action<bool>? OnAttachedChanged { get; set; }

    #endregion

    /// <summary>
    /// Places the window for this frame.
    /// </summary>
    /// <remarks>
    /// Called automatically every frame. Call it from the window's own <c>PreDraw</c> override instead when the window
    /// has to keep up with a game window being dragged: Dalamud applies the position immediately after
    /// <c>PreDraw</c> returns, whereas the automatic pass runs elsewhere in the frame and can land one frame behind.
    /// </remarks>
    /// <returns>True when the game window was found and the window was placed.</returns>
    public bool Apply()
    {
        var window = Window;

        if (window == null)
            return false;

        if (!Enabled)
        {
            ReleaseWindow();
            SetAttached(false);
            return false;
        }

        var position = PositionOverride ?? BuildPosition();
        var viewport = ImGui.GetMainViewport();
        var addonRect = UiAddon.GetRect(position.AddonName);

        if (addonRect == null || !position.TryResolve(MeasureWindow(window), viewport.Pos, viewport.Size, out var topLeft))
        {
            // The attachment holds the window only while it is actually placing it. Nothing moves on release, because
            // giving the position back just stops it being reasserted, but the window becomes draggable again instead
            // of being frozen at the last place it resolved with no way to tell why.
            ReleaseWindow();
            SetAttached(false);
            return false;
        }

        SetAttached(true);
        TakePosition(window);

        // Dalamud takes Position in screen pixels and adds the viewport origin itself only for main-window windows.
        window.Position = window.ForceMainWindow ? topLeft - viewport.Pos : topLeft;
        window.PositionCondition = ImGuiCond.Always;

        ApplyMatchedSize(window, addonRect.Value);
        return true;
    }

    /// <inheritdoc/>
    protected override void DrawCore() => Apply();

    /// <summary>
    /// Applies the visibility rule for the coming frame, before anything has had a chance to draw.
    /// </summary>
    /// <remarks>
    /// Visibility cannot be decided from <see cref="Apply"/>, and this is the reason it is not. Dalamud tests whether a
    /// window is open, and then calls its <c>PreDraw</c> and draws it, in that order and in one pass: a window closed
    /// from <c>PreDraw</c> has already been let through the test, so it draws once anyway. Opening a panel whose game
    /// window is not on screen would flash it onto the screen and take it away again.<br/>
    /// The framework tick runs before the frame does, so a window closed here is never begun at all.
    /// </remarks>
    /// <param name="framework">The framework raising the update.</param>
    private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (IsDisposed)
            return;

        var window = Window;

        if (window == null)
            return;

        try
        {
            if (!Enabled || !FollowVisibility)
            {
                // No longer managing visibility, so the record of having closed the window is dropped rather than kept
                // to fire against a decision made under rules that no longer apply.
                closedByAttachment = false;
                return;
            }

            var visible = IsAddonVisible;

            if (!visible && window.IsOpen)
            {
                window.IsOpen = false;
                closedByAttachment = true;
            }
            else if (visible && closedByAttachment)
            {
                closedByAttachment = false;

                if (RestoreOnReappear)
                    window.IsOpen = true;
            }
        }
        catch (Exception exception)
        {
            NoireLogger.LogWarning(
                $"Addon attachment '{Id}' could not apply its visibility rule: {exception.Message}");
        }
    }

    /// <summary>
    /// Builds the position the window is placed at, from the side, alignment and gap.
    /// </summary>
    /// <returns>The position to resolve.</returns>
    private UiPosition BuildPosition()
    {
        var gap = Side switch
        {
            UiSide.Left => new Vector2(-Gap, 0f),
            UiSide.Right => new Vector2(Gap, 0f),
            UiSide.Above => new Vector2(0f, -Gap),
            UiSide.Below => new Vector2(0f, Gap),
            _ => Vector2.Zero,
        };

        return UiPosition.NextToAddon(AddonName, Side, Align, gap + Offset);
    }

    /// <summary>
    /// Resizes the window to the game window's own size on the axes that asked for it.
    /// </summary>
    /// <remarks>
    /// Written as size constraints rather than as a size, because a size is both axes at once. Matching one axis and
    /// leaving the other free is something only a per-axis minimum and maximum can express: writing
    /// <see cref="Window.Size"/> would have to invent a value for the free axis, and the only value available is the
    /// one it last wrote, so the axis nobody asked to match ends up pinned to itself and stops resizing.<br/>
    /// A matched axis is its minimum and maximum meeting. A free axis spans nothing to everything, which is the same
    /// "no constraint" Dalamud writes itself.<br/>
    /// Dalamud scales both the size and the constraints on the way to ImGui while leaving
    /// <see cref="Window.Position"/> alone, so the measured rectangle has to be divided back out here or a matched
    /// window ends up wider than what it is matching at any scale but 100%.
    /// </remarks>
    /// <param name="window">The window being placed.</param>
    /// <param name="addonRect">The bounds of the game window, in real pixels.</param>
    private void ApplyMatchedSize(Window window, UiRect addonRect)
    {
        if (!MatchWidth && !MatchHeight)
        {
            // Handed back rather than simply left alone. Dalamud reapplies constraints every frame they are set, so an
            // axis switched off while its last value stayed set would leave the window unresizable for the rest of the
            // session with nothing on screen to say why.
            ReleaseSize();
            return;
        }

        TakeSize(window);

        var logical = NoireUI.Unscaled(addonRect.Size);

        window.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(
                MatchWidth ? logical.X : 0f,
                MatchHeight ? logical.Y : 0f),
            MaximumSize = new Vector2(
                MatchWidth ? logical.X : float.MaxValue,
                MatchHeight ? logical.Y : float.MaxValue),
        };
    }

    /// <summary>
    /// Measures the window as it currently stands, so alignments that depend on its size have something real to work
    /// with rather than the size it was asked to be.
    /// </summary>
    /// <param name="window">The window to measure.</param>
    /// <returns>The size in real pixels, or zero when the window has never been drawn.</returns>
    private static Vector2 MeasureWindow(Window window)
    {
        if (NoireService.IsInitialized())
        {
            var drawn = ImGuiP.FindWindowByName(window.WindowName);

            if (!drawn.IsNull && drawn.Size.X > 0f && drawn.Size.Y > 0f)
                return drawn.Size;
        }

        return NoireUI.Scaled(window.Size ?? Vector2.Zero);
    }

    /// <summary>
    /// Records whether the game window is on screen and reports the transitions.
    /// </summary>
    /// <param name="attached">Whether it was found this frame.</param>
    private void SetAttached(bool attached)
    {
        IsAttached = attached;

        if (wasAttached == attached)
            return;

        wasAttached = attached;
        OnAttachedChanged?.Invoke(attached);
    }

    /// <summary>
    /// Takes over the window's position, remembering what it was so it can be given back.
    /// </summary>
    /// <param name="window">The window being placed.</param>
    private void TakePosition(Window window)
    {
        TrackWindow(window);

        if (holdingPosition)
            return;

        heldPosition = window.Position;
        heldPositionCondition = window.PositionCondition;
        holdingPosition = true;
    }

    /// <summary>
    /// Takes over the window's size constraints, remembering what they were so they can be given back.
    /// </summary>
    /// <param name="window">The window being placed.</param>
    private void TakeSize(Window window)
    {
        TrackWindow(window);

        if (holdingSize)
            return;

        heldSizeConstraints = window.SizeConstraints;
        holdingSize = true;
    }

    /// <summary>
    /// Notices that <see cref="Window"/> has been pointed at something else, and gives the previous one back before
    /// anything is remembered about the new one.
    /// </summary>
    /// <param name="window">The window about to be written to.</param>
    private void TrackWindow(Window window)
    {
        if (ReferenceEquals(held, window))
            return;

        ReleaseWindow();
        held = window;
    }

    /// <summary>Gives back the window's position, if this attachment had taken it.</summary>
    private void ReleasePosition()
    {
        if (!holdingPosition || held == null)
            return;

        held.Position = heldPosition;
        held.PositionCondition = heldPositionCondition;
        holdingPosition = false;
    }

    /// <summary>Gives back the window's size constraints, if this attachment had taken them.</summary>
    private void ReleaseSize()
    {
        if (!holdingSize || held == null)
            return;

        held.SizeConstraints = heldSizeConstraints;
        holdingSize = false;
    }

    /// <summary>Gives back everything this attachment had taken over, leaving the window as it found it.</summary>
    private void ReleaseWindow()
    {
        ReleasePosition();
        ReleaseSize();
        held = null;
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        ReleaseWindow();

        if (!subscribed)
            return;

        subscribed = false;
        NoireService.Framework.Update -= OnFrameworkUpdate;
    }
}
