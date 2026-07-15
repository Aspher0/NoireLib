using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// An opt-in selection/highlight outline for a node: a <b>real screen-space silhouette outline</b> (a post-process
/// rim drawn from a coverage mask, not a second mesh), so it traces the object's actual outline and works for solid
/// meshes and ground decals alike. The default <see cref="MakeSelectable"/> highlight stays a tint; outline is
/// something you turn on (directly here, or via <c>editor.SelectionOutline</c>).
/// </summary>
public sealed partial class SceneNode
{
    /// <summary>Whether an outline is currently enabled (its color's alpha &gt; 0).</summary>
    public bool HasOutline => Renderer is { } renderer && renderer.OutlineColor.W > 0f;

    /// <summary>
    /// Shows a real silhouette outline around this node in the given color. No-op (logged) when the node has no
    /// renderer. Calling it again updates the color/width. Fluent.
    /// </summary>
    /// <param name="color">Outline color, straight alpha (alpha &gt; 0 to be visible).</param>
    /// <param name="widthPixels">Outline thickness in screen pixels (default 4).</param>
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
    public SceneNode HideOutline()
    {
        if (Renderer is { } renderer)
            renderer.OutlineColor = default;
        return this;
    }
}
