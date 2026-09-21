using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// An animated backdrop of translucent ribbons over a gradient under a vignette. Leans toward the pointer and ripples on demand.<br/>
/// Lay it out once per frame with <see cref="Update(Vector2, Vector2, Vector2?)"/>, then paint any number of views with <see cref="Draw(ImDrawListPtr, Vector2, Vector2, float, float)"/>. Draw thread only.
/// </summary>
public sealed class NoireRibbonField
{
    private const int MaxWaves = 32;
    private const int MaxArcSegments = 32;

    private const int MaxMeshRows = 4;

    // Keeps 16 bit indices addressable.
    private const int MaxChunkVertices = 60000;

    private readonly float[] waveX = new float[MaxWaves];
    private readonly float[] waveT = new float[MaxWaves];
    private readonly float[] arcX = new float[MaxArcSegments + 1];
    private readonly float[] arcY = new float[MaxArcSegments + 1];

    private float time;
    private bool frozen;
    private Vector2 pointer;
    private bool pointerReady;
    private Vector3 leanColor;
    private Vector3? leanTarget;
    private int waveCount;
    private Vector2 fieldMin;
    private Vector2 fieldMax;
    private bool hasField;
    private int advancedFrame = int.MinValue;

    private float[] sampleX = [];
    private float[] sampleTop = [];
    private float[] sampleBottom = [];
    private uint[] ribbonRgb = [];
    private float[] ribbonAlpha = [];
    private int sampleCount;
    private int ribbonCount;

    private float[] colX = new float[256];
    private float[] colTop = new float[256];
    private float[] colBottom = new float[256];
    private float[] colGradient = new float[256];
    private float[] colEdgeTop = new float[256];
    private float[] colEdgeBottom = new float[256];
    private int[] colSegment = new int[256];
    private float[] colFraction = new float[256];
    private float[] meshY = new float[MaxMeshRows * 256];
    private float[] meshAlpha = new float[MaxMeshRows * 256];
    private int meshStride = 256;
    private float[] outX = new float[1024];
    private float[] outY = new float[MaxMeshRows * 1024];
    private float[] outAlpha = new float[MaxMeshRows * 1024];
    private int outStride = 1024;
    private float[] rows = new float[256];
    private readonly float[] breaks = new float[8 + (2 * (MaxArcSegments + 1))];

    private Vector2[] gridPos = [];
    private uint[] gridCol = [];

    private bool laidOut;
    private float layoutTime = float.NaN;
    private Vector2 layoutPointer;
    private Vector3 layoutLean;
    private Vector2 layoutMin;
    private Vector2 layoutSize;
    private float layoutScale;
    private int layoutRibbons;
    private object? layoutSource;
    private int layoutOptions;
    private int layoutVersion;

    private const int RibbonSlots = 4;

    private readonly RibbonPaint[] paints = BuildRibbonSlots();

    private int paintClock;

    private sealed class RibbonPaint
    {
        public Vector2[] Positions = [];
        public uint[] Colors = [];
        public int[] MeshRows = [];
        public int[] MeshColumns = [];
        public int Meshes;
        public int VertexCount;
        public int Version = -1;
        public Vector2 Min = new(float.NaN, float.NaN);
        public Vector2 Max;
        public float Radius;
        public float Opacity;
        public int Used;
    }

    private static RibbonPaint[] BuildRibbonSlots()
    {
        var slots = new RibbonPaint[RibbonSlots];

        for (var i = 0; i < slots.Length; i++)
            slots[i] = new RibbonPaint();

        return slots;
    }

    // Kept per view. Two windows sharing a field would rebuild each other's every frame.
    private const int VignetteSlots = 4;

    private readonly VignetteGrid[] vignettes = BuildVignetteSlots();

    private int vignetteClock;

    private sealed class VignetteGrid
    {
        public Vector2[] Positions = [];
        public uint[] Colors = [];
        public int Rows = -1;
        public int Columns = -1;
        public Vector2 Size = new(float.NaN, float.NaN);
        public Vector2 Offset;
        public Vector2 Field;
        public float Radius;
        public float Opacity;
        public float Alpha;
        public Vector2 Inner;
        public Vector2 Outer;
        public int Used;
    }

    private static VignetteGrid[] BuildVignetteSlots()
    {
        var slots = new VignetteGrid[VignetteSlots];

        for (var i = 0; i < slots.Length; i++)
            slots[i] = new VignetteGrid();

        return slots;
    }

    private Vector2 viewMin;
    private Vector2 viewMax;
    private float radius;
    private int arcSegments;

    /// <summary>Creates a field.</summary>
    /// <param name="options">The options. A default set when <see langword="null"/>.</param>
    public NoireRibbonField(RibbonFieldOptions? options = null)
    {
        Options = options ?? new RibbonFieldOptions();
        leanColor = Options.RestLeanColor;
    }

    /// <summary>What the field computes with, read every frame.</summary>
    public RibbonFieldOptions Options { get; }

    /// <summary>
    /// Whether time, pointer smoothing and color smoothing are stopped, for reduced motion. Freezing clears every
    /// wave, and <see cref="Wave"/> does nothing while frozen.
    /// </summary>
    public bool Frozen
    {
        get => frozen;
        set
        {
            frozen = value;

            if (value)
                waveCount = 0;
        }
    }

    /// <summary>The field's clock, in seconds.</summary>
    public float Time => time;

    /// <summary>How many waves are travelling.</summary>
    public int WaveCount => waveCount;

    /// <summary>The smoothed color odd ribbons currently blend toward.</summary>
    public Vector3 LeanColor => leanColor;

    /// <summary>The top left of the field as last laid out, in screen pixels.</summary>
    public Vector2 FieldMin => fieldMin;

    /// <summary>The bottom right of the field as last laid out, in screen pixels.</summary>
    public Vector2 FieldMax => fieldMax;

    #region State

