using System;

namespace NoireLib.UI;

/// <summary>
/// An open effect scope. Close it with <see langword="using"/> or <see cref="End"/>. A second close does nothing, and a
/// scope left open closes with its window or drops at the next frame.
/// </summary>
public readonly struct EffectScope : IDisposable
{
    private readonly int depth;
    private readonly int serial;

    internal EffectScope(int depth, int serial)
    {
        this.depth = depth;
        this.serial = serial;
    }

    /// <summary>Whether the scope is recording. False when there was no ImGui frame to draw into.</summary>
    public bool IsOpen => NoireEffects.IsOpen(depth, serial);

    /// <summary>Closes the scope, applies its effects, and returns what was drawn before and after them.</summary>
    /// <returns>What was drawn. Empty when the scope was already closed.</returns>
    public EffectResult End() => NoireEffects.End(depth, serial);

    /// <summary>Closes the scope and applies its effects.</summary>
    public void Dispose() => NoireEffects.End(depth, serial);
}
