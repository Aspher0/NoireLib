using System;
using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using NoireDraw3DDemoPlugin.Windows.Sections;
using NoireLib.Draw3D;

namespace NoireDraw3DDemoPlugin.Windows;

/// <summary>
/// The single demo window: a tab bar over the four showcase sections. It owns nothing itself beyond the sections and
/// forwards disposal to them (the smoke, scenes and diagnostics sections own live Draw3D scenes).
/// </summary>
public sealed class DemoWindow : Window, IDisposable
{
    private readonly SmokeSceneSection smokeSection = new();
    private readonly GlobalSettingsSection settingsSection = new();
    private readonly ScenesSection scenesSection = new();
    private readonly DiagnosticsSection diagnosticsSection = new();

    /// <summary>Creates the window (starts hidden; opened via <c>/noire3ddemo</c> or the title-screen main-UI button).</summary>
    public DemoWindow() : base("NoireLib Draw3D Demo###noire3ddemo")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480f, 380f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    /// <summary>
    /// Hides this window while the game UI is hidden, unless the user asked otherwise. This has to be done here rather
    /// than left to Dalamud: <see cref="NoireDraw3D.KeepDrawingWhenUiHidden"/> keeps the 3D layer alive precisely by
    /// telling Dalamud not to hide this plugin, so the window can only step aside by checking the game's own state.
    /// </summary>
    public override bool DrawConditions()
        => settingsSection.KeepImGuiWhenUiHidden || !NoireDraw3D.IsGameUiHidden;

    /// <inheritdoc/>
    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("noire3ddemo_tabs");
        if (!tabs)
            return;

        DrawTab("Smoke scene", smokeSection.Draw);
        DrawTab("Global settings", settingsSection.Draw);
        DrawTab("Scenes & decals", scenesSection.Draw);
        DrawTab("Diagnostics", diagnosticsSection.Draw);
    }

    private static void DrawTab(string label, Action body)
    {
        using var tab = ImRaii.TabItem(label);
        if (!tab)
            return;

        using var child = ImRaii.Child($"{label}_child");
        body();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        smokeSection.Dispose();
        scenesSection.Dispose();
        diagnosticsSection.Dispose();
    }
}
