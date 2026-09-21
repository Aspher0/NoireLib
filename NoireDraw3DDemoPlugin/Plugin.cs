using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using NoireDraw3DDemoPlugin.Windows;
using NoireLib;
using NoireLib.Draw3D;

namespace NoireDraw3DDemoPlugin;

/// <summary>Demo plugin exercising the public NoireLib Draw3D API.</summary>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    private const string CommandName = "/noire3ddemo";

    private readonly WindowSystem windowSystem = new("NoireDraw3DDemoPlugin");
    private readonly DemoWindow demoWindow;

    public Plugin()
    {
        NoireLibMain.Initialize(PluginInterface, this);

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
