namespace NoireLib.Draw3D.Enums;

/// <summary>
/// Where the finished 3D layer lands in the game's frame. Both modes are raw D3D blits of the same offscreen layer;
/// neither involves ImGui. The choice is <i>when</i> the blit happens, which is what decides whether the game's UI
/// is something the layer simply loses to, or something it can make decisions about.
/// </summary>
public enum Draw3DLayering
{
    /// <summary>
    /// Default. A render-thread hook blits the layer into the game's present buffer after the world image lands
    /// there and before the native UI is drawn. The game then paints its HUD, addons and nameplates over the layer
    /// itself, so the UI reads on top at its own pixel granularity - letter-exact, with no mask, no rectangles and
    /// no added latency.<br/>
    /// The UI is always on top here; that is not a setting, it is what this mode is. Nameplates are the one thing
    /// still decidable, via the depth Draw3D stamps for the game's plate pass (<see cref="NameplateOcclusion"/>).
    /// </summary>
    UnderGameUi = 0,

    /// <summary>
    /// The layer is blitted over the swapchain backbuffer at present time, after the game has already drawn its UI.
    /// By default it covers the HUD, addons and nameplates; turn on <c>NativeUi.KeepUiOnTop</c> to mask it back off
    /// them per pixel.<br/>
    /// Because this composites <i>after</i> the UI exists, it is the only mode that can make per-element decisions
    /// about it: covering a nameplate outright (<see cref="NameplateOcclusion.Covered"/>) or dimming one rather than
    /// merely occluding it or not.<br/>
    /// Also the automatic fallback for any frame the injection could not run. On those frames there is no pre-UI
    /// snapshot to difference against, so the layer composites unmasked and covers the UI for that frame.
    /// </summary>
    OverEverything = 1,
}
