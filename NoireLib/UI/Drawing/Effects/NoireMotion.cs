using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Movement applied to everything drawn inside its scope: turning, rocking, scaling, floating, shaking, glitching and
/// per-character motion. Immutable: build it once. It never moves the layout, and stays inside the window's clip.
/// </summary>
public sealed partial class NoireMotion
{
    private EffectArea area = EffectArea.Drawn;
    private Vector2 rectMin;
    private Vector2 rectMax;
    private Vector2 pivot = new(0.5f, 0.5f);
    private EffectTarget target = EffectTarget.All;

    private float staticRotation;
    private Vector2 staticScale = Vector2.One;
    private Vector2 staticOffset;
    private float staticSkew;

    private float spinSpeed;

    private float rockDegrees;
    private float rockSpeed;
    private EffectWave rockWave = EffectWave.Sine;

    private bool hasScalePulse;
    private float scaleMin = 1f;
    private float scaleMax = 1f;
    private float scaleSpeed;
    private EffectWave scaleWave = EffectWave.Sine;

    private float squashAmount;
    private float squashSpeed;

    private float flipSpeed;
    private bool flipVertical;

    private float floatPixels;
    private float floatSpeed;
    private float swayPixels;
    private float swaySpeed;
    private float bounceHeight;
    private float bounceSpeed;
    private float orbitRadius;
    private float orbitSpeed;

    private float skewAmount;
    private float skewSpeed;

    private float shakeIntensity;
    private float shakeFrequency;
    private float shakeEvery;
    private float shakeLength;

    private float jitterIntensity;
    private float jitterFrequency;
    private float jitterEvery;
    private float jitterLength;

    private float glitchIntensity;
    private float glitchChance;
    private float glitchRate;
    private float glitchEvery;
    private float glitchLength;

    private float glyphWaveHeight;
    private float glyphWaveSpeed;
    private float glyphWaveSpacing;
    private EffectWave glyphWaveShape = EffectWave.Sine;

    private float wobbleDegrees;
    private float wobbleSpeed;
    private float wobbleSpacing;

    private bool hasReveal;
    private float revealDelay;
    private float revealLength;
    private float revealHold;

    private EffectTrigger trigger = EffectTrigger.Always;
    private float triggerDuration;
    private bool ignoreReducedMotion;

    private NoireMotion()
    {
    }

    /// <summary>A motion that does nothing yet, to add movements to.</summary>
    /// <returns>The motion.</returns>
    public static NoireMotion Create() => new();

    private NoireMotion Copy(Action<NoireMotion> change)
    {
        var copy = (NoireMotion)MemberwiseClone();
        change(copy);
        return copy;
    }

    /// <summary>The area the motion turns and scales around.</summary>
    public EffectArea Area => area;

    /// <summary>Which part of the drawing moves.</summary>
    public EffectTarget Target => target;

    #region Setup

    /// <summary>Returns this motion turning and scaling around another point of its area.</summary>
    /// <param name="x">From 0 (left) to 1 (right).</param>
    /// <param name="y">From 0 (top) to 1 (bottom).</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion WithPivot(float x, float y) => Copy(m => m.pivot = new Vector2(x, y));

    /// <summary>Returns this motion measured across another area. <see cref="EffectArea.Glyph"/> moves every character around its own centre.</summary>
    /// <param name="value">The area.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Over(EffectArea value) => Copy(m => m.area = value);

