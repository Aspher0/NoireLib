using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Events.EventDataTypes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.NativeWrapper;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace NoireLib.Helpers;

using AddonCursorType = AddonCursorType;
using AddonEventHandle = IAddonEventHandle;
using AddonLifecycleDelegate = IAddonLifecycle.AddonEventDelegate;
using AddonLifecycleEvent = AddonEvent;
using AddonNodeEventDelegate = IAddonEventManager.AddonEventDelegate;
using AddonNodeEventType = AddonEventType;
using AtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

/// <summary>
/// A helper class to help with addon manipulation, such as finding addons, getting data, sending callbacks, etc.
/// </summary>
public static class AddonHelper
{
    private static readonly Dictionary<string, IDisposable> KeyedRegistrations = new(StringComparer.Ordinal);

    /// <summary>
    /// Tries to get an addon by name without checking whether it is ready.
    /// </summary>
    /// <param name="addonName">The name of the addon to get.</param>
    /// <param name="addonPtr">A pointer to the addon, if found.</param>
    /// <returns>True if the addon was found; otherwise, false.</returns>
    public static unsafe bool TryGetAddon(string addonName, out AtkUnitBase* addonPtr)
    {
        addonPtr = null;

        AtkUnitBasePtr addonFromName = NoireService.GameGui.GetAddonByName(addonName);

        if (addonFromName == IntPtr.Zero)
            return false;

        addonPtr = (AtkUnitBase*)addonFromName.Address;
        return addonPtr != null;
    }

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
        => addon != null && addon->IsVisible && addon->UldManager.LoadedState == AtkLoadState.Loaded && addon->IsReady && addon->IsFullyLoaded();

    /// <summary>
    /// Tries to get the root node of an addon.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="nodePtr">The root node pointer, if found.</param>
    /// <returns>True if the addon is loaded and has a root node; otherwise, false.</returns>
    public static unsafe bool TryGetRootNode(AtkUnitBase* addon, out AtkResNode* nodePtr)
    {
        nodePtr = null;

        if (!IsAddonLoaded(addon))
            return false;

        nodePtr = addon->RootNode;
        return nodePtr != null;
    }

    /// <summary>
    /// Tries to get the root node of a ready addon by name.
    /// </summary>
    /// <param name="addonName">The name of the addon to inspect.</param>
    /// <param name="nodePtr">The root node pointer, if found.</param>
    /// <returns>True if the addon was found, ready, and has a root node; otherwise, false.</returns>
    public static unsafe bool TryGetRootNode(string addonName, out AtkResNode* nodePtr)
    {
        nodePtr = null;

        return TryGetReadyAddon(addonName, out var addonPtr) && TryGetRootNode(addonPtr, out nodePtr);
    }

    /// <summary>
    /// Tries to get a node from a ready addon by node id.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="nodeId">The node id to look up.</param>
    /// <param name="nodePtr">The node pointer, if found.</param>
    /// <returns>True if the addon is ready and the node exists; otherwise, false.</returns>
    public static unsafe bool TryGetNode(AtkUnitBase* addon, uint nodeId, out AtkResNode* nodePtr)
    {
        nodePtr = null;

        if (!IsAddonLoaded(addon))
            return false;

        nodePtr = addon->GetNodeById(nodeId);
        return nodePtr != null;
    }

    /// <summary>
    /// Tries to get a node from a ready addon by name and node id.
    /// </summary>
    /// <param name="addonName">The name of the addon to inspect.</param>
    /// <param name="nodeId">The node id to look up.</param>
    /// <param name="nodePtr">The node pointer, if found.</param>
    /// <returns>True if the addon was found, ready, and the node exists; otherwise, false.</returns>
    public static unsafe bool TryGetNode(string addonName, uint nodeId, out AtkResNode* nodePtr)
    {
        nodePtr = null;

        return TryGetReadyAddon(addonName, out var addonPtr) && TryGetNode(addonPtr, nodeId, out nodePtr);
    }

    /// <summary>
    /// Tries to get a node by traversing a chain of node ids inside a ready addon.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="nodePtr">The final node pointer, if found.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if every node in the chain was resolved; otherwise, false.</returns>
    public static unsafe bool TryGetNode(AtkUnitBase* addon, out AtkResNode* nodePtr, params int[] nodeIds)
    {
        nodePtr = null;

        ArgumentNullException.ThrowIfNull(nodeIds);

        if (!IsAddonLoaded(addon) || nodeIds.Length == 0)
            return false;

        uint[] resolvedNodeIds = new uint[nodeIds.Length];

        for (var index = 0; index < nodeIds.Length; index++)
            resolvedNodeIds[index] = (uint)nodeIds[index];

        return TryFindNodeByChain(addon->RootNode, resolvedNodeIds, 0, out nodePtr);
    }

    /// <summary>
    /// Tries to get a node by traversing a chain of node ids inside a ready addon by name.
    /// </summary>
    /// <param name="addonName">The name of the addon to inspect.</param>
    /// <param name="nodePtr">The final node pointer, if found.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the addon was found, ready, and every node in the chain was resolved; otherwise, false.</returns>
    public static unsafe bool TryGetNode(string addonName, out AtkResNode* nodePtr, params int[] nodeIds)
    {
        nodePtr = null;

        return TryGetReadyAddon(addonName, out var addonPtr) && TryGetNode(addonPtr, out nodePtr, nodeIds);
    }

    /// <summary>
    /// Tries to cast a node to a text node.
    /// </summary>
    /// <param name="node">The node to inspect.</param>
    /// <param name="textNodePtr">The text node pointer, if the cast succeeded.</param>
    /// <returns>True if the node is a text node; otherwise, false.</returns>
    public static unsafe bool TryGetTextNode(AtkResNode* node, out AtkTextNode* textNodePtr)
    {
        textNodePtr = node == null ? null : node->GetAsAtkTextNode();
        return textNodePtr != null;
    }

    /// <summary>
    /// Tries to resolve a text node from a ready addon by node id.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="nodeId">The node id to look up.</param>
    /// <param name="textNodePtr">The text node pointer, if found.</param>
    /// <returns>True if the addon is ready and the node exists as a text node; otherwise, false.</returns>
    public static unsafe bool TryGetTextNode(AtkUnitBase* addon, uint nodeId, out AtkTextNode* textNodePtr)
    {
        textNodePtr = null;

        return TryGetNode(addon, nodeId, out var nodePtr) && TryGetTextNode(nodePtr, out textNodePtr);
    }

