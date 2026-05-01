using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using NoireLib.Events;
using NoireLib.TaskQueue;
using System;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Tracks territory, map, instance, login/logout, PvP, and housing state changes.
/// </summary>
public sealed class TerritoryTracker : GameStateSubTracker
{
    private readonly EventWrapper territoryChangedEvent;
    private readonly EventWrapper mapIdChangedEvent;
    private readonly EventWrapper instanceChangedEvent;
    private readonly EventWrapper loginEvent;
    private readonly EventWrapper logoutEvent;

    private uint lastTerritoryId;
    private uint lastMapId;
    private uint lastInstance;
    private bool lastIsPvP;
    private bool lastIsInsideHousing;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerritoryTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    internal TerritoryTracker(NoireGameStateWatcher owner, bool active) : base(owner, active)
    {
        territoryChangedEvent = new(NoireService.ClientState, nameof(IClientState.TerritoryChanged), name: $"{nameof(TerritoryTracker)}.TerritoryChanged");
        mapIdChangedEvent = new(NoireService.ClientState, nameof(IClientState.MapIdChanged), name: $"{nameof(TerritoryTracker)}.MapIdChanged");
        instanceChangedEvent = new(NoireService.ClientState, nameof(IClientState.InstanceChanged), name: $"{nameof(TerritoryTracker)}.InstanceChanged");
        loginEvent = new(NoireService.ClientState, nameof(IClientState.Login), name: $"{nameof(TerritoryTracker)}.Login");
        logoutEvent = new(NoireService.ClientState, nameof(IClientState.Logout), name: $"{nameof(TerritoryTracker)}.Logout");

        territoryChangedEvent.AddCallback("handler", HandleTerritoryChanged);
        mapIdChangedEvent.AddCallback("handler", HandleMapIdChanged);
        instanceChangedEvent.AddCallback("handler", HandleInstanceChanged);
        loginEvent.AddCallback("handler", HandleLogin);
        logoutEvent.AddCallback("handler", HandleLogout);
    }

    /// <summary>
    /// Gets the current territory type identifier.
    /// </summary>
    public uint CurrentTerritoryId => NoireService.ClientState.TerritoryType;

    /// <summary>
    /// Gets the current map identifier.
    /// </summary>
    public uint CurrentMapId => NoireService.ClientState.MapId;

    /// <summary>
    /// Gets the current duty instance number.
    /// </summary>
    public uint CurrentInstance => NoireService.ClientState.Instance;

    /// <summary>
    /// Gets a value indicating whether the player is currently logged in.
    /// </summary>
    public bool IsLoggedIn => NoireService.ClientState.IsLoggedIn;

    /// <summary>
    /// Gets a value indicating whether the player is in PvP.
    /// </summary>
    public bool IsPvP => NoireService.ClientState.IsPvP;

    /// <summary>
    /// Gets a value indicating whether the player is currently inside a housing area.
    /// </summary>
    public unsafe bool IsInsideHousing => HousingManager.Instance()->IsInside();

    /// <summary>
    /// Raised when the territory changes.
    /// </summary>
    public event Action<TerritoryChangedEvent>? OnTerritoryChanged;

    /// <summary>
    /// Raised when the map identifier changes.
    /// </summary>
    public event Action<MapChangedEvent>? OnMapChanged;

    /// <summary>
    /// Raised when the duty instance number changes.
    /// </summary>
    public event Action<InstanceChangedEvent>? OnInstanceChanged;

    /// <summary>
    /// Raised when the player logs in.
    /// </summary>
    public event Action<PlayerLoginEvent>? OnLogin;

    /// <summary>
    /// Raised when the player logs out.
    /// </summary>
    public event Action<PlayerLogoutEvent>? OnLogout;

    /// <summary>
    /// Raised when the local player enters PvP.
    /// </summary>
    public event Action<PlayerEnteredPvPEvent>? OnEnteredPvP;

    /// <summary>
    /// Raised when the local player leaves PvP.
    /// </summary>
    public event Action<PlayerLeftPvPEvent>? OnLeftPvP;

    /// <summary>
    /// Raised when the local player enters a housing area.
    /// </summary>
    public event Action<PlayerEnteredHousingEvent>? OnEnteredHousing;

    /// <summary>
    /// Raised when the local player leaves a housing area.
    /// </summary>
    public event Action<PlayerLeftHousingEvent>? OnLeftHousing;

    /// <summary>
    /// Checks whether the player is currently in the specified territory.
    /// </summary>
    /// <param name="territoryId">The territory type identifier to check.</param>
    /// <returns><see langword="true"/> if the player is in the specified territory; otherwise, <see langword="false"/>.</returns>
    public bool IsInTerritory(ushort territoryId) => CurrentTerritoryId == territoryId;

