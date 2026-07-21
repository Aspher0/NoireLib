using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A label pinned to a place in the world rather than to the screen: over a gathering node, above a target's head,
/// on the spot a mechanic is about to land. It is projected every frame, fades and shrinks with distance, and behaves
/// like a quest marker once the point it follows leaves the screen.
/// </summary>
/// <remarks>
/// What it follows is read on the framework thread and handed to the draw pass as a position, never as a live object.
/// A game object can be freed between the frame that found it and the frame that draws it, and reading one from the
/// draw thread is an access violation rather than a wrong number, so the boundary is not negotiable.<br/>
/// The label is click-through until an <see cref="OnClick"/> handler is set, because something drawn over the world
/// that silently eats clicks is indistinguishable from a broken game.
/// </remarks>
/// <example>
/// <code>
/// new NoireWorldLabel("target")
/// {
///     Text = "Target",
///     WorldOffset = new Vector3(0f, 2.2f, 0f),
///     OffScreen = WorldLabelOffScreen.EdgeArrow,
/// }
/// .Follow(() => NoireService.TargetManager.Target);
/// </code>
/// </example>
[NoireFacadeFactory]
public sealed class NoireWorldLabel : NoireDrawable
{
    private const ImGuiWindowFlags LabelWindowFlags =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.AlwaysAutoResize;

    private readonly object gate = new();

    private Vector3 trackedPosition;
    private float trackedDistance;
    private bool trackedValid = true;
    private bool subscribed;
    private Vector2 lastSize;

    /// <summary>
    /// Creates a world label and starts tracking immediately.
    /// </summary>
    /// <param name="id">An optional unique identifier. Required to persist anything about the label.</param>
    public NoireWorldLabel(string? id = null) : base(id, "WorldLabel")
    {
        if (NoireService.IsInitialized())
        {
            NoireService.Framework.Update += OnFrameworkUpdate;
            subscribed = true;
        }

        // A world label has no place inside a window to be drawn from, so it opts itself in rather than waiting for the
        // NoireUI.AutoDraw master default. Set it to null to follow that default instead, or to false to draw it yourself.
        AutoDraw = true;

        Register();
    }

    #region What it follows

    /// <summary>
    /// The world position to pin to, used when no <see cref="PositionSource"/> or <see cref="ObjectSource"/> is set.
    /// </summary>
    public Vector3 WorldPosition { get; set; }

    /// <summary>
    /// An offset added to the world position, in yalms. <c>(0, 2.2, 0)</c> is roughly head height on a player.
    /// </summary>
    public Vector3 WorldOffset { get; set; }

    /// <summary>
    /// Where to read the world position from each tick, or <see langword="null"/> to use <see cref="WorldPosition"/>.
    /// Returning <see langword="null"/> hides the label.
    /// </summary>
    /// <remarks>Invoked on the framework thread, so it may read game state freely.</remarks>
    public Func<Vector3?>? PositionSource { get; set; }

    /// <summary>
    /// Which game object to follow, resolved each tick. Returning <see langword="null"/> hides the label.
    /// </summary>
    /// <remarks>
    /// Invoked on the framework thread and reduced to a position immediately, so nothing holds on to the object.
    /// </remarks>
    public Func<IGameObject?>? ObjectSource { get; set; }

    /// <summary>
    /// Follows a game object, for example <c>() =&gt; NoireService.TargetManager.Target</c>.
    /// </summary>
    /// <param name="source">Where to find the object each tick.</param>
    /// <returns>This <see cref="NoireWorldLabel"/> instance, for chaining.</returns>
    public NoireWorldLabel Follow(Func<IGameObject?> source)
    {
        ObjectSource = source;
        return this;
    }

    /// <summary>
    /// Follows a fixed point in the world.
    /// </summary>
    /// <param name="position">The world position.</param>
    /// <returns>This <see cref="NoireWorldLabel"/> instance, for chaining.</returns>
    public NoireWorldLabel At(Vector3 position)
    {
        WorldPosition = position;
        PositionSource = null;
        ObjectSource = null;
        return this;
    }

