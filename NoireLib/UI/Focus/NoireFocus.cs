using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Marks the control holding keyboard focus, so the user can see where typing and the arrow keys will go.
/// </summary>
/// <remarks>
/// Every widget NoireUI ships draws this itself, so a plugin gets focus indication by using the widgets and setting
/// nothing. <see cref="Style"/> changes how it looks everywhere at once and <see cref="Enabled"/> turns it off;
/// <see cref="OnLast(FocusStyle)"/> is there for a control the library does not provide.<br/>
/// The mark is deliberately hard edged, and that is the whole design. Hover, selection and emphasis are drawn with
/// soft, glowing or tinted marks, and a focus mark that differed from those only in brightness would be read as "this
/// one is selected harder": focus and selection have to differ in kind, not in degree. Focus is also singular,
/// transient and moves on every keystroke, where selection is plural, persistent and moves rarely, which is the other
/// reason the sharp mark belongs to focus and the soft one to selection.
/// </remarks>
/// <example>
/// <code>
/// NoireFocus.Style = new FocusStyle { Shape = FocusShape.Corners };   // everywhere, once
///
/// ImGui.InputText("##notes", ref notes, 256);
/// NoireFocus.OnLast();                                                // a control the library does not provide
/// </code>
/// </example>
[NoireFacade]
public static class NoireFocus
{
    /// <summary>
    /// Where the mark currently is, and when it arrived there.
    /// </summary>
    /// <remarks>
    /// One slot rather than a keyed store, because exactly one control holds focus at a time. That is what makes the
    /// arrival animation cost nothing: there is no id to compose, nothing to look up and nothing to prune.
    /// </remarks>
    private static uint focusedItem;
    private static float arrivedAt;
    private static int lastMarkedFrame = int.MinValue;

    /// <summary>
    /// Whether the focus mark is drawn at all. On by default.
    /// </summary>
    /// <remarks>
    /// Turning it off is a deliberate accessibility loss: without it there is nothing on screen saying where the
    /// keyboard is pointed, and a user navigating without a mouse has to guess.
    /// </remarks>
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
    /// <remarks>
    /// Nothing is drawn when the widget does not have focus, so this can be called unconditionally. Nothing is
    /// submitted to ImGui either: like a badge, the mark is painted over the layout rather than added to it, so it
    /// never moves what is around it.
    /// </remarks>
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
    /// <param name="focused">Whether it holds focus, so this can be called unconditionally.</param>
    /// <param name="id">
    /// A value identifying the control, used only to notice that focus has moved so the arrival can restart. Pass the
    /// same value on every frame the same control is marked.
    /// </param>
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
    /// <remarks>
    /// The overload to reach for when there is no ImGui id to hand: the control's position stands in for one. That is
    /// enough for the arrival to restart when focus moves between controls, and it costs an arrival replay in the one
    /// case a marked control changes position while keeping focus, such as being scrolled.
    /// </remarks>
    /// <param name="target">The control being marked, in screen pixels.</param>
    /// <param name="focused">Whether it holds focus, so this can be called unconditionally.</param>
    /// <param name="style">How it looks. When <see langword="null"/>, <see cref="Style"/>.</param>
    public static void On(UiRect target, bool focused, FocusStyle? style = null)
    {
        var id = unchecked((uint)HashCode.Combine(target.Position.X, target.Position.Y));

        On(target, focused, id, style);
    }

    /// <summary>
    /// How far through its arrival the mark is, from 0 the moment focus lands to 1 once it has settled.
    /// </summary>
    /// <remarks>
    /// Restarted by focus moving to a different control rather than by a timer, and held at 1 under
    /// <see cref="NoireUI.ReducedMotion"/> so the mark is drawn in place and at full strength rather than not at all.
    /// </remarks>
    private static float Arrival(uint id, FocusStyle style)
    {
        var frame = NoireUI.FrameCount;

        // Two ways for this to be an arrival, and only the first is obvious. Focus moving to a different control is
        // one. The other is focus coming back to the control it left: nothing tells this class that focus was lost,
        // because a control without focus simply stops calling, so the only evidence is a gap in the frames it was
        // marked on. Without that test, clicking away and back found the timestamp from the first visit still sitting
        // there, read the arrival as long finished, and placed the mark instantly for the rest of the session.
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

    /// <summary>Works out where the mark belongs this frame and hands it to the style's painter.</summary>
    private static void Draw(UiRect target, uint id, FocusStyle style)
    {
        var eased = UiEasing.OutCubic.Apply(Arrival(id, style));

        // The mark starts further out and settles in, which is what reads as landing on the control rather than
        // appearing on top of it. The fade is on the same curve so a mark that is still travelling is not yet at full
        // strength, and neither half is convincing on its own.
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

    /// <summary>
    /// Paints the shape a style asks for. Public through <see cref="UiFocusDraw.DrawShape"/> so a custom hook can add
    /// to the shipped look rather than having to reproduce it.
    /// </summary>
    /// <param name="args">Where the mark belongs and what it is drawn with.</param>
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
