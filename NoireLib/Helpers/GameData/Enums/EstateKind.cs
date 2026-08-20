namespace NoireLib.Helpers;

/// <summary>
/// The kind of an owned housing teleport target, as read from the logged-in character's teleport list. The list is
/// the source of truth for what is reachable: an estate only appears once it is teleportable (a private or shared
/// estate needs its garden aetheryte placed; an apartment always is), so nothing here re-derives that.
/// </summary>
public enum EstateKind
{
    /// <summary>The character's own private estate.</summary>
    PrivateEstate,

    /// <summary>The character's Free Company estate; its chambers and workshop are reached from here on foot.</summary>
    FreeCompanyEstate,

    /// <summary>A rented apartment, always teleportable.</summary>
    Apartment,

    /// <summary>A shared estate the character has access to.</summary>
    SharedEstate,
}
