using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a <see cref="NoireWindowMenu"/> looks and reads. Every length is at 100%. See <see cref="NoireUI.Scale"/>.
/// </summary>
public sealed class WindowMenuStyle
{
    private static readonly string[] DefaultLabels =
    [
        "Always on top", "Reduced motion", "Lock position", "Click through", "Lock width", "Lock height",
        "In gpose", "UI hidden", "In cutscene", "Auto hide",
    ];

    private static readonly string?[] DefaultHints =
    [
        null, null, null, null, null, null,
        "Keeps the window open while gpose is active.",
        "Keeps the window open when you hide the game UI.",
        "Keeps the window open during cutscenes.",
        "Keeps the window open whenever the game hides its own UI.",
    ];

    private string[] labels = (string[])DefaultLabels.Clone();
    private string?[] hints = (string?[])DefaultHints.Clone();

    #region Surface

    /// <summary>The menu's width. At 0, wide enough for two columns of its longest switch label.</summary>
    public float Width { get; set; } = 300f;

    /// <summary>The narrowest the menu gets when <see cref="Width"/> is 0.</summary>
    public float MinWidth { get; set; } = 0f;

    /// <summary>The room left and right of the contents.</summary>
    public float PaddingX { get; set; } = 14f;

    /// <summary>The room above the contents.</summary>
    public float PaddingTop { get; set; } = 14f;

    /// <summary>The room below the contents.</summary>
    public float PaddingBottom { get; set; } = 12f;

    /// <summary>The corner radius.</summary>
    public float Rounding { get; set; } = 13f;

    /// <summary>The fill.</summary>
    public Vector4 Background { get; set; } = new(14f / 255f, 17f / 255f, 23f / 255f, 0.98f);

    /// <summary>The border colour.</summary>
    public Vector4 BorderColor { get; set; } = new(1f, 1f, 1f, 0.1f);

    /// <summary>The border thickness. 0 for none.</summary>
    public float BorderSize { get; set; } = 1f;

    /// <summary>Whether the border sits outside the fill, like a CSS ring.</summary>
    public bool BorderOutside { get; set; } = true;

    /// <summary>The drop shadow's colour. When <see langword="null"/>, there is none.</summary>
    public Vector4? ShadowColor { get; set; } = new(0f, 0f, 0f, 0.95f);

    /// <summary>How far the shadow is offset.</summary>
    public Vector2 ShadowOffset { get; set; } = new(0f, 24f);

    /// <summary>How far the shadow spreads out from its edge.</summary>
    public float ShadowBlur { get; set; } = 60f;

    /// <summary>How far the shadow's box is pulled in from the menu's before it spreads.</summary>
    public float ShadowInset { get; set; } = 12f;

    /// <summary>The gap between the button that opened the menu and the menu's top.</summary>
    public float AnchorGap { get; set; } = 8f;

    /// <summary>The closest the menu comes to the edges of the screen.</summary>
    public float ScreenMargin { get; set; } = 8f;

    /// <summary>How long the menu takes to slide in, in seconds. 0 to appear at once.</summary>
    public float OpenSeconds { get; set; } = 0.2f;

    /// <summary>How long the menu takes to fade in, in seconds.</summary>
    public float FadeSeconds { get; set; } = 0.15f;

    /// <summary>How far above its place the menu starts its slide.</summary>
    public float OpenSlide { get; set; } = 4f;

    /// <summary>The curve the slide follows.</summary>
    public UiCubicBezier OpenCurve { get; set; } = new(0.22f, 1f, 0.36f, 1f);

    #endregion

    #region Text

    /// <summary>A multiplier on every text size in the menu.</summary>
    public float TextScale { get; set; } = 1f;

    /// <summary>Draws a run of text in place of the library, for a font of your own.</summary>
    public Action<UiWindowMenuText>? CustomDrawText { get; set; }

    /// <summary>Measures a run of text in place of the library. Pair it with <see cref="CustomDrawText"/>. A note is measured wrapped to its box.</summary>
    public Func<UiWindowMenuText, Vector2>? MeasureText { get; set; }

