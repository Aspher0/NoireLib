using Dalamud.Interface;
using System.Collections.Generic;

namespace NoireDraw3DDemoPlugin.Windows;

/// <summary>The demo's pages. One page is one screen; the rail lists them all.</summary>
internal enum DemoPage
{
    /// <summary>The prebuilt gallery scene.</summary>
    Showcase,

    /// <summary>Scenes, spawning, the object list and the per-object inspector.</summary>
    Scenes,

    /// <summary>Models loaded out of the game's own archives.</summary>
    GameAssets,

    /// <summary>Layer-wide render switches.</summary>
    Renderer,

    /// <summary>How decals project onto the world.</summary>
    Decals,

    /// <summary>Where the layer lands against the game's HUD and nameplates.</summary>
    NativeUi,

    /// <summary>The light <c>Material.Lit</c> shades against.</summary>
    Lighting,

    /// <summary>Pointer input.</summary>
    Interaction,

    /// <summary>Validators, live stats, fault feed.</summary>
    Diagnostics,
}

/// <summary>A rail entry: the page, its group heading, its caption and its glyph.</summary>
/// <param name="Page">The page this selects.</param>
/// <param name="Group">The heading it sits under.</param>
/// <param name="Label">The caption.</param>
/// <param name="Icon">The leading glyph.</param>
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
    };
}
