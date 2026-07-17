using NoireLib.Draw3D;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>Layer-wide render switches: whether Draw3D draws, how opaque, what it does while the game hides its UI.</summary>
internal sealed class RendererPage
{
    private readonly DemoShell shell;

    /// <summary>Creates the page.</summary>
    /// <param name="shell">Carries the window's own "keep me open" flag, which pairs with the layer's.</param>
    public RendererPage(DemoShell shell) => this.shell = shell;

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        Ui.Section("Layer");
        using (Ui.Form("renderer.layer"))
        {
            Ui.Toggle("Enabled", static () => NoireDraw3D.Enabled, static v => NoireDraw3D.Enabled = v,
                "Master switch. Turning it back on re-arms the renderer after a fault.");
            Ui.Slider("Opacity", static () => NoireDraw3D.LayerOpacity, static v => NoireDraw3D.LayerOpacity = v, 0f, 1f,
                "Applied to the whole finished layer at composite time, over whatever each material already does.");
        }

        Ui.Section("UI hidden");
        using (Ui.Form("renderer.uihidden"))
        {
            Ui.Toggle("Keep 3D layer", static () => NoireDraw3D.KeepDrawingWhenUiHidden, static v => NoireDraw3D.KeepDrawingWhenUiHidden = v,
                "Keep rendering while the game UI is hidden (HUD hotkey, cutscene, gpose). Diagnostics -> skipped (ui hidden) counts what this stops.");
            Ui.Toggle("Keep this window", () => shell.KeepWindowWhenUiHidden, v => shell.KeepWindowWhenUiHidden = v,
                "Independent of the layer.\n\nDalamud can't hide this window for us: keeping the layer alive means holding Dalamud's UI-hide overrides, which keeps the window up too. So it checks IsGameUiHidden itself in DrawConditions().");
        }

        Ui.Section("Debug draw");
        using (Ui.Form("renderer.debug"))
        {
            Ui.Toggle("Wireframe", static () => NoireDraw3D.Diagnostics.Wireframe, static v => NoireDraw3D.Diagnostics.Wireframe = v,
                "Decals have no mesh to wireframe - their shape lives in the pixel shader - so they trace their painted outline instead.");
            Ui.Toggle("Decal outlines", static () => NoireDraw3D.Diagnostics.DecalShapeOutlines, static v => NoireDraw3D.Diagnostics.DecalShapeOutlines = v,
                "Traces what every decal paints, retained and immediate alike. Objects have a per-object version in the inspector; immediate shapes have no node, so only this reaches them.\n\nAlways on while wireframe is.");
        }
    }
}
