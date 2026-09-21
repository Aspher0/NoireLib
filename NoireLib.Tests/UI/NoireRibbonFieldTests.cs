using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks <see cref="NoireRibbonField"/> to the canvas math it reproduces (two-circle radial gradient, padded gradient
/// stops, frame-rate independent smoothing), to geometry that never leaves the rounded view, and to a frame that
/// allocates nothing.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireRibbonFieldTests : IClassFixture<UiHarness>
{
    private static readonly Vector2 ViewMin = new(40f, 30f);
    private static readonly Vector2 ViewMax = new(560f, 430f);
    private const float Radius = 16f;

    private static readonly NoireRibbonField Single = new();

    private static readonly NoireRibbonField Group = new(new RibbonFieldOptions
    {
        Samples = 90,
        Overscan = 0f,
        PrimaryFrequencyScale = 1.6f,
        SecondaryFrequencyScale = 3.7f,
        ThicknessFrequency = 5f,
        LeanSpace = RibbonLeanSpace.Screen,
        LeanSharpness = 60f,
        EdgeAlpha = 0.25f,
        FadeAlpha = 0.85f,
        VignetteInnerRadius = 0.3f,
        VignetteOuterRadius = 0.62f,
        VignetteAlpha = 0.55f,
    });

    private static float worstOutside;

    private readonly UiHarness harness;

    public NoireRibbonFieldTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void ConicalT_IsZeroOnTheInnerCircleAndOneOnTheOuter()
    {
        var c0 = new Vector2(100f, 80f);
        var c1 = new Vector2(100f, 100f);

        NoireRibbonField.ConicalT(c0 + new Vector2(0f, -50f), c0, 50f, c1, 300f).Should().BeApproximately(0f, 0.0001f);
        NoireRibbonField.ConicalT(c1 + new Vector2(300f, 0f), c0, 50f, c1, 300f).Should().BeApproximately(1f, 0.0001f);
        NoireRibbonField.ConicalT(c0, c0, 50f, c1, 300f).Should().BeLessThan(0f, "the centre is inside the clear circle");
    }

    [Fact]
    public void GradientAt_MatchesTheCanvasStops()
    {
        var options = new RibbonFieldOptions { EdgeAlpha = 0.25f, FadeAlpha = 0.85f };

        NoireRibbonField.GradientAt(options, -0.2f).Should().Be(0.25f);
        NoireRibbonField.GradientAt(options, 0.3f).Should().BeApproximately(1f, 0.0001f);
        NoireRibbonField.GradientAt(options, 0.7f).Should().BeApproximately(0.85f, 0.0001f);
        NoireRibbonField.GradientAt(options, 0.5f).Should().BeApproximately(0.925f, 0.0001f);
        NoireRibbonField.GradientAt(options, 1.3f).Should().Be(0.25f);
    }

    [Theory]
    [InlineData(60f)]
    [InlineData(144f)]
    [InlineData(30f)]
    public void Follow_ConvergesAtTheSameRateAtAnyFrameRate(float fps)
    {
        var frames = (int)fps;
        var remaining = 1f;
        var step = NoireRibbonField.Follow(0.03f, 60f / fps);

        for (var i = 0; i < frames; i++)
            remaining *= 1f - step;

        remaining.Should().BeApproximately(MathF.Pow(0.97f, 60f), 0.0005f, "one second of smoothing is one second at any rate");
    }

    [Fact]
    public void Wave_IsIgnoredWhileFrozen()
    {
        var field = new NoireRibbonField();

        field.Wave(100f);
        field.WaveCount.Should().Be(1);

        field.Frozen = true;
        field.WaveCount.Should().Be(0);

        field.Wave(100f);
        field.WaveCount.Should().Be(0);
    }

    [Fact]
    public void Draw_AllocatesNothing()
    {
        var result = harness.Draw(static () => DrawBoth(), warmUpFrames: 2);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Draw_StaysInsideTheRoundedView()
    {
        worstOutside = 0f;

        harness.Draw(static () =>
        {
            var list = ImGui.GetWindowDrawList();
            var before = list.VtxBuffer.AsSpan().Length;

            Single.Update(ViewMin, ViewMax, new Vector2(80f, 60f));
            Single.Wave(ViewMin.X + 10f);
            Single.Draw(list, ViewMin, ViewMax, Radius);

            var vertices = list.VtxBuffer.AsSpan();

            for (var i = before; i < vertices.Length; i++)
                worstOutside = MathF.Max(worstOutside, Outside(vertices[i].Pos));
        });

        worstOutside.Should().BeLessThan(0.01f, "every layer is geometry already clipped to the rounded rectangle");
    }

    [Fact]
    public void FrozenField_ReplaysTheGeometryItWouldHaveRebuilt()
    {
        keptField = NewField();
        keptVertices = [];
        freshVertices = [];

        harness.Draw(static () => Capture(keptField!, false), warmUpFrames: 3);
        harness.Draw(static () => Capture(NewField(), true), warmUpFrames: 0);

        keptVertices.Should().NotBeEmpty();
        freshVertices.Should().HaveCount(keptVertices.Length);

        for (var i = 0; i < keptVertices.Length; i++)
        {
            keptVertices[i].Pos.Should().Be(freshVertices[i].Pos, $"vertex {i} position");
            keptVertices[i].Col.Should().Be(freshVertices[i].Col, $"vertex {i} colour");
            keptVertices[i].Uv.Should().Be(freshVertices[i].Uv, $"vertex {i} uv");
        }
    }

    [Fact]
    public void FrozenField_RebuildsWhenTheViewMoves()
    {
        keptField = NewField();
        keptVertices = [];
        freshVertices = [];

        harness.Draw(static () => Capture(keptField!, false), warmUpFrames: 3);
        harness.Draw(static () => CaptureMoved(keptField!, false), warmUpFrames: 1);
        harness.Draw(static () => CaptureMoved(NewField(), true), warmUpFrames: 0);

        keptVertices.Should().NotBeEmpty();
        freshVertices.Should().HaveCount(keptVertices.Length);

        for (var i = 0; i < keptVertices.Length; i++)
            keptVertices[i].Pos.Should().Be(freshVertices[i].Pos, $"vertex {i} position");
    }

    // Locks the painted picture. If this fails on purpose, rasterise the new picture and put its hash here.
    [Fact]
    public void Ribbons_PaintTheLockedPicture()
    {
        rasterField = new NoireRibbonField(new RibbonFieldOptions()) { Frozen = true };
        rasterVertices = [];
        rasterIndices = [];

        harness.Draw(static () => CaptureForRaster(), warmUpFrames: 0);

        var raster = new UiTriangleRaster(ViewMin, ViewMax);
        raster.Add(rasterVertices, rasterIndices, rasterBase);

        Fnv(raster.ToBytes()).Should().Be(PaintedPicture);
    }

    private const ulong PaintedPicture = 0x28E7C9B203E8ABD5UL;

    private static NoireRibbonField? rasterField;
    private static ImDrawVert[] rasterVertices = [];
    private static ushort[] rasterIndices = [];
    private static int rasterBase;

    private static void CaptureForRaster()
    {
        var list = ImGui.GetWindowDrawList();

        rasterField!.Update(ViewMin, ViewMax, new Vector2(200f, 200f));

        var vertexStart = list.VtxBuffer.Size;
        var indexStart = list.IdxBuffer.Size;
        rasterBase = (int)list.VtxCurrentIdx;

        rasterField.DrawRibbons(list, ViewMin, ViewMax, Radius, 0.8f);

        rasterVertices = list.VtxBuffer.AsSpan().Slice(vertexStart, list.VtxBuffer.Size - vertexStart).ToArray();
        rasterIndices = list.IdxBuffer.AsSpan().Slice(indexStart, list.IdxBuffer.Size - indexStart).ToArray();
    }

    private static ulong Fnv(byte[] bytes)
    {
        var hash = 0xcbf29ce484222325UL;

        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= 0x100000001b3UL;
        }

        return hash;
    }

    private static NoireRibbonField? keptField;
    private static ImDrawVert[] keptVertices = [];
    private static ImDrawVert[] freshVertices = [];

    private static readonly Vector2 MovedMin = ViewMin + new Vector2(37f, 19f);
    private static readonly Vector2 MovedMax = ViewMax + new Vector2(37f, 19f);

    private static NoireRibbonField NewField() => new(new RibbonFieldOptions()) { Frozen = true };

    private static void Capture(NoireRibbonField field, bool fresh) => Capture(field, ViewMin, ViewMax, fresh);

    private static void CaptureMoved(NoireRibbonField field, bool fresh) => Capture(field, MovedMin, MovedMax, fresh);

    private static void Capture(NoireRibbonField field, Vector2 min, Vector2 max, bool fresh)
    {
        var list = ImGui.GetWindowDrawList();

        field.Frozen = true;
        field.Update(min, max, new Vector2(200f, 200f));

        var start = list.VtxBuffer.Size;
        field.DrawRibbons(list, min, max, Radius, 0.8f);
        var count = list.VtxBuffer.Size - start;

        var taken = list.VtxBuffer.AsSpan().Slice(start, count).ToArray();

        if (fresh)
            freshVertices = taken;
        else
            keptVertices = taken;
    }

    private static void DrawBoth()
    {
        var list = ImGui.GetWindowDrawList();

        Single.Update(ViewMin, ViewMax, new Vector2(200f, 200f));
        Single.Draw(list, ViewMin, ViewMax, Radius, 0.8f);

        Group.Update(ViewMin - new Vector2(330f, 0f), ViewMax + new Vector2(330f, 0f), null);
        Group.Draw(list, ViewMin, ViewMax, Radius);
        Group.Draw(list, ViewMax + new Vector2(8f, -300f), ViewMax + new Vector2(300f, 0f), Radius);
    }

    private static float Outside(Vector2 point)
    {
        var dx = MathF.Max(MathF.Max(ViewMin.X - point.X, point.X - ViewMax.X), 0f);
        var dy = MathF.Max(MathF.Max(ViewMin.Y - point.Y, point.Y - ViewMax.Y), 0f);

        if (dx > 0f || dy > 0f)
            return MathF.Max(dx, dy);

        var cx = Math.Clamp(point.X, ViewMin.X + Radius, ViewMax.X - Radius);
        var cy = Math.Clamp(point.Y, ViewMin.Y + Radius, ViewMax.Y - Radius);

        return MathF.Max(0f, Vector2.Distance(point, new Vector2(cx, cy)) - Radius);
    }
}
