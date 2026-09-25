using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A gradient, and its animations, applied to everything drawn inside its scope. Immutable: build it once and keep it,
/// since every change returns a new one. Shapes drawn straight through ImGui only take it at their corners.
/// </summary>
public sealed partial class NoireGradient
{
    private const int TableSize = 1024;

    private GradientStop[] stops;
    private float[] starts = [];
    private float[] ends = [];
    private Vector4[] table = [];

    private GradientColorSpace space = GradientColorSpace.Srgb;
    private GradientEasing easing = GradientEasing.Linear;
    private int steps;
    private GradientRepeat repeat = GradientRepeat.None;
    private float repeatCount = 1f;
    private bool reversed;
    private float offset;

    private GradientShape shape = GradientShape.Linear;
    private float angle;
    private Vector2 center = new(0.5f, 0.5f);
    private Vector2 radius = Vector2.One;
    private bool circular;
    private float noiseScale = 48f;
    private int seed;
    private float glyphStep;
    private Func<Vector2, float>? custom;

    private EffectArea area = EffectArea.Drawn;
    private Vector2 rectMin;
    private Vector2 rectMax;

    private GradientBlend blend = GradientBlend.Replace;
    private float strength = 1f;
    private EffectTarget target = EffectTarget.All;
    private float tessellation = 6f;

    private float scrollSpeed;
    private float rotationSpeed;
    private float hueSpeed;

    private bool hasPulse;
    private Vector4? pulseColor;
    private float pulseAlpha = 1f;
    private float pulseBrightness;
    private float pulseSpeed = 1f;
    private EffectWave pulseWave = EffectWave.Sine;

    private float breatheSpeed;
    private float breatheDepth;

    private bool hasShimmer;
    private Vector4 shimmerColor;
    private float shimmerWidth;
    private float shimmerSpeed;
    private float shimmerAngle;
    private float shimmerPause;

    private float waveAmplitude;
    private float waveFrequency;
    private float waveSpeed;

    private bool hasSparkle;
    private Vector4 sparkleColor;
    private float sparkleDensity;
    private float sparkleSpeed;

    private float blinkRate;
    private float blinkMinAlpha;

    private EffectTrigger trigger = EffectTrigger.Always;
    private float triggerDuration;
    private bool ignoreReducedMotion;

    private NoireGradient(GradientStop[] stops)
    {
        this.stops = stops;
        RebuildTable();
    }

    #region State

    /// <summary>The colors, in order, with their positions as given.</summary>
    public IReadOnlyList<GradientStop> Stops => stops;

    /// <summary>How a point of the drawing is placed along the gradient.</summary>
    public GradientShape Shape => shape;

    /// <summary>The area the gradient is stretched across.</summary>
    public EffectArea Area => area;

    internal float CellSize => tessellation;

    /// <summary>Which part of the drawing the gradient recolors.</summary>
    public EffectTarget Target => target;

    /// <summary>Whether anything about the gradient moves over time.</summary>
    public bool IsAnimated => scrollSpeed != 0f || rotationSpeed != 0f || hueSpeed != 0f || hasPulse || breatheSpeed > 0f
        || hasShimmer || waveSpeed != 0f || hasSparkle || blinkRate > 0f;

    // Anything beyond two colors changing evenly along a line needs more vertices than the outline.
    internal bool NeedsTessellation
        => shape is not GradientShape.Linear || stops.Length > 2 || steps > 1 || repeat != GradientRepeat.None || repeatCount != 1f
        || space is not (GradientColorSpace.Srgb or GradientColorSpace.LinearRgb) || HasShapedTransitions()
        || scrollSpeed != 0f || hueSpeed != 0f || hasShimmer || waveAmplitude != 0f || hasSparkle || custom != null;

    #endregion

    #region Building

    /// <summary>A gradient through the given colors, left to right.</summary>
    /// <param name="colors">The colors, in order. A plain <see cref="Vector4"/> is spread evenly with its neighbours.</param>
    /// <returns>The gradient.</returns>
    /// <exception cref="ArgumentException">Thrown when no color is given.</exception>
    public static NoireGradient Of(params GradientStop[] colors) => new(Checked(colors));

