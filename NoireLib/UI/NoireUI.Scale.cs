using Dalamud.Interface.Utility;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The hub's UI scale: the one place that knows how large the user asked the interface to be.
/// </summary>
public static partial class NoireUI
{
    // Test seam replacing Dalamud's global scale when no ImGui context exists.
    internal static Func<float>? ScaleOverride { get; set; }

    /// <summary>
    /// The user's UI scale, where 1 is 100%.
    /// </summary>
    /// <remarks>Reads 1 before NoireLib is initialized, so a value computed off it is never zero.</remarks>
    public static float Scale
    {
        get
        {
            if (ScaleOverride is { } seam)
                return seam();

            if (!NoireService.IsInitialized())
                return 1f;

            // Guarded rather than returned straight: a scale of zero would collapse every measurement built on it into
            // nothing, and a UI that has silently become zero pixels wide is far harder to recognise than one at 100%.
            var scale = ImGuiHelpers.GlobalScale;
            return scale > 0f ? scale : 1f;
        }
    }

    /// <summary>
    /// Converts a pixel value authored at 100% into pixels at the user's scale.
    /// </summary>
    /// <remarks>
    /// Never apply it to a value read out of <c>ImGui.GetStyle()</c>, or to anything a NoireUI <c>Resolve</c> method
    /// returned; those are finished pixels.
    /// </remarks>
    /// <param name="logical">The pixel value at 100%.</param>
    /// <returns>The value at the current scale.</returns>
    public static float Scaled(float logical) => logical * Scale;

    /// <summary>
    /// Converts a pixel pair authored at 100% into pixels at the user's scale. See <see cref="Scaled(float)"/>.
    /// </summary>
    /// <param name="logical">The pixel pair at 100%.</param>
    /// <returns>The pair at the current scale.</returns>
    public static Vector2 Scaled(Vector2 logical) => logical * Scale;

    /// <summary>
    /// Converts a real pixel value back into the logical unit it would have been authored as.
    /// </summary>
    /// <param name="real">The pixel value at the current scale.</param>
    /// <returns>The value at 100%.</returns>
    public static float Unscaled(float real) => real / Scale;

    /// <summary>
    /// Converts a real pixel pair back into logical units. See <see cref="Unscaled(float)"/>.
    /// </summary>
    /// <param name="real">The pixel pair at the current scale.</param>
    /// <returns>The pair at 100%.</returns>
    public static Vector2 Unscaled(Vector2 real) => real / Scale;
}
