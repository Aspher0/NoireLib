using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NoireLib.Enums;
using System;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Geometry layer of <see cref="AddonHelper"/>: addon screen rects, the native hide-UI state, and the hit test that
/// answers whether a screen point is over native game UI.<br/>
/// Every read here is an addon read, so unlike the object table these are safe from the draw thread as well as the
/// framework thread. All of them fail soft: a torn or mid-load addon reports "nothing here" rather than throwing.
/// </summary>
public static partial class AddonHelper
{
    /// <summary>
    /// The default coverage fraction of the display above which an addon root is treated as a transparent overlay.
    /// </summary>
    public const float DefaultFullScreenSkip = 0.9f;

    /// <summary>
    /// How deep the hit test recurses into component nodes.
    /// </summary>
    private const int MaxNodeDepth = 8;

    /// <summary>
    /// How many ancestors the visibility walk climbs before giving up on a cyclic or corrupt node tree.
    /// </summary>
    private const int MaxAncestorDepth = 64;

    /// <summary>
    /// Whether the game's own hide-UI toggle currently leaves the native interface on screen.
    /// </summary>
    /// <returns>True when the interface is shown, or when the state cannot be read.</returns>
    public static unsafe bool IsNativeUiVisible()
    {
        var atkModule = RaptureAtkModule.Instance();
        return atkModule == null || atkModule->IsUiVisible;
    }

    /// <summary>
    /// Enumerates the loaded, visible addons whose root node has usable on-screen bounds, allocating nothing.
    /// </summary>
    /// <param name="displaySize">The framebuffer size the near-fullscreen skip measures against; zero disables the skip.</param>
    /// <param name="fullScreenSkip">Coverage fraction of <paramref name="displaySize"/> above which a root is skipped as a transparent overlay (nameplates, fly text, screen info); zero or less keeps them.</param>
    /// <returns>An enumerator usable directly in a <c>foreach</c>.</returns>
    public static VisibleAddonEnumerator VisibleAddons(Vector2 displaySize = default, float fullScreenSkip = DefaultFullScreenSkip)
        => new(displaySize, fullScreenSkip);

    /// <summary>
    /// The addon whose own hit region lies under a screen point, for answering whether a click belongs to native game UI.<br/>
    /// Only visible <b>collision nodes</b> count, the regions the game itself hit-tests, so the transparent margins around
    /// HUD elements (the empty space beside action-bar slots, a window's padding) do not falsely catch the point.
    /// </summary>
    /// <param name="pointPx">The point to test, in framebuffer pixels (the ImGui mouse space Dalamud shares with the game).</param>
    /// <param name="displaySize">The framebuffer size, for the near-fullscreen overlay skip.</param>
    /// <param name="fullScreenSkip">Coverage fraction of <paramref name="displaySize"/> above which a root is skipped as a transparent overlay; zero or less keeps them.</param>
    /// <param name="phantomCollisions">Which addons drop a collision node that has no visible content beside it.</param>
    /// <param name="respectNativeUiToggle">Whether a hidden native interface reports no addon under the point.</param>
    /// <returns>The addon under the point, or an invalid handle when the point is over no native game UI.</returns>
    public static unsafe NoireAddon HitTest(
        Vector2 pointPx,
        Vector2 displaySize,
        float fullScreenSkip = DefaultFullScreenSkip,
        AddonPhantomCollisionScope phantomCollisions = AddonPhantomCollisionScope.ActionBars,
        bool respectNativeUiToggle = true)
    {
        // Without a display size the overlay skip cannot measure anything, and a fullscreen overlay root would then
        // contain every point and catch every test.
        if (fullScreenSkip > 0f && (displaySize.X <= 0f || displaySize.Y <= 0f))
            return default;

        try
        {
            // While the native UI is hidden the addons stay loaded with their collision nodes live, so they would keep
            // intercepting clicks meant for the world even though nothing is drawn.
            if (respectNativeUiToggle && !IsNativeUiVisible())
                return default;

            foreach (var addon in VisibleAddons(displaySize, fullScreenSkip))
            {
                var rect = addon.ScreenRect;
                if (pointPx.X < rect.X || pointPx.X >= rect.Z || pointPx.Y < rect.Y || pointPx.Y >= rect.W)
                    continue;

                var unit = addon.Pointer;

                // Action bars keep a badge's collision node live even when its content (the bar-number label and arrows)
                // is switched off, which is why they are gated by default.
                var gatePhantoms = phantomCollisions switch
                {
                    AddonPhantomCollisionScope.All => true,
                    AddonPhantomCollisionScope.ActionBars => unit->Name.StartsWith("_ActionBar"u8),
                    _ => false,
                };

                if (NodeListHit(unit->UldManager.NodeList, unit->UldManager.NodeListCount, unit->Scale, pointPx, gatePhantoms, depth: 0))
                    return addon;
            }
        }
        catch (Exception)
        {
            // The read faulted: report no game UI rather than let a UI probe take the frame down.
        }

        return default;
    }

