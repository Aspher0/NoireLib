using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Configuration for the <see cref="NoireGameStateWatcher"/> module, controlling which sub-trackers are enabled.
/// </summary>
public sealed class GameStateWatcherConfig
{
    /// <summary>
    /// Gets or sets whether the territory tracker is enabled.<br/>
    /// Tracks territory, map, and instance changes using <see cref="IClientState"/> events.
    /// </summary>
    public bool EnableTerritoryTracker { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the object tracker is enabled.<br/>
    /// Polls the object table each frame to detect spawns and despawns.
    /// </summary>
    public bool EnableObjectTracker { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the party tracker is enabled.<br/>
    /// Polls the party list each frame to detect composition changes.
    /// </summary>
    public bool EnablePartyTracker { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the inventory tracker is enabled.<br/>
    /// Subscribes to <see cref="IGameInventory.InventoryChanged"/> events.
    /// </summary>
    public bool EnableInventoryTracker { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the status effect tracker is enabled.<br/>
    /// Polls the local player's status list each frame to detect gains, losses, and changes.
    /// </summary>
    public bool EnableStatusEffectTracker { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the duty tracker is enabled.<br/>
    /// Subscribes to <see cref="IDutyState"/> events for duty lifecycle tracking.
    /// </summary>
    public bool EnableDutyTracker { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the player-state tracker is enabled.<br/>
    /// Polls the local player to detect HP and death-state transitions and exposes live player-state helpers.
    /// </summary>
    public bool EnablePlayerStateTracker { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the addon tracker is enabled.<br/>
    /// Polls registered addon names to detect open, close, and readiness transitions.
    /// </summary>
    public bool EnableAddonTracker { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the chat tracker is enabled.<br/>
    /// Subscribes to <see cref="IChatGui.ChatMessage"/> events.
    /// </summary>
    public bool EnableChatTracker { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the action effect tracker is enabled.<br/>
    /// Hooks the native <see cref="ActionEffectHandler.Receive"/> function.
    /// </summary>
    public bool EnableActionEffectTracker { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum number of recent addon state transitions retained by the addon tracker.
    /// </summary>
    public int AddonHistoryCapacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of recent chat messages retained by the chat tracker.
    /// </summary>
    public int ChatHistoryCapacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of recent action effects retained by the action effect tracker.
    /// </summary>
    public int ActionEffectHistoryCapacity { get; set; } = 50;
}
