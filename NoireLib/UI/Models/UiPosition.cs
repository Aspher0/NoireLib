using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Describes where a UI element should be placed on screen.<br/>
/// Supports the nine screen anchors (<see cref="UiAnchor"/>), absolute pixel coordinates, screen-ratio coordinates (e.g. 10% left / 10% top),
/// and native game windows (<see cref="AtAddon"/>), each combined with an optional pixel offset, an optional pivot override and optional clamping to the viewport.<br/>
/// Every pixel value here is written at 100% and scaled when the position is resolved. See <see cref="NoireUI.Scale"/>.
/// </summary>
/// <remarks>
/// A position bound to a game window can fail to resolve, because the window may not be on screen. Use
/// <see cref="TryResolve(Vector2, out Vector2)"/> where that means the element should disappear too, which is what
/// gives "a button that exists only while the Duty Finder is open" for one line of setup.
/// </remarks>
public sealed class UiPosition
{
    /// <summary>
    /// The positioning mode of this position.
    /// </summary>
    public UiPositionMode Mode { get; private set; } = UiPositionMode.Anchor;

    /// <summary>
    /// The screen anchor used when <see cref="Mode"/> is <see cref="UiPositionMode.Anchor"/>.
    /// </summary>
    public UiAnchor Anchor { get; set; } = UiAnchor.TopLeft;

    /// <summary>
    /// The absolute coordinates used when <see cref="Mode"/> is <see cref="UiPositionMode.Absolute"/>, relative to the top left corner of the game window.<br/>
    /// In pixels at 100%: see <see cref="NoireUI.Scale"/>.
    /// </summary>
    public Vector2 AbsolutePosition { get; set; } = Vector2.Zero;

    /// <summary>
    /// The screen ratio used when <see cref="Mode"/> is <see cref="UiPositionMode.Ratio"/>.<br/>
    /// (0, 0) is the top left corner of the screen, (1, 1) the bottom right corner. Example: (0.1, 0.1) = 10% from the left, 10% from the top.
    /// </summary>
    public Vector2 Ratio { get; set; } = Vector2.Zero;

    /// <summary>
    /// The native game window this position follows when <see cref="Mode"/> is <see cref="UiPositionMode.Addon"/>,
    /// for example <c>_PartyList</c>.
    /// </summary>
    public string AddonName { get; set; } = string.Empty;

    /// <summary>
    /// Which point of the game window the element is placed against when <see cref="Mode"/> is
    /// <see cref="UiPositionMode.Addon"/>. <see cref="UiAnchor.TopRight"/> reads as "the top right corner of that
    /// window", exactly as <see cref="Anchor"/> reads as a corner of the screen.
    /// </summary>
    public UiAnchor AddonAnchor { get; set; } = UiAnchor.TopLeft;

    /// <summary>
    /// The exact normalized point inside the game window to place the element against, from (0, 0) (top left) to
    /// (1, 1) (bottom right), overriding <see cref="AddonAnchor"/> when set.<br/>
    /// Nine anchors cover what anyone names out loud; this covers the rest, such as sitting a third of the way down
    /// the right edge.
    /// </summary>
    public Vector2? AddonRatio { get; set; }

    /// <summary>
    /// An additional offset applied after the base position has been resolved. Applies in every mode.<br/>
    /// In pixels at 100%: see <see cref="NoireUI.Scale"/>.
    /// </summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// The normalized point of the element that is placed at the resolved position, from (0, 0) (top left of the element) to (1, 1) (bottom right of the element).<br/>
    /// When <see langword="null"/>, the pivot is automatic: in <see cref="UiPositionMode.Anchor"/> mode it matches the anchor
    /// (e.g. <see cref="UiAnchor.BottomRight"/> pins the bottom right corner of the element), otherwise it is the top left corner.
    /// </summary>
    public Vector2? Pivot { get; set; } = null;

    /// <summary>
    /// Whether the resolved position should be clamped so the element stays fully inside the viewport. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ClampToViewport { get; set; } = true;

    private UiPosition() { }

    /// <summary>
    /// Creates a position pinned to one of the nine screen anchor points, with an optional pixel offset.
    /// </summary>
    /// <param name="anchor">The screen anchor to pin the element to.</param>
    /// <param name="offset">An optional pixel offset applied after anchoring.</param>
    /// <returns>The created <see cref="UiPosition"/>.</returns>
    public static UiPosition AtAnchor(UiAnchor anchor, Vector2? offset = null)
        => new() { Mode = UiPositionMode.Anchor, Anchor = anchor, Offset = offset ?? Vector2.Zero };

    /// <summary>
    /// Creates a position at absolute pixel coordinates, relative to the top left corner of the game window.
    /// </summary>
    /// <param name="x">The horizontal position in pixels.</param>
    /// <param name="y">The vertical position in pixels.</param>
    /// <returns>The created <see cref="UiPosition"/>.</returns>
    public static UiPosition AtAbsolute(float x, float y)
        => AtAbsolute(new Vector2(x, y));

