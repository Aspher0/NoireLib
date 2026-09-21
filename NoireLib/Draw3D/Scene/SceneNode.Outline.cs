using System.Numerics;

namespace NoireLib.Draw3D.Scene;

public sealed partial class SceneNode
{
    /// <summary>Whether an outline is currently enabled.</summary>
    public bool HasOutline => Renderer is { } renderer && renderer.OutlineColor.W > 0f;

    /// <summary>Shows a screen-space silhouette outline around this node, or logs and does nothing without a renderer. Fluent.</summary>
    /// <param name="color">Outline color in straight alpha.</param>
    /// <param name="widthPixels">Outline thickness in screen pixels.</param>
    /// <returns>This node.</returns>
    public SceneNode ShowOutline(Vector4 color, float widthPixels = 4f)
    {
        var renderer = Renderer;
        if (renderer == null)
        {
            NoireLogger.LogWarning($"Draw3D: SceneNode '{Name ?? "(unnamed)"}'.ShowOutline with no renderer - ignored. Attach a mesh first.", "Draw3D");
            return this;
        }

        renderer.OutlineColor = color;
        renderer.OutlineWidthPixels = widthPixels > 0f ? widthPixels : 4f;
        return this;
    }

    /// <summary>Removes the outline, if any. Fluent.</summary>
    /// <returns>This node.</returns>
    public SceneNode HideOutline()
    {
        if (Renderer is { } renderer)
            renderer.OutlineColor = default;
        return this;
    }
}
