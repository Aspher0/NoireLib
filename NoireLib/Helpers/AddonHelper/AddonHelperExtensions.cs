using Dalamud.Game.Addon.Events.EventDataTypes;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.NativeWrapper;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;

namespace NoireLib.Helpers;

/// <summary>
/// Provides extension methods to bridge Dalamud addon types into the fluent <see cref="NoireAddon"/> wrapper.
/// </summary>
public static class AddonHelperExtensions
{
    /// <summary>
    /// Determines if an addon wrapper is loaded and ready to be interacted with.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <returns>True if the addon is loaded and ready to be interacted with; otherwise, false.</returns>
    public static unsafe bool IsAddonLoaded(this AtkUnitBasePtr addon)
        => addon != IntPtr.Zero && AddonHelper.IsAddonLoaded((AtkUnitBase*)addon.Address);

    /// <summary>
    /// Wraps a Dalamud addon wrapper into a fluent <see cref="NoireAddon"/>.
    /// </summary>
    /// <param name="addon">The addon wrapper to wrap.</param>
    /// <returns>The fluent addon wrapper.</returns>
    public static NoireAddon ToNoireAddon(this AtkUnitBasePtr addon)
        => new(addon);

    /// <summary>
    /// Wraps the addon associated with lifecycle event arguments into a fluent <see cref="NoireAddon"/>.
    /// </summary>
    /// <param name="addonArgs">The lifecycle event arguments to resolve the addon from.</param>
    /// <returns>The fluent addon wrapper. Invalid if the arguments carry no addon.</returns>
    public static NoireAddon ToNoireAddon(this AddonArgs addonArgs)
        => NoireAddon.From(addonArgs);

    /// <summary>
    /// Wraps the addon associated with addon event data into a fluent <see cref="NoireAddon"/>.
    /// </summary>
    /// <param name="eventData">The addon event data to resolve the addon from.</param>
    /// <returns>The fluent addon wrapper. Invalid if the event data carries no addon.</returns>
    public static NoireAddon ToNoireAddon(this AddonEventData eventData)
        => NoireAddon.From(eventData);
}