    /// <summary>
    /// Creates a position at absolute pixel coordinates, relative to the top left corner of the game window.
    /// </summary>
    /// <param name="position">The position in pixels.</param>
    /// <returns>The created <see cref="UiPosition"/>.</returns>
    public static UiPosition AtAbsolute(Vector2 position)
        => new() { Mode = UiPositionMode.Absolute, AbsolutePosition = position };

    /// <summary>
    /// Creates a position at a ratio of the screen size, with an optional pixel offset.<br/>
    /// Example: <c>UiPosition.AtRatio(0.1f, 0.1f)</c> places the element at 10% from the left and 10% from the top of the screen.
    /// </summary>
    /// <param name="ratioX">The horizontal ratio, from 0 (left edge) to 1 (right edge).</param>
    /// <param name="ratioY">The vertical ratio, from 0 (top edge) to 1 (bottom edge).</param>
    /// <param name="offset">An optional pixel offset applied after the ratio has been resolved.</param>
    /// <returns>The created <see cref="UiPosition"/>.</returns>
    public static UiPosition AtRatio(float ratioX, float ratioY, Vector2? offset = null)
        => new() { Mode = UiPositionMode.Ratio, Ratio = new Vector2(ratioX, ratioY), Offset = offset ?? Vector2.Zero };

    /// <summary>
    /// Creates a position pinned to a corner of a native game window, following it as the player moves or rescales it.<br/>
    /// Example: <c>UiPosition.AtAddon("_PartyList", UiAnchor.TopRight)</c> puts the top right corner of the element on
    /// the top right corner of the party list.
    /// </summary>
    /// <remarks>
    /// The element sits on the corner rather than beside it, matching <see cref="AtAnchor"/>. Use
    /// <see cref="NextToAddon"/> to place it outside the window instead.
    /// </remarks>
    /// <param name="addonName">The addon name, for example <c>_PartyList</c>.</param>
    /// <param name="anchor">Which point of the game window to pin to.</param>
    /// <param name="offset">An optional pixel offset applied after anchoring.</param>
    /// <returns>The created <see cref="UiPosition"/>.</returns>
    public static UiPosition AtAddon(string addonName, UiAnchor anchor = UiAnchor.TopLeft, Vector2? offset = null)
        => new()
        {
            Mode = UiPositionMode.Addon,
            AddonName = addonName ?? string.Empty,
            AddonAnchor = anchor,
            Offset = offset ?? Vector2.Zero,
            ClampToViewport = false,
        };

    /// <summary>
    /// Creates a position placed alongside a native game window rather than over it, which is what docking a panel to
    /// the party list or a bar under the target frame actually asks for.<br/>
    /// Example: <c>UiPosition.NextToAddon("_PartyList", UiSide.Right)</c>.
    /// </summary>
    /// <param name="addonName">The addon name, for example <c>_PartyList</c>.</param>
    /// <param name="side">Which side of the game window to sit on.</param>
    /// <param name="align">How to line up along that side.</param>
    /// <param name="offset">An optional pixel offset, typically the gap between the two.</param>
    /// <returns>The created <see cref="UiPosition"/>.</returns>
    public static UiPosition NextToAddon(
        string addonName,
        UiSide side,
        UiAlign align = UiAlign.Start,
        Vector2? offset = null)
    {
        var (anchor, pivot) = GetSidePlacement(side, align);

        return new UiPosition
        {
            Mode = UiPositionMode.Addon,
            AddonName = addonName ?? string.Empty,
            AddonRatio = anchor,
            Pivot = pivot,
            Offset = offset ?? Vector2.Zero,
            ClampToViewport = false,
        };
    }

    /// <summary>
    /// Sets the pivot of this position and returns it, for chaining. See <see cref="Pivot"/>.
    /// </summary>
    /// <param name="pivot">The normalized pivot point of the element, or <see langword="null"/> for the automatic pivot.</param>
    /// <returns>This <see cref="UiPosition"/> instance.</returns>
    public UiPosition WithPivot(Vector2? pivot)
    {
        Pivot = pivot;
        return this;
    }

    /// <summary>
    /// Sets the pixel offset of this position and returns it, for chaining. See <see cref="Offset"/>.
    /// </summary>
    /// <param name="offset">The pixel offset to apply.</param>
    /// <returns>This <see cref="UiPosition"/> instance.</returns>
    public UiPosition WithOffset(Vector2 offset)
    {
        Offset = offset;
        return this;
    }

