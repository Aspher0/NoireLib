using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Hotbar layer of <see cref="AddonHelper"/>: which slot of the action bars lies under a screen point, and reading and
/// writing the slots themselves through the game's hotbar module.
/// </summary>
public static partial class AddonHelper
{
    /// <summary>How many hotbars the game keeps: the ten standard ones, then the eight cross hotbars.</summary>
    public const int HotbarCount = 18;

    /// <summary>How many standard hotbars there are. Hotbar ids from this one up are cross hotbars.</summary>
    public const int StandardHotbarCount = 10;

    // The addon drawing each standard hotbar, indexed by hotbar id.
    private static readonly string[] ActionBarAddonNames =
    [
        "_ActionBar", "_ActionBar01", "_ActionBar02", "_ActionBar03", "_ActionBar04",
        "_ActionBar05", "_ActionBar06", "_ActionBar07", "_ActionBar08", "_ActionBar09",
    ];

    /// <summary>How many slots a hotbar has.</summary>
    /// <param name="hotbarId">The hotbar, from 0 to <see cref="HotbarCount"/> - 1.</param>
    /// <returns>12 for a standard hotbar, 16 for a cross hotbar, 0 for an id that names no hotbar.</returns>
    public static int HotbarSlotCount(int hotbarId) => hotbarId switch
    {
        >= 0 and < StandardHotbarCount => 12,
        >= StandardHotbarCount and < HotbarCount => 16,
        _ => 0,
    };

    /// <summary>The standard hotbar slot under a screen point, as the action bars are laid out now.</summary>
    /// <param name="point">The point, in framebuffer pixels.</param>
    /// <param name="slot">The slot under the point.</param>
    /// <returns>Whether the point is over a slot of a visible action bar.</returns>
    public static unsafe bool TryGetHotbarSlotAt(Vector2 point, out HotbarSlotBounds slot)
    {
        slot = default;

        if (!IsNativeUiVisible())
            return false;

        foreach (var addonName in ActionBarAddonNames)
        {
            if (!TryGetReadyAddon<AddonActionBarBase>(addonName, out var bar) || bar == null)
                continue;

            int hotbarId = bar->RaptureHotbarId;

            if (hotbarId >= StandardHotbarCount)
                continue;

            var addonScale = ((AtkUnitBase*)bar)->Scale;
            var slotCount = Math.Min((int)bar->SlotCount, bar->ActionBarSlotVector.Count);

            for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                ref var entry = ref bar->ActionBarSlotVector[slotIndex];

                var node = entry.ComponentDragDrop != null && entry.ComponentDragDrop->AtkComponentBase.OwnerNode != null
                    ? &entry.ComponentDragDrop->AtkComponentBase.OwnerNode->AtkResNode
                    : entry.Icon != null ? &entry.Icon->AtkResNode : null;

                if (node == null)
                    continue;

                var size = new Vector2(node->Width * node->ScaleX, node->Height * node->ScaleY) * addonScale;

                if (size.X <= 1f || size.Y <= 1f)
                    continue;

                var min = new Vector2(node->ScreenX, node->ScreenY);
                var max = min + size;

                if (point.X >= min.X && point.X < max.X && point.Y >= min.Y && point.Y < max.Y)
                {
                    slot = new HotbarSlotBounds(hotbarId, slotIndex, min, max);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>A hotbar slot, as the game's hotbar module holds it. Game thread only.</summary>
    /// <param name="hotbarId">The hotbar, from 0 to <see cref="HotbarCount"/> - 1.</param>
    /// <param name="slotIndex">The slot, from 0 to <see cref="HotbarSlotCount"/> - 1.</param>
    /// <returns>The slot, or null when the position does not exist or the module is not ready.</returns>
    public static unsafe RaptureHotbarModule.HotbarSlot* GetHotbarSlot(int hotbarId, int slotIndex)
    {
        var module = slotIndex >= 0 && slotIndex < HotbarSlotCount(hotbarId) ? HotbarModule() : null;

        if (module == null)
            return null;

        return module->GetSlotById((uint)hotbarId, (uint)slotIndex);
    }

    /// <summary>Puts a command in a hotbar slot and saves the hotbar, as dragging it there by hand does. Game thread only.</summary>
    /// <param name="hotbarId">The hotbar, from 0 to <see cref="HotbarCount"/> - 1.</param>
    /// <param name="slotIndex">The slot, from 0 to <see cref="HotbarSlotCount"/> - 1.</param>
    /// <param name="type">What kind of command the slot holds.</param>
    /// <param name="commandId">The command's row id in the sheet <paramref name="type"/> names.</param>
    /// <param name="ignoreSharedHotbars">Whether to write only the current job's copy of a hotbar shared between jobs.</param>
    /// <param name="allowSaveToPvP">Whether the write may land in the PvP hotbar set.</param>
    /// <returns>True when the slot exists and was written.</returns>
    public static unsafe bool SetHotbarSlot(int hotbarId, int slotIndex, RaptureHotbarModule.HotbarSlotType type, uint commandId,
        bool ignoreSharedHotbars = false, bool allowSaveToPvP = false)
    {
        var module = slotIndex >= 0 && slotIndex < HotbarSlotCount(hotbarId) ? HotbarModule() : null;

        if (module == null)
            return false;

        module->SetAndSaveSlot((uint)hotbarId, (uint)slotIndex, type, commandId, ignoreSharedHotbars, allowSaveToPvP);
        return true;
    }

    private static unsafe RaptureHotbarModule* HotbarModule()
    {
        var framework = Framework.Instance();

        if (framework == null)
            return null;

        var uiModule = framework->GetUIModule();
        return uiModule == null ? null : uiModule->GetRaptureHotbarModule();
    }
}
