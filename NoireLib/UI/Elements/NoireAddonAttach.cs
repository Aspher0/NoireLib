using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Pins one of your windows to a native game window, and follows it wherever the player drags or rescales it.
/// </summary>
[NoireFacadeFactory]
public sealed class NoireAddonAttach : NoireDrawable
{
    private bool closedByAttachment;
    private bool wasAttached;
    private bool subscribed;

    // What the window looked like before the attachment first wrote to it. Dalamud reapplies a window's position
    // and size every frame it draws, and only when they are set at all, so releasing a window means writing the
    // old values back; merely stopping the writes would leave it frozen wherever it was last put.
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

        // AutoDraw is set because an attachment nobody applied does nothing: the symptom is a window that simply
        // never moves.
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

    /// <summary>Which side of the game window to sit on.</summary>
    public UiSide Side { get; set; } = UiSide.Right;

    /// <summary>How the window lines up along that side.</summary>
    public UiAlign Align { get; set; } = UiAlign.Start;

    /// <summary>
    /// The gap between the two, in pixels at 100%, always measured away from the game window whichever side is used.
    /// </summary>
    public float Gap { get; set; }

    /// <summary>
    /// An additional offset applied after the placement, in pixels at 100%.
    /// </summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// A position to use instead of the one built from <see cref="Side"/>, <see cref="Align"/> and <see cref="Gap"/>.
    /// </summary>
    public UiPosition? PositionOverride { get; set; }

    #endregion

    #region Behaviour

    /// <summary>
    /// Whether the attachment is doing anything.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the window closes while the game window is not on screen.
    /// </summary>
    public bool FollowVisibility { get; set; } = true;

    /// <summary>
    /// Whether a window closed by <see cref="FollowVisibility"/> reopens when the game window comes back.
    /// </summary>
    public bool RestoreOnReappear { get; set; } = true;

    /// <summary>Whether the window is resized to the game window's width.</summary>
    public bool MatchWidth { get; set; }

    /// <summary>Whether the window is resized to the game window's height.</summary>
    public bool MatchHeight { get; set; }

    /// <summary>Whether the game window was on screen the last time the attachment ran.</summary>
    public bool IsAttached { get; private set; }

    /// <summary>
    /// Whether the game window is on screen right now, asked directly rather than remembered from the last frame.
    /// </summary>
    public bool IsAddonVisible => UiAddon.GetRect(EffectiveAddonName) != null;

    private string EffectiveAddonName => PositionOverride?.AddonName ?? AddonName;

    /// <summary>Invoked when <see cref="IsAttached"/> changes, with the new value.</summary>
    public Action<bool>? OnAttachedChanged { get; set; }

    #endregion

    /// <summary>
    /// Places the window for this frame.
    /// </summary>
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

    // Applies the visibility rule for the coming frame, before anything has had a chance to draw.
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

    private UiPosition BuildPosition()
    {
        // Rebuilt only when one of the five values it is made of has moved. A UiPosition is a class, and this runs on
        // every frame the attachment is applied, so building one each time put an object per frame on the draw thread
        // for a description that changes when the consumer changes it and at no other time.
        if (builtPosition != null
            && builtSide == Side
            && builtAlign == Align
            && builtGap == Gap
            && builtOffset == Offset
            && string.Equals(builtAddonName, AddonName, StringComparison.Ordinal))
        {
            return builtPosition;
        }

        var gap = Side switch
        {
            UiSide.Left => new Vector2(-Gap, 0f),
            UiSide.Right => new Vector2(Gap, 0f),
            UiSide.Above => new Vector2(0f, -Gap),
            UiSide.Below => new Vector2(0f, Gap),
            _ => Vector2.Zero,
        };

        builtPosition = UiPosition.NextToAddon(AddonName, Side, Align, gap + Offset);
        builtAddonName = AddonName;
        builtSide = Side;
        builtAlign = Align;
        builtGap = Gap;
        builtOffset = Offset;

        return builtPosition;
    }

    // The position last built, and the five values it was built from.
    private UiPosition? builtPosition;

    private string? builtAddonName;

    private UiSide builtSide;

    private UiAlign builtAlign;

    private float builtGap;

    private Vector2 builtOffset;

    // addonRect is in real pixels.
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

    // Measures the window as it currently stands rather than the size it was asked to be, in real pixels, or zero when
    // the window has never been drawn.
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

    private void SetAttached(bool attached)
    {
        IsAttached = attached;

        if (wasAttached == attached)
            return;

        wasAttached = attached;
        OnAttachedChanged?.Invoke(attached);
    }

    private void TakePosition(Window window)
    {
        TrackWindow(window);

        if (holdingPosition)
            return;

        heldPosition = window.Position;
        heldPositionCondition = window.PositionCondition;
        holdingPosition = true;
    }

    private void TakeSize(Window window)
    {
        TrackWindow(window);

        if (holdingSize)
            return;

        heldSizeConstraints = window.SizeConstraints;
        holdingSize = true;
    }

    // Notices that Window has been pointed at something else, and gives the previous one back before anything is
    // remembered about the new one.
    private void TrackWindow(Window window)
    {
        if (ReferenceEquals(held, window))
            return;

        ReleaseWindow();
        held = window;
    }

    private void ReleasePosition()
    {
        if (!holdingPosition || held == null)
            return;

        held.Position = heldPosition;
        held.PositionCondition = heldPositionCondition;
        holdingPosition = false;
    }

    private void ReleaseSize()
    {
        if (!holdingSize || held == null)
            return;

        held.SizeConstraints = heldSizeConstraints;
        holdingSize = false;
    }

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