    /// <summary>
    /// Tries to resolve a text node from a ready addon by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="textNodePtr">The text node pointer, if found.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the addon is ready and the resolved node is a text node; otherwise, false.</returns>
    public static unsafe bool TryGetTextNode(AtkUnitBase* addon, out AtkTextNode* textNodePtr, params int[] nodeIds)
    {
        textNodePtr = null;

        return TryGetNode(addon, out var nodePtr, nodeIds) && TryGetTextNode(nodePtr, out textNodePtr);
    }

    /// <summary>
    /// Tries to cast a node to a component node.
    /// </summary>
    /// <param name="node">The node to inspect.</param>
    /// <param name="componentNodePtr">The component node pointer, if the cast succeeded.</param>
    /// <returns>True if the node is a component node; otherwise, false.</returns>
    public static unsafe bool TryGetComponentNode(AtkResNode* node, out AtkComponentNode* componentNodePtr)
    {
        componentNodePtr = node == null ? null : node->GetAsAtkComponentNode();
        return componentNodePtr != null;
    }

    /// <summary>
    /// Tries to resolve a component node from a ready addon by node id.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="nodeId">The node id to look up.</param>
    /// <param name="componentNodePtr">The component node pointer, if found.</param>
    /// <returns>True if the addon is ready and the node exists as a component node; otherwise, false.</returns>
    public static unsafe bool TryGetComponentNode(AtkUnitBase* addon, uint nodeId, out AtkComponentNode* componentNodePtr)
    {
        componentNodePtr = null;

        return TryGetNode(addon, nodeId, out var nodePtr) && TryGetComponentNode(nodePtr, out componentNodePtr);
    }

    /// <summary>
    /// Tries to resolve a component node from a ready addon by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="componentNodePtr">The component node pointer, if found.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the addon is ready and the resolved node is a component node; otherwise, false.</returns>
    public static unsafe bool TryGetComponentNode(AtkUnitBase* addon, out AtkComponentNode* componentNodePtr, params int[] nodeIds)
    {
        componentNodePtr = null;

        return TryGetNode(addon, out var nodePtr, nodeIds) && TryGetComponentNode(nodePtr, out componentNodePtr);
    }

    /// <summary>
    /// Tries to read the current text from a text node.
    /// </summary>
    /// <param name="textNode">The text node to read.</param>
    /// <param name="text">The resolved managed text, if found.</param>
    /// <returns>True if the text node exists and its text could be read; otherwise, false.</returns>
    public static unsafe bool TryReadText(AtkTextNode* textNode, out string text)
    {
        text = string.Empty;

        if (textNode == null)
            return false;

        text = ReadUtf8String(textNode->NodeText);
        return !string.IsNullOrEmpty(text);
    }

    /// <summary>
    /// Tries to read the current text from a node.
    /// </summary>
    /// <param name="node">The node to read.</param>
    /// <param name="text">The resolved managed text, if found.</param>
    /// <returns>True if the node is a text node and its text could be read; otherwise, false.</returns>
    public static unsafe bool TryReadText(AtkResNode* node, out string text)
    {
        text = string.Empty;

        return TryGetTextNode(node, out var textNodePtr) && TryReadText(textNodePtr, out text);
    }

    /// <summary>
    /// Tries to read the current text from a ready addon by node id.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="nodeId">The node id to read.</param>
    /// <param name="text">The resolved managed text, if found.</param>
    /// <returns>True if the addon is ready and the node text could be read; otherwise, false.</returns>
    public static unsafe bool TryReadText(AtkUnitBase* addon, uint nodeId, out string text)
    {
        text = string.Empty;

        return TryGetTextNode(addon, nodeId, out var textNodePtr) && TryReadText(textNodePtr, out text);
    }

    /// <summary>
    /// Tries to read the current text from a ready addon by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="text">The resolved managed text, if found.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the addon is ready and the node text could be read; otherwise, false.</returns>
    public static unsafe bool TryReadText(AtkUnitBase* addon, out string text, params int[] nodeIds)
    {
        text = string.Empty;

        return TryGetTextNode(addon, out var textNodePtr, nodeIds) && TryReadText(textNodePtr, out text);
    }

    /// <summary>
    /// Reads the current text from a ready addon by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The resolved text, or <see cref="string.Empty"/> if it could not be read.</returns>
    public static unsafe string ReadTextOrEmpty(AtkUnitBase* addon, params int[] nodeIds)
        => TryReadText(addon, out var text, nodeIds) ? text : string.Empty;

    /// <summary>
    /// Reads the current text from a ready addon by name and a chain of node ids.
    /// </summary>
    /// <param name="addonName">The name of the addon to inspect.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The resolved text, or <see cref="string.Empty"/> if it could not be read.</returns>
    public static unsafe string ReadTextOrEmpty(string addonName, params int[] nodeIds)
        => TryGetReadyAddon(addonName, out var addonPtr) ? ReadTextOrEmpty(addonPtr, nodeIds) : string.Empty;

