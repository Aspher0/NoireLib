using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using NoireLib.Core.Modules;

namespace NoireLib;

/// <summary>
/// A static service class to access Dalamud services and manage NoireLib modules.
/// </summary>
public class NoireService
{
    /// <summary>
    /// The window system used by NoireLib for managing module windows.
    /// </summary>
    public static WindowSystem? NoireWindowSystem { get; private set; } = null;

    /// <summary>
    /// The instance of the plugin using NoireLib.<br/>
    /// Do not modify this property directly. Use <see cref="NoireLibMain.Initialize(IDalamudPluginInterface, IDalamudPlugin)"/> instead.
    /// </summary>
    public static IDalamudPlugin? PluginInstance { get; set; } = null;

    /// <summary>
    /// A list of active NoireLib modules and their types.<br/>
    /// Do not modify this list directly. Use <see cref="NoireLibMain.AddModule{T}(string?)"/> and <see cref="NoireLibMain.RemoveModule{T}(string)"/> instead.
    /// </summary>
    public static List<(Type Type, INoireModule Module)> ActiveModules = new();

    /// <summary>
    /// Should not be called directly. Use <see cref="NoireLibMain.Initialize"/> instead.
    /// </summary>
    public static bool Initialize(IDalamudPluginInterface dalamudPluginInterface, IDalamudPlugin plugin)
    {
        if (IsInitialized())
        {
            NoireLogger.LogDebug<NoireService>("NoireLib is already initialized. Initialization skipped.");
            return false;
        }

        if (dalamudPluginInterface == null || plugin == null)
        {
            NoireLogger.LogFatal<NoireService>($"Failed to initialize NoireLib {typeof(NoireLibMain).Assembly.GetName().Version}.");

            if (dalamudPluginInterface == null)
                throw new ArgumentNullException(nameof(dalamudPluginInterface), "Dalamud plugin interface cannot be null.");

            if (plugin == null)
                throw new ArgumentNullException(nameof(plugin), "Plugin instance cannot be null.");
        }

        dalamudPluginInterface.Create<NoireService>();
        PluginInstance = plugin;

        NoireWindowSystem = new WindowSystem($"NoireLib_WindowSystem_For_{dalamudPluginInterface.InternalName}");

        PluginInterface.UiBuilder.Draw += NoireWindowSystem.Draw;

        return true;
    }

    /// <summary>
    /// Gets a value indicating whether the plugin has been successfully initialized.
    /// </summary>
    public static bool IsInitialized() => PluginInstance != null && PluginInterface != null;

    /// <summary>
    /// Do not call this method directly. Use <see cref="NoireLibMain.Dispose"/> instead.
    /// </summary>
    public static void Dispose()
    {
        if (NoireWindowSystem != null)
        {
            PluginInterface.UiBuilder.Draw -= NoireWindowSystem.Draw;
            NoireWindowSystem.RemoveAllWindows();
        }
    }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

    [PluginService]
    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    public static IPluginLog PluginLog { get; private set; } = null!;

    [PluginService]
    public static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    public static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

    [PluginService]
    public static ISigScanner SigScanner { get; private set; } = null!;

    [PluginService]
    public static IFramework Framework { get; private set; } = null!;

    [PluginService]
    public static ITargetManager TargetManager { get; private set; } = null!;

    [PluginService]
    public static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    public static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    public static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    public static ICondition Condition { get; private set; } = null!;

    [PluginService]
    public static IGameConfig GameConfig { get; private set; } = null!;

    [PluginService]
    public static IGameGui GameGui { get; private set; } = null!;

    [PluginService]
    public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    [PluginService]
    public static IGameLifecycle GameLifecycle { get; private set; } = null!;

    [PluginService]
    public static ITextureProvider TextureProvider { get; private set; } = null!;

    [PluginService]
    public static IFlyTextGui FlyTextGui { get; private set; } = null!;

    [PluginService]
    public static IKeyState KeyState { get; private set; } = null!;

    [PluginService]
    public static IGamepadState GamepadState { get; private set; } = null!;

    [PluginService]
    public static IToastGui ToastGui { get; private set; } = null!;

    [PluginService]
    public static IDtrBar DtrBar { get; private set; } = null!;

    [PluginService]
    public static INotificationManager NotificationManager { get; private set; } = null!;

    [PluginService]
    public static IContextMenu ContextMenu { get; private set; } = null!;

    [PluginService]
    public static ISeStringEvaluator SeStringEvaluator { get; private set; } = null!;

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