    /// <summary>A gradient along a straight line.</summary>
    /// <param name="direction">Which way the colors run.</param>
    /// <param name="colors">The colors, in order.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient Linear(GradientDirection direction, params GradientStop[] colors)
        => new(Checked(colors)) { angle = AngleOf(direction) };

    /// <summary>A gradient along a straight line at any angle.</summary>
    /// <param name="degrees">The direction the colors run, clockwise from left to right: 90 runs top to bottom.</param>
    /// <param name="colors">The colors, in order.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient Linear(float degrees, params GradientStop[] colors)
        => new(Checked(colors)) { angle = degrees };

    /// <summary>A gradient mirrored around the centre: the first color in the middle, the last on both sides.</summary>
    /// <param name="direction">The axis the colors run along.</param>
    /// <param name="colors">The colors, from the middle outwards.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient Reflected(GradientDirection direction, params GradientStop[] colors)
        => new(Checked(colors)) { shape = GradientShape.Reflected, angle = AngleOf(direction) };

    /// <summary>A gradient from the centre outwards, across an ellipse that fills the area.</summary>
    /// <param name="colors">The colors, from the centre outwards.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient Radial(params GradientStop[] colors)
        => new(Checked(colors)) { shape = GradientShape.Radial };

    /// <summary>A gradient around the centre, one full turn from the first color to the last.</summary>
    /// <param name="colors">The colors, clockwise from the right.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient Conic(params GradientStop[] colors)
        => new(Checked(colors)) { shape = GradientShape.Conic };

    /// <summary>A gradient in diamonds around the centre.</summary>
    /// <param name="colors">The colors, from the centre outwards.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient Diamond(params GradientStop[] colors)
        => new(Checked(colors)) { shape = GradientShape.Diamond };

    /// <summary>A gradient in nested rectangles around the centre.</summary>
    /// <param name="colors">The colors, from the centre outwards.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient Square(params GradientStop[] colors)
        => new(Checked(colors)) { shape = GradientShape.Square };

    /// <summary>A gradient laid over a smooth random pattern, like marble or clouds.</summary>
    /// <param name="scale">The size of the pattern's features, in pixels.</param>
    /// <param name="colors">The colors the pattern moves between.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient Noise(float scale, params GradientStop[] colors)
        => new(Checked(colors)) { shape = GradientShape.Noise, noiseScale = MathF.Max(1f, scale) };

    /// <summary>
    /// A gradient across the characters of the text by their rank: the first character takes the first color and the
    /// last character the last one, whatever their positions.
    /// </summary>
    /// <param name="colors">The colors, from the first character to the last.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient PerGlyph(params GradientStop[] colors)
        => new(Checked(colors)) { shape = GradientShape.PerGlyph };

    /// <summary>A gradient across the lines of the text: one color per line, from the first line to the last.</summary>
    /// <param name="colors">The colors, from the first line to the last.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient PerLine(params GradientStop[] colors)
        => new(Checked(colors)) { shape = GradientShape.PerLine };

    /// <summary>A gradient placed by a function of the point.</summary>
    /// <param name="position">Maps a point, (0,0) top left to (1,1) bottom right, to its place along the gradient. Runs per vertex per frame.</param>
    /// <param name="colors">The colors, in order.</param>
    /// <returns>The gradient.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="position"/> is null.</exception>
    public static NoireGradient Custom(Func<Vector2, float> position, params GradientStop[] colors)
    {
        ArgumentNullException.ThrowIfNull(position);
        return new(Checked(colors)) { shape = GradientShape.Custom, custom = position };
    }

    /// <summary>
    /// A single color, as a base for the animations that do not need a gradient: pulsing, shimmering, sparkling or blinking.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns>The gradient.</returns>
    public static NoireGradient Solid(Vector4 color) => new([color]);

    private static GradientStop[] Checked(GradientStop[]? colors)
    {
        if (colors == null || colors.Length == 0)
            throw new ArgumentException("A gradient needs at least one color.", nameof(colors));

        return (GradientStop[])colors.Clone();
    }