    /// <summary>
    /// Sets the color odd ribbons blend toward, reached smoothly.
    /// </summary>
    /// <param name="color">The color, or <see langword="null"/> for <see cref="RibbonFieldOptions.RestLeanColor"/>.</param>
    public void Lean(Vector4? color) => leanTarget = color.HasValue ? new Vector3(color.Value.X, color.Value.Y, color.Value.Z) : null;

    /// <summary>
    /// Starts a wave travelling out from a point across the field, and optionally leans toward a color.
    /// </summary>
    /// <param name="screenX">Where the wave starts, in screen pixels, or <see langword="null"/> for the field's centre.</param>
    /// <param name="color">A color to lean toward, as <see cref="Lean"/>.</param>
    public void Wave(float? screenX = null, Vector4? color = null)
    {
        if (frozen)
            return;

        if (waveCount == MaxWaves)
        {
            Array.Copy(waveX, 1, waveX, 0, MaxWaves - 1);
            Array.Copy(waveT, 1, waveT, 0, MaxWaves - 1);
            waveCount--;
        }

        waveX[waveCount] = screenX ?? ((fieldMin.X + fieldMax.X) * 0.5f);
        waveT[waveCount] = 0f;
        waveCount++;

        if (color.HasValue)
            Lean(color);
    }

    /// <summary>
    /// Returns the clock, the pointer, the lean color and the waves to their initial state.
    /// </summary>
    public void Reset()
    {
        time = 0f;
        pointerReady = false;
        leanColor = Options.RestLeanColor;
        leanTarget = null;
        waveCount = 0;
    }

    /// <summary>
    /// Lays the field out for this frame, reading the pointer from ImGui and counting it only while it is over the field.
    /// </summary>
    /// <param name="min">The field's top left, in screen pixels.</param>
    /// <param name="max">The field's bottom right, in screen pixels.</param>
    public void Update(Vector2 min, Vector2 max)
    {
        Vector2? mouse = null;

        if (UiDraw.Available && ImGui.IsMousePosValid())
        {
            var position = ImGui.GetMousePos();

            if (position.X >= min.X && position.Y >= min.Y && position.X < max.X && position.Y < max.Y)
                mouse = position;
        }

        Update(min, max, mouse);
    }

    /// <summary>
    /// Lays the field out for this frame. Advances time once per frame however often it is called.
    /// </summary>
    /// <param name="min">The field's top left, in screen pixels.</param>
    /// <param name="max">The field's bottom right, in screen pixels.</param>
    /// <param name="mouse">The pointer in screen pixels, or <see langword="null"/> when it is over none of the field's views.</param>
    public void Update(Vector2 min, Vector2 max, Vector2? mouse)
    {
        var options = Options;
        var size = max - min;

        fieldMin = min;
        fieldMax = max;
        hasField = size.X > 0f && size.Y > 0f;

        if (!hasField)
            return;

        var screen = options.LeanSpace == RibbonLeanSpace.Screen;
        var rest = screen ? min + (options.RestPoint * size) : options.RestPoint;

        if (!pointerReady)
        {
            pointer = rest;
            pointerReady = true;
        }

        var frame = NoireUI.FrameCount;

        if (frame != advancedFrame)
        {
            advancedFrame = frame;

            if (!frozen)
                Advance(options, mouse.HasValue ? (screen ? mouse.Value : (mouse.Value - min) / size) : rest);
        }

        Layout(options, min, size);
    }

    private void Advance(RibbonFieldOptions options, Vector2 target)
    {
        var dt = Math.Clamp(NoireUI.DeltaTime, 0f, MathF.Max(0f, options.MaxTimeStep));
        var frames = dt * options.ReferenceFrameRate;

        time += dt;
        pointer += (target - pointer) * Follow(options.PointerSmoothing, frames);
        leanColor += ((leanTarget ?? options.RestLeanColor) - leanColor) * Follow(options.ColorSmoothing, frames);

        var kept = 0;

        for (var index = 0; index < waveCount; index++)
        {
            var t = waveT[index] + dt;

            if (t >= options.WaveLife)
                continue;

            waveX[kept] = waveX[index];
            waveT[kept] = t;
            kept++;
        }

        waveCount = kept;
    }

    private static int Fingerprint(RibbonFieldOptions options)
    {
        var first = HashCode.Combine(
            options.Samples,
            options.Overscan,
            options.PrimaryFrequencyScale,
            options.SecondaryFrequencyScale,
            options.SecondaryAmplitude,
            options.SecondarySpeed,
            options.ThicknessFrequency,
            options.ThicknessSpeed);

        var second = HashCode.Combine(
            options.ThicknessBase,
            options.ThicknessSwing,
            options.LeanSpace,
            options.LeanSharpness,
            options.LeanStrength,
            options.RestPoint,
            options.WaveSpeed,
            options.WaveWidth);

        var third = HashCode.Combine(
            options.WaveAmplitude,
            options.WaveLife,
            options.EdgeAlpha,
            options.PeakStop,
            options.FadeStop,
            options.FadeAlpha,
            options.StrokeAlpha,
            options.StrokeWidth);

        return HashCode.Combine(first, second, third);
    }

    internal static float Follow(float fraction, float frames)
        => 1f - MathF.Pow(1f - Math.Clamp(fraction, 0f, 1f), MathF.Max(0f, frames));