    /// <summary>
    /// Tries to read a managed value from an <see cref="AtkValue"/>.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <param name="result">The resolved managed value.</param>
    /// <returns>True if the value could be read; otherwise, false.</returns>
    public static unsafe bool TryReadValue(AtkValue* value, out object? result)
    {
        result = null;

        if (value == null)
            return false;

        switch (value->Type)
        {
            case AtkValueType.Null:
                return true;

            case AtkValueType.Bool:
                result = value->Bool;
                return true;

            case AtkValueType.Int:
                result = value->Int;
                return true;

            case AtkValueType.UInt:
                result = value->UInt;
                return true;

            case AtkValueType.Int64:
                result = value->Int64;
                return true;

            case AtkValueType.UInt64:
                result = value->UInt64;
                return true;

            case AtkValueType.Float:
                result = value->Float;
                return true;

            case AtkValueType.String:
            case AtkValueType.ManagedString:
                result = ReadCString(value->String);
                return true;

            case AtkValueType.Pointer:
                result = (nint)value->Pointer;
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads a managed value from an <see cref="AtkValue"/>.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The resolved managed value, or null if it could not be read.</returns>
    public static unsafe object? ReadValueOrDefault(AtkValue* value)
        => TryReadValue(value, out var result) ? result : null;

    /// <summary>
    /// Registers an addon lifecycle listener for multiple addon names.
    /// </summary>
    /// <param name="key">The registration key used to unregister the listener later.</param>
    /// <param name="eventType">The lifecycle event type to listen for.</param>
    /// <param name="addonNames">The addon names to listen for.</param>
    /// <param name="handler">The lifecycle handler to invoke.</param>
    /// <returns>A disposable registration that unregisters the handler when disposed.</returns>
    public static IDisposable RegisterLifecycleListener(string key, AddonLifecycleEvent eventType, IEnumerable<string> addonNames, AddonLifecycleDelegate handler)
    {
        IDisposable registration = RegisterLifecycleListener(eventType, addonNames, handler);
        RegisterEvent(key, registration);
        return registration;
    }

    /// <summary>
    /// Registers an addon lifecycle listener for multiple addon names.
    /// </summary>
    /// <param name="eventType">The lifecycle event type to listen for.</param>
    /// <param name="addonNames">The addon names to listen for.</param>
    /// <param name="handler">The lifecycle handler to invoke.</param>
    /// <returns>A disposable registration that unregisters the handler when disposed.</returns>
    public static IDisposable RegisterLifecycleListener(AddonLifecycleEvent eventType, IEnumerable<string> addonNames, AddonLifecycleDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        string[] materializedAddonNames = MaterializeAddonNames(addonNames);
        NoireService.AddonLifecycle.RegisterListener(eventType, materializedAddonNames, handler);

        return new ActionDisposable(() => NoireService.AddonLifecycle.UnregisterListener(eventType, materializedAddonNames, handler));
    }

    /// <summary>
    /// Registers an addon lifecycle listener for a single addon name.
    /// </summary>
    /// <param name="key">The registration key used to unregister the listener later.</param>
    /// <param name="eventType">The lifecycle event type to listen for.</param>
    /// <param name="addonName">The addon name to listen for.</param>
    /// <param name="handler">The lifecycle handler to invoke.</param>
    /// <returns>A disposable registration that unregisters the handler when disposed.</returns>
    public static IDisposable RegisterLifecycleListener(string key, AddonLifecycleEvent eventType, string addonName, AddonLifecycleDelegate handler)
    {
        IDisposable registration = RegisterLifecycleListener(eventType, addonName, handler);
        RegisterEvent(key, registration);
        return registration;
    }

    /// <summary>
    /// Registers an addon lifecycle listener for a single addon name.
    /// </summary>
    /// <param name="eventType">The lifecycle event type to listen for.</param>
    /// <param name="addonName">The addon name to listen for.</param>
    /// <param name="handler">The lifecycle handler to invoke.</param>
    /// <returns>A disposable registration that unregisters the handler when disposed.</returns>
    public static IDisposable RegisterLifecycleListener(AddonLifecycleEvent eventType, string addonName, AddonLifecycleDelegate handler)
    {
        ValidateAddonName(addonName, nameof(addonName));
        ArgumentNullException.ThrowIfNull(handler);

        NoireService.AddonLifecycle.RegisterListener(eventType, addonName, handler);
        return new ActionDisposable(() => NoireService.AddonLifecycle.UnregisterListener(eventType, addonName, handler));
    }

    /// <summary>
    /// Registers a global addon lifecycle listener for every addon.
    /// </summary>
    /// <param name="key">The registration key used to unregister the listener later.</param>
    /// <param name="eventType">The lifecycle event type to listen for.</param>
    /// <param name="handler">The lifecycle handler to invoke.</param>
    /// <returns>A disposable registration that unregisters the handler when disposed.</returns>
    public static IDisposable RegisterLifecycleListener(string key, AddonLifecycleEvent eventType, AddonLifecycleDelegate handler)
    {
        IDisposable registration = RegisterLifecycleListener(eventType, handler);
        RegisterEvent(key, registration);
        return registration;
    }

    /// <summary>
    /// Registers a global addon lifecycle listener for every addon.
    /// </summary>
    /// <param name="eventType">The lifecycle event type to listen for.</param>
    /// <param name="handler">The lifecycle handler to invoke.</param>
    /// <returns>A disposable registration that unregisters the handler when disposed.</returns>
    public static IDisposable RegisterLifecycleListener(AddonLifecycleEvent eventType, AddonLifecycleDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        NoireService.AddonLifecycle.RegisterListener(eventType, handler);
        return new ActionDisposable(() => NoireService.AddonLifecycle.UnregisterListener(eventType, handler));
    }

    /// <summary>
    /// Unregisters addon lifecycle listeners for multiple addon names.
    /// </summary>
    /// <param name="eventType">The lifecycle event type to stop listening for.</param>
    /// <param name="addonNames">The addon names to unregister.</param>
    /// <param name="handler">The optional specific handler to remove.</param>
    public static void UnregisterLifecycleListener(AddonLifecycleEvent eventType, IEnumerable<string> addonNames, AddonLifecycleDelegate? handler = null)
    {
        string[] materializedAddonNames = MaterializeAddonNames(addonNames);
        NoireService.AddonLifecycle.UnregisterListener(eventType, materializedAddonNames, handler!);
    }

    /// <summary>
    /// Unregisters addon lifecycle listeners for a single addon name.
    /// </summary>
    /// <param name="eventType">The lifecycle event type to stop listening for.</param>
    /// <param name="addonName">The addon name to unregister.</param>
    /// <param name="handler">The optional specific handler to remove.</param>
    public static void UnregisterLifecycleListener(AddonLifecycleEvent eventType, string addonName, AddonLifecycleDelegate? handler = null)
    {
        ValidateAddonName(addonName, nameof(addonName));
        NoireService.AddonLifecycle.UnregisterListener(eventType, addonName, handler!);
    }

    /// <summary>
    /// Unregisters global addon lifecycle listeners.
    /// </summary>
    /// <param name="eventType">The lifecycle event type to stop listening for.</param>
    /// <param name="handler">The optional specific handler to remove.</param>
    public static void UnregisterLifecycleListener(AddonLifecycleEvent eventType, AddonLifecycleDelegate? handler = null)
        => NoireService.AddonLifecycle.UnregisterListener(eventType, handler!);

    /// <summary>
    /// Unregisters every lifecycle registration for the supplied handlers.
    /// </summary>
    /// <param name="handlers">The handlers to remove.</param>
    public static void UnregisterLifecycleListener(params AddonLifecycleDelegate[] handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        NoireService.AddonLifecycle.UnregisterListener(handlers);
    }

    /// <summary>
    /// Resolves a modified virtual table address to the original addon virtual table.
    /// </summary>
    /// <param name="virtualTableAddress">The modified virtual table address.</param>
    /// <returns>The original unmodified virtual table address.</returns>
    public static nint GetOriginalVirtualTable(nint virtualTableAddress)
        => NoireService.AddonLifecycle.GetOriginalVirtualTable(virtualTableAddress);

    /// <summary>
    /// Registers a disposable addon registration under a key.
    /// </summary>
    /// <param name="key">The registration key used to unregister the registration later.</param>
    /// <param name="registration">The registration to store.</param>
    /// <returns>True if the key was newly added; otherwise, false when an existing keyed registration was replaced.</returns>
    public static bool RegisterEvent(string key, IDisposable? registration)
    {
        ValidateRegistrationKey(key);

        if (registration == null)
            return false;

        IDisposable? previousRegistration;
        bool isNewRegistration;

        lock (KeyedRegistrations)
        {
            isNewRegistration = !KeyedRegistrations.TryGetValue(key, out previousRegistration);
            KeyedRegistrations[key] = registration;
        }

        previousRegistration?.Dispose();
        return isNewRegistration;
    }

    /// <summary>
    /// Registers an addon event handle under a key.
    /// </summary>
    /// <param name="key">The registration key used to unregister the event later.</param>
    /// <param name="eventHandle">The addon event handle to store.</param>
    /// <returns>True if the event handle was stored; otherwise, false.</returns>
    public static bool RegisterEvent(string key, AddonEventHandle? eventHandle)
        => RegisterEvent(key, eventHandle == null ? null : WrapEventHandle(eventHandle));

    /// <summary>
    /// Determines whether keyed registrations exist for the supplied key.
    /// </summary>
    /// <param name="key">The registration key to inspect.</param>
    /// <returns>True if the key has stored registrations; otherwise, false.</returns>
    public static bool HasRegisteredEvents(string key)
    {
        ValidateRegistrationKey(key);

        lock (KeyedRegistrations)
            return KeyedRegistrations.ContainsKey(key);
    }

    /// <summary>
    /// Unregisters every stored registration for the supplied key.
    /// </summary>
    /// <param name="key">The registration key to unregister.</param>
    /// <returns>True if at least one registration was removed; otherwise, false.</returns>
    public static bool UnregisterEvents(string key)
    {
        ValidateRegistrationKey(key);

        IDisposable? registration;

        lock (KeyedRegistrations)
        {
            if (!KeyedRegistrations.TryGetValue(key, out registration))
                return false;

            KeyedRegistrations.Remove(key);
        }

        registration.Dispose();

        return true;
    }

    /// <summary>
    /// Unregisters every stored keyed registration.
    /// </summary>
    public static void UnregisterAllEvents()
    {
        List<IDisposable> registrations = new();

        lock (KeyedRegistrations)
        {
            registrations.AddRange(KeyedRegistrations.Values);

            KeyedRegistrations.Clear();
        }

        foreach (IDisposable registration in registrations)
            registration.Dispose();
    }

    /// <summary>
    /// Tries to prevent the original lifecycle action from running.
    /// </summary>
    /// <param name="addonArgs">The lifecycle event arguments to mutate.</param>
    /// <returns>True if the request was applied during this call; otherwise, false.</returns>
    public static bool TryPreventOriginal(AddonArgs addonArgs)
    {
        ArgumentNullException.ThrowIfNull(addonArgs);

        if (addonArgs.PreventOriginalRequested)
            return false;

        addonArgs.PreventOriginal();
        return addonArgs.PreventOriginalRequested;
    }

    /// <summary>
    /// Tries to resolve an addon pointer from lifecycle event arguments.
    /// </summary>
    /// <param name="addonArgs">The lifecycle event arguments to inspect.</param>
    /// <param name="addonPtr">The resolved addon pointer, if found.</param>
    /// <returns>True if the addon pointer was resolved; otherwise, false.</returns>
    public static unsafe bool TryGetAddon(AddonArgs addonArgs, out AtkUnitBase* addonPtr)
    {
        addonPtr = null;

        ArgumentNullException.ThrowIfNull(addonArgs);

        if (addonArgs.Addon == IntPtr.Zero)
            return false;

        addonPtr = (AtkUnitBase*)addonArgs.Addon.Address;
        return addonPtr != null;
    }

    /// <summary>
    /// Tries to resolve a ready addon pointer from lifecycle event arguments.
    /// </summary>
    /// <param name="addonArgs">The lifecycle event arguments to inspect.</param>
    /// <param name="addonPtr">The resolved addon pointer, if found.</param>
    /// <returns>True if the addon pointer was resolved and the addon is ready; otherwise, false.</returns>
    public static unsafe bool TryGetReadyAddon(AddonArgs addonArgs, out AtkUnitBase* addonPtr)
        => TryGetAddon(addonArgs, out addonPtr) && IsAddonLoaded(addonPtr);

    /// <summary>
    /// Tries to resolve an addon pointer from addon event data.
    /// </summary>
    /// <param name="eventData">The addon event data to inspect.</param>
    /// <param name="addonPtr">The resolved addon pointer, if found.</param>
    /// <returns>True if the addon pointer was resolved; otherwise, false.</returns>
    public static unsafe bool TryGetAddon(AddonEventData eventData, out AtkUnitBase* addonPtr)
    {
        addonPtr = null;

        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.AddonPointer == IntPtr.Zero)
            return false;

        addonPtr = (AtkUnitBase*)eventData.AddonPointer;
        return addonPtr != null;
    }

    /// <summary>
    /// Tries to resolve a ready addon pointer from addon event data.
    /// </summary>
    /// <param name="eventData">The addon event data to inspect.</param>
    /// <param name="addonPtr">The resolved addon pointer, if found.</param>
    /// <returns>True if the addon pointer was resolved and the addon is ready; otherwise, false.</returns>
    public static unsafe bool TryGetReadyAddon(AddonEventData eventData, out AtkUnitBase* addonPtr)
        => TryGetAddon(eventData, out addonPtr) && IsAddonLoaded(addonPtr);

    /// <summary>
    /// Tries to resolve the target node from addon event data.
    /// </summary>
    /// <param name="eventData">The addon event data to inspect.</param>
    /// <param name="nodePtr">The resolved node pointer, if found.</param>
    /// <returns>True if the node pointer was resolved; otherwise, false.</returns>
    public static unsafe bool TryGetNode(AddonEventData eventData, out AtkResNode* nodePtr)
    {
        nodePtr = null;

        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.NodeTargetPointer == IntPtr.Zero)
            return false;

        nodePtr = (AtkResNode*)eventData.NodeTargetPointer;
        return nodePtr != null;
    }

