using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

/// <summary>
/// Describes <b>who</b> a character subscription is about. Every character fact exists once as an event type;
/// a scope decides which subjects it fires for.<br/>
/// Roots define what gets iterated (and therefore the cost); <see cref="Where"/> predicates are modifiers that can
/// narrow a root but never silently widen it.<br/>
/// Scopes are immutable and freely shareable between subscriptions.
/// </summary>
public sealed class Scope
{
    // The root kinds a scope can be built from. Internal - user code uses the static factories.
    internal enum RootKind
    {
        LocalPlayer,

        // Includes the local player.
        Party,

        Alliance,

        Friends,

        AllPlayers,

        AllCharacters,

        // Tracks the object-table slot. Entity ids are reused after a despawn.
        Entity,

        // Tracks the person across despawns and entity-id reuse.
        ContentId,

        // Optionally bound to a world.
        Name,

        Union,
    }

    private readonly RootKind kind;
    private readonly uint entityId;
    private readonly ulong contentId;
    private readonly string? name;
    private readonly uint? worldId;
    private readonly Scope[] children;
    private readonly Func<CharacterSnapshot, bool>[] predicates;

    private Scope(
        RootKind kind,
        uint entityId = 0,
        ulong contentId = 0,
        string? name = null,
        uint? worldId = null,
        Scope[]? children = null,
        Func<CharacterSnapshot, bool>[]? predicates = null)
    {
        this.kind = kind;
        this.entityId = entityId;
        this.contentId = contentId;
        this.name = name;
        this.worldId = worldId;
        this.children = children ?? Array.Empty<Scope>();
        this.predicates = predicates ?? Array.Empty<Func<CharacterSnapshot, bool>>();
    }

    internal RootKind Kind => kind;
    internal uint TargetEntityId => entityId;
    internal ulong TargetContentId => contentId;
    internal string? TargetName => name;
    internal uint? TargetWorldId => worldId;
    internal IReadOnlyList<Scope> Children => children;

    #region Factories

    /// <summary>The local player only. This is the default for every scoped subscription helper.</summary>
    public static Scope LocalPlayer { get; } = new(RootKind.LocalPlayer);

    /// <summary>Every member of the local player's party, including the local player.</summary>
    public static Scope Party { get; } = new(RootKind.Party);

    /// <summary>Every member of the local player's alliance (excluding your own party).</summary>
    public static Scope Alliance { get; } = new(RootKind.Alliance);

    /// <summary>
    /// Friends currently present in the object table.<br/>
    /// Friend membership comes from the client's relation flags; freshness follows the game's social list refresh.
    /// </summary>
    public static Scope Friends { get; } = new(RootKind.Friends);

    /// <summary>Every player character in the object table.</summary>
    public static Scope AllPlayers { get; } = new(RootKind.AllPlayers);

    /// <summary>Every character in the object table: players, battle NPCs and companions.</summary>
    public static Scope AllCharacters { get; } = new(RootKind.AllCharacters);

    /// <summary>
    /// A single character identified by entity id.<br/>
    /// Entity ids can be reused by the game after despawn: this scope tracks the <i>slot</i>.
    /// Use <see cref="ContentId"/> or <see cref="Name"/> to track the <i>person</i>.
    /// </summary>
    /// <param name="entityId">The entity id to watch.</param>
    /// <returns>The scope.</returns>
    public static Scope Entity(uint entityId) => new(RootKind.Entity, entityId: entityId);

    /// <summary>
    /// A single player identified by content id - survives despawn/respawn and entity-id reuse.
    /// </summary>
    /// <param name="contentId">The content id to watch.</param>
    /// <returns>The scope.</returns>
    public static Scope ContentId(ulong contentId) => new(RootKind.ContentId, contentId: contentId);

    /// <summary>
    /// A character identified by full name, optionally bound to a home world.
    /// </summary>
    /// <param name="fullName">The character's full display name (case-insensitive exact match).</param>
    /// <param name="worldId">The home world row id, or null to match the name on any world.</param>
    /// <returns>The scope.</returns>
    public static Scope Name(string fullName, uint? worldId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        return new Scope(RootKind.Name, name: fullName, worldId: worldId);
    }

    #endregion

    #region Modifiers

    /// <summary>Narrows this scope with a predicate; predicates can only reduce matches, never widen the root's iteration.</summary>
    /// <param name="predicate">The predicate a subject snapshot must satisfy.</param>
    /// <returns>A new scope with the predicate appended.</returns>
    public Scope Where(Func<CharacterSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var newPredicates = new Func<CharacterSnapshot, bool>[predicates.Length + 1];
        Array.Copy(predicates, newPredicates, predicates.Length);
        newPredicates[predicates.Length] = predicate;

        return new Scope(kind, entityId, contentId, name, worldId, children, newPredicates);
    }

    /// <summary>
    /// Combines this scope with another: a subject matches when it matches either scope.
    /// </summary>
    /// <param name="other">The scope to union with.</param>
    /// <returns>A new union scope.</returns>
    public Scope Union(Scope other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new Scope(RootKind.Union, children: new[] { this, other });
    }

