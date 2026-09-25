using Newtonsoft.Json;
using System.Collections.Generic;

namespace NoireLib.UI;

// What the user changed in one window's arrangement for one skin, keyed by parent path then child id.
internal sealed class ComponentLayout
{
    // Movable child ids in the user's order; children missing from it keep their declared place.
    public Dictionary<string, List<string>> Order { get; set; } = new();

    // Hideable child ids the user hid.
    public Dictionary<string, List<string>> Hidden { get; set; } = new();

    // Title buttons the user hid, by TitleButton.Id.
    public HashSet<string> HiddenButtons { get; set; } = new();

    // Moves on every edit: cached arrangements rebuild.
    [JsonIgnore]
    public int Version { get; set; }

    [JsonIgnore]
    public bool IsDefault => Order.Count == 0 && Hidden.Count == 0 && HiddenButtons.Count == 0;

    public bool IsHidden(string parentPath, string childId)
        => Hidden.TryGetValue(parentPath, out var hidden) && hidden.Contains(childId);

    public void SetHidden(string parentPath, string childId, bool hidden)
    {
        if (!Hidden.TryGetValue(parentPath, out var list))
        {
            if (!hidden)
                return;

            Hidden[parentPath] = list = [];
        }

        if (hidden && !list.Contains(childId))
            list.Add(childId);
        else if (!hidden)
            list.Remove(childId);

        if (list.Count == 0)
            Hidden.Remove(parentPath);

        Version++;
    }

    public void SetOrder(string parentPath, List<string> ids)
    {
        if (ids.Count == 0)
            Order.Remove(parentPath);
        else
            Order[parentPath] = ids;

        Version++;
    }

    public void SetButtonHidden(string buttonId, bool hidden)
    {
        if (hidden)
            HiddenButtons.Add(buttonId);
        else
            HiddenButtons.Remove(buttonId);

        Version++;
    }

    public void Clear()
    {
        Order.Clear();
        Hidden.Clear();
        HiddenButtons.Clear();
        Version++;
    }
}
