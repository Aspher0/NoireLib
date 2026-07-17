using System;
using NoireLib.Draw3D;

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
                "Renders collision into a top-down highest-surface-per-column map on frames that have ground decals. It is how HighestOnly tells a tabletop from the floor under it.\n\nOff, a HighestOnly decal degrades to AllSurfaces. Nothing to do with cutting characters out - that is the stencil below.");
            Ui.Slider("Top-surface band", static () => NoireDraw3D.TopSurfaceThreshold, static v => NoireDraw3D.TopSurfaceThreshold = v, 0f, 1f,
                "Metres below its column's highest surface before a surface is skipped. Larger tolerates coarser collision; smaller nibbles real ground where collision sits off the visual floor.\n\n0 disables HighestOnly outright - the shader tests this to decide whether the feature exists.");
        }

        Ui.Section("Character cut-out");
        using (Ui.Form("decals.stencil"))
        {
            Ui.Int("Character stencil", static () => (int)NoireDraw3D.CharacterStencilValue, static v => NoireDraw3D.CharacterStencilValue = (uint)Math.Max(0, v),
                "The game stencil value marking characters, used to cut them out of a decal along their exact silhouette. Default 0x08; 0 disables.\n\nIt moves between patches - /noire3d stencil logs what is actually in view.");
        }
    }
}