    #endregion

    #region Matching

    /// <summary>
    /// Determines whether a subject snapshot matches this scope, using the snapshot's precomputed
    /// <see cref="CharacterSnapshot.Flags"/> and identity fields - never a re-query of game state.
    /// </summary>
    /// <param name="subject">The subject snapshot to test.</param>
    /// <returns>True when the subject is in scope.</returns>
    public bool Matches(CharacterSnapshot subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (!MatchesRoot(subject))
            return false;

        foreach (var predicate in predicates)
        {
            if (!predicate(subject))
                return false;
        }

        return true;
    }

    private bool MatchesRoot(CharacterSnapshot subject) => kind switch
    {
        RootKind.LocalPlayer => (subject.Flags & SubjectFlags.IsLocalPlayer) != 0,
        RootKind.Party => (subject.Flags & (SubjectFlags.IsPartyMember | SubjectFlags.IsLocalPlayer)) != 0,
        RootKind.Alliance => (subject.Flags & SubjectFlags.IsAllianceMember) != 0,
        RootKind.Friends => (subject.Flags & SubjectFlags.IsFriend) != 0,
        RootKind.AllPlayers => subject.IsPlayer,
        RootKind.AllCharacters => true,
        RootKind.Entity => subject.EntityId == entityId,
        RootKind.ContentId => subject.ContentId != 0 && subject.ContentId == contentId,
        RootKind.Name => string.Equals(subject.Name, name, StringComparison.OrdinalIgnoreCase)
            && (worldId == null || subject.HomeWorldId == worldId.Value),
        RootKind.Union => children.Any(child => child.Matches(subject)),
        _ => false,
    };

    #endregion

    #region Iteration planning (internal)

    // The iteration classes a scope can require, ordered by breadth. Sources use the widest class among their
    // registrations to decide what to iterate per tick.
    internal enum IterationClass
    {
        LocalOnly = 0,

        // Party, alliance, friend and targeted scopes pre-filter per subject.
        Players = 1,

        AllCharacters = 2,
    }

    internal IterationClass GetIterationClass() => kind switch
    {
        RootKind.LocalPlayer => IterationClass.LocalOnly,
        RootKind.AllCharacters => IterationClass.AllCharacters,
        RootKind.Entity => IterationClass.AllCharacters,
        RootKind.Union => children.Length == 0
            ? IterationClass.LocalOnly
            : children.Max(child => child.GetIterationClass()),
        _ => IterationClass.Players,
    };

    // Cheap pre-capture root test used during iteration: decides whether a subject is worth diffing at all, from data
    // available without materializing a snapshot. Predicates are not applied here - they run at dispatch, against
    // real snapshots.
    internal bool PreMatches(in SubjectProbe probe)
    {
        switch (kind)
        {
            case RootKind.LocalPlayer:
                return (probe.Flags & SubjectFlags.IsLocalPlayer) != 0;
            case RootKind.Party:
                return (probe.Flags & (SubjectFlags.IsPartyMember | SubjectFlags.IsLocalPlayer)) != 0;
            case RootKind.Alliance:
                return (probe.Flags & SubjectFlags.IsAllianceMember) != 0;
            case RootKind.Friends:
                return (probe.Flags & SubjectFlags.IsFriend) != 0;
            case RootKind.AllPlayers:
                return probe.IsPlayer;
            case RootKind.AllCharacters:
                return true;
            case RootKind.Entity:
                return probe.EntityId == entityId;
            case RootKind.ContentId:
                return probe.ContentId != 0 && probe.ContentId == contentId;
            case RootKind.Name:
                return string.Equals(probe.Name, name, StringComparison.OrdinalIgnoreCase)
                    && (worldId == null || probe.HomeWorldId == worldId.Value);
            case RootKind.Union:
                {
                    foreach (var child in children)
                    {
                        if (child.PreMatches(in probe))
                            return true;
                    }

                    return false;
                }
            default:
                return false;
        }
    }

    #endregion

    /// <summary>Returns a readable description of the scope, used in logs and diagnostics.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        var root = kind switch
        {
            RootKind.Entity => $"Entity({entityId:X})",
            RootKind.ContentId => $"ContentId({contentId:X})",
            RootKind.Name => worldId == null ? $"Name({name})" : $"Name({name}@{worldId})",
            RootKind.Union => $"Union({string.Join(", ", children.Select(c => c.ToString()))})",
            _ => kind.ToString(),
        };

        return predicates.Length > 0 ? $"{root}+{predicates.Length} predicate(s)" : root;
    }
}

// A light-weight per-subject view used for pre-capture scope checks during iteration. Internal to the watcher's diff
// engines.
internal readonly struct SubjectProbe
{
    public required uint EntityId { get; init; }

    // 0 when unavailable.
    public required ulong ContentId { get; init; }

    public required string Name { get; init; }

    // 0 when unavailable.
    public required uint HomeWorldId { get; init; }

    public required SubjectFlags Flags { get; init; }

    public required bool IsPlayer { get; init; }
}