    #endregion

    #region What it shows

    /// <summary>Whether the label is shown at all.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>The text on the label, used when no <see cref="Content"/> or <see cref="Renderer"/> is set.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The size the text is drawn at. Defaults to <see cref="TextSize.Caption"/>.</summary>
    public TextSize TextSize { get; set; } = TextSize.Caption;

    /// <summary>Rich content to draw instead of plain text: icons, key caps, images, live values.</summary>
    public NoireContent? Content { get; set; }

    /// <summary>
    /// Draws the body of the label yourself, which is how a gauge, a bar or a button ends up on a world marker.
    /// Takes precedence over <see cref="Content"/> and <see cref="Text"/>.
    /// </summary>
    public Action? Renderer { get; set; }

    /// <summary>The text colour. When <see langword="null"/>, the theme's own text colour.</summary>
    public Vector4? TextColor { get; set; }

    /// <summary>The plate colour behind the label. When <see langword="null"/>, the theme's surface.</summary>
    /// <remarks>Its alpha is scaled by <see cref="BackgroundOpacity"/>, whether it is set here or taken from the theme.</remarks>
    public Vector4? Background { get; set; }

    /// <summary>
    /// How opaque the plate behind the label is, from 0 for none at all to 1 for the colour as given. Defaults to 0.8.
    /// </summary>
    /// <remarks>
    /// Scales the alpha of <see cref="Background"/> rather than replacing the colour, so the plate can be faded without
    /// the caller having to rebuild the theme's own surface colour to do it. Zero leaves the text floating on the world
    /// with no plate at all, which is what a dense field of markers usually wants.
    /// </remarks>
    public float BackgroundOpacity { get; set; } = 0.8f;

    /// <summary>The padding inside the plate, in pixels at 100%.</summary>
    public Vector2 Padding { get; set; } = new(6f, 3f);

    /// <summary>The corner rounding of the plate, in pixels at 100%.</summary>
    public float Rounding { get; set; } = 3f;

    /// <summary>
    /// Which point of the label sits on the world point. Defaults to the bottom centre, so the label stands above it.
    /// </summary>
    public Vector2 Pivot { get; set; } = new(0.5f, 1f);

    /// <summary>An offset applied after projection, in pixels at 100%.</summary>
    public Vector2 ScreenOffset { get; set; }

    #endregion

    #region Distance

    /// <summary>
    /// How far away the label stops being drawn, in yalms. Zero means no limit and no fading.
    /// </summary>
    public float MaxDistance { get; set; }

    /// <summary>How far away the label starts fading out, in yalms. See <see cref="MaxDistance"/>.</summary>
    public float FadeDistance { get; set; }

    /// <summary>
    /// A fixed multiplier on the whole label: the text, the padding, the rounding and the arrow together. Defaults to 1.
    /// </summary>
    /// <remarks>
    /// This is the size knob to reach for when a label should simply be larger or smaller than the rest, and it applies
    /// whether or not the label also scales with distance: the two multiply.<br/>
    /// It is a multiplier rather than a pixel size so that it moves the plate with the text. Setting
    /// <see cref="TextSize"/> alone leaves the padding and the arrow where they were, which reads as a label whose
    /// proportions drift as it grows.<br/>
    /// Text is drawn with a font built at the size this works out to, so each distinct value a plugin uses is a distinct
    /// font size. A few are free; a value that varies per label across dozens of labels is not. See
    /// <see cref="ScaleStep"/>.
    /// </remarks>
    public float BaseScale { get; set; } = 1f;

    /// <summary>Whether the label shrinks with distance. Off by default. See <see cref="Scaling"/>.</summary>
    public bool ScaleWithDistance { get; set; }

