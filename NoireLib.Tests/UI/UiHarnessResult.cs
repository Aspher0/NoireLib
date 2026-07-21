using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Tests;

/// <summary>
/// What one measured frame produced.
/// </summary>
/// <remarks>
/// The three readings answer three different questions, and an optimization has to satisfy all of them at once.
/// <b>Bytes</b> is the regression gate: it is the same number on every machine, where a timing is not. <b>Scopes</b>
/// proves the drawing was instrumented, which is what stops a surface going invisible to the profiler. <b>Vertices</b>
/// proves the drawing still happened, because an optimization that quietly stops drawing something reads as a win in
/// every other measure.
/// </remarks>
/// <param name="AllocatedBytes">
/// Bytes allocated on the draw thread during the measured frame, excluding the warm-up frames and the harness's own
/// bookkeeping. Zero is the target for a steady-state draw.
/// </param>
/// <param name="Scopes">
/// The profiler scopes that opened while drawing, in no particular order. Empty when the draw opened none, which for a
/// NoireUI surface is a finding rather than a detail.
/// </param>
/// <param name="TotalVtxCount">
/// Vertices across every draw list in the frame. Zero means nothing reached the screen.
/// </param>
/// <param name="TotalIdxCount">
/// Indices across every draw list in the frame. Moves with <paramref name="TotalVtxCount"/> for ordinary geometry and
/// separates from it when a shape is re-triangulated rather than re-tessellated.
/// </param>
public readonly record struct UiHarnessResult(
    long AllocatedBytes,
    IReadOnlyList<string> Scopes,
    int TotalVtxCount,
    int TotalIdxCount)
{
    /// <summary>
    /// Whether a scope of this name opened, or one nested under a name beginning with it.
    /// </summary>
    /// <remarks>
    /// Prefix matching because a widget measures itself under <c>{kind}:{id}</c>, so a caller asserting that
    /// <c>NoireTable</c> drew should not have to know which id the test gave it.
    /// </remarks>
    /// <param name="name">The scope name, or the start of one.</param>
    /// <returns><see langword="true"/> when a matching scope opened.</returns>
    public bool HasScope(string name)
        => Scopes.Any(scope => scope.StartsWith(name, System.StringComparison.Ordinal));
}
