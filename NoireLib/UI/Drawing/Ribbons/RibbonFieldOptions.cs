using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>Everything a <see cref="NoireRibbonField"/> computes with. Lengths are logical pixels at 100%. Read each frame.</summary>
public sealed class RibbonFieldOptions
{
    /// <summary>The ribbons, drawn in order.</summary>
    public IReadOnlyList<Ribbon> Ribbons { get; set; } = DefaultRibbons;

    /// <summary>A set of five ribbons in violet, ice and blue.</summary>
    public static IReadOnlyList<Ribbon> DefaultRibbons { get; } =
    [
        new(0.18f, 0.06f, 5f, 0.35f, 0f, 0.03f, new Vector3(158f, 140f, 255f) / 255f, 0.16f),
        new(0.36f, 0.08f, 4f, 0.28f, 2f, 0.05f, new Vector3(191f, 211f, 236f) / 255f, 0.15f),
        new(0.55f, 0.07f, 6f, 0.22f, 4f, 0.035f, new Vector3(120f, 170f, 255f) / 255f, 0.16f),
        new(0.72f, 0.06f, 3.5f, 0.3f, 1f, 0.045f, new Vector3(191f, 211f, 236f) / 255f, 0.12f),
        new(0.88f, 0.05f, 5.5f, 0.25f, 3f, 0.03f, new Vector3(158f, 140f, 255f) / 255f, 0.13f),
    ];

    #region Background and vignette

    /// <summary>The background color at the top of the field.</summary>
    public Vector4 BackgroundTop { get; set; } = new(7f / 255f, 11f / 255f, 22f / 255f, 1f);

    /// <summary>The background color at the bottom of the field.</summary>
    public Vector4 BackgroundBottom { get; set; } = new(4f / 255f, 6f / 255f, 12f / 255f, 1f);

    /// <summary>The height of the vignette's inner circle centre, as a fraction of the field height.</summary>
    public float VignetteInnerCenterY { get; set; } = 0.4f;

    /// <summary>The height of the vignette's outer circle centre, as a fraction of the field height.</summary>
    public float VignetteOuterCenterY { get; set; } = 0.5f;

    /// <summary>The vignette's clear radius, as a fraction of the field's smaller side.</summary>
    public float VignetteInnerRadius { get; set; } = 0.25f;

    /// <summary>The radius the vignette reaches full strength at, as a fraction of the field's larger side.</summary>
    public float VignetteOuterRadius { get; set; } = 0.75f;

    /// <summary>The vignette's black opacity at full strength. 0 draws none.</summary>
    public float VignetteAlpha { get; set; } = 0.6f;

    /// <summary>The largest cell the vignette is shaded over, in real pixels.</summary>
    public float VignetteCellSize { get; set; } = 20f;

    #endregion

    #region Ribbon shape

    /// <summary>How many segments each ribbon is sampled into across the sampled span.</summary>
    public int Samples { get; set; } = 60;

    /// <summary>
    /// How far, in real pixels, a ribbon edge may lag before the ribbons are laid out again. The last layout replays until
    /// then. 0 lays them out every frame.
    /// </summary>
    public float MaxDriftPixels { get; set; }

    /// <summary>How far the sampled span reaches past each side of the field.</summary>
    public float Overscan { get; set; } = 80f;

    /// <summary>The multiplier on each ribbon's frequency for the main wave.</summary>
    public float PrimaryFrequencyScale { get; set; } = 1f;

    /// <summary>The multiplier on each ribbon's frequency for the secondary wave.</summary>
    public float SecondaryFrequencyScale { get; set; } = 2.3f;

    /// <summary>The secondary wave's height relative to the main wave.</summary>
    public float SecondaryAmplitude { get; set; } = 0.35f;

    /// <summary>The secondary wave's speed relative to the ribbon's, travelling the other way.</summary>
    public float SecondarySpeed { get; set; } = 0.7f;

