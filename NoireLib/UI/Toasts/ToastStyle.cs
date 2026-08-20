using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The look of the toasts drawn by a <see cref="NoireToastArea"/>. Every color left <see langword="null"/> resolves
/// through <see cref="NoireTheme.Current"/>, and every pixel value is written at 100% and scaled when drawn.
/// See <see cref="NoireUI.Scale"/>.
/// </summary>
public sealed class ToastStyle
{
    /// <summary>The toast background. When <see langword="null"/>, the raised surface color is used.</summary>
    public Vector4? BackgroundColor { get; set; }

    /// <summary>The toast border color. When <see langword="null"/>, the theme border color is used.</summary>
    public Vector4? BorderColor { get; set; }

    /// <summary>The message color. When <see langword="null"/>, the theme text color is used.</summary>
    public Vector4? TextColor { get; set; }

    /// <summary>The title color. When <see langword="null"/>, the severity color is used.</summary>
    public Vector4? TitleColor { get; set; }

    /// <summary>The border thickness at 100%.</summary>
    public float BorderSize { get; set; } = 1f;

    /// <summary>The corner radius at 100%. When <see langword="null"/>, the theme surface rounding is used.</summary>
    public float? Rounding { get; set; }

    /// <summary>The padding inside a toast, at 100%.</summary>
    public Vector2 Padding { get; set; } = new(12f, 10f);

    /// <summary>The vertical gap between stacked toasts, at 100%.</summary>
    public float Gap { get; set; } = 8f;

    /// <summary>The width at 100% of the colored stripe down the leading edge of a toast. Zero removes it.</summary>
    public float StripeWidth { get; set; } = 3f;

    /// <summary>Whether a severity icon is drawn beside the message.</summary>
    public bool ShowIcon { get; set; } = true;

    /// <summary>How a toast shows the time it has left.</summary>
    public ToastTimerMode Timer { get; set; } = ToastTimerMode.BottomBar;

    /// <summary>The thickness of the countdown at 100%, for the bar and outline modes. Zero removes it.</summary>
    public float TimerThickness { get; set; } = 2f;

    /// <summary>The countdown color. When <see langword="null"/>, the toast's severity color is used.</summary>
    public Vector4? TimerColor { get; set; }

    /// <summary>The opacity of the countdown's tint modes.</summary>
    public float TimerTintAlpha { get; set; } = 0.16f;

    /// <summary>Whether the countdown shrinks as the time runs out rather than growing.</summary>
    public bool TimerDrains { get; set; } = true;

    /// <summary>
    /// The filled part of a toast's progress bar. When <see langword="null"/>, a darkened form of the toast's
    /// severity color is used.
    /// </summary>
    public Vector4? ProgressColor { get; set; }

    /// <summary>
    /// The unfilled track of a toast's progress bar. When <see langword="null"/>, the theme's sunken surface is used.
    /// </summary>
    public Vector4? ProgressTrackColor { get; set; }

    /// <summary>
    /// How far the filled part is darkened from the severity color when <see cref="ProgressColor"/> is not set.
    /// </summary>
    public float ProgressDarken { get; set; } = 0.2f;

    /// <summary>The height of a toast's progress bar, at 100%.</summary>
    public float ProgressHeight { get; set; } = 4f;

    /// <summary>How far a toast slides in from, at 100%, along the axis it enters on.</summary>
    public float SlideDistance { get; set; } = 24f;

    /// <summary>How long a toast takes to appear and to leave, in seconds.</summary>
    public float TransitionDuration { get; set; } = 0.22f;

    /// <summary>
    /// Replaces the chrome's own painting (background, stripe, border and countdown), while the body and its layout
    /// stay NoireUI's. The body is still drawn either way, since its measured height drives the stack.
    /// </summary>
    public Action<UiToastDraw>? CustomDraw { get; set; }

    /// <summary>The style of the dismiss cross. When <see langword="null"/>, a muted ghost button.</summary>
    public ButtonStyle? CloseButtonStyle { get; set; }

    /// <summary>
    /// The style of every action button. When <see langword="null"/>, each action draws in its own
    /// <see cref="ToastAction.Tone"/>.
    /// </summary>
    public ButtonStyle? ActionButtonStyle { get; set; }

    // Scaled here and nowhere else, so a value is never scaled twice or left unscaled.

    /// <summary><see cref="BorderSize"/> at the current scale.</summary>
    internal float ScaledBorderSize => NoireUI.Scaled(BorderSize);

    /// <summary><see cref="Padding"/> at the current scale.</summary>
    internal Vector2 ScaledPadding => NoireUI.Scaled(Padding);

    /// <summary><see cref="Gap"/> at the current scale.</summary>
    internal float ScaledGap => NoireUI.Scaled(Gap);

    /// <summary><see cref="StripeWidth"/> at the current scale.</summary>
    internal float ScaledStripeWidth => NoireUI.Scaled(StripeWidth);

    /// <summary><see cref="TimerThickness"/> at the current scale.</summary>
    internal float ScaledTimerThickness => NoireUI.Scaled(TimerThickness);

    /// <summary><see cref="ProgressHeight"/> at the current scale.</summary>
    internal float ScaledProgressHeight => NoireUI.Scaled(ProgressHeight);

    /// <summary><see cref="SlideDistance"/> at the current scale.</summary>
    internal float ScaledSlideDistance => NoireUI.Scaled(SlideDistance);

    /// <summary>Resolves the corner radius a toast is drawn with, falling back to the theme's surface rounding.</summary>
    /// <returns>The scaled corner radius.</returns>
    internal float ResolveRounding()
        => Rounding.HasValue ? NoireUI.Scaled(Rounding.Value) : NoireTheme.Current.ResolveSurfaceRounding();

    /// <summary>Creates an independent copy, so a variant can be adjusted without touching the original.</summary>
    /// <returns>The copy.</returns>
    public ToastStyle Clone() => new()
    {
        BackgroundColor = BackgroundColor,
        BorderColor = BorderColor,
        TextColor = TextColor,
        TitleColor = TitleColor,
        BorderSize = BorderSize,
        Rounding = Rounding,
        Padding = Padding,
        Gap = Gap,
        StripeWidth = StripeWidth,
        ShowIcon = ShowIcon,
        Timer = Timer,
        TimerThickness = TimerThickness,
        TimerColor = TimerColor,
        TimerTintAlpha = TimerTintAlpha,
        TimerDrains = TimerDrains,
        ProgressColor = ProgressColor,
        ProgressTrackColor = ProgressTrackColor,
        ProgressDarken = ProgressDarken,
        ProgressHeight = ProgressHeight,
        SlideDistance = SlideDistance,
        TransitionDuration = TransitionDuration,
        CustomDraw = CustomDraw,
        CloseButtonStyle = CloseButtonStyle,
        ActionButtonStyle = ActionButtonStyle,
    };
}