    private void Layout(RibbonFieldOptions options, Vector2 min, Vector2 size)
    {
        var ribbons = options.Ribbons;
        var segments = Math.Max(1, options.Samples);
        var stride = segments + 1;

        sampleCount = stride;
        ribbonCount = ribbons.Count;

        if (waveCount == 0
            && laidOut
            && layoutTime == time
            && layoutPointer == pointer
            && layoutLean == leanColor
            && layoutMin == min
            && layoutSize == size
            && layoutScale == NoireUI.Scale
            && layoutRibbons == ribbonCount
            && layoutOptions == Fingerprint(options)
            && ReferenceEquals(layoutSource, ribbons))
        {
            return;
        }

        using var relayout = UiDraw.BeginMethod();

        laidOut = true;
        layoutTime = time;
        layoutPointer = pointer;
        layoutLean = leanColor;
        layoutMin = min;
        layoutSize = size;
        layoutScale = NoireUI.Scale;
        layoutRibbons = ribbonCount;
        layoutSource = ribbons;
        layoutOptions = Fingerprint(options);
        layoutVersion++;

        Ensure(ref sampleX, stride);
        Ensure(ref sampleTop, stride * ribbonCount);
        Ensure(ref sampleBottom, stride * ribbonCount);
        Ensure(ref ribbonRgb, ribbonCount);
        Ensure(ref ribbonAlpha, ribbonCount);

        var scale = NoireUI.Scale;
        var width = size.X;
        var height = size.Y;
        var overscan = options.Overscan * scale;
        var spanLeft = min.X - overscan;
        var spanWidth = width + (overscan * 2f);

        for (var i = 0; i < stride; i++)
            sampleX[i] = spanLeft + (i / (float)segments * spanWidth);

        var screen = options.LeanSpace == RibbonLeanSpace.Screen;
        var waveSpeed = options.WaveSpeed * scale;
        var waveWidth = options.WaveWidth * scale;
        var waveAmplitude = options.WaveAmplitude * scale;
        var life = MathF.Max(0.0001f, options.WaveLife);

        for (var k = 0; k < ribbonCount; k++)
        {
            var ribbon = ribbons[k];
            var sign = (k % 2) == 1 ? 1f : -1f;
            var primary = ribbon.Frequency * options.PrimaryFrequencyScale;
            var secondary = ribbon.Frequency * options.SecondaryFrequencyScale;
            var amplitude = height * ribbon.Amplitude;

            for (var i = 0; i < stride; i++)
            {
                var u = i / (float)segments;
                var px = sampleX[i];

                var y = (height * ribbon.Y)
                    + (MathF.Sin((u * primary) + (time * ribbon.Speed) + ribbon.Phase) * amplitude)
                    + (MathF.Sin((u * secondary) - (time * ribbon.Speed * options.SecondarySpeed)) * amplitude * options.SecondaryAmplitude);

                if (screen)
                {
                    var dx = (px - pointer.X) / width;
                    y += MathF.Exp(-dx * dx * options.LeanSharpness) * ((pointer.Y - min.Y) - y) * options.LeanStrength;
                }
                else
                {
                    var dx = u - pointer.X;
                    y += MathF.Exp(-dx * dx * options.LeanSharpness) * ((pointer.Y * height) - y) * options.LeanStrength;
                }

                for (var w = 0; w < waveCount; w++)
                {
                    var phase = (waveT[w] * waveSpeed) - MathF.Abs(px - waveX[w]);

                    if (phase > 0f && phase < waveWidth)
                        y += MathF.Sin(phase / waveWidth * MathF.PI) * waveAmplitude * (1f - (waveT[w] / life)) * sign;
                }

                var thickness = height * ribbon.Width
                    * (options.ThicknessBase + (options.ThicknessSwing * MathF.Sin((u * options.ThicknessFrequency) + (time * options.ThicknessSpeed) + k)));

                sampleTop[(k * stride) + i] = min.Y + y - thickness;
                sampleBottom[(k * stride) + i] = min.Y + y + thickness;
            }

            // Odd ribbons carry half the lean color.
            var color = ribbon.Color * 255f;

            if ((k % 2) == 1)
                color = (color * 0.5f) + (leanColor * 255f * 0.5f);

            ribbonRgb[k] = Channel(color.X) | (Channel(color.Y) << 8) | (Channel(color.Z) << 16);
            ribbonAlpha[k] = ribbon.Alpha;
        }
    }

    #endregion

    #region Drawing

    /// <summary>
    /// Paints the whole field (background, ribbons, vignette) into the current window, clipped to a rounded rectangle.
    /// </summary>
    /// <param name="min">The view's top left, in screen pixels.</param>
    /// <param name="max">The view's bottom right, in screen pixels.</param>
    /// <param name="rounding">The view's corner radius in real pixels.</param>
    /// <param name="opacity">A multiplier on every layer's opacity.</param>
    public void Draw(Vector2 min, Vector2 max, float rounding, float opacity = 1f)
    {
        using var draw = UiDraw.Begin();
        Draw(draw.List, min, max, rounding, opacity);
    }

    /// <summary>
    /// Paints the whole field (background, ribbons, vignette) into a draw list, clipped to a rounded rectangle.
    /// </summary>
    /// <param name="drawList">The draw list.</param>
    /// <param name="min">The view's top left, in screen pixels.</param>
    /// <param name="max">The view's bottom right, in screen pixels.</param>
    /// <param name="rounding">The view's corner radius in real pixels.</param>
    /// <param name="opacity">A multiplier on every layer's opacity.</param>
    public void Draw(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float opacity = 1f)
    {
        DrawBackground(drawList, min, max, rounding, opacity);
        DrawRibbons(drawList, min, max, rounding, opacity);
        DrawVignette(drawList, min, max, rounding, opacity);
    }

    /// <summary>
    /// Paints only the background gradient. See <see cref="Draw(ImDrawListPtr, Vector2, Vector2, float, float)"/>.
    /// </summary>
    /// <param name="drawList">The draw list.</param>
    /// <param name="min">The view's top left, in screen pixels.</param>
    /// <param name="max">The view's bottom right, in screen pixels.</param>
    /// <param name="rounding">The view's corner radius in real pixels.</param>
    /// <param name="opacity">A multiplier on the layer's opacity.</param>
    public void DrawBackground(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float opacity = 1f)
    {
        if (!CanDraw(drawList, min, max, opacity))
            return;

        using var scope = UiDraw.BeginMethod();

        SetView(min, max, rounding);

        var count = BuildRows(0f, fieldMin.Y, fieldMax.Y);
        EmitGrid(drawList, count, 1, vignette: false, opacity);
    }