    /// <summary>
    /// How the distance is turned into a size. Defaults to <see cref="WorldLabelScaling.Perspective"/>.
    /// </summary>
    public WorldLabelScaling Scaling { get; set; } = WorldLabelScaling.Perspective;

    /// <summary>
    /// The distance at which a scaling label is drawn at its authored size, in yalms.
    /// Used by <see cref="WorldLabelScaling.Perspective"/>.
    /// </summary>
    public float ScaleReferenceDistance { get; set; } = 20f;

    /// <summary>
    /// How far away the label starts shrinking, in yalms. At or below it the label is at <see cref="MaxScale"/>.
    /// Used by <see cref="WorldLabelScaling.Ramp"/>.
    /// </summary>
    public float ShrinkFromDistance { get; set; } = 10f;

    /// <summary>
    /// How far away the label has finished shrinking, in yalms. At or beyond it the label is at <see cref="MinScale"/>.
    /// Used by <see cref="WorldLabelScaling.Ramp"/>.
    /// </summary>
    public float ShrinkToDistance { get; set; } = 60f;

    /// <summary>The smallest a scaling label may become.</summary>
    public float MinScale { get; set; } = 0.6f;

    /// <summary>The largest a scaling label may become.</summary>
    public float MaxScale { get; set; } = 1.4f;

    /// <summary>
    /// The steps the distance scale is rounded to, so the label takes a few sharp sizes rather than every size between
    /// its bounds. Zero scales smoothly instead.
    /// </summary>
    /// <remarks>
    /// Text is the reason this exists. <see cref="NoireText"/> draws at a size by building a real font at it, and a
    /// label scaled continuously would ask for one per pixel of distance; each is a full glyph atlas, and the cache
    /// that holds them is deliberately small. Stepped, a label costs a handful of sizes across its whole range and
    /// every one of them is rasterized rather than resampled.<br/>
    /// The default spans <see cref="MinScale"/> to <see cref="MaxScale"/> in four steps. Set it smaller for a finer
    /// ramp and more sizes, or to zero to scale smoothly and accept that the text is stretched between sizes.
    /// </remarks>
    public float ScaleStep { get; set; } = 0.25f;

    #endregion

    #region Off screen

    /// <summary>What the label does once its world point leaves the screen. Defaults to hiding.</summary>
    public WorldLabelOffScreen OffScreen { get; set; } = WorldLabelOffScreen.Hide;

    /// <summary>
    /// How far a pinned label stays clear of the screen edges, in pixels at 100%. An edge arrow is given its own room
    /// on top of this, so turning it on never pushes it off the edge the label was kept clear of.
    /// </summary>
    public float EdgeMargin { get; set; } = 24f;

    /// <summary>The length of the edge arrow from tip to base, in pixels at 100%.</summary>
    public float ArrowSize { get; set; } = 14f;

    /// <summary>How far the edge arrow stands clear of the label, in pixels at 100%.</summary>
    /// <remarks>Counted inside the space reserved by <see cref="EdgeMargin"/>, so widening it never costs the arrow its room.</remarks>
    public float ArrowGap { get; set; } = 4f;

    /// <summary>The colour of the edge arrow. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? ArrowColor { get; set; }

    #endregion

    #region Interaction and state

    /// <summary>
    /// What to do when the label is clicked. While it is <see langword="null"/> the label takes no input at all and
    /// clicks pass straight through to the game.
    /// </summary>
    public Action? OnClick { get; set; }

    /// <summary>A tooltip shown on hover. Setting one makes the label take the mouse, like <see cref="OnClick"/>.</summary>
    public string? Tooltip { get; set; }

    /// <summary>
    /// Whether the label is kept in front of every other window, for clicks as well as for drawing. Off by default.
    /// </summary>
    /// <remarks>
    /// Being drawn on top and receiving the mouse are two different orders in ImGui, and moving only the first is what
    /// produces a marker plainly visible above a window and completely dead under it. This moves both, so a label that
    /// takes input at all (see <see cref="OnClick"/>) stays clickable where it overlaps a window.<br/>
    /// Off by default because a marker that covers the window a plugin is asking you to read is worse than one hidden
    /// behind it, and a world label is drawn wherever the world happens to put it.
    /// </remarks>
    public bool AlwaysOnTop { get; set; }