    /// <summary>
    /// Sets whether the resolved position is clamped to the viewport and returns this instance, for chaining. See <see cref="ClampToViewport"/>.
    /// </summary>
    /// <param name="clamp">Whether to clamp the element inside the viewport.</param>
    /// <returns>This <see cref="UiPosition"/> instance.</returns>
    public UiPosition WithClampToViewport(bool clamp)
    {
        ClampToViewport = clamp;
        return this;
    }

    /// <summary>
    /// Sets the game window this position follows and returns this instance, for chaining. See <see cref="AddonName"/>.
    /// </summary>
    /// <param name="addonName">The addon name, for example <c>_PartyList</c>.</param>
    /// <returns>This <see cref="UiPosition"/> instance.</returns>
    public UiPosition WithAddon(string addonName)
    {
        Mode = UiPositionMode.Addon;
        AddonName = addonName ?? string.Empty;
        return this;
    }

    /// <summary>
    /// Resolves this position to the top left screen coordinates of an element, using the main ImGui viewport.<br/>
    /// Must be called from the UI thread while an ImGui frame is active.
    /// </summary>
    /// <remarks>
    /// A position bound to a game window that is not on screen falls back to treating the viewport as the target, so
    /// the element lands where the equivalent screen anchor would put it rather than at an arbitrary point. Call
    /// <see cref="TryResolve(Vector2, out Vector2)"/> instead when the element should be hidden in that case.
    /// </remarks>
    /// <param name="elementSize">The size of the element to position.</param>
    /// <returns>The top left screen position of the element.</returns>
    public Vector2 Resolve(Vector2 elementSize)
    {
        var viewport = ImGui.GetMainViewport();
        return Resolve(elementSize, viewport.Pos, viewport.Size);
    }

    /// <summary>
    /// Resolves this position, reporting whether its target exists at all.
    /// </summary>
    /// <remarks>
    /// Only <see cref="UiPositionMode.Addon"/> can fail: every other mode is always resolvable. Must be called from
    /// the UI thread while an ImGui frame is active.
    /// </remarks>
    /// <param name="elementSize">The size of the element to position.</param>
    /// <param name="position">The top left screen position of the element, or zero when it cannot be resolved.</param>
    /// <returns>True when the position resolved and the element should be drawn.</returns>
    public bool TryResolve(Vector2 elementSize, out Vector2 position)
    {
        var viewport = ImGui.GetMainViewport();
        return TryResolve(elementSize, viewport.Pos, viewport.Size, UiAddon.LiveRects, out position);
    }

    /// <summary>
    /// Resolves this position inside the given viewport, reporting whether its target exists at all.
    /// </summary>
    /// <param name="elementSize">The size of the element to position, in real pixels.</param>
    /// <param name="viewportPos">The top left position of the viewport.</param>
    /// <param name="viewportSize">The size of the viewport.</param>
    /// <param name="position">The top left position of the element, or zero when it cannot be resolved.</param>
    /// <returns>True when the position resolved and the element should be drawn.</returns>
    public bool TryResolve(Vector2 elementSize, Vector2 viewportPos, Vector2 viewportSize, out Vector2 position)
        => TryResolve(elementSize, viewportPos, viewportSize, UiAddon.LiveRects, out position);

    /// <summary>
    /// Resolves this position against a supplied source of game window rectangles.
    /// </summary>
    /// <remarks>
    /// The rectangles are relative to the top left corner of the game window, in real pixels, which is what
    /// <see cref="UiAddon.GetRect"/> returns. Supplying the source rather than reading the game is what makes this the
    /// whole of the positioning logic and leaves nothing untestable behind it.
    /// </remarks>
    /// <param name="elementSize">The size of the element to position, in real pixels.</param>
    /// <param name="viewportPos">The top left position of the viewport.</param>
    /// <param name="viewportSize">The size of the viewport.</param>
    /// <param name="addonRects">Where to look up a game window by name.</param>
    /// <param name="position">The top left position of the element, or zero when it cannot be resolved.</param>
    /// <returns>True when the position resolved and the element should be drawn.</returns>
    public bool TryResolve(
        Vector2 elementSize,
        Vector2 viewportPos,
        Vector2 viewportSize,
        Func<string, UiRect?> addonRects,
        out Vector2 position)
    {
        position = Vector2.Zero;

        var target = new UiRect(viewportPos, viewportSize);
        var targetRatio = GetAnchorRatio(Anchor);

        if (Mode == UiPositionMode.Addon)
        {
            var rect = addonRects?.Invoke(AddonName);

            if (rect == null || rect.Value.IsEmpty)
                return false;

            target = new UiRect(viewportPos + rect.Value.Position, rect.Value.Size);
            targetRatio = AddonRatio ?? GetAnchorRatio(AddonAnchor);
        }

        position = Place(elementSize, target, targetRatio, viewportPos, viewportSize);
        return true;
    }