    /// <summary>Returns this motion measured across a set rectangle.</summary>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Over(Vector2 min, Vector2 max) => Copy(m =>
    {
        m.area = EffectArea.Rect;
        m.rectMin = Vector2.Min(min, max);
        m.rectMax = Vector2.Max(min, max);
    });

    /// <summary>Returns this motion moving only a part of the drawing.</summary>
    /// <param name="value">Which part.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion OnlyOn(EffectTarget value) => Copy(m => m.target = value);

    /// <summary>Returns this motion with a fixed turn.</summary>
    /// <param name="degrees">Clockwise.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Rotated(float degrees) => Copy(m => m.staticRotation = degrees);

    /// <summary>Returns this motion with a fixed scale.</summary>
    /// <param name="x">Across.</param>
    /// <param name="y">Down.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Scaled(float x, float y) => Copy(m => m.staticScale = new Vector2(x, y));

    /// <summary>Returns this motion with a fixed scale, the same both ways.</summary>
    /// <param name="value">The scale.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Scaled(float value) => Scaled(value, value);

    /// <summary>Returns this motion with a fixed shift.</summary>
    /// <param name="x">Pixels to the right.</param>
    /// <param name="y">Pixels down.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Offset(float x, float y) => Copy(m => m.staticOffset = new Vector2(x, y));

    /// <summary>Returns this motion with a fixed slant.</summary>
    /// <param name="amount">How far the top leans right per pixel of height. 0.2 is a clear italic.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Skewed(float amount) => Copy(m => m.staticSkew = amount);

    #endregion

    #region Whole drawing

    /// <summary>Returns this motion spinning round and round.</summary>
    /// <param name="degreesPerSecond">Negative spins anticlockwise.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Spinning(float degreesPerSecond = 90f) => Copy(m => m.spinSpeed = degreesPerSecond);

    /// <summary>Returns this motion rocking from side to side like a seesaw.</summary>
    /// <param name="degrees">How far it tips each way.</param>
    /// <param name="speed">Rocks per second.</param>
    /// <param name="wave">The shape of a rock.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Rocking(float degrees = 6f, float speed = 0.5f, EffectWave wave = EffectWave.Sine) => Copy(m =>
    {
        m.rockDegrees = degrees;
        m.rockSpeed = speed;
        m.rockWave = wave;
    });

    /// <summary>Returns this motion swinging from its top like a pendulum.</summary>
    /// <param name="degrees">How far it swings each way.</param>
    /// <param name="speed">Swings per second.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Swinging(float degrees = 10f, float speed = 0.5f) => Rocking(degrees, speed).WithPivot(0.5f, 0f);

    /// <summary>Returns this motion growing and shrinking.</summary>
    /// <param name="smallest">The scale at rest.</param>
    /// <param name="largest">The scale at the peak.</param>
    /// <param name="speed">Pulses per second.</param>
    /// <param name="wave">The shape of a pulse. <see cref="EffectWave.Heartbeat"/> beats like a heart.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Scaling(float smallest = 0.95f, float largest = 1.05f, float speed = 0.5f, EffectWave wave = EffectWave.Sine) => Copy(m =>
    {
        m.hasScalePulse = true;
        m.scaleMin = smallest;
        m.scaleMax = largest;
        m.scaleSpeed = speed;
        m.scaleWave = wave;
    });

    /// <summary>Returns this motion squashing and stretching like jelly: wider when shorter, taller when narrower.</summary>
    /// <param name="amount">How much, as a fraction of the size.</param>
    /// <param name="speed">Squashes per second.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Squashing(float amount = 0.08f, float speed = 1f) => Copy(m =>
    {
        m.squashAmount = amount;
        m.squashSpeed = speed;
    });

    /// <summary>Returns this motion turning over like a card, showing its edge halfway.</summary>
    /// <param name="speed">Turns per second.</param>
    /// <param name="vertical">Turns over top to bottom rather than side to side.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Flipping(float speed = 0.5f, bool vertical = false) => Copy(m =>
    {
        m.flipSpeed = speed;
        m.flipVertical = vertical;
    });

    /// <summary>Returns this motion floating up and down.</summary>
    /// <param name="pixels">How far it rises and sinks.</param>
    /// <param name="speed">Floats per second.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Floating(float pixels = 3f, float speed = 0.5f) => Copy(m =>
    {
        m.floatPixels = pixels;
        m.floatSpeed = speed;
    });

    /// <summary>Returns this motion swaying left and right.</summary>
    /// <param name="pixels">How far it goes each way.</param>
    /// <param name="speed">Sways per second.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Swaying(float pixels = 3f, float speed = 0.5f) => Copy(m =>
    {
        m.swayPixels = pixels;
        m.swaySpeed = speed;
    });

    /// <summary>Returns this motion bouncing up off its resting place.</summary>
    /// <param name="height">How high it bounces, in pixels.</param>
    /// <param name="speed">Bounces per second.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Bouncing(float height = 4f, float speed = 1f) => Copy(m =>
    {
        m.bounceHeight = height;
        m.bounceSpeed = speed;
    });

    /// <summary>Returns this motion circling around its resting place.</summary>
    /// <param name="pixels">The circle's radius.</param>
    /// <param name="speed">Circles per second. Negative circles anticlockwise.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Orbiting(float pixels = 3f, float speed = 0.5f) => Copy(m =>
    {
        m.orbitRadius = pixels;
        m.orbitSpeed = speed;
    });

    /// <summary>Returns this motion leaning left and right.</summary>
    /// <param name="amount">How far the top leans each way per pixel of height.</param>
    /// <param name="speed">Leans per second.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Leaning(float amount = 0.15f, float speed = 0.5f) => Copy(m =>
    {
        m.skewAmount = amount;
        m.skewSpeed = speed;
    });

    /// <summary>Returns this motion shaking, all of it at once.</summary>
    /// <param name="pixels">How far a shake moves it.</param>
    /// <param name="frequency">Shakes per second while shaking.</param>
    /// <param name="every">Shakes in bursts this many seconds apart. 0 shakes all the time.</param>
    /// <param name="length">How long a burst lasts, in seconds.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Shaking(float pixels = 2f, float frequency = 30f, float every = 0f, float length = 0.3f) => Copy(m =>
    {
        m.shakeIntensity = pixels;
        m.shakeFrequency = MathF.Max(0.01f, frequency);
        m.shakeEvery = MathF.Max(0f, every);
        m.shakeLength = MathF.Max(0f, length);
    });

    #endregion

    #region Each character

    /// <summary>Returns this motion trembling, every character on its own.</summary>
    /// <param name="pixels">How far a character moves.</param>
    /// <param name="frequency">Trembles per second while trembling.</param>
    /// <param name="every">Trembles in bursts this many seconds apart. 0 trembles all the time.</param>
    /// <param name="length">How long a burst lasts, in seconds.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Trembling(float pixels = 1f, float frequency = 20f, float every = 0f, float length = 0.3f) => Copy(m =>
    {
        m.jitterIntensity = pixels;
        m.jitterFrequency = MathF.Max(0.01f, frequency);
        m.jitterEvery = MathF.Max(0f, every);
        m.jitterLength = MathF.Max(0f, length);
    });

    /// <summary>Returns this motion glitching: in bursts, some characters jump sideways for a split second.</summary>
    /// <param name="pixels">How far a character jumps.</param>
    /// <param name="every">Seconds between bursts. 0 glitches all the time.</param>
    /// <param name="length">How long a burst lasts, in seconds.</param>
    /// <param name="chance">The share of characters that jump at each step of a burst, from 0 to 1.</param>
    /// <param name="rate">Steps per second during a burst: how often the jumps change.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Glitching(float pixels = 3f, float every = 2f, float length = 0.2f, float chance = 0.35f, float rate = 24f) => Copy(m =>
    {
        m.glitchIntensity = pixels;
        m.glitchEvery = MathF.Max(0f, every);
        m.glitchLength = MathF.Max(0f, length);
        m.glitchChance = Math.Clamp(chance, 0f, 1f);
        m.glitchRate = MathF.Max(0.01f, rate);
    });

    /// <summary>Returns this motion rippling a wave through the characters, one after another.</summary>
    /// <param name="pixels">How high a character rises.</param>
    /// <param name="speed">Waves per second.</param>
    /// <param name="spacing">How far behind its neighbour each character follows, as a fraction of a wave.</param>
    /// <param name="wave">The shape of a wave. <see cref="EffectWave.Bounce"/> makes characters hop.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion WavingGlyphs(float pixels = 3f, float speed = 1f, float spacing = 0.08f, EffectWave wave = EffectWave.Sine) => Copy(m =>
    {
        m.glyphWaveHeight = pixels;
        m.glyphWaveSpeed = speed;
        m.glyphWaveSpacing = spacing;
        m.glyphWaveShape = wave;
    });

    /// <summary>Returns this motion rocking every character on its own, one after another.</summary>
    /// <param name="degrees">How far a character tips each way.</param>
    /// <param name="speed">Rocks per second.</param>
    /// <param name="spacing">How far behind its neighbour each character follows, as a fraction of a rock.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion WobblingGlyphs(float degrees = 8f, float speed = 1f, float spacing = 0.1f) => Copy(m =>
    {
        m.wobbleDegrees = degrees;
        m.wobbleSpeed = speed;
        m.wobbleSpacing = spacing;
    });

    /// <summary>
    /// Returns this motion bringing the characters in one after another, growing from nothing and dropping into place.
    /// It runs once with <see cref="ForSeconds"/> or <see cref="Once"/>, and loops otherwise.
    /// </summary>
    /// <param name="delay">Seconds between one character and the next.</param>
    /// <param name="length">Seconds one character takes to arrive.</param>
    /// <param name="hold">Seconds the whole text stays in place before the loop starts over.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Revealing(float delay = 0.04f, float length = 0.3f, float hold = 2f) => Copy(m =>
    {
        m.hasReveal = true;
        m.revealDelay = MathF.Max(0f, delay);
        m.revealLength = MathF.Max(0.01f, length);
        m.revealHold = MathF.Max(0f, hold);
    });

    #endregion

    #region Triggers

    /// <summary>Returns this motion moving only while the mouse is over it.</summary>
    /// <returns>The changed motion.</returns>
    public NoireMotion OnHover() => Copy(m => m.trigger = EffectTrigger.Hover);

    /// <summary>Returns this motion moving for a set time after the moment given to <see cref="Begin"/>.</summary>
    /// <param name="seconds">How long it moves.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion ForSeconds(float seconds) => Copy(m =>
    {
        m.trigger = EffectTrigger.Timed;
        m.triggerDuration = MathF.Max(0f, seconds);
    });

    /// <summary>
    /// Returns this motion moving for a set time the first time it is drawn under a key given to <see cref="Begin"/>,
    /// and never again for that key until <see cref="NoireEffects.ResetOnce"/>.
    /// </summary>
    /// <param name="seconds">How long it moves.</param>
    /// <returns>The changed motion.</returns>
    public NoireMotion Once(float seconds) => Copy(m =>
    {
        m.trigger = EffectTrigger.Once;
        m.triggerDuration = MathF.Max(0f, seconds);
    });

    /// <summary>Returns this motion still moving while <see cref="NoireUI.ReducedMotion"/> is on.</summary>
    /// <returns>The changed motion.</returns>
    public NoireMotion IgnoringReducedMotion() => Copy(m => m.ignoreReducedMotion = true);

    #endregion

    #region Scopes

    /// <summary>Opens a scope: everything drawn until it closes takes this motion.</summary>
    /// <param name="drawList">The draw list to record. The default is the one <see cref="NoireShapes"/> paints into.</param>
    /// <param name="startedAt">The moment a motion with <see cref="ForSeconds"/> starts, in <see cref="NoireUI.Time"/> seconds.</param>
    /// <param name="onceKey">The key a motion with <see cref="Once"/> moves once for.</param>
    /// <returns>The open scope. Close it with <see langword="using"/> or <see cref="EffectScope.End"/>.</returns>
    public EffectScope Begin(ImDrawListPtr drawList = default, float startedAt = float.NaN, string? onceKey = null)
        => NoireEffects.Begin(null, this, drawList, startedAt, onceKey);

    /// <summary>Runs a block of drawing with this motion.</summary>
    /// <param name="body">The drawing.</param>
    /// <returns>What was drawn, before and after the motion.</returns>
    public EffectResult With(Action body) => NoireEffects.With(null, this, body);

    /// <summary>Runs a block of drawing with this motion, passing a value through.</summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing.</param>
    /// <returns>What was drawn, before and after the motion.</returns>
    public EffectResult With<TState>(TState state, Action<TState> body) => NoireEffects.With(null, this, state, body);

    /// <summary>Draws a line of text with this motion.</summary>
    /// <param name="text">The text.</param>
    /// <returns>What was drawn, before and after the motion.</returns>
    public EffectResult Text(string text) => NoireEffects.Text(text, null, this);

    /// <summary>Draws text wrapped at the edge of the window, with this motion.</summary>
    /// <param name="text">The text.</param>
    /// <returns>What was drawn, before and after the motion.</returns>
    public EffectResult TextWrapped(string text) => NoireEffects.TextWrapped(text, null, this);

    #endregion
}