    #endregion

    #region Headings

    /// <summary>The heading above the two sliders.</summary>
    public string WindowHeading { get; set; } = "WINDOW";

    /// <summary>The heading above the behaviour switches.</summary>
    public string BehaviourHeading { get; set; } = "BEHAVIOUR";

    /// <summary>The heading above the stay-visible switches.</summary>
    public string VisibilityHeading { get; set; } = "STAY VISIBLE";

    /// <summary>The heading text size.</summary>
    public float HeadingSizePx { get; set; } = 10f;

    /// <summary>The heading tracking, in ems.</summary>
    public float HeadingTrackingEm { get; set; } = 0.14f;

    /// <summary>The heading colour.</summary>
    public Vector4 HeadingColor { get; set; } = new(0x6b / 255f, 0x73 / 255f, 0x82 / 255f, 1f);

    /// <summary>The heading's line height, as a multiple of its size.</summary>
    public float HeadingLineHeight { get; set; } = 1.4f;

    /// <summary>How far in from the padding the headings start.</summary>
    public float HeadingInset { get; set; } = 2f;

    /// <summary>The room above the first heading.</summary>
    public float FirstHeadingTop { get; set; } = 2f;

    /// <summary>The room above every other heading.</summary>
    public float HeadingGapAbove { get; set; } = 14f;

    /// <summary>The room below a heading.</summary>
    public float HeadingGapBelow { get; set; } = 9f;

    /// <summary>The room below the first heading. When <see langword="null"/>, <see cref="HeadingGapBelow"/>.</summary>
    public float? FirstHeadingGapBelow { get; set; }

    #endregion

    #region Sliders

    /// <summary>The opacity slider's label.</summary>
    public string OpacityLabel { get; set; } = "Opacity";

    /// <summary>The text size slider's label.</summary>
    public string TextSizeLabel { get; set; } = "Text size";

    /// <summary>The lowest opacity the slider allows.</summary>
    public float OpacityMin { get; set; } = 0.2f;

    /// <summary>The highest opacity the slider allows.</summary>
    public float OpacityMax { get; set; } = 1f;

    /// <summary>The text size multiplier of each step, shown as a percentage.</summary>
    public float[] TextSteps { get; set; } = [0.85f, 0.92f, 1f, 1.1f, 1.2f];

    /// <summary>Names shown for each step. When <see langword="null"/>, percentages.</summary>
    public string[]? TextStepNames { get; set; }

    /// <summary>How many text size steps there are.</summary>
    public int TextStepCount => TextStepNames?.Length ?? TextSteps.Length;

    /// <summary>Draws the opacity row through <see cref="NoireSliders"/> with this style.</summary>
    public SliderStyle? OpacitySlider { get; set; }

    /// <summary>Draws the text size row through <see cref="NoireSliders"/> with this style.</summary>
    public SliderStyle? TextStepSlider { get; set; }

    /// <summary>Paints the built-in slider's track and thumb in place of the library.</summary>
    public Action<UiWindowMenuSliderDraw>? CustomDrawSlider { get; set; }

    /// <summary>The height of a slider row.</summary>
    public float SliderRowHeight { get; set; } = 30f;

    /// <summary>The room between the two sliders.</summary>
    public float SliderGap { get; set; } = 0f;

    /// <summary>How far in from the padding a slider row starts and ends.</summary>
    public float SliderInset { get; set; } = 2f;

    /// <summary>The room between a row's label, slider and value.</summary>
    public float SliderSpacing { get; set; } = 10f;

    /// <summary>The width of a slider's label column.</summary>
    public float SliderLabelWidth { get; set; } = 64f;

    /// <summary>The slider label text size.</summary>
    public float SliderLabelSizePx { get; set; } = 12f;

    /// <summary>The slider label colour.</summary>
    public Vector4 SliderLabelColor { get; set; } = new(0xaa / 255f, 0xb2 / 255f, 0xc0 / 255f, 1f);