    private static float AngleOf(GradientDirection direction) => direction switch
    {
        GradientDirection.RightToLeft => 180f,
        GradientDirection.TopToBottom => 90f,
        GradientDirection.BottomToTop => 270f,
        GradientDirection.TopLeftToBottomRight => 45f,
        GradientDirection.BottomRightToTopLeft => 225f,
        GradientDirection.TopRightToBottomLeft => 135f,
        GradientDirection.BottomLeftToTopRight => 315f,
        _ => 0f,
    };

    private NoireGradient Copy(Action<NoireGradient> change, bool colorsChanged = false)
    {
        var copy = (NoireGradient)MemberwiseClone();
        change(copy);

        if (colorsChanged)
            copy.RebuildTable();

        return copy;
    }

    #endregion

    #region Colors

    /// <summary>Returns this gradient with other colors, keeping everything else.</summary>
    /// <param name="colors">The colors, in order.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithColors(params GradientStop[] colors)
    {
        var checkedColors = Checked(colors);
        return Copy(g => g.stops = checkedColors, true);
    }

    /// <summary>Returns this gradient mixing its colors in another space.</summary>
    /// <param name="colorSpace">The space neighbouring colors are mixed in.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithSpace(GradientColorSpace colorSpace) => Copy(g => g.space = colorSpace, true);

    /// <summary>Returns this gradient moving between its colors at another pace. A stop's own easing still wins.</summary>
    /// <param name="value">How every transition moves.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithEasing(GradientEasing value) => Copy(g => g.easing = value, true);

    /// <summary>Returns this gradient cut into flat bands, for a posterized look.</summary>
    /// <param name="count">How many bands, at least 2. 0 turns the bands off.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithSteps(int count) => Copy(g => g.steps = count < 2 ? 0 : count);