    /// <summary>
    /// Whether every ancestor of the node is visible. The node's own flag is the caller's to check.
    /// </summary>
    internal static unsafe bool AreAncestorsVisible(AtkResNode* node)
    {
        var parent = node->ParentNode;
        var guard = 0;
        while (parent != null && guard++ < MaxAncestorDepth)
        {
            if (!parent->IsVisible())
                return false;

            parent = parent->ParentNode;
        }

        return true;
    }

    /// <summary>
    /// Whether the point falls in a visible collision node reachable from this node list. Component nodes hold their own
    /// node list, so a component's inner controls (an action-bar slot, a window button) are only found by recursing into
    /// them. A display-only addon such as a job gauge carries no collision node and so never catches a point.
    /// </summary>
    private static unsafe bool NodeListHit(AtkResNode** nodes, int nodeCount, float unitScale, Vector2 pointPx, bool gatePhantoms, int depth)
    {
        if (nodes == null || depth > MaxNodeDepth)
            return false;

        for (var n = 0; n < nodeCount; n++)
        {
            var node = nodes[n];
            if (node == null || !node->IsVisible())
                continue;

            if (node->Type == NodeType.Collision)
            {
                // The game leaves a node's own visibility flag set while a hidden parent hides it on screen, so the
                // ancestor chain is what stops a switched-off element from catching the point.
                if (!AreAncestorsVisible(node))
                    continue;

                if (gatePhantoms && CollisionLacksVisibleSibling(nodes, nodeCount, node))
                    continue;

                var w = node->Width * node->ScaleX * unitScale;
                var h = node->Height * node->ScaleY * unitScale;
                if (w > 1 && h > 1)
                {
                    var x = node->ScreenX;
                    var y = node->ScreenY;
                    if (pointPx.X >= x && pointPx.X < x + w && pointPx.Y >= y && pointPx.Y < y + h)
                        return true;
                }

                continue;
            }

            var compNode = node->GetAsAtkComponentNode();
            if (compNode != null && AreAncestorsVisible(node))
            {
                var comp = compNode->Component;
                if (comp != null && NodeListHit(comp->UldManager.NodeList, comp->UldManager.NodeListCount, unitScale, pointPx, gatePhantoms, depth + 1))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a visible collision node has no visible non-collision sibling under the same parent. True marks a phantom
    /// hit region: a hotbar keeps its number-badge collision node visible while the label and arrows beside it are
    /// hidden, so nothing visible next to the collision means the control is switched off. A live button or slot always
    /// keeps visible content beside its collision.
    /// </summary>
    private static unsafe bool CollisionLacksVisibleSibling(AtkResNode** nodes, int nodeCount, AtkResNode* collision)
    {
        var parent = collision->ParentNode;
        if (parent == null)
            return false;

        for (var i = 0; i < nodeCount; i++)
        {
            var node = nodes[i];
            if (node == null || node == collision || node->ParentNode != parent)
                continue;
            if (node->Type != NodeType.Collision && node->IsVisible())
                return false;
        }

        return true;
    }
}