    /// <summary>
    /// Registers an addon event on the supplied addon node.
    /// </summary>
    /// <param name="key">The registration key used to unregister the event later.</param>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="node">The node to bind the event to.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(string key, AtkUnitBase* addon, AtkResNode* node, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
    {
        AddonEventHandle? eventHandle = AddEvent(addon, node, eventType, eventDelegate);
        RegisterEvent(key, eventHandle);
        return eventHandle;
    }

    /// <summary>
    /// Registers an addon event on the supplied addon node.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="node">The node to bind the event to.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(AtkUnitBase* addon, AtkResNode* node, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
    {
        ArgumentNullException.ThrowIfNull(eventDelegate);

        if (!IsAddonLoaded(addon) || node == null)
            return null;

        return NoireService.AddonEventManager.AddEvent((nint)addon, (nint)node, eventType, eventDelegate);
    }

    /// <summary>
    /// Registers an addon event on the root node of a ready addon.
    /// </summary>
    /// <param name="key">The registration key used to unregister the event later.</param>
    /// <param name="addon">The addon that owns the root node.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon root node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(string key, AtkUnitBase* addon, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
    {
        AddonEventHandle? eventHandle = AddEvent(addon, eventType, eventDelegate);
        RegisterEvent(key, eventHandle);
        return eventHandle;
    }

