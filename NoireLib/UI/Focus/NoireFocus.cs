using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Marks the control holding keyboard focus, so the user can see where typing and the arrow keys will go.
/// </summary>
[NoireFacade]
public static class NoireFocus
{
    private static uint focusedItem;
    private static float arrivedAt;
    private static int lastMarkedFrame = int.MinValue;

    /// <summary>
    /// Whether the focus mark is drawn at all.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// How the mark looks, everywhere that does not pass a style of its own.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when set to <see langword="null"/>.</exception>
    public static FocusStyle Style
    {
        get => style;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            style = value;
        }
    }

    private static FocusStyle style = new();

    /// <summary>
    /// Whether the widget that was just submitted holds keyboard focus.
    /// </summary>
    /// <returns>True when it does.</returns>
    public static bool IsLastFocused() => UiDraw.Available && ImGui.IsItemFocused();

    /// <summary>
    /// Draws the focus mark on the widget that was just submitted, if it has focus.
    /// </summary>
    /// <param name="style">How it looks. When <see langword="null"/>, <see cref="Style"/>.</param>
    public static void OnLast(FocusStyle? style = null)
    {
        if (!Enabled || !UiDraw.Available || !ImGui.IsItemFocused())
            return;

        Draw(UiRect.FromBounds(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()), ImGuiP.GetItemID(), style ?? Style);
    }

    /// <summary>
    /// Draws the focus mark on a rectangle.
    /// </summary>
    /// <param name="target">The control being marked, in screen pixels.</param>
    /// <param name="focused">Whether it holds focus.</param>
    /// <param name="id">A value identifying the control, the same on every frame that control is marked.</param>
    /// <param name="style">How it looks. When <see langword="null"/>, <see cref="Style"/>.</param>
    public static void On(UiRect target, bool focused, uint id, FocusStyle? style = null)
    {
        if (!Enabled || !focused || !UiDraw.Available || target.IsEmpty)
            return;

        Draw(target, id, style ?? Style);
    }

    /// <summary>
    /// Draws the focus mark on a rectangle, identifying the control by where it is.
    /// </summary>
    /// <remarks>The control's position stands in for an ImGui id, replaying the arrival if a marked control moves.</remarks>
    /// <param name="target">The control being marked, in screen pixels.</param>
    /// <param name="focused">Whether it holds focus.</param>
    /// <param name="style">How it looks. When <see langword="null"/>, <see cref="Style"/>.</param>
    public static void On(UiRect target, bool focused, FocusStyle? style = null)
    {
        var id = unchecked((uint)HashCode.Combine(target.Position.X, target.Position.Y));

        On(target, focused, id, style);
    }

    // How far through its arrival the mark is, from 0 the moment focus lands to 1 once it has settled, and held at 1
    // under NoireUI.ReducedMotion.
    private static float Arrival(uint id, FocusStyle style)
    {
        var frame = NoireUI.FrameCount;

        // Two ways for this to be an arrival: focus moving to a different control, or focus coming back to the one
        // it left. Nothing tells this class that focus was lost, since a control without focus simply stops
        // calling, so a gap in the frames marked is the only evidence. Without this check, clicking away and back
        // would reuse the first visit's timestamp, read the arrival as already finished, and place the mark
        // instantly for the rest of the session.
        if (id != focusedItem || frame > lastMarkedFrame + 1)
        {
            focusedItem = id;
            arrivedAt = NoireUI.Time;
        }

        lastMarkedFrame = frame;

        if (NoireUI.ReducedMotion || style.ArrivalSeconds <= 0f)
            return 1f;

        return Math.Clamp((NoireUI.Time - arrivedAt) / style.ArrivalSeconds, 0f, 1f);
    }

    private static void Draw(UiRect target, uint id, FocusStyle style)
    {
        var eased = UiEasing.OutCubic.Apply(Arrival(id, style));

        // The mark starts further out and settles in, reading as landing on the control rather than appearing on
        // top of it. The fade follows the same curve, so a mark still travelling is not yet at full strength.
        var spread = NoireUI.Scaled(style.Spread + (style.ArrivalSpread * (1f - eased)));
        var color = ColorHelper.ScaleAlpha(style.ResolveColor(), eased);

        var min = target.Position - new Vector2(spread);
        var max = target.Max + new Vector2(spread);

        if (max.X <= min.X || max.Y <= min.Y)
            return;

        // The window's own list, because this establishes a redirect: reading NoireShapes.DrawList here would read back
        // a redirect already in force and make the call a no-op.
        using var draw = UiDraw.BeginWindow();

        if (draw.List.IsNull)
            return;

        var args = new UiFocusDraw(draw.List, min, max, target, color, eased, style);

        if (style.CustomDraw is { } custom)
        {
            custom(args);
            return;
        }

        NoireShapes.On(draw.List, args, static state => Paint(state));
    }

    // Paints the shape a style asks for, exposed publicly through UiFocusDraw.DrawShape so a custom hook can add to
    // the shipped look rather than having to reproduce it.
    internal static void Paint(UiFocusDraw args)
    {
        var style = args.Style;
        var size = args.Size;

        switch (style.Shape)
        {
            case FocusShape.None:
                break;

            case FocusShape.Corners:
                NoireShapes.CornerTicks(
                    args.Min, args.Max, args.Color, style.ResolveArmLength(size), style.ScaledThickness, style.Corners);
                break;

            case FocusShape.Brackets:
                NoireShapes.Brackets(
                    args.Min, args.Max, args.Color, style.ResolveArmLength(size), style.ScaledThickness);
                break;

            case FocusShape.Underline:
                var bar = style.ScaledUnderlineThickness;

                NoireShapes.Rect(
                    new Vector2(args.Min.X, args.Max.Y - bar), new Vector2(args.Max.X, args.Max.Y), args.Color);
                break;

            default:
                NoireShapes.RectOutline(
                    args.Min,
                    args.Max,
                    args.Color,
                    style.ScaledThickness,
                    style.CornerShape,
                    style.ResolveCornerSize(),
                    style.Corners);
                break;
        }
    }
}
