using NoireLib.Draw3D;
using System;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>The collision height-map that <c>HighestOnly</c> reads, and the stencil value that cuts actors out of a decal.</summary>
internal sealed class DecalsPage
{
    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        Ui.Section("Top-surface projection");
        Ui.Note("HighestOnly decals only. Nothing else on this page's first group changes a pixel without one on screen.");
        Ui.Gap();

        using (Ui.Form("decals.projection"))
        {
            Ui.Toggle("Collision height-map", static () => NoireDraw3D.CollisionHeightMap, static v => NoireDraw3D.CollisionHeightMap = v,
                "Builds the top-down surface map HighestOnly decals use to tell a tabletop from the floor beneath it. Off, they degrade to AllSurfaces.");
            Ui.Slider("Top-surface band", static () => NoireDraw3D.TopSurfaceThreshold, static v => NoireDraw3D.TopSurfaceThreshold = v, 0f, 1f,
                "Metres below a column's highest surface before a surface is skipped. 0 disables HighestOnly.");
        }

        Ui.Section("Character cut-out");
        using (Ui.Form("decals.stencil"))
        {
            Ui.Int("Character stencil", static () => (int)NoireDraw3D.CharacterStencilValue, static v => NoireDraw3D.CharacterStencilValue = (uint)Math.Max(0, v),
                "The game stencil value marking characters, used to cut them out of decals. Default 8; 0 disables.");
        }
    }
}