    /// <summary>
    /// Registers an addon event on the root node of a ready addon.
    /// </summary>
    /// <param name="addon">The addon that owns the root node.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon root node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(AtkUnitBase* addon, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
        => TryGetRootNode(addon, out var nodePtr) ? AddEvent(addon, nodePtr, eventType, eventDelegate) : null;

    /// <summary>
    /// Registers an addon event on a node resolved by node id.
    /// </summary>
    /// <param name="key">The registration key used to unregister the event later.</param>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="nodeId">The node id to bind the event to.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(string key, AtkUnitBase* addon, uint nodeId, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
    {
        AddonEventHandle? eventHandle = AddEvent(addon, nodeId, eventType, eventDelegate);
        RegisterEvent(key, eventHandle);
        return eventHandle;
    }

    /// <summary>
    /// Registers an addon event on a node resolved by node id.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="nodeId">The node id to bind the event to.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(AtkUnitBase* addon, uint nodeId, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
        => TryGetNode(addon, nodeId, out var nodePtr) ? AddEvent(addon, nodePtr, eventType, eventDelegate) : null;

    /// <summary>
    /// Registers an addon event on a node resolved by a chain of node ids.
    /// </summary>
    /// <param name="key">The registration key used to unregister the event later.</param>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(string key, AtkUnitBase* addon, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate, params int[] nodeIds)
    {
        AddonEventHandle? eventHandle = AddEvent(addon, eventType, eventDelegate, nodeIds);
        RegisterEvent(key, eventHandle);
        return eventHandle;
    }

    /// <summary>
    /// Registers an addon event on a node resolved by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(AtkUnitBase* addon, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate, params int[] nodeIds)
        => TryGetNode(addon, out var nodePtr, nodeIds) ? AddEvent(addon, nodePtr, eventType, eventDelegate) : null;

    /// <summary>
    /// Registers an addon event on the root node of a ready addon by name.
    /// </summary>
    /// <param name="key">The registration key used to unregister the event later.</param>
    /// <param name="addonName">The addon name to resolve.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon root node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(string key, string addonName, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
    {
        AddonEventHandle? eventHandle = AddEvent(addonName, eventType, eventDelegate);
        RegisterEvent(key, eventHandle);
        return eventHandle;
    }

    /// <summary>
    /// Registers an addon event on the root node of a ready addon by name.
    /// </summary>
    /// <param name="addonName">The addon name to resolve.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon root node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(string addonName, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
        => TryGetReadyAddon(addonName, out var addonPtr) ? AddEvent(addonPtr, eventType, eventDelegate) : null;

    /// <summary>
    /// Registers an addon event on a node resolved by name and node id.
    /// </summary>
    /// <param name="key">The registration key used to unregister the event later.</param>
    /// <param name="addonName">The addon name to resolve.</param>
    /// <param name="nodeId">The node id to bind the event to.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(string key, string addonName, uint nodeId, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
    {
        AddonEventHandle? eventHandle = AddEvent(addonName, nodeId, eventType, eventDelegate);
        RegisterEvent(key, eventHandle);
        return eventHandle;
    }

    /// <summary>
    /// Registers an addon event on a node resolved by name and node id.
    /// </summary>
    /// <param name="addonName">The addon name to resolve.</param>
    /// <param name="nodeId">The node id to bind the event to.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(string addonName, uint nodeId, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate)
        => TryGetReadyAddon(addonName, out var addonPtr) ? AddEvent(addonPtr, nodeId, eventType, eventDelegate) : null;

    /// <summary>
    /// Registers an addon event on a node resolved by addon name and a chain of node ids.
    /// </summary>
    /// <param name="key">The registration key used to unregister the event later.</param>
    /// <param name="addonName">The addon name to resolve.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(string key, string addonName, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate, params int[] nodeIds)
    {
        AddonEventHandle? eventHandle = AddEvent(addonName, eventType, eventDelegate, nodeIds);
        RegisterEvent(key, eventHandle);
        return eventHandle;
    }

    /// <summary>
    /// Registers an addon event on a node resolved by addon name and a chain of node ids.
    /// </summary>
    /// <param name="addonName">The addon name to resolve.</param>
    /// <param name="eventType">The event type to listen for.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddEvent(string addonName, AddonNodeEventType eventType, AddonNodeEventDelegate eventDelegate, params int[] nodeIds)
        => TryGetReadyAddon(addonName, out var addonPtr) ? AddEvent(addonPtr, eventType, eventDelegate, nodeIds) : null;

    /// <summary>
    /// Removes a previously registered addon event.
    /// </summary>
    /// <param name="eventHandle">The event handle to remove.</param>
    public static void RemoveEvent(AddonEventHandle? eventHandle)
    {
        if (eventHandle != null)
            NoireService.AddonEventManager.RemoveEvent(eventHandle);
    }

    /// <summary>
    /// Removes previously registered addon events.
    /// </summary>
    /// <param name="eventHandles">The event handles to remove.</param>
    public static void RemoveEvents(params AddonEventHandle?[] eventHandles)
    {
        ArgumentNullException.ThrowIfNull(eventHandles);

        foreach (AddonEventHandle? eventHandle in eventHandles)
            RemoveEvent(eventHandle);
    }

    /// <summary>
    /// Forces the game cursor to the supplied cursor type.
    /// </summary>
    /// <param name="cursor">The cursor type to force.</param>
    public static void SetCursor(AddonCursorType cursor)
        => NoireService.AddonEventManager.SetCursor(cursor);

    /// <summary>
    /// Resets the forced game cursor.
    /// </summary>
    public static void ResetCursor()
        => NoireService.AddonEventManager.ResetCursor();

