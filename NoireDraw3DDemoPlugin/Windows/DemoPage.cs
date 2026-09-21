using Dalamud.Interface;
using System.Collections.Generic;

namespace NoireDraw3DDemoPlugin.Windows;

internal enum DemoPage
{
    Showcase,

    Scenes,

    GameAssets,

    Renderer,

    Decals,

    NativeUi,

    Lighting,

    Interaction,

    Diagnostics,

#if DEBUG
    Debug,
#endif
}

internal readonly record struct DemoPageInfo(DemoPage Page, string Group, string Label, FontAwesomeIcon Icon)
{
    /// <summary>Every page, in rail order.</summary>
    public static readonly IReadOnlyList<DemoPageInfo> All = new[]
    {
        new DemoPageInfo(DemoPage.Showcase, "Scenes", "Showcase", FontAwesomeIcon.Cubes),
        new DemoPageInfo(DemoPage.Scenes, "Scenes", "Objects", FontAwesomeIcon.Shapes),
        new DemoPageInfo(DemoPage.GameAssets, "Scenes", "Game assets", FontAwesomeIcon.Archive),
        new DemoPageInfo(DemoPage.Renderer, "Render", "Renderer", FontAwesomeIcon.Desktop),
        new DemoPageInfo(DemoPage.Decals, "Render", "Decals", FontAwesomeIcon.Stamp),
        new DemoPageInfo(DemoPage.NativeUi, "Render", "Native UI", FontAwesomeIcon.LayerGroup),
        new DemoPageInfo(DemoPage.Lighting, "Render", "Lighting", FontAwesomeIcon.Lightbulb),
        new DemoPageInfo(DemoPage.Interaction, "Input", "Interaction", FontAwesomeIcon.MousePointer),
        new DemoPageInfo(DemoPage.Diagnostics, "Tools", "Diagnostics", FontAwesomeIcon.Heartbeat),
#if DEBUG
        new DemoPageInfo(DemoPage.Debug, "Tools", "Debug", FontAwesomeIcon.Bug),
#endif
    };
}
