using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;

namespace NoireLib.Helpers;

public static partial class AddonHelper
{
    /// <summary>
    /// Tries to read the kind of a component, as the addon layout declares it.
    /// A component node's own type is its id in that layout, never the kind of component it holds. This is the only reliable check.
    /// </summary>
    /// <param name="component">The component to inspect.</param>
    /// <param name="componentType">The kind of the component, if it is loaded.</param>
    /// <returns>True if the component is loaded and its kind could be read.</returns>
    public static unsafe bool TryGetComponentType(AtkComponentBase* component, out ComponentType componentType)
    {
        componentType = default;

        if (component == null || component->UldManager.LoadedState != AtkLoadState.Loaded)
            return false;

        var info = (AtkUldComponentInfo*)component->UldManager.Objects;

        if (info == null)
            return false;

        componentType = info->ComponentType;
        return true;
    }

    /// <summary>
    /// Tries to get the component a node holds, only when it is of the expected kind.
    /// </summary>
    /// <param name="node">The node to inspect.</param>
    /// <param name="componentType">The kind of component expected.</param>
    /// <param name="componentPtr">The component, if the node holds one of that kind.</param>
    /// <returns>True if the node holds a loaded component of that kind.</returns>
    public static unsafe bool TryGetComponent(AtkResNode* node, ComponentType componentType, out AtkComponentBase* componentPtr)
    {
        componentPtr = null;

        if (!TryGetComponentNode(node, out var componentNodePtr))
            return false;

        var component = componentNodePtr->Component;

        if (!TryGetComponentType(component, out var actualType) || actualType != componentType)
            return false;

        componentPtr = component;
        return true;
    }

    /// <summary>
    /// Tries to get the component of a ready addon's node by node id, only when it is of the expected kind.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="nodeId">The id of the component node.</param>
    /// <param name="componentType">The kind of component expected.</param>
    /// <param name="componentPtr">The component, if the node holds one of that kind.</param>
    /// <returns>True if the node exists and holds a loaded component of that kind.</returns>
    public static unsafe bool TryGetComponent(AtkUnitBase* addon, uint nodeId, ComponentType componentType, out AtkComponentBase* componentPtr)
    {
        componentPtr = null;

        return TryGetNode(addon, nodeId, out var nodePtr) && TryGetComponent(nodePtr, componentType, out componentPtr);
    }

    /// <summary>
    /// Tries to find the first component of a kind in a ready addon, whatever its depth in the node tree.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="componentType">The kind of component to look for.</param>
    /// <param name="componentPtr">The first component of that kind, if any.</param>
    /// <returns>True if a loaded component of that kind was found.</returns>
    public static unsafe bool TryFindComponent(AtkUnitBase* addon, ComponentType componentType, out AtkComponentBase* componentPtr)
    {
        componentPtr = null;

        return IsAddonLoaded(addon) && TryFindComponent(&addon->UldManager, componentType, 0, out componentPtr);
    }

    /// <summary>
    /// Tries to read what a text input holds, as it was typed, markup left out.
    /// </summary>
    /// <param name="textInput">The text input to read.</param>
    /// <param name="text">The typed text, if any.</param>
    /// <returns>True if the text input holds some text.</returns>
    public static unsafe bool TryReadTextInput(AtkComponentTextInput* textInput, out string text)
    {
        text = string.Empty;

        if (textInput == null)
            return false;

        text = ReadUtf8String(textInput->AtkComponentInputBase.RawString);
        return text.Length > 0;
    }

    /// <summary>
    /// Tries to read what the first text input of a ready addon holds, as it was typed, markup left out.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="text">The typed text, if any.</param>
    /// <returns>True if the addon has a text input holding some text.</returns>
    public static unsafe bool TryReadTextInput(AtkUnitBase* addon, out string text)
    {
        text = string.Empty;

        return TryFindComponent(addon, ComponentType.TextInput, out var componentPtr)
            && TryReadTextInput((AtkComponentTextInput*)componentPtr, out text);
    }

    /// <summary>
    /// Reads every text shown inside a component, nested components included, in node order and without duplicates.
    /// </summary>
    /// <param name="component">The component to read, a list item renderer for instance.</param>
    /// <returns>The texts shown, empty texts left out.</returns>
    public static unsafe IReadOnlyList<string> ReadComponentTexts(AtkComponentBase* component)
        => ReadComponentTexts(component, 0);

    // The flat node list holds every node of one layout. Sibling order runs backwards from ChildNode.
    private static unsafe bool TryFindComponent(AtkUldManager* uldManager, ComponentType componentType, int depth, out AtkComponentBase* componentPtr)
    {
        componentPtr = null;

        if (depth > MaxComponentDepth || uldManager->NodeList == null)
            return false;

        for (var index = 0; index < uldManager->NodeListCount; index++)
        {
            if (TryGetComponent(uldManager->NodeList[index], componentType, out componentPtr))
                return true;
        }

        for (var index = 0; index < uldManager->NodeListCount; index++)
        {
            if (TryGetComponentNode(uldManager->NodeList[index], out var componentNodePtr)
                && TryGetComponentType(componentNodePtr->Component, out _)
                && TryFindComponent(&componentNodePtr->Component->UldManager, componentType, depth + 1, out componentPtr))
                return true;
        }

        return false;
    }
}