    /// <summary>
    /// Checks whether the player is currently in any of the specified territories.
    /// </summary>
    /// <param name="territoryIds">The territory type identifiers to check.</param>
    /// <returns><see langword="true"/> if the player is in any of the specified territories; otherwise, <see langword="false"/>.</returns>
    public bool IsInAnyTerritory(params ushort[] territoryIds)
    {
        var current = CurrentTerritoryId;

        for (var i = 0; i < territoryIds.Length; i++)
        {
            if (territoryIds[i] == current)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether the player is currently on the specified map.
    /// </summary>
    /// <param name="mapId">The map identifier to check.</param>
    /// <returns><see langword="true"/> if the player is on the specified map; otherwise, <see langword="false"/>.</returns>
    public bool IsOnMap(uint mapId) => CurrentMapId == mapId;

    /// <summary>
    /// Checks whether the player is currently in the specified instance.
    /// </summary>
    /// <param name="instance">The duty-instance number to check.</param>
    /// <returns><see langword="true"/> if the player is in the specified instance; otherwise, <see langword="false"/>.</returns>
    public bool IsInInstance(uint instance) => CurrentInstance == instance;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is in PvP.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when PvP is active.</returns>
    public Func<bool> WaitForPvP() => () => NoireService.ClientState.IsPvP;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the player enters the specified territory.<br/>
    /// Useful as a wait condition for <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <param name="territoryId">The target territory type identifier.</param>
    /// <returns>A predicate returning <see langword="true"/> when the player is in the target territory.</returns>
    public Func<bool> WaitForTerritory(ushort territoryId) => () => CurrentTerritoryId == territoryId;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the player reaches the specified map.
    /// </summary>
    /// <param name="mapId">The target map identifier.</param>
    /// <returns>A predicate returning <see langword="true"/> when the player is on the target map.</returns>
    public Func<bool> WaitForMap(uint mapId) => () => CurrentMapId == mapId;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the player reaches the specified instance.
    /// </summary>
    /// <param name="instance">The target duty-instance number.</param>
    /// <returns>A predicate returning <see langword="true"/> when the player is in the target instance.</returns>
    public Func<bool> WaitForInstance(uint instance) => () => CurrentInstance == instance;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is inside a housing area.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the player is inside housing.</returns>
    public Func<bool> WaitForHousing() => () => IsInsideHousing;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is not inside a housing area.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the player is outside housing.</returns>
    public Func<bool> WaitForNotHousing() => () => !IsInsideHousing;

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        lastTerritoryId = NoireService.ClientState.TerritoryType;
        lastMapId = NoireService.ClientState.MapId;
        lastInstance = NoireService.ClientState.Instance;
        lastIsPvP = NoireService.ClientState.IsPvP;
        lastIsInsideHousing = IsInsideHousing;

        territoryChangedEvent.Enable();
        mapIdChangedEvent.Enable();
        instanceChangedEvent.Enable();
        loginEvent.Enable();
        logoutEvent.Enable();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(TerritoryTracker)} activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        territoryChangedEvent.Disable();
        mapIdChangedEvent.Disable();
        instanceChangedEvent.Disable();
        loginEvent.Disable();
        logoutEvent.Disable();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(TerritoryTracker)} deactivated.");
    }

    /// <inheritdoc/>
    internal override void Update()
    {
        var isPvP = NoireService.ClientState.IsPvP;
        if (isPvP != lastIsPvP)
        {
            lastIsPvP = isPvP;

            if (isPvP)
                PublishEvent(OnEnteredPvP, new PlayerEnteredPvPEvent(CurrentTerritoryId));
            else
                PublishEvent(OnLeftPvP, new PlayerLeftPvPEvent(CurrentTerritoryId));
        }

        var isInsideHousing = IsInsideHousing;
        if (isInsideHousing != lastIsInsideHousing)
        {
            lastIsInsideHousing = isInsideHousing;

            if (isInsideHousing)
                PublishEvent(OnEnteredHousing, new PlayerEnteredHousingEvent(CurrentTerritoryId));
            else
                PublishEvent(OnLeftHousing, new PlayerLeftHousingEvent(CurrentTerritoryId));
        }
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        territoryChangedEvent.Dispose();
        mapIdChangedEvent.Dispose();
        instanceChangedEvent.Dispose();
        loginEvent.Dispose();
        logoutEvent.Dispose();
    }

    private void HandleTerritoryChanged(ushort newTerritoryId)
    {
        var previous = lastTerritoryId;
        lastTerritoryId = newTerritoryId;

        var evt = new TerritoryChangedEvent(previous, newTerritoryId);

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"Territory changed: {previous} -> {newTerritoryId}.");

        PublishEvent(OnTerritoryChanged, evt);
    }

    private void HandleMapIdChanged(uint newMapId)
    {
        var previous = lastMapId;
        lastMapId = newMapId;

        var evt = new MapChangedEvent(previous, newMapId);

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"Map changed: {previous} -> {newMapId}.");

        PublishEvent(OnMapChanged, evt);
    }

    private void HandleInstanceChanged(uint newInstance)
    {
        var previous = lastInstance;
        lastInstance = newInstance;

        var evt = new InstanceChangedEvent(previous, newInstance);

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"Instance changed: {previous} -> {newInstance}.");

        PublishEvent(OnInstanceChanged, evt);
    }

    private void HandleLogin()
    {
        lastTerritoryId = NoireService.ClientState.TerritoryType;
        lastMapId = NoireService.ClientState.MapId;
        lastInstance = NoireService.ClientState.Instance;

        var evt = new PlayerLoginEvent();

        if (Owner.EnableLogging)
            NoireLogger.LogInfo(Owner, "Player logged in.");

        PublishEvent(OnLogin, evt);
    }

    private void HandleLogout(int type, int flags)
    {
        var evt = new PlayerLogoutEvent(type, flags);

        if (Owner.EnableLogging)
            NoireLogger.LogInfo(Owner, $"Player logged out (Type: {type}, Flags: {flags}).");

        PublishEvent(OnLogout, evt);
    }
}