    /// <summary>
    /// Resolves this position to the top left coordinates of an element inside the given viewport.
    /// </summary>
    /// <remarks>
    /// <see cref="AbsolutePosition"/> and <see cref="Offset"/> are scaled here, which is the only place they are, so an
    /// overlay pinned 20 pixels off a corner stays 20 pixels off it at 100% and clears the same margin at 200%.
    /// <paramref name="elementSize"/> is a measured size and is already at the right scale.
    /// </remarks>
    /// <param name="elementSize">The size of the element to position, in real pixels.</param>
    /// <param name="viewportPos">The top left position of the viewport.</param>
    /// <param name="viewportSize">The size of the viewport.</param>
    /// <returns>The top left position of the element.</returns>
    public Vector2 Resolve(Vector2 elementSize, Vector2 viewportPos, Vector2 viewportSize)
    {
        if (TryResolve(elementSize, viewportPos, viewportSize, UiAddon.LiveRects, out var position))
            return position;

        return Place(
            elementSize,
            new UiRect(viewportPos, viewportSize),
            AddonRatio ?? GetAnchorRatio(AddonAnchor),
            viewportPos,
            viewportSize);
    }

    /// <summary>
    /// Places an element of the given size against a resolved target rectangle.
    /// </summary>
    /// <param name="elementSize">The size of the element, in real pixels.</param>
    /// <param name="target">The rectangle the element is placed against, in screen pixels.</param>
    /// <param name="targetRatio">The normalized point of that rectangle to place it at.</param>
    /// <param name="viewportPos">The top left position of the viewport, for clamping.</param>
    /// <param name="viewportSize">The size of the viewport, for clamping.</param>
    /// <returns>The top left position of the element.</returns>
    private Vector2 Place(
        Vector2 elementSize,
        UiRect target,
        Vector2 targetRatio,
        Vector2 viewportPos,
        Vector2 viewportSize)
    {
        var basePoint = Mode switch
        {
            UiPositionMode.Ratio => viewportPos + Ratio * viewportSize,
            UiPositionMode.Absolute => viewportPos + NoireUI.Scaled(AbsolutePosition),
            _ => target.PointAt(targetRatio),
        };

        var pivot = Pivot ?? (Mode is UiPositionMode.Anchor or UiPositionMode.Addon ? targetRatio : Vector2.Zero);
        var position = basePoint - pivot * elementSize + NoireUI.Scaled(Offset);

        return ClampToViewport
            ? new UiRect(viewportPos, viewportSize).Clamp(position, elementSize)
            : position;
    }

    /// <summary>
    /// Works out the pair of normalized points that places an element on one side of a target: where on the target it
    /// attaches, and which point of the element attaches there.
    /// </summary>
    /// <remarks>
    /// Sitting to the right of something means pinning the element's left edge to the target's right edge, so the two
    /// ratios are mirrors along the placement axis and equal along the other. Keeping that in one place is what lets
    /// docking, edge arrows and attached windows agree with each other.
    /// </remarks>
    /// <param name="side">Which side of the target to sit on.</param>
    /// <param name="align">How to line up along that side.</param>
    /// <returns>The point on the target, and the pivot of the element.</returns>
    public static (Vector2 Target, Vector2 Pivot) GetSidePlacement(UiSide side, UiAlign align = UiAlign.Start)
    {
        var along = align switch
        {
            UiAlign.Center => 0.5f,
            UiAlign.End => 1f,
            _ => 0f,
        };

        return side switch
        {
            UiSide.Left => (new Vector2(0f, along), new Vector2(1f, along)),
            UiSide.Right => (new Vector2(1f, along), new Vector2(0f, along)),
            UiSide.Above => (new Vector2(along, 0f), new Vector2(along, 1f)),
            UiSide.Below => (new Vector2(along, 1f), new Vector2(along, 0f)),
            _ => (new Vector2(along), new Vector2(along)),
        };
    }

    /// <summary>
    /// Gets the normalized screen point of an anchor, from (0, 0) (top left) to (1, 1) (bottom right).
    /// </summary>
    /// <param name="anchor">The anchor to convert.</param>
    /// <returns>The normalized position of the anchor.</returns>
    public static Vector2 GetAnchorRatio(UiAnchor anchor) => anchor switch
    {
        UiAnchor.TopLeft => new Vector2(0f, 0f),
        UiAnchor.TopCenter => new Vector2(0.5f, 0f),
        UiAnchor.TopRight => new Vector2(1f, 0f),
        UiAnchor.MiddleLeft => new Vector2(0f, 0.5f),
        UiAnchor.MiddleCenter => new Vector2(0.5f, 0.5f),
        UiAnchor.MiddleRight => new Vector2(1f, 0.5f),
        UiAnchor.BottomLeft => new Vector2(0f, 1f),
        UiAnchor.BottomCenter => new Vector2(0.5f, 1f),
        UiAnchor.BottomRight => new Vector2(1f, 1f),
        _ => Vector2.Zero,
    };
}
