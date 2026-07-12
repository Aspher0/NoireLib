namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when a character gains a status effect.
/// </summary>
/// <param name="Owner">The character carrying the status.</param>
/// <param name="Status">The gained status.</param>
public sealed record StatusGainedEvent(CharacterSnapshot Owner, StatusSnapshot Status) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Owner;
}

/// <summary>
/// Fired when a character loses a status effect.
/// </summary>
/// <param name="Owner">The character that carried the status.</param>
/// <param name="Status">The lost status (last observed state).</param>
public sealed record StatusLostEvent(CharacterSnapshot Owner, StatusSnapshot Status) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Owner;
}

/// <summary>
/// Fired when a status effect's stack count changes.
/// </summary>
/// <param name="Owner">The character carrying the status.</param>
/// <param name="Previous">The status before the change.</param>
/// <param name="Current">The status after the change.</param>
public sealed record StatusStackChangedEvent(CharacterSnapshot Owner, StatusSnapshot Previous, StatusSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Owner;
}
