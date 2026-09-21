using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// What <see cref="WorldObjectHelper.Find"/> looks for. Every criterion is optional and the ones set combine: a row
/// comes back only when it meets all of them. Build one with a shortcut, then adjust it with <c>with { }</c>.
/// </summary>
public sealed record WorldObjectQuery
{
    /// <summary>The kinds to return. <see cref="PlacementKinds.SharedGroup"/> only answers <see cref="AssetPathFragment"/>.</summary>
    public PlacementKinds Kinds { get; init; } = PlacementKinds.All;

    /// <summary>Exact EObj, ENpcBase or Aetheryte row ids, or null for any.</summary>
    public IReadOnlySet<uint>? BaseIds { get; init; }

    /// <summary>A display name to match, or null for any.</summary>
    public string? Name { get; init; }

    /// <summary>How <see cref="Name"/> is compared. Both compare case insensitively.</summary>
    public NameMatch NameMatch { get; init; } = NameMatch.Exact;

    /// <summary>The language names are read in. The client's own when null.</summary>
    public ClientLanguage? Language { get; init; }

    /// <summary>The start of a CustomTalk script name the row runs, compared case sensitively, or null for any.</summary>
    public string? ScriptPrefix { get; init; }

    /// <summary>Event handler ids the row runs, any of them, or null for any.</summary>
    public IReadOnlySet<uint>? HandlerIds { get; init; }

    /// <summary>A handler family the row runs, such as every special shop, or null for any.</summary>
    public EventHandlerContent? HandlerContent { get; init; }

    /// <summary>Text to look for in a shared group's SGB path, compared case insensitively, or null for none.</summary>
    public string? AssetPathFragment { get; init; }

    /// <summary>The territories placements are read in. <see cref="PlacementScope.World"/> by default.</summary>
    public PlacementScope Scope { get; init; } = PlacementScope.World;

    /// <summary>Whether placements are read. False stops at the sheets.</summary>
    public bool IncludePlacements { get; init; } = true;

    internal bool NamesARowCriterion
        => BaseIds != null || Name != null || ScriptPrefix != null || HandlerIds != null || HandlerContent != null;

    /// <summary>Finds rows by display name.</summary>
    /// <param name="name">The name to match.</param>
    /// <param name="match">How the name is compared.</param>
    /// <returns>The query.</returns>
    /// <exception cref="ArgumentException">If <paramref name="name"/> is null or blank.</exception>
    public static WorldObjectQuery ByName(string name, NameMatch match = NameMatch.Exact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new WorldObjectQuery { Name = name, NameMatch = match };
    }

    /// <summary>Finds the event objects and NPCs running a CustomTalk script, "CmnDefMarketBoard" being every market board.</summary>
    /// <param name="scriptPrefix">The start of the script name.</param>
    /// <returns>The query.</returns>
    /// <exception cref="ArgumentException">If <paramref name="scriptPrefix"/> is null or blank.</exception>
    public static WorldObjectQuery ByScript(string scriptPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPrefix);
        return new WorldObjectQuery { ScriptPrefix = scriptPrefix, Kinds = PlacementKinds.EventObject | PlacementKinds.EventNpc };
    }

    /// <summary>Finds rows by id.</summary>
    /// <param name="kind">The kind the ids belong to.</param>
    /// <param name="baseIds">The row ids.</param>
    /// <returns>The query.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="baseIds"/> is null.</exception>
    public static WorldObjectQuery ByBaseId(PlacementKinds kind, params uint[] baseIds)
    {
        ArgumentNullException.ThrowIfNull(baseIds);
        return new WorldObjectQuery { Kinds = kind, BaseIds = new HashSet<uint>(baseIds) };
    }

    /// <summary>Finds the event objects and NPCs running any of the given handlers.</summary>
    /// <param name="handlerIds">The event handler ids.</param>
    /// <returns>The query.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="handlerIds"/> is null.</exception>
    public static WorldObjectQuery ByHandler(params uint[] handlerIds)
    {
        ArgumentNullException.ThrowIfNull(handlerIds);
        return new WorldObjectQuery { HandlerIds = new HashSet<uint>(handlerIds), Kinds = PlacementKinds.EventObject | PlacementKinds.EventNpc };
    }

    /// <summary>Finds the event objects and NPCs running any handler of a family.</summary>
    /// <param name="content">The handler family.</param>
    /// <returns>The query.</returns>
    public static WorldObjectQuery ByHandlerContent(EventHandlerContent content)
        => new() { HandlerContent = content, Kinds = PlacementKinds.EventObject | PlacementKinds.EventNpc };

    /// <summary>Finds the shared groups whose SGB path contains a fragment.</summary>
    /// <param name="assetPathFragment">The text to look for.</param>
    /// <returns>The query.</returns>
    /// <exception cref="ArgumentException">If <paramref name="assetPathFragment"/> is null or blank.</exception>
    public static WorldObjectQuery ByAssetPath(string assetPathFragment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPathFragment);
        return new WorldObjectQuery { AssetPathFragment = assetPathFragment, Kinds = PlacementKinds.SharedGroup };
    }
}