    /// <summary>
    /// Paints only the vignette. See <see cref="Draw(ImDrawListPtr, Vector2, Vector2, float, float)"/>.
    /// </summary>
    /// <param name="drawList">The draw list.</param>
    /// <param name="min">The view's top left, in screen pixels.</param>
    /// <param name="max">The view's bottom right, in screen pixels.</param>
    /// <param name="rounding">The view's corner radius in real pixels.</param>
    /// <param name="opacity">A multiplier on the layer's opacity.</param>
    public void DrawVignette(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float opacity = 1f)
    {
        if (!CanDraw(drawList, min, max, opacity) || Options.VignetteAlpha <= 0f)
            return;

        using var scope = UiDraw.BeginMethod();

        SetView(min, max, rounding);

        var cell = MathF.Max(2f, Options.VignetteCellSize);
        var columns = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / cell));
        var count = BuildRows(cell, float.NaN, float.NaN);

        EmitGrid(drawList, count, columns, vignette: true, opacity);
    }

    /// <summary>
    /// Paints only the ribbons and their centre lines. See <see cref="Draw(ImDrawListPtr, Vector2, Vector2, float, float)"/>.
    /// </summary>
    /// <param name="drawList">The draw list.</param>
    /// <param name="min">The view's top left, in screen pixels.</param>
    /// <param name="max">The view's bottom right, in screen pixels.</param>
    /// <param name="rounding">The view's corner radius in real pixels.</param>
    /// <param name="opacity">A multiplier on the layer's opacity.</param>
    public void DrawRibbons(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float opacity = 1f)
    {
        if (!CanDraw(drawList, min, max, opacity) || ribbonCount == 0)
            return;

        using var scope = UiDraw.BeginMethod();

        SetView(min, max, rounding);

        recording = null;

        if (frozen)
        {
            var slot = RibbonSlot(min, max, rounding, opacity, out var cached);

            if (cached)
            {
                ReplayRibbons(drawList, slot);
                return;
            }

            slot.Meshes = 0;
            slot.VertexCount = 0;
            recording = slot;
        }

        var options = Options;
        var strokeHalf = MathF.Max(0.5f, options.StrokeWidth * NoireUI.Scale);
        var columns = BuildColumns();

        if (columns < 2)
        {
            recording = null;
            return;
        }

        for (var k = 0; k < ribbonCount; k++)
        {
            FillRibbonEdges(k, columns);

            var alpha = ribbonAlpha[k] * opacity;
            var rgb = ribbonRgb[k];

            // Feathered half a pixel either side of each edge, like an antialiased polygon.
            for (var i = 0; i < columns; i++)
            {
                var top = colTop[i];
                var bottom = colBottom[i];
                var fill = colGradient[i] * alpha;
                var half = MathF.Min(0.5f, (bottom - top) * 0.5f);

                SetRow(0, i, top - half, 0f);
                SetRow(1, i, top + half, fill);
                SetRow(2, i, bottom - half, fill);
                SetRow(3, i, bottom + half, 0f);
            }

            EmitMesh(drawList, columns, 4, rgb);

            var stroke = MathF.Min(1f, ribbonAlpha[k] * options.StrokeAlpha) * opacity;

            if (stroke <= 0f)
                continue;

            for (var i = 0; i < columns; i++)
            {
                var centre = (colTop[i] + colBottom[i]) * 0.5f;

                SetRow(0, i, centre - strokeHalf, 0f);
                SetRow(1, i, centre, stroke);
                SetRow(2, i, centre + strokeHalf, 0f);
            }

            EmitMesh(drawList, columns, 3, rgb);
        }

        if (recording != null)
        {
            Stamp(recording, min, max, rounding, opacity);
            recording = null;
        }
    }

    // Kept as computed. A reservation crossing into a new draw command renumbers its indices.
    private RibbonPaint? recording;

    private void Keep(int rows, int count)
    {
        var slot = recording;

        if (slot == null)
            return;

        var vertexCount = count * rows;

        Keep(ref slot.Positions, slot.VertexCount + vertexCount);
        Keep(ref slot.Colors, slot.VertexCount + vertexCount);
        Keep(ref slot.MeshRows, slot.Meshes + 1);
        Keep(ref slot.MeshColumns, slot.Meshes + 1);

        slot.MeshRows[slot.Meshes] = rows;
        slot.MeshColumns[slot.Meshes] = count;
        slot.Meshes++;
    }

    private void Stamp(RibbonPaint slot, Vector2 min, Vector2 max, float rounding, float opacity)
    {
        slot.Version = layoutVersion;
        slot.Min = min;
        slot.Max = max;
        slot.Radius = rounding;
        slot.Opacity = opacity;
    }

    private void ReplayRibbons(ImDrawListPtr drawList, RibbonPaint slot)
    {
        using var scope = UiDraw.BeginMethod();

        var white = ImGui.GetFontTexUvWhitePixel();
        var at = 0;

        for (var m = 0; m < slot.Meshes; m++)
        {
            var rows = slot.MeshRows[m];
            var count = slot.MeshColumns[m];
            var vertexCount = count * rows;
            var indexCount = (count - 1) * (rows - 1) * 6;

            drawList.PrimReserve(indexCount, vertexCount);

            var baseVertex = (ushort)drawList.VtxCurrentIdx;
            var vertices = drawList.VtxBuffer.AsSpan()[^vertexCount..];
            var indices = drawList.IdxBuffer.AsSpan()[^indexCount..];

            for (var v = 0; v < vertexCount; v++)
                vertices[v] = new ImDrawVert { Pos = slot.Positions[at + v], Uv = white, Col = slot.Colors[at + v] };

            WriteMeshIndices(indices, count, rows, baseVertex);
            NoireShapes.AdvancePrimWrite(drawList, vertexCount, indexCount);

            at += vertexCount;
        }
    }

    private RibbonPaint RibbonSlot(Vector2 min, Vector2 max, float rounding, float opacity, out bool cached)
    {
        var oldest = paints[0];

        foreach (var slot in paints)
        {
            if (slot.Version == layoutVersion
                && slot.Min == min
                && slot.Max == max
                && slot.Radius == rounding
                && slot.Opacity == opacity
                && slot.VertexCount > 0)
            {
                slot.Used = ++paintClock;
                cached = true;
                return slot;
            }

            if (slot.Used < oldest.Used)
                oldest = slot;
        }

        oldest.Used = ++paintClock;
        cached = false;

        return oldest;
    }

    private bool CanDraw(ImDrawListPtr drawList, Vector2 min, Vector2 max, float opacity)
        => hasField && !drawList.IsNull && opacity > 0f && max.X > min.X && max.Y > min.Y;

    private void SetRow(int row, int i, float y, float alpha)
    {
        meshY[(row * meshStride) + i] = y;
        meshAlpha[(row * meshStride) + i] = alpha;
    }

    #endregion

    #region Rounded view

    private void SetView(Vector2 min, Vector2 max, float rounding)
    {
        viewMin = min;
        viewMax = max;
        radius = Math.Clamp(rounding, 0f, MathF.Min(max.X - min.X, max.Y - min.Y) * 0.5f);

        arcSegments = radius <= 0.5f ? 0 : Math.Clamp((int)MathF.Ceiling(radius * MathF.PI * 0.5f / 3f), 2, MaxArcSegments);

        if (arcSegments == 0)
        {
            radius = 0f;
            arcX[0] = 0f;
            arcY[0] = 0f;
            return;
        }

        for (var k = 0; k <= arcSegments; k++)
        {
            var angle = MathF.PI * 0.5f * k / arcSegments;
            arcX[k] = radius - (radius * MathF.Cos(angle));
            arcY[k] = radius - (radius * MathF.Sin(angle));
        }

        arcX[arcSegments] = radius;
        arcY[arcSegments] = 0f;
    }

    private float TopAt(float x)
    {
        var dx = MathF.Max(0f, MathF.Min(x - viewMin.X, viewMax.X - x));

        if (arcSegments == 0 || dx >= radius)
            return viewMin.Y;

        for (var k = 0; k < arcSegments; k++)
        {
            if (dx > arcX[k + 1])
                continue;

            var s = (dx - arcX[k]) / (arcX[k + 1] - arcX[k]);
            return viewMin.Y + arcY[k] + ((arcY[k + 1] - arcY[k]) * s);
        }

        return viewMin.Y;
    }

    private float InsetAt(float y)
    {
        var dy = MathF.Max(0f, MathF.Min(y - viewMin.Y, viewMax.Y - y));

        if (arcSegments == 0 || dy >= radius)
            return 0f;

        for (var k = 0; k < arcSegments; k++)
        {
            if (dy < arcY[k + 1])
                continue;

            var span = arcY[k] - arcY[k + 1];
            var s = span > 0f ? (arcY[k] - dy) / span : 0f;
            return arcX[k] + ((arcX[k + 1] - arcX[k]) * s);
        }

        return radius;
    }

    private int BuildRows(float cell, float splitA, float splitB)
    {
        var middle = cell > 0f ? Math.Max(0, (int)MathF.Ceiling((viewMax.Y - viewMin.Y - (radius * 2f)) / cell) - 1) : 0;
        Ensure(ref rows, ((arcSegments + 1) * 2) + middle + 2);

        var count = 0;

        for (var k = arcSegments; k >= 0; k--)
            rows[count++] = viewMin.Y + arcY[k];

        if (middle > 0)
        {
            var from = viewMin.Y + radius;
            var step = (viewMax.Y - radius - from) / (middle + 1);

            for (var j = 1; j <= middle; j++)
                rows[count++] = from + (step * j);
        }

        for (var k = 0; k <= arcSegments; k++)
            rows[count++] = viewMax.Y - arcY[k];

        if (splitA > viewMin.Y && splitA < viewMax.Y)
            rows[count++] = splitA;

        if (splitB > viewMin.Y && splitB < viewMax.Y)
            rows[count++] = splitB;

        return SortUnique(rows.AsSpan(0, count));
    }

    private void EmitGrid(ImDrawListPtr drawList, int rowCount, int columns, bool vignette, float opacity)
    {
        if (rowCount < 2)
            return;

        var perRow = columns + 1;
        var vertexCount = rowCount * perRow;

        // Every vignette vertex costs a two-circle gradient solve with a square root.
        if (vignette)
        {
            var slot = VignetteSlot(rowCount, columns, opacity, out var cached);

            if (!cached)
            {
                Ensure(ref slot.Positions, vertexCount);
                Ensure(ref slot.Colors, vertexCount);
                FillGrid(rowCount, columns, true, opacity, slot.Positions, slot.Colors);
                Stamp(slot, rowCount, columns, opacity);
            }

            slot.Used = ++vignetteClock;
            EmitGridBuffer(drawList, rowCount, columns, slot.Positions, slot.Colors);
            return;
        }

        Ensure(ref gridPos, vertexCount);
        Ensure(ref gridCol, vertexCount);

        FillGrid(rowCount, columns, false, opacity, gridPos, gridCol);
        EmitGridBuffer(drawList, rowCount, columns, gridPos, gridCol);
    }

    // Relative to the view's top left. A view that only moved can be replayed.
    private void FillGrid(int rowCount, int columns, bool vignette, float opacity, Vector2[] positions, uint[] colors)
    {
        var options = Options;
        var fieldSize = fieldMax - fieldMin;
        var top = options.BackgroundTop;
        var bottom = options.BackgroundBottom;

        var centreX = (fieldMin.X + fieldMax.X) * 0.5f;
        var innerCentre = new Vector2(centreX, fieldMin.Y + (fieldSize.Y * options.VignetteInnerCenterY));
        var outerCentre = new Vector2(centreX, fieldMin.Y + (fieldSize.Y * options.VignetteOuterCenterY));
        var innerRadius = MathF.Min(fieldSize.X, fieldSize.Y) * options.VignetteInnerRadius;
        var outerRadius = MathF.Max(fieldSize.X, fieldSize.Y) * options.VignetteOuterRadius;
        var vignetteAlpha = options.VignetteAlpha * opacity;

        var v = 0;

        for (var r = 0; r < rowCount; r++)
        {
            var y = rows[r];
            var inset = InsetAt(y);
            var left = viewMin.X + inset;
            var right = viewMax.X - inset;

            var rowColor = 0u;

            if (!vignette)
            {
                var t = fieldSize.Y > 0f ? Math.Clamp((y - fieldMin.Y) / fieldSize.Y, 0f, 1f) : 0f;
                var color = Vector4.Lerp(top, bottom, t);
                rowColor = Pack(color.X, color.Y, color.Z, color.W * opacity);
            }

            for (var c = 0; c <= columns; c++)
            {
                var x = left + ((right - left) * c / columns);
                var packed = rowColor;

                if (vignette)
                {
                    var t = ConicalT(new Vector2(x, y), innerCentre, innerRadius, outerCentre, outerRadius);
                    packed = Pack(0f, 0f, 0f, Math.Clamp(t, 0f, 1f) * vignetteAlpha);
                }

                positions[v] = new Vector2(x - viewMin.X, y - viewMin.Y);
                colors[v] = packed;
                v++;
            }
        }
    }

    private void EmitGridBuffer(ImDrawListPtr drawList, int rowCount, int columns, Vector2[] positions, uint[] colors)
    {
        var perRow = columns + 1;
        var rowsPerChunk = Math.Max(2, MaxChunkVertices / perRow);
        var white = ImGui.GetFontTexUvWhitePixel();
        var origin = viewMin;
        var start = 0;

        while (start < rowCount - 1)
        {
            var end = Math.Min(rowCount - 1, start + rowsPerChunk - 1);
            var chunkRows = end - start + 1;
            var vertexCount = chunkRows * perRow;
            var indexCount = (chunkRows - 1) * columns * 6;

            drawList.PrimReserve(indexCount, vertexCount);

            var baseVertex = drawList.VtxCurrentIdx;
            var vertices = drawList.VtxBuffer.AsSpan()[^vertexCount..];
            var indices = drawList.IdxBuffer.AsSpan()[^indexCount..];

            var source = start * perRow;

            for (var v = 0; v < vertexCount; v++)
            {
                vertices[v] = new ImDrawVert
                {
                    Pos = origin + positions[source + v],
                    Uv = white,
                    Col = colors[source + v],
                };
            }

            var written = 0;

            for (var r = 0; r < chunkRows - 1; r++)
            {
                for (var c = 0; c < columns; c++)
                {
                    var a = (ushort)(baseVertex + (r * perRow) + c);
                    var b = (ushort)(a + 1);
                    var d = (ushort)(a + perRow);
                    var e = (ushort)(d + 1);

                    indices[written++] = a;
                    indices[written++] = b;
                    indices[written++] = e;
                    indices[written++] = a;
                    indices[written++] = e;
                    indices[written++] = d;
                }
            }

            NoireShapes.AdvancePrimWrite(drawList, vertexCount, indexCount);
            start = end;
        }
    }

    private VignetteGrid VignetteSlot(int rowCount, int columns, float opacity, out bool cached)
    {
        var options = Options;
        var size = viewMax - viewMin;
        var offset = viewMin - fieldMin;
        var field = fieldMax - fieldMin;
        var inner = new Vector2(options.VignetteInnerCenterY, options.VignetteInnerRadius);
        var outer = new Vector2(options.VignetteOuterCenterY, options.VignetteOuterRadius);

        var oldest = vignettes[0];

        foreach (var slot in vignettes)
        {
            if (slot.Rows == rowCount
                && slot.Columns == columns
                && slot.Size == size
                && slot.Offset == offset
                && slot.Field == field
                && slot.Radius == radius
                && slot.Opacity == opacity
                && slot.Alpha == options.VignetteAlpha
                && slot.Inner == inner
                && slot.Outer == outer)
            {
                cached = true;
                return slot;
            }

            if (slot.Used < oldest.Used)
                oldest = slot;
        }

        cached = false;
        return oldest;
    }

    private void Stamp(VignetteGrid slot, int rowCount, int columns, float opacity)
    {
        var options = Options;

        slot.Rows = rowCount;
        slot.Columns = columns;
        slot.Size = viewMax - viewMin;
        slot.Offset = viewMin - fieldMin;
        slot.Field = fieldMax - fieldMin;
        slot.Radius = radius;
        slot.Opacity = opacity;
        slot.Alpha = options.VignetteAlpha;
        slot.Inner = new Vector2(options.VignetteInnerCenterY, options.VignetteInnerRadius);
        slot.Outer = new Vector2(options.VignetteOuterCenterY, options.VignetteOuterRadius);
    }

    // The largest t whose circle passes through the point with a non-negative radius, or -1 when none does.
    internal static float ConicalT(Vector2 point, Vector2 c0, float r0, Vector2 c1, float r1)
    {
        var cd = c1 - c0;
        var pd = point - c0;
        var dr = r1 - r0;

        var a = Vector2.Dot(cd, cd) - (dr * dr);
        var b = Vector2.Dot(pd, cd) + (r0 * dr);
        var c = Vector2.Dot(pd, pd) - (r0 * r0);

        if (MathF.Abs(a) < 1e-6f)
        {
            if (MathF.Abs(b) < 1e-9f)
                return -1f;

            var single = c / (2f * b);
            return r0 + (single * dr) >= 0f ? single : -1f;
        }

        var discriminant = (b * b) - (a * c);

        if (discriminant < 0f)
            return -1f;

        var root = MathF.Sqrt(discriminant);
        var t1 = (b + root) / a;
        var t2 = (b - root) / a;
        var high = MathF.Max(t1, t2);
        var low = MathF.Min(t1, t2);

        if (r0 + (high * dr) >= 0f)
            return high;

        return r0 + (low * dr) >= 0f ? low : -1f;
    }

    #endregion

    #region Ribbon bands

    private int BuildColumns()
    {
        var options = Options;
        var stride = sampleCount;
        var first = sampleX[0];
        var last = sampleX[stride - 1];
        var step = (last - first) / (stride - 1);
        var left = MathF.Max(viewMin.X, first);
        var right = MathF.Min(viewMax.X, last);

        if (right <= left || step <= 0f)
            return 0;

        var fieldWidth = fieldMax.X - fieldMin.X;
        var count = 0;

        breaks[count++] = fieldMin.X;
        breaks[count++] = fieldMin.X + (fieldWidth * options.PeakStop);
        breaks[count++] = fieldMin.X + (fieldWidth * options.FadeStop);
        breaks[count++] = fieldMax.X;
        breaks[count++] = left;
        breaks[count++] = right;

        for (var j = 0; j <= arcSegments; j++)
        {
            breaks[count++] = viewMin.X + arcX[j];
            breaks[count++] = viewMax.X - arcX[j];
        }

        var breakCount = SortUnique(breaks.AsSpan(0, count));

        Ensure(ref colX, stride + breakCount);
        Ensure(ref colTop, stride + breakCount);
        Ensure(ref colBottom, stride + breakCount);
        Ensure(ref colGradient, stride + breakCount);
        Ensure(ref colEdgeTop, stride + breakCount);
        Ensure(ref colEdgeBottom, stride + breakCount);
        Ensure(ref colSegment, stride + breakCount);
        Ensure(ref colFraction, stride + breakCount);
        if (meshStride < stride + breakCount)
        {
            meshStride = Math.Max(stride + breakCount, meshStride * 2);
            meshY = new float[MaxMeshRows * meshStride];
            meshAlpha = new float[MaxMeshRows * meshStride];
        }

        var columns = 0;
        var i = 0;
        var b = 0;
        var previous = float.NegativeInfinity;

        while (i < stride || b < breakCount)
        {
            float x;

            if (b >= breakCount || (i < stride && sampleX[i] <= breaks[b]))
                x = sampleX[i++];
            else
                x = breaks[b++];

            if (x < left - 0.0001f || x > right + 0.0001f || x - previous < 0.001f)
                continue;

            x = Math.Clamp(x, left, right);
            previous = x;

            var segment = Math.Clamp((int)((x - first) / step), 0, stride - 2);

            colX[columns] = x;
            colSegment[columns] = segment;
            colFraction[columns] = Math.Clamp((x - sampleX[segment]) / step, 0f, 1f);
            colGradient[columns] = GradientAt(options, (x - fieldMin.X) / fieldWidth);
            colEdgeTop[columns] = TopAt(x);
            colEdgeBottom[columns] = viewMax.Y - (colEdgeTop[columns] - viewMin.Y);
            columns++;
        }

        return columns;
    }

    private void FillRibbonEdges(int k, int columns)
    {
        var stride = sampleCount;
        var offset = k * stride;

        for (var i = 0; i < columns; i++)
        {
            var at = offset + colSegment[i];
            var s = colFraction[i];

            colTop[i] = sampleTop[at] + ((sampleTop[at + 1] - sampleTop[at]) * s);
            colBottom[i] = sampleBottom[at] + ((sampleBottom[at + 1] - sampleBottom[at]) * s);
        }
    }

    // Padded past both ends like a canvas gradient.
    internal static float GradientAt(RibbonFieldOptions options, float s)
    {
        var edge = options.EdgeAlpha;
        var peak = Math.Clamp(options.PeakStop, 0.0001f, 0.9998f);
        var fade = Math.Clamp(options.FadeStop, peak, 0.9999f);

        if (s <= 0f || s >= 1f)
            return edge;

        if (s < peak)
            return edge + ((1f - edge) * (s / peak));

        if (s < fade)
            return 1f + ((options.FadeAlpha - 1f) * ((s - peak) / MathF.Max(0.0001f, fade - peak)));

        return options.FadeAlpha + ((edge - options.FadeAlpha) * ((s - fade) / (1f - fade)));
    }

    // A column is added wherever a row crosses the view's edge. Rows pushed outside collapse onto the edge.
    private void EmitMesh(ImDrawListPtr drawList, int columns, int rows, uint rgb)
    {
        var capacity = columns * 5;

        Ensure(ref outX, capacity);

        if (outStride < capacity)
        {
            outStride = Math.Max(capacity, outStride * 2);
            outY = new float[MaxMeshRows * outStride];
            outAlpha = new float[MaxMeshRows * outStride];
        }

        Span<float> crossings = stackalloc float[MaxMeshRows * 2];
        var count = 0;

        for (var i = 0; i < columns; i++)
        {
            ClipColumn(ref count, rows, colX[i], i, 0f, colEdgeTop[i], colEdgeBottom[i]);

            if (i == columns - 1)
                break;

            var edgeTop0 = colEdgeTop[i];
            var edgeBottom0 = colEdgeBottom[i];
            var edgeTop1 = colEdgeTop[i + 1];
            var edgeBottom1 = colEdgeBottom[i + 1];

            var found = 0;

            for (var r = 0; r < rows; r++)
            {
                var y0 = meshY[(r * meshStride) + i];
                var y1 = meshY[(r * meshStride) + i + 1];

                Crossing(crossings, ref found, y0 - edgeTop0, y1 - edgeTop1);
                Crossing(crossings, ref found, y0 - edgeBottom0, y1 - edgeBottom1);
            }

            if (found == 0)
                continue;

            crossings[..found].Sort();

            for (var c = 0; c < found; c++)
            {
                var t = crossings[c];

                ClipColumn(
                    ref count,
                    rows,
                    colX[i] + ((colX[i + 1] - colX[i]) * t),
                    i,
                    t,
                    edgeTop0 + ((edgeTop1 - edgeTop0) * t),
                    edgeBottom0 + ((edgeBottom1 - edgeBottom0) * t));
            }
        }

        if (count < 2)
        {
            recording = null;
            return;
        }

        var vertexCount = count * rows;
        var indexCount = (count - 1) * (rows - 1) * 6;

        drawList.PrimReserve(indexCount, vertexCount);

        var baseVertex = (ushort)drawList.VtxCurrentIdx;
        var vertices = drawList.VtxBuffer.AsSpan()[^vertexCount..];
        var indices = drawList.IdxBuffer.AsSpan()[^indexCount..];
        var white = ImGui.GetFontTexUvWhitePixel();

        Keep(rows, count);

        var slot = recording;
        var kept = slot != null ? slot.VertexCount : 0;

        for (var i = 0; i < count; i++)
        {
            var x = outX[i];

            for (var r = 0; r < rows; r++)
            {
                var at = (r * outStride) + i;
                var position = new Vector2(x, outY[at]);
                var colour = rgb | (Alpha(outAlpha[at]) << 24);
                var v = (i * rows) + r;

                vertices[v] = new ImDrawVert { Pos = position, Uv = white, Col = colour };

                if (slot != null)
                {
                    slot.Positions[kept + v] = position;
                    slot.Colors[kept + v] = colour;
                }
            }
        }

        if (slot != null)
            slot.VertexCount = kept + vertexCount;

        WriteMeshIndices(indices, count, rows, baseVertex);
        NoireShapes.AdvancePrimWrite(drawList, vertexCount, indexCount);
    }

    private static void WriteMeshIndices(Span<ushort> indices, int count, int rows, ushort baseVertex)
    {
        var written = 0;

        for (var i = 0; i < count - 1; i++)
        {
            for (var r = 0; r < rows - 1; r++)
            {
                var a = (ushort)(baseVertex + (i * rows) + r);
                var b = (ushort)(a + 1);
                var c = (ushort)(a + rows);
                var d = (ushort)(c + 1);

                indices[written++] = a;
                indices[written++] = b;
                indices[written++] = c;
                indices[written++] = b;
                indices[written++] = d;
                indices[written++] = c;
            }
        }
    }

    private static void Crossing(Span<float> crossings, ref int found, float before, float after)
    {
        if ((before < 0f && after > 0f) || (before > 0f && after < 0f))
            crossings[found++] = before / (before - after);
    }

    private void ClipColumn(ref int count, int rows, float x, int i, float t, float edgeTop, float edgeBottom)
    {
        for (var r = 0; r < rows; r++)
        {
            var y = Row(r, i, t, out var alpha);

            if (y < edgeTop)
            {
                y = edgeTop;
                alpha = SampleAlpha(rows, i, t, edgeTop);
            }
            else if (y > edgeBottom)
            {
                y = edgeBottom;
                alpha = SampleAlpha(rows, i, t, edgeBottom);
            }

            var to = (r * outStride) + count;
            outY[to] = y;
            outAlpha[to] = alpha;
        }

        outX[count] = x;
        count++;
    }

    // The row straddling the view's edge carries the value a hard cut would.
    private float SampleAlpha(int rows, int i, float t, float y)
    {
        var previousY = Row(0, i, t, out var previousAlpha);

        if (y <= previousY)
            return previousAlpha;

        for (var r = 1; r < rows; r++)
        {
            var nextY = Row(r, i, t, out var nextAlpha);

            if (y <= nextY)
            {
                var span = nextY - previousY;
                return span > 1e-5f ? previousAlpha + ((nextAlpha - previousAlpha) * ((y - previousY) / span)) : nextAlpha;
            }

            previousY = nextY;
            previousAlpha = nextAlpha;
        }

        return previousAlpha;
    }

    private float Row(int r, int i, float t, out float alpha)
    {
        var at = (r * meshStride) + i;

        if (t == 0f)
        {
            alpha = meshAlpha[at];
            return meshY[at];
        }

        alpha = meshAlpha[at] + ((meshAlpha[at + 1] - meshAlpha[at]) * t);
        return meshY[at] + ((meshY[at + 1] - meshY[at]) * t);
    }

    #endregion

    #region Helpers

    private static int SortUnique(Span<float> values)
    {
        if (values.Length == 0)
            return 0;

        values.Sort();

        var kept = 1;

        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] - values[kept - 1] < 0.01f)
                continue;

            values[kept++] = values[i];
        }

        return kept;
    }

    private static void Ensure<T>(ref T[] buffer, int size)
    {
        if (buffer.Length < size)
            buffer = new T[Math.Max(size, buffer.Length * 2)];
    }

    private static void Keep<T>(ref T[] buffer, int size)
    {
        if (buffer.Length < size)
            Array.Resize(ref buffer, Math.Max(size, buffer.Length * 2));
    }

    private static uint Channel(float value) => (uint)Math.Clamp(MathF.Round(value, MidpointRounding.AwayFromZero), 0f, 255f);

    private static uint Alpha(float alpha) => (uint)((Math.Clamp(alpha, 0f, 1f) * 255f) + 0.5f);

    private static uint Pack(float r, float g, float b, float a)
        => (uint)((Math.Clamp(r, 0f, 1f) * 255f) + 0.5f)
            | ((uint)((Math.Clamp(g, 0f, 1f) * 255f) + 0.5f) << 8)
            | ((uint)((Math.Clamp(b, 0f, 1f) * 255f) + 0.5f) << 16)
            | (Alpha(a) << 24);

    #endregion
}
