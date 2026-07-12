using Dalamud.Game.Addon.Events;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;

namespace NoireLib.Helpers;

using AddonCursorType = AddonCursorType;
using AddonEventHandle = IAddonEventHandle;
using AddonNodeEventDelegate = IAddonEventManager.AddonEventDelegate;
using AddonNodeEventType = AddonEventType;

/// <summary>
/// A lightweight wrapper around an addon node that makes node operations chainable and usable without unsafe code.<br/>
/// Every member is safe to call on an invalid node: getters return sensible defaults and actions become no-ops returning false/null.<br/>
/// Get an instance via <see cref="NoireAddon.GetNode(uint)"/>, <see cref="NoireAddon.GetNode(int[])"/>, or <see cref="NoireAddon.RootNode"/>.
/// </summary>
public readonly unsafe struct NoireAddonNode
{
    private readonly nint addonAddress;
    private readonly nint nodeAddress;

    /// <summary>
    /// Constructs a wrapper from a raw addon address and node address.
    /// </summary>
    /// <param name="addonAddress">The address of the addon owning the node.</param>
    /// <param name="nodeAddress">The address of the node, or 0 for an invalid wrapper.</param>
    public NoireAddonNode(nint addonAddress, nint nodeAddress)
    {
        this.addonAddress = addonAddress;
        this.nodeAddress = nodeAddress;
    }

    /// <summary>
    /// Constructs a wrapper from an addon pointer and node pointer.
    /// </summary>
    /// <param name="addon">The addon owning the node.</param>
    /// <param name="node">The node pointer, or null for an invalid wrapper.</param>
    public NoireAddonNode(AtkUnitBase* addon, AtkResNode* node)
    {
        addonAddress = (nint)addon;
        nodeAddress = (nint)node;
    }

    /// <summary>
    /// The raw address of the node, or 0 if invalid.
    /// </summary>
    public nint Address => nodeAddress;

    /// <summary>
    /// The node pointer, or null if invalid.
    /// </summary>
    public AtkResNode* Pointer => (AtkResNode*)nodeAddress;

    /// <summary>
    /// The text node pointer, or null if the node is invalid or not a text node.
    /// </summary>
    public AtkTextNode* TextNodePointer
        => AddonHelper.TryGetTextNode(Pointer, out var textNodePtr) ? textNodePtr : null;

    /// <summary>
    /// The component node pointer, or null if the node is invalid or not a component node.
    /// </summary>
    public AtkComponentNode* ComponentNodePointer
        => AddonHelper.TryGetComponentNode(Pointer, out var componentNodePtr) ? componentNodePtr : null;

    /// <summary>
    /// The addon owning this node.
    /// </summary>
    public NoireAddon Addon => new(addonAddress);

    /// <summary>
    /// Whether this wrapper points to an existing node.
    /// </summary>
    public bool IsValid => nodeAddress != nint.Zero;

    /// <summary>
    /// The node id of the node, or 0 if invalid.
    /// </summary>
    public uint NodeId => IsValid ? Pointer->NodeId : 0;

    /// <summary>
    /// Whether the node is a text node.
    /// </summary>
    public bool IsTextNode => TextNodePointer != null;

    /// <summary>
    /// Whether the node is a component node.
    /// </summary>
    public bool IsComponentNode => ComponentNodePointer != null;

    /// <summary>
    /// Whether the node is currently visible.
    /// </summary>
    public bool IsVisible => IsValid && (Pointer->NodeFlags & NodeFlags.Visible) != 0;

    /// <summary>
    /// The width of the node, or 0 if invalid.
    /// </summary>
    public ushort Width => IsValid ? Pointer->Width : (ushort)0;

    /// <summary>
    /// The height of the node, or 0 if invalid.
    /// </summary>
    public ushort Height => IsValid ? Pointer->Height : (ushort)0;

    /// <summary>
    /// The X position of the node on screen, or 0 if invalid.
    /// </summary>
    public float ScreenX => IsValid ? Pointer->ScreenX : 0f;

    /// <summary>
    /// The Y position of the node on screen, or 0 if invalid.
    /// </summary>
    public float ScreenY => IsValid ? Pointer->ScreenY : 0f;

    /// <summary>
    /// The current text of the node, or <see cref="string.Empty"/> if the node is invalid or not a text node.
    /// </summary>
    public string Text
        => AddonHelper.TryReadText(Pointer, out var text) ? text : string.Empty;

    /// <summary>
    /// Tries to read the current text of the node.
    /// </summary>
    /// <param name="text">The resolved managed text, if found.</param>
    /// <returns>True if the node is a text node and its text could be read; otherwise, false.</returns>
    public bool TryReadText(out string text)
        => AddonHelper.TryReadText(Pointer, out text);

    /// <summary>
    /// Tries to set the text of the node.
    /// </summary>
    /// <param name="text">The text to set.</param>
    /// <returns>True if the node is a text node and the text was set; otherwise, false.</returns>
    public bool TrySetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var textNode = TextNodePointer;

        if (textNode == null)
            return false;

        textNode->SetText(text);
        return true;
    }

    /// <summary>
    /// Sets the visibility of the node.
    /// </summary>
    /// <param name="visible">Whether the node should be visible.</param>
    /// <returns>True if the node was valid; otherwise, false.</returns>
    public bool SetVisible(bool visible)
    {
        if (!IsValid)
            return false;

        Pointer->ToggleVisibility(visible);
        return true;
    }

    /// <summary>
    /// Shows the node.
    /// </summary>
    /// <returns>True if the node was valid; otherwise, false.</returns>
    public bool Show() => SetVisible(true);

    /// <summary>
    /// Hides the node.
    /// </summary>
    /// <returns>True if the node was valid; otherwise, false.</returns>
    public bool Hide() => SetVisible(false);

    /// <summary>
    /// Sets the alpha of the node.
    /// </summary>
    /// <param name="alpha">The alpha value to set (0 = transparent, 255 = opaque).</param>
    /// <returns>True if the node was valid; otherwise, false.</returns>
    public bool SetAlpha(byte alpha)
    {
        if (!IsValid)
            return false;

        Pointer->SetAlpha(alpha);
        return true;
    }

    /// <summary>
    /// Registers an addon event on the node.
    /// </summary>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public AddonEventHandle? AddEvent(AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
        => AddonHelper.AddEvent((AtkUnitBase*)addonAddress, Pointer, eventType, eventDelegate);

    /// <summary>
    /// Registers a mouse click event on the node.
    /// </summary>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public AddonEventHandle? AddClickEvent(AddonNodeEventDelegate eventDelegate)
        => AddonHelper.AddEvent((AtkUnitBase*)addonAddress, Pointer, AddonNodeEventType.MouseClick, eventDelegate);

    /// <summary>
    /// Registers hover handlers on the node.
    /// </summary>
    /// <param name="onMouseOver">The handler to invoke when the node is hovered.</param>
    /// <param name="onMouseOut">The optional handler to invoke when the cursor leaves the node.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public IDisposable? AddHoverEvents(AddonNodeEventDelegate onMouseOver, AddonNodeEventDelegate? onMouseOut = null)
        => AddonHelper.AddHoverEvents((AtkUnitBase*)addonAddress, Pointer, onMouseOver, onMouseOut);

    /// <summary>
    /// Registers cursor behavior on hover for the node.
    /// </summary>
    /// <param name="cursor">The cursor to show while hovered.</param>
    /// <param name="resetCursorOnMouseOut">Whether the cursor should be reset when hover ends.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public IDisposable? AddCursorOnHover(AddonCursorType cursor, bool resetCursorOnMouseOut = true)
        => AddonHelper.AddCursorOnHover((AtkUnitBase*)addonAddress, Pointer, cursor, resetCursorOnMouseOut);

    /// <summary>
    /// Allows using the node wrapper directly in boolean contexts, evaluating to <see cref="IsValid"/>.
    /// </summary>
    /// <param name="node">The node wrapper to evaluate.</param>
    public static implicit operator bool(NoireAddonNode node) => node.IsValid;

    /// <summary>
    /// Returns a readable representation of the node wrapper for logging.
    /// </summary>
    /// <returns>The node id and address, or "Invalid" when the wrapper is invalid.</returns>
    public override string ToString()
        => IsValid ? $"Node {NodeId} (0x{nodeAddress:X})" : "Invalid";
}
