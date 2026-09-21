using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;

namespace NoireLib.Helpers;

public static partial class AddonHelper
{
    private const int MaxComponentDepth = 3;

    /// <summary>
    /// Tries to resolve a list component from a ready addon by a chain of node ids.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="listPtr">The list component, if the chain resolves to one.</param>
    /// <param name="nodeIds">The chain of node ids to resolve.</param>
    /// <returns>True if the chain resolves to a list component.</returns>
    public static unsafe bool TryGetComponentList(AtkUnitBase* addon, out AtkComponentList* listPtr, params int[] nodeIds)
    {
        listPtr = null;

        return TryGetNode(addon, out var nodePtr, nodeIds)
            && TryGetComponentNode(nodePtr, out var componentNodePtr)
            && TryReadComponentList(componentNodePtr, out listPtr);
    }

    /// <summary>
    /// Tries to resolve a list component from a ready addon by the id of its node, wherever it sits in the tree.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="nodeId">The id of the list's component node.</param>
    /// <param name="listPtr">The list component, if the node holds one.</param>
    /// <returns>True if the node exists and holds a list component.</returns>
    public static unsafe bool TryGetComponentList(AtkUnitBase* addon, uint nodeId, out AtkComponentList* listPtr)
    {
        listPtr = null;

        return TryGetNode(addon, nodeId, out var nodePtr)
            && TryGetComponentNode(nodePtr, out var componentNodePtr)
            && TryReadComponentList(componentNodePtr, out listPtr);
    }

    /// <summary>
    /// Tries to find the first list component of an addon, whatever its depth in the node tree.
    /// </summary>
    /// <param name="addon">The addon to inspect.</param>
    /// <param name="listPtr">The first list component found, if any.</param>
    /// <returns>True if a list component was found.</returns>
    public static unsafe bool TryFindComponentList(AtkUnitBase* addon, out AtkComponentList* listPtr)
    {
        listPtr = null;

        if (!TryFindComponent(addon, ComponentType.List, out var componentPtr))
            return false;

        listPtr = (AtkComponentList*)componentPtr;
        return true;
    }

    /// <summary>
    /// Reads how many items a list component holds, including the items no renderer shows.
    /// </summary>
    /// <param name="list">The list component to read.</param>
    /// <returns>The item count, or zero when the list is null.</returns>
    public static unsafe int GetListItemCount(AtkComponentList* list) => list == null ? 0 : list->ListLength;

    /// <summary>
    /// Reads the text of the list items a renderer holds, keyed by item index.
    /// A list only keeps renderers for the items around the scroll position. Items further away are missing.
    /// The first text node of a renderer is read when the given id resolves to nothing.
    /// </summary>
    /// <param name="list">The list component to read.</param>
    /// <param name="textNodeId">The id of the text node inside one item renderer.</param>
    /// <returns>The text of every item a renderer holds.</returns>
    public static unsafe IReadOnlyDictionary<int, string> ReadLoadedListItems(AtkComponentList* list, uint textNodeId)
    {
        var items = new Dictionary<int, string>();

        foreach (var entry in ReadLoadedListItemTexts(list, textNodeId))
        {
            if (entry.Value.Count > 0)
                items[entry.Key] = entry.Value[0];
        }

        return items;
    }

    /// <summary>
    /// Reads the text of every text node of the list items a renderer holds, keyed by item index.
    /// A list only keeps renderers for the items around the scroll position. Items further away are missing.
    /// </summary>
    /// <param name="list">The list component to read.</param>
    /// <param name="textNodeId">The id of the text node to read first, zero to keep the node order.</param>
    /// <returns>The texts of every item a renderer holds, empty texts left out.</returns>
    public static unsafe IReadOnlyDictionary<int, IReadOnlyList<string>> ReadLoadedListItemTexts(AtkComponentList* list, uint textNodeId = 0)
    {
        var items = new Dictionary<int, IReadOnlyList<string>>();

        if (list == null || list->ItemRendererList == null)
            return items;

        var itemCount = list->ListLength;

        for (var rendererIndex = 0; rendererIndex < list->AllocatedItemRendererListLength; rendererIndex++)
        {
            var renderer = list->ItemRendererList[rendererIndex].AtkComponentListItemRenderer;

            if (renderer == null)
                continue;

            var itemIndex = renderer->ListItemIndex;

            if (itemIndex < 0 || itemIndex >= itemCount || items.ContainsKey(itemIndex))
                continue;

            var texts = ReadComponentTexts((AtkComponentBase*)renderer, textNodeId);

            if (texts.Count > 0)
                items[itemIndex] = texts;
        }

        return items;
    }

    /// <summary>
    /// Tries to read the text of one list item from the renderer that holds it.
    /// </summary>
    /// <param name="list">The list component to read.</param>
    /// <param name="itemIndex">The index of the item in the list.</param>
    /// <param name="textNodeId">The id of the text node inside one item renderer.</param>
    /// <param name="text">The text of the item, if a renderer holds it.</param>
    /// <returns>True if the item text could be read.</returns>
    public static unsafe bool TryReadListItemText(AtkComponentList* list, int itemIndex, uint textNodeId, out string text)
    {
        text = string.Empty;

        return ReadLoadedListItems(list, textNodeId).TryGetValue(itemIndex, out text!) && text.Length > 0;
    }

    /// <summary>
    /// Scrolls a list component until one item is in view. The renderers take a frame to follow.
    /// </summary>
    /// <param name="list">The list component to scroll.</param>
    /// <param name="itemIndex">The index of the item to bring into view.</param>
    /// <returns>True if the scroll was asked for.</returns>
    public static unsafe bool ScrollListToItem(AtkComponentList* list, int itemIndex)
    {
        if (list == null || itemIndex < 0 || itemIndex >= list->ListLength)
            return false;

        list->ScrollToItem((short)itemIndex);
        return true;
    }

    private static unsafe List<string> ReadComponentTexts(AtkComponentBase* componentBase, uint textNodeId, int depth = 0)
    {
        var texts = new List<string>();

        if (depth > MaxComponentDepth || !TryGetComponentType(componentBase, out _))
            return texts;

        if (textNodeId != 0 && TryReadText(componentBase->GetTextNodeById(textNodeId), out var wantedText))
            texts.Add(wantedText);

        if (componentBase->UldManager.NodeList == null)
            return texts;

        for (var nodeIndex = 0; nodeIndex < componentBase->UldManager.NodeListCount; nodeIndex++)
        {
            var node = componentBase->UldManager.NodeList[nodeIndex];

            if (TryReadText(node, out var text) && !texts.Contains(text))
                texts.Add(text);

            if (TryGetComponentNode(node, out var componentNodePtr) && componentNodePtr->Component != null)
            {
                foreach (var nested in ReadComponentTexts(componentNodePtr->Component, 0, depth + 1))
                {
                    if (!texts.Contains(nested))
                        texts.Add(nested);
                }
            }
        }

        return texts;
    }

    private static unsafe bool TryReadComponentList(AtkComponentNode* componentNode, out AtkComponentList* listPtr)
    {
        listPtr = null;

        if (componentNode == null
            || !TryGetComponentType(componentNode->Component, out var componentType)
            || componentType != ComponentType.List)
            return false;

        listPtr = (AtkComponentList*)componentNode->Component;
        return true;
    }
}