    /// <summary>
    /// Tries to configure cursor hover flags on a node.
    /// </summary>
    /// <param name="node">The node to update.</param>
    /// <param name="clickableCursorOnHover">Whether the clickable cursor should be shown on hover.</param>
    /// <param name="textInputCursorOnHover">Whether the text input cursor should be shown on hover.</param>
    /// <returns>True if the node was updated; otherwise, false.</returns>
    public static unsafe bool TrySetNodeCursor(AtkResNode* node, bool clickableCursorOnHover = true, bool textInputCursorOnHover = false)
    {
        if (node == null)
            return false;

        node->IsClickableCursorOnHover = clickableCursorOnHover;
        node->IsTextInputCursorOnHover = textInputCursorOnHover;
        return true;
    }

    /// <summary>
    /// Tries to configure cursor hover flags on a node resolved by node id.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="nodeId">The node id to resolve.</param>
    /// <param name="clickableCursorOnHover">Whether the clickable cursor should be shown on hover.</param>
    /// <param name="textInputCursorOnHover">Whether the text input cursor should be shown on hover.</param>
    /// <returns>True if the node was resolved and updated; otherwise, false.</returns>
    public static unsafe bool TrySetNodeCursor(AtkUnitBase* addon, uint nodeId, bool clickableCursorOnHover = true, bool textInputCursorOnHover = false)
        => TryGetNode(addon, nodeId, out var nodePtr) && TrySetNodeCursor(nodePtr, clickableCursorOnHover, textInputCursorOnHover);

    /// <summary>
    /// Tries to configure cursor hover flags on a node resolved by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="clickableCursorOnHover">Whether the clickable cursor should be shown on hover.</param>
    /// <param name="textInputCursorOnHover">Whether the text input cursor should be shown on hover.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the node was resolved and updated; otherwise, false.</returns>
    public static unsafe bool TrySetNodeCursor(AtkUnitBase* addon, bool clickableCursorOnHover, bool textInputCursorOnHover, params int[] nodeIds)
        => TryGetNode(addon, out var nodePtr, nodeIds) && TrySetNodeCursor(nodePtr, clickableCursorOnHover, textInputCursorOnHover);

    /// <summary>
    /// Registers a mouse click event on the supplied addon node.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="node">The node to bind the event to.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddClickEvent(AtkUnitBase* addon, AtkResNode* node, AddonNodeEventDelegate eventDelegate)
        => AddEvent(addon, node, AddonNodeEventType.MouseClick, eventDelegate);

    /// <summary>
    /// Registers a mouse click event on a node resolved by node id.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="nodeId">The node id to bind the event to.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddClickEvent(AtkUnitBase* addon, uint nodeId, AddonNodeEventDelegate eventDelegate)
        => AddEvent(addon, nodeId, AddonNodeEventType.MouseClick, eventDelegate);

    /// <summary>
    /// Registers a mouse click event on a node resolved by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddClickEvent(AtkUnitBase* addon, AddonNodeEventDelegate eventDelegate, params int[] nodeIds)
        => AddEvent(addon, AddonNodeEventType.MouseClick, eventDelegate, nodeIds);

    /// <summary>
    /// Registers a button click event on the supplied addon node.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="node">The node to bind the event to.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddButtonClickEvent(AtkUnitBase* addon, AtkResNode* node, AddonNodeEventDelegate eventDelegate)
        => AddEvent(addon, node, AddonNodeEventType.ButtonClick, eventDelegate);

    /// <summary>
    /// Registers a button press event on the supplied addon node.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="node">The node to bind the event to.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddButtonPressEvent(AtkUnitBase* addon, AtkResNode* node, AddonNodeEventDelegate eventDelegate)
        => AddEvent(addon, node, AddonNodeEventType.ButtonPress, eventDelegate);

    /// <summary>
    /// Registers a button release event on the supplied addon node.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="node">The node to bind the event to.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddButtonReleaseEvent(AtkUnitBase* addon, AtkResNode* node, AddonNodeEventDelegate eventDelegate)
        => AddEvent(addon, node, AddonNodeEventType.ButtonRelease, eventDelegate);

    /// <summary>
    /// Registers a double click event on the supplied addon node.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="node">The node to bind the event to.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddDoubleClickEvent(AtkUnitBase* addon, AtkResNode* node, AddonNodeEventDelegate eventDelegate)
        => AddEvent(addon, node, AddonNodeEventType.MouseDoubleClick, eventDelegate);

    /// <summary>
    /// Registers an input received event on the supplied addon node.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="node">The node to bind the event to.</param>
    /// <param name="eventDelegate">The event handler to invoke.</param>
    /// <returns>The registered event handle, or null if the addon or node was unavailable.</returns>
    public static unsafe AddonEventHandle? AddInputReceivedEvent(AtkUnitBase* addon, AtkResNode* node, AddonNodeEventDelegate eventDelegate)
        => AddEvent(addon, node, AddonNodeEventType.InputReceived, eventDelegate);

    /// <summary>
    /// Registers hover handlers on the supplied addon node.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="node">The node to bind the hover events to.</param>
    /// <param name="onMouseOver">The handler to invoke when the node is hovered.</param>
    /// <param name="onMouseOut">The optional handler to invoke when the cursor leaves the node.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public static unsafe IDisposable? AddHoverEvents(AtkUnitBase* addon, AtkResNode* node, AddonNodeEventDelegate onMouseOver, AddonNodeEventDelegate? onMouseOut = null)
    {
        ArgumentNullException.ThrowIfNull(onMouseOver);

        AddonEventHandle? mouseOverHandle = AddEvent(addon, node, AddonNodeEventType.MouseOver, onMouseOver);
        AddonEventHandle? mouseOutHandle = onMouseOut == null ? null : AddEvent(addon, node, AddonNodeEventType.MouseOut, onMouseOut);

        return CreateEventRegistration(mouseOverHandle, mouseOutHandle);
    }

    /// <summary>
    /// Registers hover handlers on a node resolved by node id.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="nodeId">The node id to bind the hover events to.</param>
    /// <param name="onMouseOver">The handler to invoke when the node is hovered.</param>
    /// <param name="onMouseOut">The optional handler to invoke when the cursor leaves the node.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public static unsafe IDisposable? AddHoverEvents(AtkUnitBase* addon, uint nodeId, AddonNodeEventDelegate onMouseOver, AddonNodeEventDelegate? onMouseOut = null)
        => TryGetNode(addon, nodeId, out var nodePtr) ? AddHoverEvents(addon, nodePtr, onMouseOver, onMouseOut) : null;

    /// <summary>
    /// Registers hover handlers on a node resolved by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="onMouseOver">The handler to invoke when the node is hovered.</param>
    /// <param name="onMouseOut">The optional handler to invoke when the cursor leaves the node.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public static unsafe IDisposable? AddHoverEvents(AtkUnitBase* addon, AddonNodeEventDelegate onMouseOver, AddonNodeEventDelegate? onMouseOut, params int[] nodeIds)
        => TryGetNode(addon, out var nodePtr, nodeIds) ? AddHoverEvents(addon, nodePtr, onMouseOver, onMouseOut) : null;

