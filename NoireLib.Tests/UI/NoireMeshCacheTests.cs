using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Replays move the recorded drawing, repaints keep fringes, hits allocate nothing, and split recordings are refused.</summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireMeshCacheTests : IClassFixture<UiHarness>
{
    private static readonly Vector2 From = new(40f, 50f);
    private static readonly Vector2 To = new(170.5f, 90.25f);

    private readonly UiHarness harness;

    public NoireMeshCacheTests(UiHarness harness) => this.harness = harness;

    private static void Circle(ImDrawListPtr list, Vector2 origin, uint color)
        => list.AddCircleFilled(origin + new Vector2(20f, 20f), 12f, color, 24);

    // Asks for the key on two frames, the second of which records it, then returns the recorded vertices.
    private ImDrawVert[] Recorded(NoireMeshCache<int> cache, int key, uint color)
    {
        var recorded = Array.Empty<ImDrawVert>();

        for (var frame = 0; frame < 2; frame++)
        {
            harness.Draw(() =>
            {
                var list = ImGui.GetWindowDrawList();

                if (cache.TryReplay(list, key, From, color))
                    return;

                var start = list.VtxBuffer.Size;

                using (cache.Record(list, key, From, color))
                    Circle(list, From, color);

                recorded = list.VtxBuffer.AsSpan()[start..].ToArray();
            }, warmUpFrames: 0);
        }

        return recorded;
    }

    [Fact]
    public void Replay_IsTheRecordingMovedToTheNewOrigin()
    {
        var cache = new NoireMeshCache<int>();
        var recorded = Recorded(cache, 1, 0xFF3366CCu);
        var replayed = Array.Empty<ImDrawVert>();
        var hit = false;

        harness.Draw(() =>
        {
            var list = ImGui.GetWindowDrawList();
            var start = list.VtxBuffer.Size;

            hit = cache.TryReplay(list, 1, To, 0xFF3366CCu);
            replayed = list.VtxBuffer.AsSpan()[start..].ToArray();
        }, warmUpFrames: 0);

        hit.Should().BeTrue();
        replayed.Should().HaveCount(recorded.Length);

        for (var i = 0; i < recorded.Length; i++)
        {
            (replayed[i].Pos - (recorded[i].Pos - From + To)).Length().Should().BeLessThan(0.001f);
            replayed[i].Uv.Should().Be(recorded[i].Uv);
            replayed[i].Col.Should().Be(recorded[i].Col);
        }
    }

    [Fact]
    public void Repaint_KeepsTheFringeTransparent_AndScalesOpacity()
    {
        var cache = new NoireMeshCache<int>();
        var recorded = Recorded(cache, 2, 0xFFFFFFFFu);
        var replayed = Array.Empty<ImDrawVert>();

        harness.Draw(() =>
        {
            var list = ImGui.GetWindowDrawList();
            var start = list.VtxBuffer.Size;

            cache.TryReplay(list, 2, From, 0x800000FFu).Should().BeTrue();
            replayed = list.VtxBuffer.AsSpan()[start..].ToArray();
        }, warmUpFrames: 0);

        recorded.Should().Contain(v => (v.Col >> 24) == 0u, "an antialiased fill has a transparent fringe");

        for (var i = 0; i < recorded.Length; i++)
        {
            (replayed[i].Col & 0x00FFFFFFu).Should().Be(0x0000FFu);
            var expected = (recorded[i].Col >> 24) * 0x80u / 0xFFu;
            ((int)(replayed[i].Col >> 24)).Should().BeInRange((int)expected - 1, (int)expected + 1);
        }
    }

    [Fact]
    public void Replay_AllocatesNothing()
    {
        var cache = new NoireMeshCache<int>();
        Recorded(cache, 3, 0xFFFFFFFFu);

        var hits = 0;

        var result = harness.Draw(
            () =>
            {
                var list = ImGui.GetWindowDrawList();
                hits = 0;

                for (var i = 0; i < 20; i++)
                {
                    if (cache.TryReplay(list, 3, To + new Vector2(i, 0f), 0xFF00FF00u))
                        hits++;
                }
            },
            warmUpFrames: 2);

        hits.Should().Be(20);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void DrawingThatChangesEveryFrame_IsNeverRecorded()
    {
        var cache = new NoireMeshCache<int>();
        var frame = 0;

        for (var i = 0; i < 6; i++)
        {
            harness.Draw(() =>
            {
                var list = ImGui.GetWindowDrawList();
                var key = 100 + frame++;

                if (cache.TryReplay(list, key, From))
                    return;

                using (cache.Record(list, key, From))
                    Circle(list, From, 0xFFFFFFFFu);
            }, warmUpFrames: 0);
        }

        cache.Count.Should().Be(0);
    }

    [Fact]
    public void RecordingSplitAcrossDrawCommands_IsNotReplayed()
    {
        var cache = new NoireMeshCache<int>();

        for (var i = 0; i < 3; i++)
        {
            harness.Draw(() =>
            {
                var list = ImGui.GetWindowDrawList();

                if (cache.TryReplay(list, 4, From))
                    return;

                using (cache.Record(list, 4, From))
                {
                    Circle(list, From, 0xFFFFFFFFu);
                    list.AddDrawCmd();
                    Circle(list, From + new Vector2(40f, 0f), 0xFFFFFFFFu);
                }
            }, warmUpFrames: 0);
        }

        var replayed = true;

        harness.Draw(() => replayed = cache.TryReplay(ImGui.GetWindowDrawList(), 4, From), warmUpFrames: 0);

        replayed.Should().BeFalse();
    }

    [Fact]
    public void Rect_FromTheCache_DrawsTheSameGeometryMoved()
    {
        var first = Array.Empty<ImDrawVert>();
        var later = Array.Empty<ImDrawVert>();
        var size = new Vector2(83f, 37f);
        var color = new Vector4(0.2f, 0.4f, 0.8f, 0.9f);

        for (var i = 0; i < 3; i++)
        {
            var at = i == 2 ? To : From;

            harness.Draw(() =>
            {
                var list = ImGui.GetWindowDrawList();
                var start = list.VtxBuffer.Size;

                NoireShapes.On(list, () => NoireShapes.Rect(at, at + size, color, CornerShape.Rounded, 9f));

                var drawn = list.VtxBuffer.AsSpan()[start..].ToArray();

                if (i == 0)
                    first = drawn;
                else
                    later = drawn;
            }, warmUpFrames: 0);
        }

        later.Should().HaveCount(first.Length);

        for (var v = 0; v < first.Length; v++)
        {
            (later[v].Pos - (first[v].Pos - From + To)).Length().Should().BeLessThan(0.001f);
            ((int)(later[v].Col >> 24)).Should().BeInRange((int)(first[v].Col >> 24) - 1, (int)(first[v].Col >> 24) + 1);
            (later[v].Col & 0x00FFFFFFu).Should().Be(first[v].Col & 0x00FFFFFFu);
        }
    }
}
