using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The look of the toasts drawn by a <see cref="NoireToastArea"/>.<br/>
/// Every color left <see langword="null"/> resolves through <see cref="NoireTheme.Current"/>, so toasts match the rest
/// of the interface without being configured.
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

    /// <summary>The border thickness in pixels.</summary>
    public float BorderSize { get; set; } = 1f;

    /// <summary>The corner radius. When <see langword="null"/>, the theme surface rounding is used.</summary>
    public float? Rounding { get; set; }

    /// <summary>The padding inside a toast.</summary>
    public Vector2 Padding { get; set; } = new(12f, 10f);

    /// <summary>The vertical gap between stacked toasts, in pixels.</summary>
    public float Gap { get; set; } = 8f;

    /// <summary>
    /// The width of the colored stripe down the leading edge of a toast, which is what makes a severity readable
    /// before the text is. Zero removes it.
    /// </summary>
    public float StripeWidth { get; set; } = 3f;

    /// <summary>
    /// Whether a severity icon is drawn beside the message.
    /// </summary>
    public bool ShowIcon { get; set; } = true;

    /// <summary>
    /// How a toast shows the time it has left. See <see cref="ToastTimerMode"/>.
    /// </summary>
    public ToastTimerMode Timer { get; set; } = ToastTimerMode.BottomBar;

    /// <summary>
    /// The thickness of the countdown in pixels, for the bar and outline modes. Zero removes it.
    /// </summary>
    public float TimerThickness { get; set; } = 2f;

    /// <summary>
    /// The countdown color. When <see langword="null"/>, the toast's severity color is used.
    /// </summary>
    public Vector4? TimerColor { get; set; }

    /// <summary>
    /// The opacity of the tint modes, which have to stay faint enough to read the message through.
    /// </summary>
    public float TimerTintAlpha { get; set; } = 0.16f;

    /// <summary>
    /// Whether the countdown shrinks as the time runs out rather than growing.<br/>
    /// Draining is the default because it reads as time left; growing reads as progress towards something, which is
    /// what <see cref="NoireToast.Progress"/> is for.
    /// </summary>
    public bool TimerDrains { get; set; } = true;

    /// <summary>
    /// The filled part of a toast's progress bar. When <see langword="null"/>, a slightly darker form of the toast's
    /// severity color is used.
    /// </summary>
    /// <remarks>
    /// Darker rather than the severity color itself so the bar sits under the message instead of competing with the
    /// stripe and the icon, which are already showing that color at full strength.
    /// </remarks>
    public Vector4? ProgressColor { get; set; }

    /// <summary>
    /// The unfilled track of a toast's progress bar. When <see langword="null"/>, the theme's sunken surface is used.
    /// </summary>
    public Vector4? ProgressTrackColor { get; set; }

    /// <summary>
    /// How far the filled part is darkened from the severity color when <see cref="ProgressColor"/> is not set.
    /// </summary>
    public float ProgressDarken { get; set; } = 0.2f;

    /// <summary>
    /// The height of a toast's progress bar in pixels. When <see langword="null"/>, it is derived from
    /// <see cref="TimerThickness"/>.
    /// </summary>
    public float? ProgressHeight { get; set; }

    /// <summary>
    /// How far a toast slides in from, in pixels, along the axis it enters on.
    /// </summary>
    public float SlideDistance { get; set; } = 24f;

    /// <summary>
    /// How long a toast takes to appear and to leave, in seconds.
    /// </summary>
    public float TransitionDuration { get; set; } = 0.22f;

    /// <summary>
    /// Creates an independent copy, so a variant can be adjusted without touching the original.
    /// </summary>
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
    };
}
