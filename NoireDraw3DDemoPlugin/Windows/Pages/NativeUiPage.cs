using NoireLib.Draw3D;
using NoireLib.Draw3D.Enums;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>Where the layer lands in the game's frame, and what it does about the HUD and plates it finds there.</summary>
internal sealed class NativeUiPage
{
    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        var over = NoireDraw3D.NativeUi.Layering == Draw3DLayering.OverEverything;

        Ui.Section("Layering");
        using (Ui.Form("nativeui.layering"))
        {
            Ui.Enum("Composite at", static () => NoireDraw3D.NativeUi.Layering, static v => NoireDraw3D.NativeUi.Layering = v,
                "Both modes are raw D3D blits; neither uses ImGui. When the layer composites decides whether the game's UI is something it loses to or something it can reason about.\n\n"
                + "UnderGameUi: a render-thread hook blits into the present buffer after the world, before the native UI. The game then paints its HUD over the layer itself - letter-exact, free, nothing to configure.\n\n"
                + "OverEverything: blitted over the backbuffer at present time, after the UI exists. The only mode that can decide per element.\n\n"
                + "Falls back to OverEverything on any frame the injection can't run.");
        }

        Ui.Section("Masking");
        if (!over)
            Ui.Callout("UnderGameUi: the game paints over the layer itself, so there is nothing here to configure.");

        using (Ui.Disabled(!over))
        using (Ui.Form("nativeui.over"))
        {
            Ui.Toggle("Keep UI on top", static () => NoireDraw3D.NativeUi.KeepUiOnTop, static v => NoireDraw3D.NativeUi.KeepUiOnTop = v,
                "Masks the layer per-pixel so the HUD reads on top.\n\nThe mask cuts no rectangles: Draw3D photographs the present buffer before and after the UI is drawn into it, and the difference is exactly where the UI painted - antialiased glyph edges included.\n\nOn a frame where the injection can't fire there is no 'before' photo, and the layer composites unmasked.\n\n/noire3d uimask shows whether the difference is finding anything.");
            Ui.Slider("Nameplate dim", static () => NoireDraw3D.NativeUi.NameplateDim, static v => NoireDraw3D.NativeUi.NameplateDim = v, 0f, 1f,
                "How much a covered plate still shows through: 0 fully covered, toward 1 faintly readable. Needs the mask on, and only applies to a plate the mode below decided is covered.");
        }

        Ui.Section("Nameplates");
        using (Ui.Form("nativeui.plates"))
        {
            Ui.Enum("Occlusion", static () => NoireDraw3D.NativeUi.Nameplates, static v => NoireDraw3D.NativeUi.Nameplates = v,
                "Honoured in both layering modes by different mechanisms - the game draws the plates either way, so it is letter-exact regardless.\n\n"
                + "DepthAware: a plate behind your content is covered, one in front stays readable. Under the UI that is the game's own depth test against depth Draw3D stamps before the plate pass; over everything it compares plate distance against the content covering it.\n\n"
                + "AlwaysVisible: plates read on top at any distance.\n\n"
                + "Covered: the layer covers plates everywhere. Needs OverEverything and the mask on.");
        }

        if (over || NoireDraw3D.NativeUi.Nameplates != NameplateOcclusion.Covered)
            return;

        Ui.Gap();
        Ui.Callout("Covered does nothing under UnderGameUi - the game draws plates after the layer, so this behaves as AlwaysVisible.");
    }
}