    /// <summary>The width of a slider's value column, right aligned.</summary>
    public float SliderValueWidth { get; set; } = 36f;

    /// <summary>The slider value text size.</summary>
    public float SliderValueSizePx { get; set; } = 11f;

    /// <summary>The slider value colour.</summary>
    public Vector4 SliderValueColor { get; set; } = new(0xee / 255f, 0xf1 / 255f, 0xf6 / 255f, 1f);

    /// <summary>The track's thickness.</summary>
    public float TrackHeight { get; set; } = 4f;

    /// <summary>The track's empty part.</summary>
    public Vector4 TrackColor { get; set; } = new(1f, 1f, 1f, 0.1f);

    /// <summary>The track's filled part. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? TrackFillColor { get; set; }

    /// <summary>The thumb's diameter.</summary>
    public float ThumbSize { get; set; } = 14f;

    /// <summary>The thumb's colour.</summary>
    public Vector4 ThumbColor { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>The width of the ring around the thumb. 0 for none.</summary>
    public float ThumbRingWidth { get; set; } = 3f;

    /// <summary>The ring's colour. When <see langword="null"/>, the theme's accent at 35%.</summary>
    public Vector4? ThumbRingColor { get; set; }

    /// <summary>The thumb's shadow. When <see langword="null"/>, there is none.</summary>
    public Vector4? ThumbShadowColor { get; set; } = new(0f, 0f, 0f, 0.6f);

    /// <summary>How far down the thumb's shadow falls.</summary>
    public float ThumbShadowOffsetY { get; set; } = 2f;

    /// <summary>How far the thumb's shadow spreads.</summary>
    public float ThumbShadowBlur { get; set; } = 6f;

    #endregion

    #region Toggles

    /// <summary>Draws a switch that is on through <see cref="NoireButtons"/> with this style. Pair it with <see cref="ToggleOff"/>.</summary>
    public ButtonStyle? ToggleOn { get; set; }

    /// <summary>Draws a switch that is off through <see cref="NoireButtons"/> with this style.</summary>
    public ButtonStyle? ToggleOff { get; set; }

    /// <summary>Paints the built-in switch in place of the library.</summary>
    public Action<UiWindowMenuToggleDraw>? CustomDrawToggle { get; set; }

    /// <summary>The room between the two columns.</summary>
    public float ToggleColumnGap { get; set; } = 5f;

    /// <summary>The room between two rows.</summary>
    public float ToggleRowGap { get; set; } = 5f;

    /// <summary>The room below each grid of switches.</summary>
    public float GridBottomGap { get; set; } = 0f;

    /// <summary>A switch's height. At 0, one line of its label plus <see cref="TogglePaddingY"/> above and below.</summary>
    public float ToggleHeight { get; set; } = 32f;

    /// <summary>The room above and below a label when <see cref="ToggleHeight"/> is 0.</summary>
    public float TogglePaddingY { get; set; } = 7f;

    /// <summary>The room left and right of a switch's contents.</summary>
    public float TogglePaddingX { get; set; } = 10f;

    /// <summary>The room either side of the longest label when <see cref="Width"/> is 0.</summary>
    public float ToggleLabelPadding { get; set; } = 14f;

    /// <summary>A switch's corner radius.</summary>
    public float ToggleRounding { get; set; } = 8f;

    /// <summary>The switch label text size.</summary>
    public float ToggleSizePx { get; set; } = 11.5f;

    /// <summary>A switch label at rest.</summary>
    public Vector4 ToggleText { get; set; } = new(0x8a / 255f, 0x93 / 255f, 0xa3 / 255f, 1f);

    /// <summary>A switch label under the pointer or on.</summary>
    public Vector4 ToggleActiveText { get; set; } = new(0xee / 255f, 0xf1 / 255f, 0xf6 / 255f, 1f);

    /// <summary>A switch's fill when off.</summary>
    public Vector4 ToggleFill { get; set; } = new(1f, 1f, 1f, 0.03f);

    /// <summary>A switch's border when off.</summary>
    public Vector4 ToggleBorder { get; set; } = new(1f, 1f, 1f, 0.06f);

    /// <summary>A switch's fill when on. When <see langword="null"/>, the theme's accent at 12%.</summary>
    public Vector4? ToggleOnFill { get; set; }

    /// <summary>A switch's border when on. When <see langword="null"/>, the theme's accent at 35%.</summary>
    public Vector4? ToggleOnBorder { get; set; }

    /// <summary>The indicator dot's diameter. 0 for none.</summary>
    public float DotSize { get; set; } = 6f;

    /// <summary>The room between the dot and the label.</summary>
    public float DotGap { get; set; } = 8f;

    /// <summary>The dot when off.</summary>
    public Vector4 DotColor { get; set; } = new(1f, 1f, 1f, 0.14f);

    /// <summary>The dot when on. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? DotOnColor { get; set; }

    /// <summary>How far the lit dot's glow spreads. 0 for none.</summary>
    public float DotGlowSpread { get; set; } = 8f;

    /// <summary>How long a switch takes to change its look, in seconds.</summary>
    public float ToggleTransitionSeconds { get; set; } = 0.2f;

    #endregion

    #region Note and hints

    /// <summary>A note under the switches. When <see langword="null"/>, none.</summary>
    public string? Note { get; set; }

    /// <summary>A sentence added to the note while click through is on. When <see langword="null"/>, none.</summary>
    public string? ClickThroughNote { get; set; } = "The window lets clicks through to the game. Its title bar stays clickable.";

    /// <summary>The note text size.</summary>
    public float NoteSizePx { get; set; } = 11f;

    /// <summary>The note colour.</summary>
    public Vector4 NoteColor { get; set; } = new(0x6b / 255f, 0x73 / 255f, 0x82 / 255f, 1f);

    /// <summary>The note's line height, as a multiple of its size.</summary>
    public float NoteLineHeight { get; set; } = 1.45f;

    /// <summary>The room above the note.</summary>
    public float NoteGap { get; set; } = 10f;

    /// <summary>How far in from the padding the note starts and ends.</summary>
    public float NoteInset { get; set; } = 2f;

    /// <summary>The look of the hints shown over the switches.</summary>
    public TooltipStyle? HintStyle { get; set; }

    /// <summary>Shows a switch's hint in place of <see cref="NoireTooltip"/>.</summary>
    public Action<UiWindowMenuHint>? CustomShowHint { get; set; }

    #endregion

    /// <summary>A switch's label.</summary>
    /// <param name="toggle">The switch.</param>
    /// <returns>Its label.</returns>
    public string GetLabel(WindowMenuToggle toggle) => labels[(int)toggle];

    /// <summary>Renames a switch.</summary>
    /// <param name="toggle">The switch.</param>
    /// <param name="label">Its new label.</param>
    public void SetLabel(WindowMenuToggle toggle, string label) => labels[(int)toggle] = label ?? string.Empty;

    /// <summary>A switch's hint, shown under the pointer.</summary>
    /// <param name="toggle">The switch.</param>
    /// <returns>Its hint, or <see langword="null"/> for none.</returns>
    public string? GetHint(WindowMenuToggle toggle) => hints[(int)toggle];

    /// <summary>Sets a switch's hint.</summary>
    /// <param name="toggle">The switch.</param>
    /// <param name="hint">Its hint, or <see langword="null"/> for none.</param>
    public void SetHint(WindowMenuToggle toggle, string? hint) => hints[(int)toggle] = hint;

    /// <summary>Returns a copy.</summary>
    /// <returns>A copy with its own labels and hints.</returns>
    public WindowMenuStyle Clone()
    {
        var copy = (WindowMenuStyle)MemberwiseClone();
        copy.labels = (string[])labels.Clone();
        copy.hints = (string?[])hints.Clone();
        return copy;
    }
}
