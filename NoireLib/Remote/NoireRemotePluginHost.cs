using Dalamud.Utility;
using NoireLib.Helpers;
using System;
using System.Threading.Tasks;

namespace NoireLib.Remote;

internal sealed class NoireRemotePluginHost : INoireRemoteHost
{
    public NoireRemoteIdentity Identity => new(PluginName(), PluginVersion(), NoireLibMain.Version);

    public NoireRemoteThread DefaultThread => NoireRemoteThread.Framework;

    // Read live. A plugin publishing before initialization would keep reading game memory off-thread otherwise.
    public bool HasHostThread => NoireService.IsInitialized();

    public Task<TResult> RunOnHostThreadAsync<TResult>(Func<Task<TResult>> work)
        => AsyncHelper.StartOnFrameworkThreadAsync(work);

    // Framework thread only. Dalamud's object table throws anywhere else.
    public NoireRemoteReadinessResult CheckReadiness(NoireRemoteReadiness requires, string route)
    {
        switch (requires)
        {
            case NoireRemoteReadiness.StateReady when !CharacterHelper.IsStateReady:
                return NoireRemoteReadinessResult.NotReady("'" + route + "' reads character state and no character is loaded.");

            case NoireRemoteReadiness.PlayerLoaded when !CharacterHelper.IsPlayerLoaded:
                return NoireRemoteReadinessResult.NotReady("'" + route + "' calls into game code and the player object does not exist.");

            default:
                return NoireRemoteReadinessResult.Ready;
        }
    }

    public void RefreshLabel(Action<string> setLabel)
    {
        if (!NoireService.IsInitialized())
            return;

        // The heartbeat runs on a pool thread.
        AsyncHelper.RunOnFramework(() =>
        {
            if (!CharacterHelper.IsPlayerLoaded)
                return;

            var player = NoireService.ObjectTable.LocalPlayer;

            if (player == null)
                return;

            var world = player.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;
            setLabel(world.Length == 0 ? player.Name.TextValue : player.Name.TextValue + " @ " + world);
        });
    }

    public void Log(NoireRemoteLogLevel level, string message, Exception? exception)
    {
        switch (level)
        {
            case NoireRemoteLogLevel.Error when exception != null:
                NoireLogger.LogError(exception, message);
                break;

            case NoireRemoteLogLevel.Error:
                NoireLogger.LogError(message);
                break;

            case NoireRemoteLogLevel.Warning:
                NoireLogger.LogWarning(message);
                break;

            case NoireRemoteLogLevel.Debug:
                NoireLogger.LogDebug(message);
                break;

            default:
                NoireLogger.LogInfo(message);
                break;
        }
    }

    private static string PluginName()
        => NoireService.IsInitialized() ? NoireService.PluginInterface.InternalName : "NoireLib";

    private static string PluginVersion()
        => NoireService.PluginInstance?.GetType().Assembly.GetName().Version?.ToString() ?? NoireLibMain.Version;
}