    /// <summary>Whether the label was drawn on screen last frame, rather than hidden or pinned to an edge.</summary>
    public bool IsOnScreen { get; private set; }

    /// <summary>Whether the world point was in front of the camera and inside the viewport last frame.</summary>
    public bool IsInView { get; private set; }

    /// <summary>The distance from the player to the world point, in yalms, as of the last tick.</summary>
    public float Distance
    {
        get
        {
            lock (gate)
                return trackedDistance;
        }
    }

    /// <summary>Where the label was drawn last frame, in screen pixels.</summary>
    public Vector2 ScreenPosition { get; private set; }

    #endregion

    /// <summary>
    /// Reads what the label follows and reduces it to a position and a distance, once per game tick.
    /// </summary>
    /// <param name="framework">The framework raising the update.</param>
    private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (IsDisposed)
            return;

        try
        {
            var position = ResolveTrackedPosition();
            var origin = NoireService.ObjectTable.LocalPlayer?.Position;

            lock (gate)
            {
                trackedValid = position != null;
                trackedPosition = position ?? Vector3.Zero;
                trackedDistance = position != null && origin != null
                    ? Vector3.Distance(origin.Value, position.Value)
                    : 0f;
            }
        }
        catch (Exception exception)
        {
            lock (gate)
                trackedValid = false;

            NoireLogger.LogWarning($"World label '{Id}' could not resolve what it follows: {exception.Message}");
        }
    }

    /// <summary>
    /// Works out the world position from whichever source was configured.
    /// </summary>
    /// <returns>The position, or <see langword="null"/> when there is nothing to follow.</returns>
    private Vector3? ResolveTrackedPosition()
    {
        if (ObjectSource != null)
        {
            var target = ObjectSource();
            return target != null && target.IsValid() ? target.Position : null;
        }

        return PositionSource != null ? PositionSource() : WorldPosition;
    }

    /// <inheritdoc/>
    protected override void DrawCore()
    {
        IsOnScreen = false;
        IsInView = false;

        if (!Visible || !NoireService.IsInitialized())
            return;

        Vector3 world;
        float distance;

        lock (gate)
        {
            if (!trackedValid)
                return;

            world = trackedPosition;
            distance = trackedDistance;
        }

        var alpha = UiWorldProjection.DistanceAlpha(distance, FadeDistance, MaxDistance);

        if (alpha <= 0f)
            return;

        var inFront = NoireService.GameGui.WorldToScreen(world + WorldOffset, out var screen, out var inView);
        IsInView = inFront && inView;

        if (!IsInView && OffScreen == WorldLabelOffScreen.Hide)
            return;

        var viewport = ImGui.GetMainViewport();
        var bounds = new UiRect(viewport.Pos, viewport.Size);

        Vector2 pinned;
        Vector2 direction;
        Vector2 pivot;

        if (IsInView)
        {
            pinned = screen + NoireUI.Scaled(ScreenOffset);
            direction = Vector2.Zero;
            pivot = Pivot;
        }
        else
        {
            // Off screen, only the direction to the point survives the projection, so the label is placed on the edge
            // along it rather than at the coordinate. It is pinned by its centre, which is what PinToEdge answers with,
            // so the authored pivot does not apply and would push it back off the screen it was just pulled onto.
            // The arrow stands off past the plate, so it is given room inside the margin rather than being allowed to
            // hang over an edge the label itself was carefully kept clear of.
            var margin = NoireUI.Scaled(EdgeMargin)
                + (OffScreen == WorldLabelOffScreen.EdgeArrow ? NoireUI.Scaled(ArrowSize + ArrowGap) : 0f);

            direction = UiWorldProjection.OffScreenDirection(screen, bounds);
            pinned = UiWorldProjection.PinToEdge(bounds, direction, lastSize, margin);
            pivot = new Vector2(0.5f, 0.5f);
        }

        ScreenPosition = pinned;
        IsOnScreen = true;

        DrawPlate(pinned, pivot, alpha, distance);

        if (!IsInView && OffScreen == WorldLabelOffScreen.EdgeArrow)
            DrawEdgeArrow(pinned, direction, alpha);
    }

    /// <summary>
    /// Draws the label itself, as an auto-sized window so the plate is exactly as wide as what is on it.
    /// </summary>
    /// <param name="position">Where the label goes, in screen pixels.</param>
    /// <param name="pivot">Which point of the label sits on that position.</param>
    /// <param name="alpha">The distance fade to draw at.</param>
    /// <param name="distance">The distance to the world point, for scaling.</param>
    private void DrawPlate(Vector2 position, Vector2 pivot, float alpha, float distance)
    {
        var theme = NoireTheme.Current;
        var scale = ResolveScale(distance);

        var interactive = OnClick != null || !string.IsNullOrEmpty(Tooltip);
        var flags = LabelWindowFlags | (interactive ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoInputs);

        if (AlwaysOnTop)
            flags |= UiWindowOrder.TopLayerFlag;

        // A pivot on SetNextWindowPos is what makes an auto-sized window placeable at all: the size is not known until
        // after it has been drawn, and ImGui is the only thing that can apply it without a frame of lag.
        ImGui.SetNextWindowPos(position, ImGuiCond.Always, pivot);

        using var alphaStyle = ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * alpha);
        using var paddingStyle = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, NoireUI.Scaled(Padding) * scale);
        using var roundingStyle = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, NoireUI.Scaled(Rounding));
        using var borderStyle = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, 0f);
        using var minSizeStyle = ImRaii.PushStyle(ImGuiStyleVar.WindowMinSize, Vector2.One);

        // ImGui picks which colour a window's background comes from by flag, and the flag that promotes this one to the
        // top layer is also the one that switches it from WindowBg to PopupBg. Pushed to the wrong index the plate keeps
        // the theme's popup colour instead, so Background and BackgroundOpacity read as broken on exactly the labels
        // that asked to stay in front. See UiWindowOrder.TopLayerFlag.
        var background = ImRaii.PushColor(
            AlwaysOnTop ? ImGuiCol.PopupBg : ImGuiCol.WindowBg,
            ColorHelper.ScaleAlpha(Background ?? theme.Resolve(ThemeColor.Surface), BackgroundOpacity));

        var open = ImGui.Begin(ImGuiId, flags);

        // Dropped the moment the window exists, because Begin is what reads it. Held any longer, PopupBg would repaint
        // the tooltip this label shows on hover in the plate's own colour.
        background.Dispose();

        if (open)
        {
            if (AlwaysOnTop)
                UiWindowOrder.KeepInFront();

            // The whole body draws inside one NoireText scope, so a font built at the size actually wanted covers the
            // custom renderer and the rich content too, not only the plain text. Scaling the window font instead is the
            // stretched bitmap NoireText exists to replace, and on a marker that shrinks as you walk it is the one
            // thing on screen that never stops resampling.
            NoireText.At(
                theme.ResolveTextSize(TextSize) * scale,
                (label: this, theme),
                static state => state.label.DrawBody(state.theme));

            lastSize = ImGui.GetWindowSize();

            if (interactive)
                HandleInput();
        }

        ImGui.End();
    }

    /// <summary>
    /// Works out how large the label is drawn, from its fixed size and the distance to what it follows.
    /// </summary>
    /// <remarks>
    /// Only the part that varies with distance is stepped. <see cref="BaseScale"/> multiplies afterwards, so it stays a
    /// free-form number without adding a font size per value it could take between the steps.
    /// </remarks>
    /// <param name="distance">The distance to the world point, in yalms.</param>
    /// <returns>The multiplier the whole label is drawn at.</returns>
    private float ResolveScale(float distance)
    {
        if (!ScaleWithDistance)
            return BaseScale;

        var withDistance = Scaling == WorldLabelScaling.Ramp
            ? UiWorldProjection.RampScale(distance, ShrinkFromDistance, ShrinkToDistance, MinScale, MaxScale)
            : UiWorldProjection.DistanceScale(distance, ScaleReferenceDistance, MinScale, MaxScale);

        return BaseScale * UiWorldProjection.QuantizeScale(withDistance, ScaleStep);
    }

    /// <summary>
    /// Draws whatever the label carries: a custom body, rich content, or the plain text.
    /// </summary>
    /// <remarks>
    /// Runs inside the <see cref="NoireText"/> scope <see cref="DrawPlate"/> opened, so the plain text asks for no size
    /// of its own: it is already being drawn at the label's size, and asking again would resolve the step a second time
    /// and lose the distance scaling with it.
    /// </remarks>
    /// <param name="theme">The palette in force.</param>
    private void DrawBody(NoireTheme theme)
    {
        if (Renderer != null)
        {
            Renderer();
            return;
        }

        if (Content != null)
        {
            Content.Draw();
            return;
        }

        using var color = ImRaii.PushColor(ImGuiCol.Text, TextColor ?? theme.Resolve(ThemeColor.Text));
        ImGui.TextUnformatted(Text);
    }

    /// <summary>
    /// Handles hovering and clicking for a label that has been given a reason to take the mouse.
    /// </summary>
    private void HandleInput()
    {
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            return;

        if (!string.IsNullOrEmpty(Tooltip))
            NoireTooltip.Show(Tooltip);

        if (OnClick != null && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            OnClick();
    }

    /// <summary>
    /// Draws the arrow that points off screen toward the world point, the way a quest marker does.
    /// </summary>
    /// <remarks>
    /// The arrow points outward, along the direction the label was pinned by, because that is where the thing it marks
    /// actually is. Pointing it at the projected coordinate instead is what makes a marker for something behind you
    /// point back into the middle of the screen, which reads as an instruction to walk into your own camera.
    /// </remarks>
    /// <param name="pinned">Where the label ended up, at its centre.</param>
    /// <param name="direction">The direction the label was pinned along.</param>
    /// <param name="alpha">The distance fade to draw at.</param>
    private void DrawEdgeArrow(Vector2 pinned, Vector2 direction, float alpha)
    {
        var angle = UiWorldProjection.ArrowAngle(direction);
        var size = NoireUI.Scaled(ArrowSize);

        // Stood off along the direction rather than by a fixed radius, so the arrow clears the corner of a wide plate
        // pointing sideways as cleanly as it clears a square one. The gap is measured from the plate to the base of the
        // arrow, which is the edge of it a reader actually sees against the label.
        var unit = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var standoff = UiWorldProjection.EdgeDistance(lastSize, unit);
        var reach = (float.IsInfinity(standoff) ? 0f : standoff) + NoireUI.Scaled(ArrowGap) + size;
        var tip = pinned + (unit * reach);

        var color = ColorHelper.ScaleAlpha(ArrowColor ?? NoireTheme.Current.Resolve(ThemeColor.Accent), alpha);

        // A world label is drawn over the game rather than inside a window, so it belongs on the foreground list.
        using var draw = UiDraw.BeginForeground();

        NoireShapes.On(draw.List, (tip, angle, size, color), static state =>
        {
            Span<Vector2> points = stackalloc Vector2[3];
            UiWorldProjection.ArrowPoints(state.tip, state.angle, state.size, points);
            NoireShapes.Fill(points, state.color);
        });
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        if (!subscribed)
            return;

        subscribed = false;
        NoireService.Framework.Update -= OnFrameworkUpdate;
    }
}
