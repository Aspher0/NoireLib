using NoireLib.Draw3D;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>The stylized light <c>Material.Lit</c> shades against. Unlit, textured and decal materials ignore all of it.</summary>
internal sealed class LightingPage
{
    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        var light = NoireDraw3D.Lighting;

        Ui.Section("Ambient");
        Ui.Note("Material.Lit only. Spawn a lit primitive before expecting to see anything move.");
        Ui.Gap();

        using (Ui.Form("lighting.ambient"))
        {
            Ui.Color3("Color", () => light.AmbientColor, v => light.AmbientColor = v,
                "Reaches every surface regardless of facing. This is what fills the shadowed side.");
            Ui.Slider("Intensity", () => light.AmbientIntensity, v => light.AmbientIntensity = v, 0f, 1f,
                "At 0, a surface facing away from the light goes black.");
        }

        Ui.Section("Directional");
        using (Ui.Form("lighting.directional"))
        {
            Ui.Slider3("Direction", () => light.LightDirection, v => light.LightDirection = v, -1f, 1f,
                "Direction TOWARD the source, normalized on upload. +Y is lit from above.");
            Ui.Color3("Color", () => light.LightColor, v => light.LightColor = v);
            Ui.Slider("Intensity", () => light.LightIntensity, v => light.LightIntensity = v, 0f, 2f,
                "Above 1 over-brightens the facing side deliberately.");
        }
    }
}
