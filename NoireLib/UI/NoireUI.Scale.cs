using Dalamud.Interface.Utility;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The hub's UI scale: the one place that knows how large the user asked the interface to be.
/// </summary>
public static partial class NoireUI
{
    /// <summary>
    /// Test seam replacing Dalamud's global scale when no ImGui context exists.
    /// </summary>
    internal static Func<float>? ScaleOverride { get; set; }

    /// <summary>
    /// The user's UI scale, where 1 is 100%.<br/>
    /// Dalamud applies this to the ImGui style, so text, frame padding and everything else read out of
    /// <c>ImGui.GetStyle()</c> already arrives at the right size. Numbers NoireUI and your own code ship do not, which is
    /// what <see cref="Scaled(float)"/> is for.<br/>
    /// Reads 1 before NoireLib is initialized, so a value computed off it is never zero.
    /// </summary>
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
    /// Every pixel value in the NoireUI surface is a logical unit: a toast <c>Width</c> of 400 means 400 at 100% and
    /// arrives 600 wide at 150%, without the plugin knowing the scale exists. Use this for pixel values of your own so
    /// they follow the same rule.<br/>
    /// Never apply it to a value read out of <c>ImGui.GetStyle()</c>, or to anything a NoireUI <c>Resolve</c> method
    /// returned. Those are finished pixels, and scaling them again is the one way to get this wrong.
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
}
