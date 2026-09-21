using Dalamud.Interface.Textures;
using System;

namespace NoireLib.Helpers;

/// <summary>
/// Everything one title-screen menu entry can be configured with. <see cref="Name"/> and <see cref="OnTriggered"/>
/// are required, and exactly one of the three icon properties supplies the icon.
/// </summary>
public sealed class TitleScreenEntryOptions
{
    /// <summary>The text shown beside the icon.</summary>
    public required string Name { get; init; }

    /// <summary>Runs when the entry is clicked.</summary>
    public required Action OnTriggered { get; init; }

    /// <summary>A game icon row to draw the entry with, resolved through Dalamud's texture provider.</summary>
    public uint? IconId { get; init; }

    /// <summary>A file on disk to draw the entry with, such as an image shipped beside the plugin.</summary>
    public string? IconPath { get; init; }

    /// <summary>A texture the caller already resolved, for an icon this helper cannot name.</summary>
    public ISharedImmediateTexture? Icon { get; init; }

    /// <summary>Whether the icon is scaled to <see cref="TitleScreenMenuHelper.RequiredIconSize"/>. Dalamud removes an entry of another size.</summary>
    public bool NormalizeIcon { get; init; } = true;

    /// <summary>
    /// Where the entry sits among the others, lower first. <b>Leave it null</b> unless the order actually matters:
    /// Dalamud then places the entry where a plugin's belongs, and a value ordering it past the entries the title
    /// screen has room for leaves it registered but off the bottom of the column.
    /// </summary>
    public ulong? Priority { get; init; }
}
