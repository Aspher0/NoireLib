using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Allocation-free enumerator over the currently visible game addons, obtained from <see cref="AddonHelper.VisibleAddons(Vector2, float)"/>.<br/>
/// It is its own enumerable, so it is consumed with a plain <c>foreach</c>. A read fault ends the enumeration instead of throwing.
/// </summary>
public struct VisibleAddonEnumerator
{
    private readonly nint manager;
    private readonly Vector2 displaySize;
    private readonly float fullScreenSkip;
    private int index;
    private bool finished;
    private NoireAddon current;

    internal unsafe VisibleAddonEnumerator(Vector2 displaySize, float fullScreenSkip)
    {
        manager = (nint)RaptureAtkUnitManager.Instance();
        this.displaySize = displaySize;
        this.fullScreenSkip = fullScreenSkip;
        index = 0;
        finished = manager == nint.Zero;
        current = default;
    }

    /// <summary>
    /// The addon reached by the last <see cref="MoveNext"/>.
    /// </summary>
    public readonly NoireAddon Current => current;

    /// <summary>
    /// Returns this enumerator, so it can be used directly in a <c>foreach</c>.
    /// </summary>
    /// <returns>A copy of this enumerator positioned before the first addon.</returns>
    public readonly VisibleAddonEnumerator GetEnumerator() => this;

    /// <summary>
    /// Advances to the next visible addon.
    /// </summary>
    /// <returns>True when <see cref="Current"/> holds another addon; otherwise, false.</returns>
    public unsafe bool MoveNext()
    {
        if (finished)
            return false;

        try
        {
            var units = (RaptureAtkUnitManager*)manager;
            ref var list = ref units->AllLoadedUnitsList;
            var entries = list.Entries;
            int loaded = list.Count;
            var skipOverlays = fullScreenSkip > 0f && displaySize.X > 0f && displaySize.Y > 0f;

            while (index < loaded && index < entries.Length)
            {
                var unit = entries[index++].Value;
                if (unit == null || !unit->IsVisible)
                    continue;

                var root = unit->RootNode;
                if (root == null || !root->IsVisible())
                    continue;

                var w = root->Width * root->ScaleX * unit->Scale;
                var h = root->Height * root->ScaleY * unit->Scale;
                if (w <= 1 || h <= 1)
                    continue;

                // Near-fullscreen transparent overlay roots (nameplates, fly text, screen info) cover the whole viewport,
                // so anything treating an addon rect as coverage would take the entire screen from one of them.
                if (skipOverlays && w >= displaySize.X * fullScreenSkip && h >= displaySize.Y * fullScreenSkip)
                    continue;

                current = new NoireAddon(unit);
                return true;
            }
        }
        catch (Exception)
        {
            // A torn or mid-load addon ends the walk rather than taking the frame down.
        }

        finished = true;
        current = default;
        return false;
    }
}
