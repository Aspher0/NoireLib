using Dalamud.Bindings.ImGui;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Tests;

/// <summary>
/// Runs NoireUI drawing inside a real ImGui frame, with no game and no rendering backend, and reports what happened.
/// </summary>
/// <remarks>
/// An ImGui context is process-wide, exactly like the hub, the transient state store and the animation clock, so a test
/// class using this must join <see cref="NoireUiTestCollection"/>. One harness owns one context and destroys it on
/// disposal, which is what keeps a class from inheriting the windows, settings and ids of the class before it.<br/>
/// The native library is reached through the bindings Dalamud ships rather than through a private P/Invoke, so a test
/// measures the same code path a plugin runs.
/// </remarks>
/// <example>
/// <code>
/// [Collection(NoireUiTestCollection.Name)]
/// public sealed class MyDrawingTests : IClassFixture&lt;UiHarness&gt;
/// {
///     private readonly UiHarness harness;
///
///     public MyDrawingTests(UiHarness harness) => this.harness = harness;
///
///     [Fact]
///     public void A_filled_rectangle_reaches_the_draw_data()
///     {
///         var result = harness.Draw(static () =&gt;
///             ImGui.GetWindowDrawList().AddRectFilled(new Vector2(0f, 0f), new Vector2(10f, 10f), 0xFFFFFFFF));
///
///         result.TotalVtxCount.Should().BeGreaterThan(0);
///     }
/// }
/// </code>
/// </example>
public sealed class UiHarness : IDisposable
{
    /// <summary>
    /// The display the frame is laid out against. Large enough that a window under test is not clipped by the viewport
    /// and does not have its auto-size fought by the edge of the screen.
    /// </summary>
    private static readonly Vector2 DisplaySize = new(1920f, 1080f);

    /// <summary>
    /// The frame time reported to ImGui. Non-zero because ImGui divides by it, and fixed rather than real so that a
    /// test measuring animation advances by the same amount on a fast machine and a slow one.
    /// </summary>
    private const float DeltaTime = 1f / 60f;

    /// <summary>
    /// The window every draw runs inside unless the draw opens its own.
    /// </summary>
    /// <remarks>
    /// Drawing outside a window is not an error ImGui reports; it is geometry that goes nowhere, and the vertex count
    /// then reads zero for a draw that looked correct. Opening one here removes that failure from every test.<br/>
    /// A UTF-8 literal rather than a string, so opening the window marshals nothing. A UTF-16 title would be re-encoded
    /// on every frame, and those bytes would land in the reading the harness exists to take.
    /// </remarks>
    private static ReadOnlySpan<byte> HostWindowTitle => "NoireLib.Tests"u8;

    /// <summary>
    /// Strips the host window of everything that would draw.
    /// </summary>
    /// <remarks>
    /// A default window contributes its own title bar, border and background to the frame, which measured 36 vertices
    /// before anything under test drew. That floor would put a magic number in every assertion and would move whenever
    /// the ImGui style changed, so the host draws nothing and a vertex count is entirely the caller's.<br/>
    /// <c>NoSavedSettings</c> is not cosmetic: without it ImGui persists window state to an ini file, and a test run
    /// would inherit the position and size left by the run before it.
    /// </remarks>
    private const ImGuiWindowFlags HostWindowFlags =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;

    private readonly Func<int>? previousFrameOverride;
    private readonly Func<float>? previousTimeOverride;
    private readonly Func<bool>? previousAvailableOverride;

    private bool disposed;

