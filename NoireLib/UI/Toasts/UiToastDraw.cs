using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything a <see cref="ToastStyle.CustomDraw"/> hook needs to paint a toast's chrome itself: both rectangles, the
/// toast, and the colours NoireUI would have used.
/// </summary>
/// <remarks>
/// The chrome is the background, the severity stripe, the border and the countdown; the body stays the widget's,
/// since its measured height drives the stack's layout. The full-height rectangle and the slot differ while a toast
/// arrives or leaves: the background paints at full height so a leaving toast looks covered rather than squashed,
/// while the countdown uses the slot so its geometry matches the clip rectangle.
/// </remarks>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Toast">The toast being painted, for telling toasts apart under one area-wide style.</param>
/// <param name="Min">The top left of the toast's full-height rectangle.</param>
/// <param name="Max">The bottom right of the toast's full-height rectangle.</param>
/// <param name="SlotMin">The top left of the slot the toast is clipped to.</param>
/// <param name="SlotMax">The bottom right of that slot.</param>
/// <param name="Accent">The severity colour, unscaled, for deriving further colours.</param>
/// <param name="Alpha">The toast's presence, from 0 to 1, already applied to every colour here.</param>
/// <param name="Hovered">Whether the mouse is over the slot.</param>
/// <param name="Rounding">The corner radius the toast would have used, in real pixels.</param>
/// <param name="Background">The background colour, already resolved and presence-scaled.</param>
/// <param name="StripeColor">The severity stripe colour, already presence-scaled.</param>
/// <param name="StripeWidth">The stripe width in real pixels; zero means no stripe.</param>
/// <param name="BorderColor">The border colour, already resolved and presence-scaled.</param>
/// <param name="BorderSize">The border thickness in real pixels; zero means no border.</param>
/// <param name="TimerMode">The countdown shape the style asked for.</param>
/// <param name="TimerFraction">How much of the countdown is drawn, drain direction already applied; zero when there is nothing to count.</param>
/// <param name="TimerColor">The countdown colour, already resolved and presence-scaled.</param>
/// <param name="TimerThickness">The countdown thickness in real pixels, for the bar and outline modes.</param>
/// <param name="TimerTintAlpha">The opacity of the tint modes, before the presence is applied.</param>
public readonly record struct UiToastDraw(
    ImDrawListPtr DrawList,
    NoireToast Toast,
    Vector2 Min,
    Vector2 Max,
    Vector2 SlotMin,
    Vector2 SlotMax,
    Vector4 Accent,
    float Alpha,
    bool Hovered,
    float Rounding,
    Vector4 Background,
    Vector4 StripeColor,
    float StripeWidth,
    Vector4 BorderColor,
    float BorderSize,
    ToastTimerMode TimerMode,
    float TimerFraction,
    Vector4 TimerColor,
    float TimerThickness,
    float TimerTintAlpha)
{
    /// <summary>Draws the toast's own background, at the full-height rectangle.</summary>
    public void DrawBackground()
        => DrawList.AddRectFilled(Min, Max, ColorHelper.Vector4ToUint(Background), Rounding);

    /// <summary>Draws the toast's own severity stripe down the leading edge, or nothing when the style removed it.</summary>
    public void DrawStripe()
    {
        if (StripeWidth > 0f)
        {
            DrawList.AddRectFilled(
                Min, new Vector2(Min.X + StripeWidth, Max.Y), ColorHelper.Vector4ToUint(StripeColor), Rounding);
        }
    }

    /// <summary>Draws the toast's own border, or nothing when the style removed it.</summary>
    public void DrawBorder()
    {
        if (BorderSize > 0f)
        {
            DrawList.AddRect(
                Min, Max, ColorHelper.Vector4ToUint(BorderColor), Rounding, ImDrawFlags.None, BorderSize);
        }
    }

    /// <summary>Draws the toast's own countdown in the shape the style asked for, or nothing when there is nothing to count.</summary>
    public void DrawTimer()
    {
        if (TimerMode == ToastTimerMode.None || TimerFraction <= 0f)
            return;

        var color = ColorHelper.Vector4ToUint(TimerColor);
        var width = SlotMax.X - SlotMin.X;

        switch (TimerMode)
        {
            case ToastTimerMode.BottomBar:
                DrawList.AddRectFilled(
                    new Vector2(SlotMin.X, SlotMax.Y - TimerThickness),
                    new Vector2(SlotMin.X + (width * TimerFraction), SlotMax.Y),
                    color);
                break;

            case ToastTimerMode.TopBar:
                DrawList.AddRectFilled(
                    SlotMin,
                    new Vector2(SlotMin.X + (width * TimerFraction), SlotMin.Y + TimerThickness),
                    color);
                break;

            case ToastTimerMode.Stripe:
                // Placed beside the severity stripe rather than over it; sharing its column would swallow any
                // countdown thinner than the stripe.
                var height = SlotMax.Y - SlotMin.Y;
                var stripeLeft = SlotMin.X + StripeWidth;
                DrawList.AddRectFilled(
                    new Vector2(stripeLeft, SlotMax.Y - (height * TimerFraction)),
                    new Vector2(stripeLeft + TimerThickness, SlotMax.Y),
                    color);
                break;

            case ToastTimerMode.Border:
                UiOutline.TraceClockwise(DrawList, SlotMin, SlotMax, color, TimerThickness, TimerFraction);
                break;

            case ToastTimerMode.TintLeftToRight:
                DrawList.AddRectFilled(
                    SlotMin,
                    new Vector2(SlotMin.X + (width * TimerFraction), SlotMax.Y),
                    ColorHelper.Vector4ToUint(ColorHelper.WithAlpha(TimerColor, TimerTintAlpha * Alpha)),
                    Rounding);
                break;

            case ToastTimerMode.TintRightToLeft:
                DrawList.AddRectFilled(
                    new Vector2(SlotMax.X - (width * TimerFraction), SlotMin.Y),
                    SlotMax,
                    ColorHelper.Vector4ToUint(ColorHelper.WithAlpha(TimerColor, TimerTintAlpha * Alpha)),
                    Rounding);
                break;
        }
    }
}
