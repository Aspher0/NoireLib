using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Describes where a UI element should be placed on screen.<br/>
/// Supports the nine screen anchors (<see cref="UiAnchor"/>), absolute pixel coordinates and screen-ratio coordinates (e.g. 10% left / 10% top),
/// each combined with an optional pixel offset, an optional pivot override and optional clamping to the viewport.<br/>
/// Every pixel value here is written at 100% and scaled when the position is resolved. See <see cref="NoireUI.Scale"/>.
/// </summary>
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
    /// Resolves this position to the top left screen coordinates of an element, using the main ImGui viewport.<br/>
    /// Must be called from the UI thread while an ImGui frame is active.
    /// </summary>
    /// <param name="elementSize">The size of the element to position.</param>
    /// <returns>The top left screen position of the element.</returns>
    public Vector2 Resolve(Vector2 elementSize)
    {
        var viewport = ImGui.GetMainViewport();
        return Resolve(elementSize, viewport.Pos, viewport.Size);
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
        var basePoint = Mode switch
        {
            UiPositionMode.Anchor => viewportPos + GetAnchorRatio(Anchor) * viewportSize,
            UiPositionMode.Ratio => viewportPos + Ratio * viewportSize,
            UiPositionMode.Absolute => viewportPos + NoireUI.Scaled(AbsolutePosition),
            _ => viewportPos,
        };

        var pivot = Pivot ?? (Mode == UiPositionMode.Anchor ? GetAnchorRatio(Anchor) : Vector2.Zero);
        var position = basePoint - pivot * elementSize + NoireUI.Scaled(Offset);

        if (ClampToViewport)
        {
            var max = viewportPos + viewportSize - elementSize;
            position = new Vector2(
                MathF.Max(viewportPos.X, MathF.Min(position.X, max.X)),
                MathF.Max(viewportPos.Y, MathF.Min(position.Y, max.Y)));
        }

        return position;
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
