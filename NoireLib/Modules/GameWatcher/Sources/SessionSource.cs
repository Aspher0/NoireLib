using Lumina.Excel.Sheets;
using NoireLib.Helpers;
using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// Wraps the native <see cref="Dalamud.Plugin.Services.IClientState"/> events (login, logout, territory, map,
/// instance, class/job, level, PvP, content-finder pop) and polls the two session facts without native events:
/// housing-interior presence and group pose.
/// </summary>
internal sealed class SessionSource : GameWatcherSource
{
    private uint lastTerritoryId;
    private uint lastClassJobId;
    private bool lastIsInside;
    private bool lastIsGPosing;

    public SessionSource(NoireGameWatcher owner) : base(owner, SourceKind.Session) { }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        lastTerritoryId = NoireService.ClientState.TerritoryType;
        lastClassJobId = 0;
        lastIsInside = ReadIsInside();
        lastIsGPosing = NoireService.ClientState.IsGPosing;

        NoireService.ClientState.Login += OnLogin;
        NoireService.ClientState.Logout += OnLogout;
        NoireService.ClientState.TerritoryChanged += OnTerritoryChanged;
        NoireService.ClientState.MapIdChanged += OnMapIdChanged;
        NoireService.ClientState.InstanceChanged += OnInstanceChanged;
        NoireService.ClientState.ClassJobChanged += OnClassJobChanged;
        NoireService.ClientState.LevelChanged += OnLevelChanged;
        NoireService.ClientState.EnterPvP += OnEnterPvP;
        NoireService.ClientState.LeavePvP += OnLeavePvP;
        NoireService.ClientState.CfPop += OnCfPop;
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        NoireService.ClientState.Login -= OnLogin;
        NoireService.ClientState.Logout -= OnLogout;
        NoireService.ClientState.TerritoryChanged -= OnTerritoryChanged;
        NoireService.ClientState.MapIdChanged -= OnMapIdChanged;
        NoireService.ClientState.InstanceChanged -= OnInstanceChanged;
        NoireService.ClientState.ClassJobChanged -= OnClassJobChanged;
        NoireService.ClientState.LevelChanged -= OnLevelChanged;
        NoireService.ClientState.EnterPvP -= OnEnterPvP;
        NoireService.ClientState.LeavePvP -= OnLeavePvP;
        NoireService.ClientState.CfPop -= OnCfPop;
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        // Housing interior and gpose have no native events — polled (V1 parity).
        var isInside = ReadIsInside();

        if (isInside != lastIsInside)
        {
            lastIsInside = isInside;

            if (isInside)
                Owner.DispatchEvent(new HousingInteriorEnteredEvent());
            else
                Owner.DispatchEvent(new HousingInteriorLeftEvent());
        }

        var isGPosing = NoireService.ClientState.IsGPosing;

        if (isGPosing != lastIsGPosing)
        {
            lastIsGPosing = isGPosing;
            Owner.DispatchEvent(new GPoseStateChangedEvent(isGPosing));
        }
    }

    /// <summary>Reads housing-interior presence from game memory.</summary>
    internal static unsafe bool ReadIsInside()
    {
        var housingManager = FFXIVClientStructs.FFXIV.Client.Game.HousingManager.Instance();
        return housingManager != null && housingManager->IsInside();
    }

    private void OnLogin()
        => Owner.DispatchEvent(new LoginEvent());

    private void OnLogout(int type, int code)
        => Owner.DispatchEvent(new LogoutEvent(type, code));

    private void OnTerritoryChanged(uint territoryId)
    {
        var previous = lastTerritoryId;
        lastTerritoryId = territoryId;
        Owner.DispatchEvent(new TerritoryChangedEvent(previous, territoryId));
    }

    private uint lastMapId;

    private void OnMapIdChanged(uint mapId)
    {
        var previous = lastMapId;
        lastMapId = mapId;
        Owner.DispatchEvent(new MapChangedEvent(previous, mapId));
    }

    private uint lastInstance;

    private void OnInstanceChanged(uint instance)
    {
        var previous = lastInstance;
        lastInstance = instance;
        Owner.DispatchEvent(new InstanceChangedEvent(previous, instance));
    }

    private void OnClassJobChanged(uint classJobId)
    {
        var previous = lastClassJobId;
        lastClassJobId = classJobId;
        Owner.DispatchEvent(new LocalClassJobChangedEvent(previous, classJobId));
    }

    private void OnLevelChanged(uint classJobId, uint level)
        => Owner.DispatchEvent(new LocalLevelChangedEvent(classJobId, level));

    private void OnEnterPvP()
        => Owner.DispatchEvent(new PvpEnteredEvent());

    private void OnLeavePvP()
        => Owner.DispatchEvent(new PvpLeftEvent());

    private void OnCfPop(ContentFinderCondition content)
    {
        var name = content.Name.ExtractText();
        Owner.DispatchEvent(new CfPopEvent(content.RowId, name));
    }
}