    /// <summary>How many radians the thickness modulation turns across the sampled span.</summary>
    public float ThicknessFrequency { get; set; } = 3.1f;

    /// <summary>How fast the thickness modulation travels, in radians per second.</summary>
    public float ThicknessSpeed { get; set; } = 0.4f;

    /// <summary>The thinnest part of the modulation's centre, as a fraction of <see cref="Ribbon.Width"/>.</summary>
    public float ThicknessBase { get; set; } = 0.55f;

    /// <summary>How far the modulation swings around <see cref="ThicknessBase"/>.</summary>
    public float ThicknessSwing { get; set; } = 0.45f;

    #endregion

    #region Fill and stroke

    /// <summary>The fill opacity at both ends of the field, relative to <see cref="Ribbon.Alpha"/>.</summary>
    public float EdgeAlpha { get; set; }

    /// <summary>Where across the field the fill reaches full opacity, from 0 to 1.</summary>
    public float PeakStop { get; set; } = 0.3f;

    /// <summary>Where across the field the fill starts fading to <see cref="EdgeAlpha"/>, from 0 to 1.</summary>
    public float FadeStop { get; set; } = 0.7f;

    /// <summary>The fill opacity at <see cref="FadeStop"/>, relative to <see cref="Ribbon.Alpha"/>.</summary>
    public float FadeAlpha { get; set; } = 0.8f;

    /// <summary>The centre line's opacity relative to <see cref="Ribbon.Alpha"/>. 0 draws none.</summary>
    public float StrokeAlpha { get; set; } = 1.6f;

    /// <summary>The centre line's width.</summary>
    public float StrokeWidth { get; set; } = 1f;

    #endregion

    #region Pointer lean

    /// <summary>Where the smoothed pointer is kept. See <see cref="RibbonLeanSpace"/>.</summary>
    public RibbonLeanSpace LeanSpace { get; set; } = RibbonLeanSpace.Field;

    /// <summary>Where the ribbons lean toward with no pointer over the field, as a fraction of the field.</summary>
    public Vector2 RestPoint { get; set; } = new(0.5f, 0.45f);

    /// <summary>How narrow the lean is: the Gaussian's exponent multiplier.</summary>
    public float LeanSharpness { get; set; } = 9f;

    /// <summary>How far toward the pointer a ribbon moves at the lean's centre, from 0 to 1.</summary>
    public float LeanStrength { get; set; } = 0.28f;

    /// <summary>The fraction of the way the smoothed pointer moves toward the real one per reference frame.</summary>
    public float PointerSmoothing { get; set; } = 0.03f;

    /// <summary>The fraction of the way the lean color moves toward its target per reference frame.</summary>
    public float ColorSmoothing { get; set; } = 0.05f;

    /// <summary>The frame rate the two smoothing fractions are expressed at. Other frame rates converge equally fast.</summary>
    public float ReferenceFrameRate { get; set; } = 60f;

    /// <summary>The color odd ribbons blend half toward while nothing asks for another, each channel from 0 to 1.</summary>
    public Vector3 RestLeanColor { get; set; } = new Vector3(191f, 211f, 236f) / 255f;

    #endregion

    #region Waves

    /// <summary>How fast a wave's front travels.</summary>
    public float WaveSpeed { get; set; } = 900f;

    /// <summary>How wide one wave's crest is.</summary>
    public float WaveWidth { get; set; } = 260f;

    /// <summary>How far a wave displaces a ribbon at its crest.</summary>
    public float WaveAmplitude { get; set; } = 28f;

    /// <summary>How long a wave lasts, in seconds, fading linearly.</summary>
    public float WaveLife { get; set; } = 1.4f;

    #endregion

    /// <summary>The longest step time may advance by in one frame, in seconds.</summary>
    public float MaxTimeStep { get; set; } = 0.05f;

    /// <summary>Creates an independent copy.</summary>
    /// <returns>The copy.</returns>
    public RibbonFieldOptions Clone() => (RibbonFieldOptions)MemberwiseClone();
}