    /// <summary>Returns this gradient repeated across the area.</summary>
    /// <param name="mode">What shows past the last color.</param>
    /// <param name="count">How many times the gradient fits in the area.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithRepeat(GradientRepeat mode, float count = 1f) => Copy(g =>
    {
        g.repeat = mode;
        g.repeatCount = MathF.Max(0.0001f, count);
    });

    /// <summary>Returns this gradient running the other way, last color first.</summary>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Reversed() => Copy(g => g.reversed = !g.reversed);

    /// <summary>Returns this gradient shifted along itself.</summary>
    /// <param name="value">How far to shift, as a fraction of the gradient.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithOffset(float value) => Copy(g => g.offset = value);

    #endregion

    #region Geometry

    /// <summary>Returns this gradient laid out in another shape.</summary>
    /// <param name="value">How a point is placed along the gradient. <see cref="GradientShape.Custom"/> needs <see cref="Custom"/>.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithShape(GradientShape value) => Copy(g => g.shape = value == GradientShape.Custom && g.custom == null ? g.shape : value);

    /// <summary>Returns this gradient running in another direction.</summary>
    /// <param name="direction">Which way the colors run.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithDirection(GradientDirection direction) => Copy(g => g.angle = AngleOf(direction));

    /// <summary>Returns this gradient at another angle: the direction of a line, or where a conic gradient starts.</summary>
    /// <param name="degrees">Clockwise from left to right: 90 runs top to bottom.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithAngle(float degrees) => Copy(g => g.angle = degrees);

    /// <summary>Returns this gradient around another centre.</summary>
    /// <param name="x">The centre across the area, from 0 (left) to 1 (right).</param>
    /// <param name="y">The centre down the area, from 0 (top) to 1 (bottom).</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithCenter(float x, float y) => Copy(g => g.center = new Vector2(x, y));

    /// <summary>Returns this gradient reaching its last color at another distance from the centre.</summary>
    /// <param name="x">The reach across, as a fraction of half the area's width.</param>
    /// <param name="y">The reach down, as a fraction of half the area's height.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithRadius(float x, float y) => Copy(g => g.radius = new Vector2(MathF.Max(0.0001f, x), MathF.Max(0.0001f, y)));

    /// <summary>Returns this gradient reaching its last color at another distance from the centre, the same both ways.</summary>
    /// <param name="value">The reach, as a fraction of half the area's size.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithRadius(float value) => WithRadius(value, value);

    /// <summary>Returns this gradient with round rings rather than ellipses stretched to the area.</summary>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Circular() => Copy(g => g.circular = true);

    /// <summary>Returns this gradient with another noise pattern.</summary>
    /// <param name="scale">The size of the pattern's features, in pixels.</param>
    /// <param name="patternSeed">Picks one pattern among many.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithNoise(float scale, int patternSeed = 0) => Copy(g =>
    {
        g.noiseScale = MathF.Max(1f, scale);
        g.seed = patternSeed;
    });

    /// <summary>Returns this gradient moving a set amount per character or per line rather than spreading over them all.</summary>
    /// <param name="step">How far along the gradient each character or line moves. 0 spreads the gradient over them all.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithGlyphStep(float step) => Copy(g => g.glyphStep = MathF.Max(0f, step));

    /// <summary>Returns this gradient stretched across another area.</summary>
    /// <param name="value">The area. <see cref="EffectArea.Rect"/> needs <see cref="Over(Vector2, Vector2)"/>.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Over(EffectArea value) => Copy(g => g.area = value);

    /// <summary>Returns this gradient stretched across a set rectangle, whatever the scope draws.</summary>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Over(Vector2 min, Vector2 max) => Copy(g =>
    {
        g.area = EffectArea.Rect;
        g.rectMin = Vector2.Min(min, max);
        g.rectMax = Vector2.Max(min, max);
    });

    /// <summary>Returns this gradient with shapes subdivided into finer cells, for smoother shapes and more work.</summary>
    /// <param name="cellSize">The size of a cell, in pixels. The default is 6.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithTessellation(float cellSize) => Copy(g => g.tessellation = Math.Clamp(cellSize, 1f, 256f));

    #endregion

    #region Application

    /// <summary>Returns this gradient combining with the drawing another way.</summary>
    /// <param name="mode">How the colors combine.</param>
    /// <param name="amount">How much of the effect shows, from 0 (none) to 1 (all).</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithBlend(GradientBlend mode, float amount = 1f) => Copy(g =>
    {
        g.blend = mode;
        g.strength = Math.Clamp(amount, 0f, 1f);
    });

    /// <summary>Returns this gradient showing at a part of its strength over the drawing's own colors.</summary>
    /// <param name="amount">From 0 (none) to 1 (all).</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient WithStrength(float amount) => Copy(g => g.strength = Math.Clamp(amount, 0f, 1f));

    /// <summary>Returns this gradient recoloring only a part of the drawing.</summary>
    /// <param name="value">Which part.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient OnlyOn(EffectTarget value) => Copy(g => g.target = value);

    #endregion

    #region Motion

    /// <summary>Returns this gradient sliding along itself. A gradient that does not repeat repeats once it scrolls.</summary>
    /// <param name="speed">How many times the whole gradient passes per second. Negative slides the other way.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Scrolling(float speed = 0.25f) => Copy(g => g.scrollSpeed = speed);

    /// <summary>Returns this gradient turning: the line of a linear gradient, or the start of a conic one.</summary>
    /// <param name="degreesPerSecond">How fast it turns. Negative turns anticlockwise.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Rotating(float degreesPerSecond = 45f) => Copy(g => g.rotationSpeed = degreesPerSecond);

    /// <summary>Returns this gradient with every hue turning round the color wheel.</summary>
    /// <param name="speed">How many full turns per second.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient HueCycling(float speed = 0.2f) => Copy(g => g.hueSpeed = speed);

    /// <summary>Returns this gradient pulsing towards another color and back.</summary>
    /// <param name="color">The color reached at the top of each pulse. Its alpha is how far the pulse goes.</param>
    /// <param name="speed">Pulses per second.</param>
    /// <param name="wave">The shape of a pulse.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Pulsing(Vector4 color, float speed = 1f, EffectWave wave = EffectWave.Sine) => Copy(g =>
    {
        g.hasPulse = true;
        g.pulseColor = color;
        g.pulseSpeed = speed;
        g.pulseWave = wave;
    });

    /// <summary>Returns this gradient fading in and out.</summary>
    /// <param name="minimumAlpha">The opacity at the bottom of each pulse, from 0 to 1.</param>
    /// <param name="speed">Pulses per second.</param>
    /// <param name="wave">The shape of a pulse.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient PulsingAlpha(float minimumAlpha = 0.3f, float speed = 1f, EffectWave wave = EffectWave.Sine) => Copy(g =>
    {
        g.hasPulse = true;
        g.pulseAlpha = Math.Clamp(minimumAlpha, 0f, 1f);
        g.pulseSpeed = speed;
        g.pulseWave = wave;
    });

    /// <summary>Returns this gradient brightening and dimming.</summary>
    /// <param name="amount">How much brighter at the top of each pulse: 0.5 is half as bright again. Negative dims.</param>
    /// <param name="speed">Pulses per second.</param>
    /// <param name="wave">The shape of a pulse.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient PulsingBrightness(float amount = 0.5f, float speed = 1f, EffectWave wave = EffectWave.Sine) => Copy(g =>
    {
        g.hasPulse = true;
        g.pulseBrightness = amount;
        g.pulseSpeed = speed;
        g.pulseWave = wave;
    });

    /// <summary>Returns this gradient slowly fading its strength in and out over the drawing's own colors.</summary>
    /// <param name="speed">Breaths per second.</param>
    /// <param name="depth">How much of the strength each breath takes away, from 0 to 1.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Breathing(float speed = 0.25f, float depth = 0.6f) => Copy(g =>
    {
        g.breatheSpeed = MathF.Max(0f, speed);
        g.breatheDepth = Math.Clamp(depth, 0f, 1f);
    });

    /// <summary>Returns this gradient with a band of light sweeping across it.</summary>
    /// <param name="color">The light. Its alpha is how bright the band is.</param>
    /// <param name="width">The band's width, as a fraction of the area.</param>
    /// <param name="speed">Sweeps per second, pauses left out.</param>
    /// <param name="degrees">The direction the band travels, clockwise from left to right.</param>
    /// <param name="pause">The rest after each sweep, as a fraction of a sweep's duration.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Shimmering(Vector4 color, float width = 0.15f, float speed = 0.6f, float degrees = 20f, float pause = 1.5f) => Copy(g =>
    {
        g.hasShimmer = true;
        g.shimmerColor = color;
        g.shimmerWidth = Math.Clamp(width, 0.01f, 2f);
        g.shimmerSpeed = speed;
        g.shimmerAngle = degrees;
        g.shimmerPause = MathF.Max(0f, pause);
    });

    /// <summary>Returns this gradient rippling back and forth along itself.</summary>
    /// <param name="amplitude">How far a ripple moves the colors, as a fraction of the gradient.</param>
    /// <param name="frequency">How many ripples fit across the area.</param>
    /// <param name="speed">How fast the ripples travel, in ripples per second.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Waving(float amplitude = 0.1f, float frequency = 2f, float speed = 1f) => Copy(g =>
    {
        g.waveAmplitude = amplitude;
        g.waveFrequency = frequency;
        g.waveSpeed = speed;
    });

    /// <summary>Returns this gradient with characters, or parts of shapes, flashing at random.</summary>
    /// <param name="color">The flash. Its alpha is how bright a flash gets.</param>
    /// <param name="density">The share of characters that flash, from 0 to 1.</param>
    /// <param name="speed">How often each of them flashes, per second.</param>
    /// <param name="patternSeed">Picks which characters flash.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Sparkling(Vector4 color, float density = 0.2f, float speed = 0.5f, int patternSeed = 0) => Copy(g =>
    {
        g.hasSparkle = true;
        g.sparkleColor = color;
        g.sparkleDensity = Math.Clamp(density, 0f, 1f);
        g.sparkleSpeed = MathF.Max(0f, speed);
        g.seed = patternSeed;
    });

    /// <summary>Returns this gradient blinking on and off.</summary>
    /// <param name="rate">Blinks per second.</param>
    /// <param name="minimumAlpha">The opacity while off, from 0 to 1.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Blinking(float rate = 1f, float minimumAlpha = 0f) => Copy(g =>
    {
        g.blinkRate = MathF.Max(0f, rate);
        g.blinkMinAlpha = Math.Clamp(minimumAlpha, 0f, 1f);
    });

    /// <summary>Returns this gradient animating only while the mouse is over it.</summary>
    /// <returns>The changed gradient.</returns>
    public NoireGradient OnHover() => Copy(g => g.trigger = EffectTrigger.Hover);

    /// <summary>
    /// Returns this gradient animating for a set time after the moment given to <see cref="Begin"/>, such as the moment
    /// something changed.
    /// </summary>
    /// <param name="seconds">How long the animation runs.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient ForSeconds(float seconds) => Copy(g =>
    {
        g.trigger = EffectTrigger.Timed;
        g.triggerDuration = MathF.Max(0f, seconds);
    });

    /// <summary>
    /// Returns this gradient animating for a set time the first time it is drawn under a key given to <see cref="Begin"/>,
    /// and never again for that key until <see cref="NoireEffects.ResetOnce"/>.
    /// </summary>
    /// <param name="seconds">How long the animation runs.</param>
    /// <returns>The changed gradient.</returns>
    public NoireGradient Once(float seconds) => Copy(g =>
    {
        g.trigger = EffectTrigger.Once;
        g.triggerDuration = MathF.Max(0f, seconds);
    });

    /// <summary>Returns this gradient still animating while <see cref="NoireUI.ReducedMotion"/> is on.</summary>
    /// <returns>The changed gradient.</returns>
    public NoireGradient IgnoringReducedMotion() => Copy(g => g.ignoreReducedMotion = true);

    #endregion

    #region Sampling

    /// <summary>
    /// The color at a point along the gradient, with its repetition, offset and bands applied and its animations left out.
    /// </summary>
    /// <param name="position">From 0 (the first color) to 1 (the last). Values outside follow the repetition.</param>
    /// <returns>The color, RGBA from 0 to 1.</returns>
    public Vector4 Sample(float position) => Evaluate(Resolve(position, 0f));

    private float Resolve(float t, float scroll)
    {
        if (reversed)
            t = 1f - t;

        t = ((t + offset) * repeatCount) + scroll;

        var mode = repeat == GradientRepeat.None && scroll != 0f ? GradientRepeat.Repeat : repeat;

        t = mode switch
        {
            GradientRepeat.Repeat => t - MathF.Floor(t),
            GradientRepeat.Mirror => Mirror(t),
            _ => Math.Clamp(t, 0f, 1f),
        };

        if (steps > 1)
            t = MathF.Min(MathF.Floor(t * steps), steps - 1) / (steps - 1);

        return t;
    }

    private static float Mirror(float t)
    {
        var m = t - (2f * MathF.Floor(t * 0.5f));
        return m > 1f ? 2f - m : m;
    }

    // The per-vertex path: evaluating every stop is too slow.
    private Vector4 Lookup(float t)
    {
        var position = Math.Clamp(t, 0f, 1f) * (TableSize - 1);
        var index = Math.Min((int)position, TableSize - 2);
        return Vector4.Lerp(table[index], table[index + 1], position - index);
    }

    #endregion

    #region Table

    private bool HasShapedTransitions()
    {
        if (easing != GradientEasing.Linear)
            return true;

        foreach (var stop in stops)
        {
            if (stop.Width > 0f || MathF.Abs(stop.Midpoint - 0.5f) > 0.0001f || (stop.Easing is { } own && own != GradientEasing.Linear))
                return true;
        }

        return false;
    }

    private void RebuildTable()
    {
        var count = stops.Length;
        var positions = new float[count];

        for (var i = 0; i < count; i++)
            positions[i] = stops[i].Position;

        if (float.IsNaN(positions[0]))
            positions[0] = 0f;

        if (count > 1 && float.IsNaN(positions[count - 1]))
            positions[count - 1] = 1f;

        // Missing positions are spread evenly between the known ones on either side.
        for (var i = 1; i < count - 1; i++)
        {
            if (!float.IsNaN(positions[i]))
                continue;

            var next = i + 1;

            while (float.IsNaN(positions[next]))
                next++;

            var from = positions[i - 1];
            var to = positions[next];
            var gaps = next - (i - 1);

            for (var k = i; k < next; k++)
                positions[k] = from + ((to - from) * (k - (i - 1)) / gaps);
        }

        starts = new float[count];
        ends = new float[count];

        var previousEnd = float.NegativeInfinity;

        for (var i = 0; i < count; i++)
        {
            var start = MathF.Max(positions[i], previousEnd);
            starts[i] = start;
            ends[i] = start + MathF.Max(0f, stops[i].Width);
            previousEnd = ends[i];
        }

        table = new Vector4[TableSize];

        for (var i = 0; i < TableSize; i++)
            table[i] = Evaluate(i / (float)(TableSize - 1));
    }

    private Vector4 Evaluate(float u)
    {
        var last = stops.Length - 1;

        if (last == 0 || u <= starts[0])
            return stops[0].Color;

        if (u >= ends[last])
            return stops[last].Color;

        for (var i = 0; i < last; i++)
        {
            if (u <= ends[i])
                return stops[i].Color;

            if (u < starts[i + 1])
            {
                var span = starts[i + 1] - ends[i];
                var f = span <= 0f ? 1f : (u - ends[i]) / span;

                f = Midpoint(f, stops[i].Midpoint);
                f = Ease(stops[i].Easing ?? easing, f);

                return Mix(stops[i].Color, stops[i + 1].Color, f);
            }
        }

        return stops[last].Color;
    }

    private static float Midpoint(float f, float midpoint)
    {
        var m = Math.Clamp(midpoint, 0.001f, 0.999f);

        if (MathF.Abs(m - 0.5f) < 0.0001f)
            return f;

        return f < m ? 0.5f * f / m : 0.5f + (0.5f * (f - m) / (1f - m));
    }

    private static float Ease(GradientEasing kind, float f) => kind switch
    {
        GradientEasing.EaseIn => f * f,
        GradientEasing.EaseOut => 1f - ((1f - f) * (1f - f)),
        GradientEasing.EaseInOut => f < 0.5f ? 2f * f * f : 1f - (2f * (1f - f) * (1f - f)),
        GradientEasing.Smoothstep => f * f * (3f - (2f * f)),
        GradientEasing.Hard => f < 0.5f ? 0f : 1f,
        _ => f,
    };

    private Vector4 Mix(Vector4 from, Vector4 to, float f)
    {
        var alpha = from.W + ((to.W - from.W) * f);

        switch (space)
        {
            case GradientColorSpace.LinearRgb:
            {
                var a = ColorHelper.SrgbToLinear(new Vector3(from.X, from.Y, from.Z));
                var b = ColorHelper.SrgbToLinear(new Vector3(to.X, to.Y, to.Z));
                var mixed = ColorHelper.LinearToSrgb(Vector3.Lerp(a, b, f));
                return new Vector4(mixed, alpha);
            }
            case GradientColorSpace.Oklab:
                return ColorHelper.FromOklab(Vector3.Lerp(ColorHelper.ToOklab(from), ColorHelper.ToOklab(to), f), alpha);
            case GradientColorSpace.HsvShortest:
            case GradientColorSpace.HsvLongest:
                return MixHsv(from, to, f, alpha, space == GradientColorSpace.HsvLongest);
            default:
                return Vector4.Lerp(from, to, f) with { W = alpha };
        }
    }

    private static Vector4 MixHsv(Vector4 from, Vector4 to, float f, float alpha, bool longest)
    {
        var (h0, s0, v0) = ColorHelper.ToHsv(from);
        var (h1, s1, v1) = ColorHelper.ToHsv(to);

        // A grey takes the other color's hue: the mix never swings through red.
        if (s0 <= 0f)
            h0 = h1;

        if (s1 <= 0f)
            h1 = h0;

        var delta = h1 - h0;

        if (longest)
        {
            if (MathF.Abs(delta) < 0.5f)
                delta = delta >= 0f ? delta - 1f : delta + 1f;
        }
        else if (delta > 0.5f)
        {
            delta -= 1f;
        }
        else if (delta < -0.5f)
        {
            delta += 1f;
        }

        return ColorHelper.FromHsv(h0 + (delta * f), s0 + ((s1 - s0) * f), v0 + ((v1 - v0) * f), alpha);
    }

    #endregion
}
