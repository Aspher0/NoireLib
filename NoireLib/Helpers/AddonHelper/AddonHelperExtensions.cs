using Dalamud.Game.Addon.Events;
using Dalamud.Game.NativeWrapper;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;

namespace NoireLib.Helpers;

using AddonCursorType = AddonCursorType;
using AddonEventHandle = IAddonEventHandle;
using AddonNodeEventDelegate = IAddonEventManager.AddonEventDelegate;
using AddonNodeEventType = AddonEventType;

/// <summary>
/// Provides extension methods for addon callback helpers.
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
    /// Tries to get the root node of a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodePtr">The root node pointer, if found.</param>
    /// <returns>True if the addon is ready and has a root node; otherwise, false.</returns>
    public static unsafe bool TryGetRootNode(this AtkUnitBasePtr addon, out AtkResNode* nodePtr)
    {
        nodePtr = null;

        return addon != IntPtr.Zero && AddonHelper.TryGetRootNode((AtkUnitBase*)addon.Address, out nodePtr);
    }

    /// <summary>
    /// Tries to get a node from a ready addon wrapper by node id.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodeId">The node id to look up.</param>
    /// <param name="nodePtr">The node pointer, if found.</param>
    /// <returns>True if the addon is ready and the node exists; otherwise, false.</returns>
    public static unsafe bool TryGetNode(this AtkUnitBasePtr addon, uint nodeId, out AtkResNode* nodePtr)
    {
        nodePtr = null;

        return addon != IntPtr.Zero && AddonHelper.TryGetNode((AtkUnitBase*)addon.Address, nodeId, out nodePtr);
    }

    /// <summary>
    /// Tries to get a node from a ready addon wrapper by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodePtr">The final node pointer, if found.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the addon is ready and every node in the chain was resolved; otherwise, false.</returns>
    public static unsafe bool TryGetNode(this AtkUnitBasePtr addon, out AtkResNode* nodePtr, params int[] nodeIds)
    {
        nodePtr = null;

        return addon != IntPtr.Zero && AddonHelper.TryGetNode((AtkUnitBase*)addon.Address, out nodePtr, nodeIds);
    }

    /// <summary>
    /// Tries to resolve a text node from a ready addon wrapper by node id.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodeId">The node id to look up.</param>
    /// <param name="textNodePtr">The text node pointer, if found.</param>
    /// <returns>True if the addon is ready and the node exists as a text node; otherwise, false.</returns>
    public static unsafe bool TryGetTextNode(this AtkUnitBasePtr addon, uint nodeId, out AtkTextNode* textNodePtr)
    {
        textNodePtr = null;

        return addon != IntPtr.Zero && AddonHelper.TryGetTextNode((AtkUnitBase*)addon.Address, nodeId, out textNodePtr);
    }

    /// <summary>
    /// Tries to resolve a text node from a ready addon wrapper by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="textNodePtr">The text node pointer, if found.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the addon is ready and the resolved node is a text node; otherwise, false.</returns>
    public static unsafe bool TryGetTextNode(this AtkUnitBasePtr addon, out AtkTextNode* textNodePtr, params int[] nodeIds)
    {
        textNodePtr = null;

        return addon != IntPtr.Zero && AddonHelper.TryGetTextNode((AtkUnitBase*)addon.Address, out textNodePtr, nodeIds);
    }

    /// <summary>
    /// Tries to resolve a component node from a ready addon wrapper by node id.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodeId">The node id to look up.</param>
    /// <param name="componentNodePtr">The component node pointer, if found.</param>
    /// <returns>True if the addon is ready and the node exists as a component node; otherwise, false.</returns>
    public static unsafe bool TryGetComponentNode(this AtkUnitBasePtr addon, uint nodeId, out AtkComponentNode* componentNodePtr)
    {
        componentNodePtr = null;

        return addon != IntPtr.Zero && AddonHelper.TryGetComponentNode((AtkUnitBase*)addon.Address, nodeId, out componentNodePtr);
    }

    /// <summary>
    /// Tries to resolve a component node from a ready addon wrapper by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="componentNodePtr">The component node pointer, if found.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the addon is ready and the resolved node is a component node; otherwise, false.</returns>
    public static unsafe bool TryGetComponentNode(this AtkUnitBasePtr addon, out AtkComponentNode* componentNodePtr, params int[] nodeIds)
    {
        componentNodePtr = null;

        return addon != IntPtr.Zero && AddonHelper.TryGetComponentNode((AtkUnitBase*)addon.Address, out componentNodePtr, nodeIds);
    }

    /// <summary>
    /// Tries to read the current text from a ready addon wrapper by node id.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodeId">The node id to read.</param>
    /// <param name="text">The resolved managed text, if found.</param>
    /// <returns>True if the addon is ready and the node text could be read; otherwise, false.</returns>
    public static unsafe bool TryReadText(this AtkUnitBasePtr addon, uint nodeId, out string text)
    {
        text = string.Empty;

        return addon != IntPtr.Zero && AddonHelper.TryReadText((AtkUnitBase*)addon.Address, nodeId, out text);
    }

    /// <summary>
    /// Tries to read the current text from a ready addon wrapper by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="text">The resolved managed text, if found.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the addon is ready and the node text could be read; otherwise, false.</returns>
    public static unsafe bool TryReadText(this AtkUnitBasePtr addon, out string text, params int[] nodeIds)
    {
        text = string.Empty;

        return addon != IntPtr.Zero && AddonHelper.TryReadText((AtkUnitBase*)addon.Address, out text, nodeIds);
    }

    /// <summary>
    /// Reads the current text from a ready addon wrapper by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The resolved text, or <see cref="string.Empty"/> if it could not be read.</returns>
    public static unsafe string ReadTextOrEmpty(this AtkUnitBasePtr addon, params int[] nodeIds)
        => addon != IntPtr.Zero ? AddonHelper.ReadTextOrEmpty((AtkUnitBase*)addon.Address, nodeIds) : string.Empty;

    /// <summary>
    /// Tries to send callback values to a ready addon.
    /// </summary>
    /// <param name="addon">The addon wrapper to send callback values to.</param>
    /// <param name="updateState">Whether the addon should update its internal state after the callback is fired.</param>
    /// <param name="values">The callback values to marshal into <see cref="AtkValue"/> instances.</param>
    /// <returns>True if the addon was ready and the callback was sent successfully; otherwise, false.</returns>
    public static unsafe bool SendCallback(this AtkUnitBasePtr addon, bool updateState, params object[] values)
        => addon != IntPtr.Zero && AddonHelper.SendCallback((AtkUnitBase*)addon.Address, updateState, values);

    /// <summary>
    /// Registers an addon event on the root node of a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon root node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(this AtkUnitBasePtr addon, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
        => addon != IntPtr.Zero ? AddonHelper.AddEvent((AtkUnitBase*)addon.Address, eventType, eventDelegate) : null;

    /// <summary>
    /// Registers an addon event on a node resolved by node id from a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodeId">The node id to bind the event to.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(this AtkUnitBasePtr addon, uint nodeId, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
        => addon != IntPtr.Zero ? AddonHelper.AddEvent((AtkUnitBase*)addon.Address, nodeId, eventType, eventDelegate) : null;

    /// <summary>
    /// Registers an addon event on a node resolved by a chain of node ids from a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(this AtkUnitBasePtr addon, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate, params int[] nodeIds)
        => addon != IntPtr.Zero ? AddonHelper.AddEvent((AtkUnitBase*)addon.Address, eventType, eventDelegate, nodeIds) : null;

    /// <summary>
    /// Registers a click event on a node resolved by node id from a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodeId">The node id to bind the event to.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddClickEvent(this AtkUnitBasePtr addon, uint nodeId, AddonNodeEventDelegate eventDelegate)
        => addon != IntPtr.Zero ? AddonHelper.AddClickEvent((AtkUnitBase*)addon.Address, nodeId, eventDelegate) : null;

    /// <summary>
    /// Registers a click event on a node resolved by a chain of node ids from a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddClickEvent(this AtkUnitBasePtr addon, AddonNodeEventDelegate eventDelegate, params int[] nodeIds)
        => addon != IntPtr.Zero ? AddonHelper.AddClickEvent((AtkUnitBase*)addon.Address, eventDelegate, nodeIds) : null;

    /// <summary>
    /// Registers hover handlers on a node resolved by node id from a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodeId">The node id to bind the hover events to.</param>
    /// <param name="onMouseOver">The handler to invoke when the node is hovered.</param>
    /// <param name="onMouseOut">The optional handler to invoke when the cursor leaves the node.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public static unsafe IDisposable? AddHoverEvents(this AtkUnitBasePtr addon, uint nodeId, AddonNodeEventDelegate onMouseOver, AddonNodeEventDelegate? onMouseOut = null)
        => addon != IntPtr.Zero ? AddonHelper.AddHoverEvents((AtkUnitBase*)addon.Address, nodeId, onMouseOver, onMouseOut) : null;

    /// <summary>
    /// Registers hover handlers on a node resolved by a chain of node ids from a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="onMouseOver">The handler to invoke when the node is hovered.</param>
    /// <param name="onMouseOut">The optional handler to invoke when the cursor leaves the node.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public static unsafe IDisposable? AddHoverEvents(this AtkUnitBasePtr addon, AddonNodeEventDelegate onMouseOver, AddonNodeEventDelegate? onMouseOut, params int[] nodeIds)
        => addon != IntPtr.Zero ? AddonHelper.AddHoverEvents((AtkUnitBase*)addon.Address, onMouseOver, onMouseOut, nodeIds) : null;

    /// <summary>
    /// Registers cursor behavior on hover for a node resolved by node id from a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodeId">The node id to bind the hover cursor behavior to.</param>
    /// <param name="cursor">The cursor to show while hovered.</param>
    /// <param name="resetCursorOnMouseOut">Whether the cursor should be reset when hover ends.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public static unsafe IDisposable? AddCursorOnHover(this AtkUnitBasePtr addon, uint nodeId, AddonCursorType cursor, bool resetCursorOnMouseOut = true)
        => addon != IntPtr.Zero ? AddonHelper.AddCursorOnHover((AtkUnitBase*)addon.Address, nodeId, cursor, resetCursorOnMouseOut) : null;

    /// <summary>
    /// Registers cursor behavior on hover for a node resolved by a chain of node ids from a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="cursor">The cursor to show while hovered.</param>
    /// <param name="resetCursorOnMouseOut">Whether the cursor should be reset when hover ends.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public static unsafe IDisposable? AddCursorOnHover(this AtkUnitBasePtr addon, AddonCursorType cursor, bool resetCursorOnMouseOut, params int[] nodeIds)
        => addon != IntPtr.Zero ? AddonHelper.AddCursorOnHover((AtkUnitBase*)addon.Address, cursor, resetCursorOnMouseOut, nodeIds) : null;

    /// <summary>
    /// Tries to configure cursor hover flags on a node resolved by node id from a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="nodeId">The node id to resolve.</param>
    /// <param name="clickableCursorOnHover">Whether the clickable cursor should be shown on hover.</param>
    /// <param name="textInputCursorOnHover">Whether the text input cursor should be shown on hover.</param>
    /// <returns>True if the node was resolved and updated; otherwise, false.</returns>
    public static unsafe bool TrySetNodeCursor(this AtkUnitBasePtr addon, uint nodeId, bool clickableCursorOnHover = true, bool textInputCursorOnHover = false)
        => addon != IntPtr.Zero && AddonHelper.TrySetNodeCursor((AtkUnitBase*)addon.Address, nodeId, clickableCursorOnHover, textInputCursorOnHover);

    /// <summary>
    /// Tries to configure cursor hover flags on a node resolved by a chain of node ids from a ready addon wrapper.
    /// </summary>
    /// <param name="addon">The addon wrapper to inspect.</param>
    /// <param name="clickableCursorOnHover">Whether the clickable cursor should be shown on hover.</param>
    /// <param name="textInputCursorOnHover">Whether the text input cursor should be shown on hover.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the node was resolved and updated; otherwise, false.</returns>
    public static unsafe bool TrySetNodeCursor(this AtkUnitBasePtr addon, bool clickableCursorOnHover, bool textInputCursorOnHover, params int[] nodeIds)
        => addon != IntPtr.Zero && AddonHelper.TrySetNodeCursor((AtkUnitBase*)addon.Address, clickableCursorOnHover, textInputCursorOnHover, nodeIds);
}