    /// <summary>
    /// Registers cursor behavior on hover for the supplied addon node.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="node">The node to bind the hover cursor behavior to.</param>
    /// <param name="cursor">The cursor to show while hovered.</param>
    /// <param name="resetCursorOnMouseOut">Whether the cursor should be reset when hover ends.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public static unsafe IDisposable? AddCursorOnHover(AtkUnitBase* addon, AtkResNode* node, AddonCursorType cursor, bool resetCursorOnMouseOut = true)
    {
        if (!TrySetNodeCursor(node, clickableCursorOnHover: cursor is AddonCursorType.Clickable or AddonCursorType.Hand, textInputCursorOnHover: cursor is AddonCursorType.TextInput or AddonCursorType.TextClick))
            return null;

        AddonEventHandle? mouseOverHandle = AddEvent(addon, node, AddonNodeEventType.MouseOver, (_, _) => SetCursor(cursor));
        AddonEventHandle? mouseOutHandle = resetCursorOnMouseOut ? AddEvent(addon, node, AddonNodeEventType.MouseOut, static (_, _) => ResetCursor()) : null;

        return CreateEventRegistration(mouseOverHandle, mouseOutHandle);
    }

    /// <summary>
    /// Registers cursor behavior on hover for a node resolved by node id.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="nodeId">The node id to bind the hover cursor behavior to.</param>
    /// <param name="cursor">The cursor to show while hovered.</param>
    /// <param name="resetCursorOnMouseOut">Whether the cursor should be reset when hover ends.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public static unsafe IDisposable? AddCursorOnHover(AtkUnitBase* addon, uint nodeId, AddonCursorType cursor, bool resetCursorOnMouseOut = true)
        => TryGetNode(addon, nodeId, out var nodePtr) ? AddCursorOnHover(addon, nodePtr, cursor, resetCursorOnMouseOut) : null;

    /// <summary>
    /// Registers cursor behavior on hover for a node resolved by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon that owns the node.</param>
    /// <param name="cursor">The cursor to show while hovered.</param>
    /// <param name="resetCursorOnMouseOut">Whether the cursor should be reset when hover ends.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>A disposable registration that unregisters every created event, or null if nothing was registered.</returns>
    public static unsafe IDisposable? AddCursorOnHover(AtkUnitBase* addon, AddonCursorType cursor, bool resetCursorOnMouseOut, params int[] nodeIds)
        => TryGetNode(addon, out var nodePtr, nodeIds) ? AddCursorOnHover(addon, nodePtr, cursor, resetCursorOnMouseOut) : null;

    /// <summary>
    /// Tries to send callback values to a ready addon by name.
    /// </summary>
    /// <param name="addonName">The name of the addon to send callback values to.</param>
    /// <param name="updateState">Whether the addon should update its internal state after the callback is fired.</param>
    /// <param name="values">The callback values to marshal into <see cref="AtkValue"/> instances.</param>
    /// <returns>True if the addon was found, ready, and the callback was sent successfully; otherwise, false.</returns>
    public static unsafe bool SendCallback(string addonName, bool updateState, params object[] values)
    {
        if (!TryGetReadyAddon(addonName, out var addonPtr))
            return false;

        return SendCallback(addonPtr, updateState, values);
    }

    /// <summary>
    /// Tries to send callback values to a ready addon.
    /// </summary>
    /// <param name="addon">The addon to send callback values to.</param>
    /// <param name="updateState">Whether the addon should update its internal state after the callback is fired.</param>
    /// <param name="values">The callback values to marshal into <see cref="AtkValue"/> instances.</param>
    /// <returns>True if the addon was ready and the callback was sent successfully; otherwise, false.</returns>
    public static unsafe bool SendCallback(AtkUnitBase* addon, bool updateState, params object[] values)
    {
        if (!IsAddonLoaded(addon))
            return false;

        ArgumentNullException.ThrowIfNull(values);

        if (values.Length == 0)
            return addon->FireCallback(0, null, updateState);

        var atkValues = new AtkValue[values.Length];

        fixed (AtkValue* atkValuesPtr = atkValues)
        {
            try
            {
                for (var index = 0; index < values.Length; index++)
                {
                    atkValuesPtr[index].Ctor();
                    TryWriteAtkValue(&atkValuesPtr[index], values[index]);
                }

                return addon->FireCallback((uint)values.Length, atkValuesPtr, updateState);
            }
            finally
            {
                for (var index = 0; index < values.Length; index++)
                    atkValuesPtr[index].Dtor();
            }
        }
    }

    private static unsafe bool TryFindNodeByChain(AtkResNode* scopeNode, IReadOnlyList<uint> nodeIds, int index, out AtkResNode* nodePtr)
    {
        nodePtr = null;

        if (scopeNode == null || index >= nodeIds.Count)
            return false;

        return TryFindNodeByChainInDirection(scopeNode, nodeIds, index, searchPreviousSiblings: false, out nodePtr)
            || TryFindNodeByChainInDirection(scopeNode->PrevSiblingNode, nodeIds, index, searchPreviousSiblings: true, out nodePtr);
    }

    private static unsafe bool TryFindNodeByChainInDirection(AtkResNode* node, IReadOnlyList<uint> nodeIds, int index, bool searchPreviousSiblings, out AtkResNode* nodePtr)
    {
        nodePtr = null;

        for (var currentNode = node; currentNode != null; currentNode = searchPreviousSiblings ? currentNode->PrevSiblingNode : currentNode->NextSiblingNode)
        {
            if (TryMatchNodeByChain(currentNode, nodeIds, index, out nodePtr))
                return true;
        }

        return false;
    }

    private static unsafe bool TryMatchNodeByChain(AtkResNode* node, IReadOnlyList<uint> nodeIds, int index, out AtkResNode* nodePtr)
    {
        nodePtr = null;

        if (node == null || node->NodeId != nodeIds[index])
            return false;

        if (index == nodeIds.Count - 1)
        {
            nodePtr = node;
            return true;
        }

        return TryFindNodeByChain(node->ChildNode, nodeIds, index + 1, out nodePtr)
            || TryGetComponentChildRoot(node, out var componentChildRoot)
            && TryFindNodeByChain(componentChildRoot, nodeIds, index + 1, out nodePtr);
    }