    /// <summary>
    /// Creates the context, builds the font atlas, and points NoireUI's frame and clock at the real ones.
    /// </summary>
    public UiHarness()
    {
        ImGui.CreateContext();

        var io = ImGui.GetIO();

        io.DisplaySize = DisplaySize;
        io.DeltaTime = DeltaTime;

        // NoireUI reads the ImGui frame and clock only when the plugin service is initialized, which it is not outside
        // a plugin, so without these it would see frame 0 and time 0 forever. Anything that settles over frames would
        // then never settle, and a warm-up frame would not be a different frame from the one it warms.
        previousFrameOverride = NoireUI.FrameOverride;
        previousTimeOverride = NoireUI.TimeOverride;

        NoireUI.FrameOverride = static () => ImGui.GetFrameCount();
        NoireUI.TimeOverride = static () => (float)ImGui.GetTime();

        // The gate's own guard asks whether a plugin is behind the library, which is false here even though a real
        // context exists. Without this every gated surface that does not follow a NoireShapes redirect, which is the
        // window channel plumbing and the two viewport lists, hands back a null list and silently draws nothing.
        previousAvailableOverride = UiDraw.AvailableOverride;
        UiDraw.AvailableOverride = static () => true;

        // ImGui refuses to start a frame against an atlas that has not been built, and the default font is enough:
        // nothing here rasterizes at a size a test asserts on.
        io.Fonts.AddFontDefault();
        io.Fonts.Build();
    }

    /// <summary>
    /// Runs a draw inside a real ImGui frame and reports what it produced.
    /// </summary>
    /// <param name="draw">The drawing to run. Runs inside an open window, so it may draw immediately.</param>
    /// <param name="warmUpFrames">
    /// How many frames to run and discard before the measured one. The frame a window first appears on is not
    /// representative: its auto-size has not resolved, so its contents are laid out against a size ImGui is still
    /// working out. One warm-up frame is enough for that; more is for state that settles over several frames.
    /// </param>
    /// <returns>What the measured frame produced. Never accumulates across the warm-up frames.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draw"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the harness has been disposed.</exception>
    public UiHarnessResult Draw(Action draw, int warmUpFrames = 1)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ObjectDisposedException.ThrowIf(disposed, this);

        var profiler = NoireUI.Profiler;
        var wasEnabled = profiler.Enabled;

        // Reset once, before the warm-up rather than before the measured frame. The profiler allocates a node the
        // first time it sees a call path, and resetting immediately before the measurement would push that allocation
        // into the frame being measured and report it as the caller's. The same draw runs every frame, so the scopes
        // seen across the warm-up are the scopes of the measured frame.
        profiler.Reset();
        profiler.Enabled = true;

        try
        {
            for (var frame = 0; frame < warmUpFrames; frame++)
                RunFrame(draw);

            // Read as late and as early as possible around the frame itself, so neither the loop above nor the
            // snapshot below is inside the window.
            var before = GC.GetAllocatedBytesForCurrentThread();
            var (vertices, indices) = RunFrame(draw);
            var after = GC.GetAllocatedBytesForCurrentThread();

            return new UiHarnessResult(after - before, ScopeNames(profiler), vertices, indices);
        }
        finally
        {
            profiler.Enabled = wasEnabled;
            profiler.Reset();
        }
    }

    /// <summary>
    /// The names of every scope the profiler saw, taken after the measured frame.
    /// </summary>
    private static IReadOnlyList<string> ScopeNames(UiProfiler profiler)
    {
        var snapshot = profiler.Snapshot();
        var names = new List<string>(snapshot.Count);

        foreach (var entry in snapshot)
            names.Add(entry.Name);

        return names;
    }

    /// <summary>
    /// Runs one whole frame and reads what reached the draw data.
    /// </summary>
    private static (int Vertices, int Indices) RunFrame(Action draw)
    {
        ImGui.NewFrame();

        try
        {
            if (ImGui.Begin(HostWindowTitle, HostWindowFlags))
                draw();
        }
        finally
        {
            // Paired with Begin unconditionally: ImGui's window stack is balanced by call count rather than by return
            // value, and leaving it unbalanced corrupts every later frame rather than only this one.
            ImGui.End();

            // Render rather than EndFrame, because the draw data a test reads is what Render assembles. Inside the
            // finally so that a draw which throws still leaves the context ready for the next test.
            ImGui.Render();
        }

        var drawData = ImGui.GetDrawData();

        return (drawData.TotalVtxCount, drawData.TotalIdxCount);
    }

    /// <summary>
    /// Destroys the context and puts the frame and clock overrides back, so the next harness starts from nothing and
    /// the tests that drive those overrides themselves are unaffected.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        NoireUI.FrameOverride = previousFrameOverride;
        NoireUI.TimeOverride = previousTimeOverride;
        UiDraw.AvailableOverride = previousAvailableOverride;

        ImGui.DestroyContext();
    }
}
