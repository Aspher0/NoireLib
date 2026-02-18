using Dalamud.Game.NativeWrapper;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;

namespace NoireLib.Helpers;

/// <summary>
/// A helper class to help with addon manipulation, such as finding addons, getting data, sending callbacks, etc.
/// </summary>
public static class AddonHelper
{
    /// <summary>
    /// Tries to get an addon by name, and checks if it's loaded and ready to be interacted with.
    /// </summary>
    /// <param name="addonName">The name of the addon to get.</param>
    /// <param name="addonPtr">
    /// A pointer to the addon, if found.<br/>
    /// Will also be populated even if the addon is not ready.
    /// </param>
    /// <returns>True if the addon is found and ready to be interacted with; otherwise, false.</returns>
    public static unsafe bool TryGetReadyAddon(string addonName, out AtkUnitBase* addonPtr)
    {
        addonPtr = null;

        AtkUnitBasePtr addonFromName = NoireService.GameGui.GetAddonByName(addonName);

        if (addonFromName == IntPtr.Zero)
            return false;

        addonPtr = (AtkUnitBase*)addonFromName.Address;

        return IsAddonLoaded(addonPtr);
    }

    /// <summary>
    /// Determines if an addon is visible and loaded, and ready to be interacted with.
    /// </summary>
    /// <param name="addon">The addon to check.</param>
    /// <returns>True if the addon is loaded and ready to be interacted with; otherwise, false.</returns>
    public static unsafe bool IsAddonLoaded(AtkUnitBase* addon)
        => addon->IsVisible && addon->UldManager.LoadedState == AtkLoadState.Loaded && addon->IsReady && addon->IsFullyLoaded();
}
