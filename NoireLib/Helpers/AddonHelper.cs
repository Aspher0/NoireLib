using Dalamud.Game.NativeWrapper;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;
using System;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;

namespace NoireLib.Helpers;

using AtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

/// <summary>
/// A helper class to help with addon manipulation, such as finding addons, getting data, sending callbacks, etc.
/// </summary>
public static class AddonHelper
{
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

        nodePtr = addon->GetNodeById((uint)nodeIds[0]);

        if (nodePtr == null)
            return false;

        for (var index = 1; index < nodeIds.Length; index++)
        {
            nodePtr = FindNestedNode(nodePtr, (uint)nodeIds[index]);

            if (nodePtr == null)
                return false;
        }

        return true;
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

    private static unsafe AtkResNode* FindNestedNode(AtkResNode* node, uint nodeId)
    {
        if (node == null)
            return null;

        if (TryGetComponentNode(node, out var componentNodePtr) && componentNodePtr->Component != null)
        {
            AtkResNode* componentMatch = componentNodePtr->Component->UldManager.SearchNodeById(nodeId);

            if (componentMatch != null)
                return componentMatch;
        }

        return FindNodeInTree(node->ChildNode, nodeId);
    }

    private static unsafe AtkResNode* FindNodeInTree(AtkResNode* node, uint nodeId)
    {
        for (var currentNode = node; currentNode != null; currentNode = currentNode->NextSiblingNode)
        {
            if (currentNode->NodeId == nodeId)
                return currentNode;

            AtkResNode* childMatch = FindNestedNode(currentNode, nodeId);

            if (childMatch != null)
                return childMatch;
        }

        return null;
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


}

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
}