    private static unsafe bool TryGetComponentChildRoot(AtkResNode* node, out AtkResNode* childRoot)
    {
        childRoot = null;

        if (!TryGetComponentNode(node, out var componentNodePtr) || componentNodePtr->Component == null)
            return false;

        childRoot = componentNodePtr->Component->UldManager.NodeList[0];
        return childRoot != null;
    }

    private static string ReadCString(CStringPointer textPointer)
    {
        string? managedText = textPointer.ToString();
        return managedText ?? string.Empty;
    }

    private static string ReadUtf8String(FFXIVClientStructs.FFXIV.Client.System.String.Utf8String textValue)
    {
        string? managedText = textValue.ToString();
        return managedText ?? string.Empty;
    }

    private static unsafe string ReadCString(byte* textPointer)
        => textPointer == null ? string.Empty : Marshal.PtrToStringUTF8((nint)textPointer) ?? string.Empty;

    private static unsafe void TryWriteAtkValue(AtkValue* target, object? value)
    {
        switch (value)
        {
            case null:
                target->ChangeType(AtkValueType.Null);
                return;

            case AtkValue atkValue:
                target->Copy(&atkValue);
                return;

            case CStringPointer cStringPointer:
                target->SetManagedString(cStringPointer);
                return;

            case string text:
                target->SetManagedString(text);
                return;

            case bool boolValue:
                target->SetBool(boolValue);
                return;

            case byte byteValue:
                target->SetInt(byteValue);
                return;

            case sbyte sbyteValue:
                target->SetInt(sbyteValue);
                return;

            case short shortValue:
                target->SetInt(shortValue);
                return;

            case ushort ushortValue:
                target->SetInt(ushortValue);
                return;

            case int intValue:
                target->SetInt(intValue);
                return;

            case uint uintValue:
                target->SetUInt(uintValue);
                return;

            case long longValue:
                target->Type = AtkValueType.Int64;
                target->Int64 = longValue;
                return;

            case ulong ulongValue:
                target->Type = AtkValueType.UInt64;
                target->UInt64 = ulongValue;
                return;

            case float floatValue:
                target->SetFloat(floatValue);
                return;

            case double doubleValue:
                target->SetFloat((float)doubleValue);
                return;

            case decimal decimalValue:
                target->SetFloat((float)decimalValue);
                return;

            case Half halfValue:
                target->SetFloat((float)halfValue);
                return;

            case char charValue:
                target->SetManagedString(charValue.ToString());
                return;

            case IntPtr intPtrValue:
                target->Type = AtkValueType.Pointer;
                target->Pointer = intPtrValue.ToPointer();
                return;

            case UIntPtr uintPtrValue:
                target->Type = AtkValueType.Pointer;
                target->Pointer = uintPtrValue.ToPointer();
                return;

            case Enum enumValue:
                TryWriteConvertibleAtkValue(target, enumValue, enumValue.GetTypeCode());
                return;

            case IEnumerable enumerable when value is not string:
                TryWriteVectorAtkValue(target, enumerable);
                return;

            case IConvertible convertible:
                TryWriteConvertibleAtkValue(target, convertible, convertible.GetTypeCode());
                return;
        }

        target->SetManagedString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static unsafe void TryWriteConvertibleAtkValue(AtkValue* target, IConvertible convertible, TypeCode typeCode)
    {
        switch (typeCode)
        {
            case TypeCode.Boolean:
                target->SetBool(convertible.ToBoolean(CultureInfo.InvariantCulture));
                return;

            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
                target->SetInt(convertible.ToInt32(CultureInfo.InvariantCulture));
                return;

            case TypeCode.UInt32:
                target->SetUInt(convertible.ToUInt32(CultureInfo.InvariantCulture));
                return;

            case TypeCode.Int64:
                target->Type = AtkValueType.Int64;
                target->Int64 = convertible.ToInt64(CultureInfo.InvariantCulture);
                return;

            case TypeCode.UInt64:
                target->Type = AtkValueType.UInt64;
                target->UInt64 = convertible.ToUInt64(CultureInfo.InvariantCulture);
                return;

            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                target->SetFloat(convertible.ToSingle(CultureInfo.InvariantCulture));
                return;

            case TypeCode.Char:
            case TypeCode.String:
                target->SetManagedString(convertible.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                return;
        }

        target->SetManagedString(convertible.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static unsafe void TryWriteVectorAtkValue(AtkValue* target, IEnumerable values)
    {
        var entries = new System.Collections.Generic.List<object?>();

        foreach (var value in values)
            entries.Add(value);

        target->CreateVector((uint)entries.Count);

        for (var index = 0; index < entries.Count; index++)
        {
            AtkValue childValue = default;
            childValue.Ctor();

            try
            {
                TryWriteAtkValue(&childValue, entries[index]);
                target->SetVectorValue((uint)index, &childValue);
            }
            finally
            {
                childValue.Dtor();
            }
        }
    }

    private static IDisposable? CreateEventRegistration(params AddonEventHandle?[] eventHandles)
    {
        ArgumentNullException.ThrowIfNull(eventHandles);

        List<AddonEventHandle> handles = new();

        foreach (AddonEventHandle? eventHandle in eventHandles)
        {
            if (eventHandle != null)
                handles.Add(eventHandle);
        }

        if (handles.Count == 0)
            return null;

        return new ActionDisposable(() =>
        {
            foreach (AddonEventHandle eventHandle in handles)
                NoireService.AddonEventManager.RemoveEvent(eventHandle);
        });
    }

    private static IDisposable WrapEventHandle(AddonEventHandle eventHandle)
    {
        ArgumentNullException.ThrowIfNull(eventHandle);
        return new ActionDisposable(() => RemoveEvent(eventHandle));
    }

    private static string[] MaterializeAddonNames(IEnumerable<string> addonNames)
    {
        ArgumentNullException.ThrowIfNull(addonNames);

        List<string> names = new();

        foreach (string addonName in addonNames)
        {
            ValidateAddonName(addonName, nameof(addonNames));
            names.Add(addonName);
        }

        return names.ToArray();
    }

    private static void ValidateAddonName(string addonName, string paramName)
    {
        if (string.IsNullOrWhiteSpace(addonName))
            throw new ArgumentException("Addon name cannot be null or whitespace.", paramName);
    }

    private static void ValidateRegistrationKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Registration key cannot be null or whitespace.", nameof(key));
    }

    private sealed class ActionDisposable : IDisposable
    {
        private Action? disposeAction;

        public ActionDisposable(Action disposeAction)
        {
            ArgumentNullException.ThrowIfNull(disposeAction);
            this.disposeAction = disposeAction;
        }

        public void Dispose()
        {
            Action? action = this.disposeAction;

            if (action == null)
                return;

            this.disposeAction = null;
            action();
        }
    }
}
