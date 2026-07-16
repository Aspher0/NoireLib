using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using NoireDraw3DDemoPlugin.Windows;
using NoireLib;
using NoireLib.Draw3D;

namespace NoireDraw3DDemoPlugin;

/// <summary>
/// A demonstration plugin for the NoireLib Draw3D renderer. Everything it does goes through the public Draw3D API, so
/// it doubles as a worked reference - spawning the smoke showcase scene, tweaking every global knob, building scenes and
/// decals, and running the render diagnostics. The library's own <c>/noire3d</c> validators stay in the library and are
/// enabled here too.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    private const string CommandName = "/noire3ddemo";

    private readonly WindowSystem windowSystem = new("NoireDraw3DDemoPlugin");
    private readonly DemoWindow demoWindow;

    /// <summary>Initializes NoireLib, wires the demo window and command.</summary>
    /// <param name="pluginInterface">The Dalamud plugin interface (the only service the demo injects; the rest come from <see cref="NoireService"/>).</param>
    public Plugin()
    {
        NoireLibMain.Initialize(PluginInterface, this);

        // The render validators (validate / probe / camtrace / ...) live in the library; expose their /noire3d command.
        NoireDraw3D.EnableDiagnosticsCommand();

        demoWindow = new DemoWindow();
        windowSystem.AddWindow(demoWindow);

        NoireService.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the NoireLib Draw3D demo window.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenUi;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenUi;

        windowSystem.RemoveAllWindows();

        NoireService.CommandManager.RemoveHandler(CommandName);

        demoWindow.Dispose();
        NoireLibMain.Dispose();
    }

    private void OnCommand(string command, string args) => demoWindow.Toggle();

    private void OpenUi() => demoWindow.IsOpen = true;
}
