using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when the current duty starts (barriers drop).
/// </summary>
/// <param name="TerritoryId">The duty's territory row id.</param>
public sealed record DutyStartedEvent(uint TerritoryId);

/// <summary>
/// Fired when the current duty wipes.
/// </summary>
/// <param name="TerritoryId">The duty's territory row id.</param>
public sealed record DutyWipedEvent(uint TerritoryId);

/// <summary>
/// Fired when the current duty recommences after a wipe.
/// </summary>
/// <param name="TerritoryId">The duty's territory row id.</param>
public sealed record DutyRecommencedEvent(uint TerritoryId);

/// <summary>
/// Fired when the current duty is completed.
/// </summary>
/// <param name="TerritoryId">The duty's territory row id.</param>
public sealed record DutyCompletedEvent(uint TerritoryId);

/// <summary>Fired when the local player enters a duty queue.</summary>
public sealed record DutyQueueEnteredEvent;

/// <summary>
/// Fired when the local player leaves the duty queue without a pop (withdrew, or the queue was cancelled).
/// </summary>
/// <param name="QueueDuration">How long the player was queued.</param>
public sealed record DutyQueueLeftEvent(TimeSpan QueueDuration);

/// <summary>
/// Fired when the duty queue pops.
/// </summary>
/// <param name="ContentFinderConditionId">The content-finder-condition row id.</param>
/// <param name="ContentName">The content's display name, or empty when unavailable.</param>
/// <param name="QueueDuration">The measured queue duration, or null when the queue entry was not observed.</param>
public sealed record DutyPopEvent(uint ContentFinderConditionId, string ContentName, TimeSpan? QueueDuration);
