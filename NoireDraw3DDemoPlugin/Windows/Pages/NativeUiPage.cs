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
                "UnderGameUi draws before the native UI, so the HUD reads on top. OverEverything draws after it and can decide per element.");
        }

        Ui.Section("Masking");
        if (!over)
            Ui.Callout("UnderGameUi: the game paints over the layer itself, so there is nothing here to configure.");

        using (Ui.Disabled(!over))
        using (Ui.Form("nativeui.over"))
        {
            Ui.Toggle("Keep UI on top", static () => NoireDraw3D.NativeUi.KeepUiOnTop, static v => NoireDraw3D.NativeUi.KeepUiOnTop = v,
                "Masks the layer per pixel so the HUD reads on top, following the UI's exact shape.");
            Ui.Slider("Nameplate dim", static () => NoireDraw3D.NativeUi.NameplateDim, static v => NoireDraw3D.NativeUi.NameplateDim = v, 0f, 1f,
                "How much a covered plate still shows through: 0 fully covered, toward 1 faintly readable. Needs the mask on, and only applies to a plate the mode below decided is covered.");
        }

        Ui.Section("Nameplates");
        using (Ui.Form("nativeui.plates"))
        {
            Ui.Enum("Occlusion", static () => NoireDraw3D.NativeUi.Nameplates, static v => NoireDraw3D.NativeUi.Nameplates = v,
                "DepthAware covers a plate behind your content and keeps one in front readable. AlwaysVisible keeps plates on top. Covered hides them everywhere.");
        }

        if (over || NoireDraw3D.NativeUi.Nameplates != NameplateOcclusion.Covered)
            return;

        Ui.Gap();
        Ui.Callout("Covered does nothing under UnderGameUi - the game draws plates after the layer, so this behaves as AlwaysVisible.");
    }
}
