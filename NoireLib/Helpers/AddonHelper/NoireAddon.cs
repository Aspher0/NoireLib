using Dalamud.Game.Addon.Events.EventDataTypes;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.NativeWrapper;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace NoireLib.Helpers;

/// <summary>
/// A lightweight wrapper around an addon pointer that makes <see cref="AddonHelper"/> operations chainable and usable without unsafe code.<br/>
/// Every member is safe to call on an invalid or not-ready addon: getters return sensible defaults and actions become no-ops returning false/null.<br/>
/// Get an instance via <see cref="AddonHelper.GetAddon(string)"/>, <see cref="Get(string)"/>, or the constructors/factories below.<br/>
/// Node-level operations (text writing, visibility, events, cursor) are available on <see cref="NoireAddonNode"/>, obtained via <see cref="GetNode(uint)"/>, <see cref="GetNode(int[])"/>, or <see cref="RootNode"/>.
/// </summary>
public readonly unsafe struct NoireAddon
{
    private readonly nint address;

    /// <summary>
    /// Constructs a wrapper from a raw addon address.
    /// </summary>
    /// <param name="address">The addon address, or 0 for an invalid wrapper.</param>
    public NoireAddon(nint address)
        => this.address = address;

    /// <summary>
    /// Constructs a wrapper from an addon pointer.
    /// </summary>
    /// <param name="addon">The addon pointer, or null for an invalid wrapper.</param>
    public NoireAddon(AtkUnitBase* addon)
        => address = (nint)addon;

    /// <summary>
    /// Constructs a wrapper from a Dalamud addon wrapper.
    /// </summary>
    /// <param name="addon">The Dalamud addon wrapper.</param>
    public NoireAddon(AtkUnitBasePtr addon)
        => address = addon.Address;

    /// <summary>
    /// Gets a wrapper for the addon with the given name, whether it is ready or not.<br/>
    /// Check <see cref="IsReady"/> (or use the wrapper in a boolean context) before interacting with it.
    /// </summary>
    /// <param name="addonName">The name of the addon to get.</param>
    /// <returns>The addon wrapper. Invalid if the addon does not exist.</returns>
    public static NoireAddon Get(string addonName)
        => new(NoireService.GameGui.GetAddonByName(addonName).Address);

    /// <summary>
    /// Gets a wrapper for the addon associated with the given lifecycle event arguments.
    /// </summary>
    /// <param name="addonArgs">The lifecycle event arguments to resolve the addon from.</param>
    /// <returns>The addon wrapper. Invalid if the arguments carry no addon.</returns>
    public static NoireAddon From(AddonArgs addonArgs)
        => new(addonArgs?.Addon.Address ?? nint.Zero);

    /// <summary>
    /// Gets a wrapper for the addon associated with the given addon event data.
    /// </summary>
    /// <param name="eventData">The addon event data to resolve the addon from.</param>
    /// <returns>The addon wrapper. Invalid if the event data carries no addon.</returns>
    public static NoireAddon From(AddonEventData eventData)
        => new(eventData?.AddonPointer ?? nint.Zero);

    /// <summary>
    /// The raw address of the addon, or 0 if invalid.
    /// </summary>
    public nint Address => address;

    /// <summary>
    /// The addon pointer, or null if invalid.
    /// </summary>
    public AtkUnitBase* Pointer => (AtkUnitBase*)address;

    /// <summary>
    /// Whether this wrapper points to an existing addon.
    /// </summary>
    public bool IsValid => address != nint.Zero;

    /// <summary>
    /// Whether the addon is loaded, visible, and ready to be interacted with.
    /// </summary>
    public bool IsReady => IsValid && AddonHelper.IsAddonLoaded(Pointer);

    /// <summary>
    /// Whether the addon is currently visible.
    /// </summary>
    public bool IsVisible => IsValid && Pointer->IsVisible;

    /// <summary>
    /// The name of the addon, or <see cref="string.Empty"/> if invalid.
    /// </summary>
    public string Name => IsValid ? Pointer->NameString : string.Empty;

    /// <summary>
    /// The X position of the addon on screen, or 0 if invalid.
    /// </summary>
    public short X => IsValid ? Pointer->X : (short)0;

    /// <summary>
    /// The Y position of the addon on screen, or 0 if invalid.
    /// </summary>
    public short Y => IsValid ? Pointer->Y : (short)0;

    /// <summary>
    /// The scale of the addon, or 0 if invalid.
    /// </summary>
    public float Scale => IsValid ? Pointer->Scale : 0f;

    /// <summary>
    /// The scaled width of the addon, or 0 if invalid.
    /// </summary>
    public float Width => IsValid ? Pointer->GetScaledWidth(true) : 0f;

    /// <summary>
    /// The scaled height of the addon, or 0 if invalid.
    /// </summary>
    public float Height => IsValid ? Pointer->GetScaledHeight(true) : 0f;

    /// <summary>
    /// The root node of the addon. Invalid if the addon is not ready or has no root node.
    /// </summary>
    public NoireAddonNode RootNode
        => AddonHelper.TryGetRootNode(Pointer, out var nodePtr) ? new NoireAddonNode(address, (nint)nodePtr) : default;

    /// <summary>
    /// Gets a node of the addon by node id. Invalid if the addon is not ready or the node does not exist.
    /// </summary>
    /// <param name="nodeId">The node id to look up.</param>
    public NoireAddonNode this[uint nodeId] => GetNode(nodeId);

    /// <summary>
    /// Gets a node of the addon by node id.
    /// </summary>
    /// <param name="nodeId">The node id to look up.</param>
    /// <returns>The node wrapper. Invalid if the addon is not ready or the node does not exist.</returns>
    public NoireAddonNode GetNode(uint nodeId)
        => AddonHelper.TryGetNode(Pointer, nodeId, out var nodePtr) ? new NoireAddonNode(address, (nint)nodePtr) : default;

    /// <summary>
    /// Gets a node of the addon by traversing a chain of node ids.
    /// </summary>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The node wrapper. Invalid if the addon is not ready or the chain could not be resolved.</returns>
    public NoireAddonNode GetNode(params int[] nodeIds)
        => AddonHelper.TryGetNode(Pointer, out var nodePtr, nodeIds) ? new NoireAddonNode(address, (nint)nodePtr) : default;

    /// <summary>
    /// Reads the text of a node resolved by a chain of node ids.
    /// </summary>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The resolved text, or <see cref="string.Empty"/> if it could not be read.</returns>
    public string ReadText(params int[] nodeIds)
        => AddonHelper.ReadTextOrEmpty(Pointer, nodeIds);

    /// <summary>
    /// Tries to read the text of a node resolved by a chain of node ids.
    /// </summary>
    /// <param name="text">The resolved managed text, if found.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the addon is ready and the node text could be read; otherwise, false.</returns>
    public bool TryReadText(out string text, params int[] nodeIds)
        => GetNode(nodeIds).TryReadText(out text);

    /// <summary>
    /// Sends callback values to the addon and updates its internal state.<br/>
    /// Equivalent to <see cref="SendCallback(bool, object[])"/> with updateState set to true.
    /// </summary>
    /// <param name="values">The callback values to marshal into <see cref="AtkValue"/> instances.</param>
    /// <returns>True if the addon was ready and the callback was sent successfully; otherwise, false.</returns>
    public bool SendCallback(params object[] values)
        => AddonHelper.SendCallback(Pointer, true, values);

    /// <summary>
    /// Sends callback values to the addon.
    /// </summary>
    /// <param name="updateState">Whether the addon should update its internal state after the callback is fired.</param>
    /// <param name="values">The callback values to marshal into <see cref="AtkValue"/> instances.</param>
    /// <returns>True if the addon was ready and the callback was sent successfully; otherwise, false.</returns>
    public bool SendCallback(bool updateState, params object[] values)
        => AddonHelper.SendCallback(Pointer, updateState, values);

    /// <summary>
    /// Shows the addon.
    /// </summary>
    /// <param name="silenceOpenSoundEffect">Whether the open sound effect should be silenced.</param>
    /// <returns>True if the addon was valid; otherwise, false.</returns>
    public bool Show(bool silenceOpenSoundEffect = false)
    {
        if (!IsValid)
            return false;

        Pointer->Show(silenceOpenSoundEffect, 0);
        return true;
    }

    /// <summary>
    /// Hides the addon.
    /// </summary>
    /// <param name="callHideCallback">Whether the addon's hide callback should be invoked.</param>
    /// <returns>True if the addon was valid; otherwise, false.</returns>
    public bool Hide(bool callHideCallback = true)
    {
        if (!IsValid)
            return false;

        Pointer->Hide(false, callHideCallback, 0);
        return true;
    }

    /// <summary>
    /// Closes the addon.
    /// </summary>
    /// <param name="fireCallback">Whether the addon's close callback should be fired.</param>
    /// <returns>True if the addon was valid; otherwise, false.</returns>
    public bool Close(bool fireCallback = true)
    {
        if (!IsValid)
            return false;

        Pointer->Close(fireCallback);
        return true;
    }

    /// <summary>
    /// Allows using the addon wrapper directly in boolean contexts, evaluating to <see cref="IsReady"/>.
    /// </summary>
    /// <param name="addon">The addon wrapper to evaluate.</param>
    public static implicit operator bool(NoireAddon addon) => addon.IsReady;

    /// <summary>
    /// Wraps an addon pointer into a <see cref="NoireAddon"/>.
    /// </summary>
    /// <param name="addon">The addon pointer to wrap.</param>
    public static implicit operator NoireAddon(AtkUnitBase* addon) => new(addon);

    /// <summary>
    /// Wraps a Dalamud addon wrapper into a <see cref="NoireAddon"/>.
    /// </summary>
    /// <param name="addon">The Dalamud addon wrapper to wrap.</param>
    public static implicit operator NoireAddon(AtkUnitBasePtr addon) => new(addon);

    /// <summary>
    /// Returns a readable representation of the addon wrapper for logging.
    /// </summary>
    /// <returns>The addon name and address, or "Invalid" when the wrapper is invalid.</returns>
    public override string ToString()
        => IsValid ? $"{Name} (0x{address:X})" : "Invalid";
}
