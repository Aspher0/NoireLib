using NoireLib.Draw3D.Enums;
using System;

namespace NoireLib.Draw3D;

/// <summary>
/// Grouped configuration surface: the scattered native-UI flags collected under <see cref="NativeUi"/>, and a batch
/// <see cref="Configure"/> entry point over a live view of every knob (render + interaction). Sugar over the same
/// settings - no separate config store; the old flat properties remain as <c>[Obsolete]</c> forwarders.
/// </summary>
public static partial class NoireDraw3D
{
    /// <summary>The native-UI layering knobs, grouped (mirrors how <see cref="Lighting"/> is a sub-object).</summary>
    public static NativeUiConfig NativeUi { get; } = new();

    /// <summary>
    /// One-shot batch setup over a live view of the settings (render config + <see cref="Interaction"/>). Sugar for a
    /// sequence of property assignments; the view writes straight through to the same live settings.
    /// </summary>
    /// <param name="configure">Receives the config view to mutate.</param>
    public static void Configure(Action<Draw3DConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureInitialized();
        configure(Draw3DConfig.Instance);
    }

    /// <summary>
    /// The grouped native-UI configuration, reached via <see cref="NoireDraw3D.NativeUi"/>.
    /// <br/>
    /// <see cref="Layering"/> picks where the layer lands; <see cref="KeepUiOnTop"/> and <see cref="NameplateDim"/>
    /// configure what it does about the UI it finds there, and apply only under
    /// <see cref="Draw3DLayering.OverEverything"/> (under the game UI the game paints over the layer itself, so keeping
    /// the UI readable is inherent rather than optional). <see cref="Nameplates"/> applies to both, by different means.
    /// </summary>
    public sealed class NativeUiConfig
    {
        internal NativeUiConfig() { }

        /// <summary>
        /// Where the finished layer lands in the game's frame. Default <see cref="Draw3DLayering.UnderGameUi"/>.
        /// Both modes are raw D3D blits; neither involves ImGui.
        /// </summary>
        public Draw3DLayering Layering
        {
            get => layering;
            set => SetLayering(value);
        }

        /// <summary>
        /// Masks the layer per-pixel so the game's HUD, addons and nameplates read on top of it. Default true.
        /// <br/>
        /// <b>Only applies while <see cref="Layering"/> is <see cref="Draw3DLayering.OverEverything"/>.</b> Under the game
        /// UI the game paints its own UI over the layer a moment after it composites, so this is neither needed nor
        /// consulted there.
        /// <br/>
        /// The mask is letter-exact and carries no rectangles: Draw3D snapshots the game's present buffer before and after
        /// the native UI is drawn into it, and wherever the two differ is where the UI painted. This needs the render-thread
        /// hook armed, so on a frame where the injection point cannot fire there is no snapshot to difference and the layer
        /// composites unmasked for that frame. <c>/noire3d uimask</c> reports whether the difference is working.
        /// </summary>
        public bool KeepUiOnTop
        {
            get => keepUiOnTop;
            set => SetKeepUiOnTop(value);
        }

        /// <summary>
        /// Whether the game's own nameplates are occluded by 3D objects standing in front of them.
        /// Default <see cref="NameplateOcclusion.DepthAware"/>; fail-soft. Honoured under both layering modes.
        /// <br/>
        /// Under <see cref="Draw3DLayering.UnderGameUi"/> it works by stamping depth for the game's plate pass to test
        /// against. Under <see cref="Draw3DLayering.OverEverything"/> it gates where the
        /// <see cref="KeepUiOnTop"/> mask applies, so it needs that on, plus nameplates actually on screen.
        /// <see cref="NameplateOcclusion.Covered"/> requires <see cref="Draw3DLayering.OverEverything"/>.
        /// </summary>
        public NameplateOcclusion Nameplates
        {
            get => nameplateOcclusion;
            set => nameplateOcclusion = value;
        }

        /// <summary>
        /// How much a nameplate that your content covers still shows through it: 0 (default) fully covered, toward 1
        /// faintly readable.
        /// <br/>
        /// <b>Only applies while <see cref="Layering"/> is <see cref="Draw3DLayering.OverEverything"/></b>, with
        /// <see cref="KeepUiOnTop"/> on, and only to a plate <see cref="Nameplates"/> decided is covered - so
        /// <see cref="NameplateOcclusion.AlwaysVisible"/> never reaches it. Under the game UI a plate is drawn by the
        /// game against a depth test, which can only occlude it or not, so there is no partial value to apply.
        /// </summary>
        public float NameplateDim
        {
            get => nameplateDimFactor;
            set => nameplateDimFactor = value;
        }
    }

    /// <summary>
    /// A thin, live view over the Draw3D settings for <see cref="Configure"/> - a single object gathering the top-level
    /// render knobs, the grouped <see cref="NativeUi"/>, the <see cref="Lighting"/> sub-object and the
    /// <see cref="Interaction"/> facade. Every property reads/writes the live setting directly.
    /// </summary>
    public sealed class Draw3DConfig
    {
        internal static readonly Draw3DConfig Instance = new();

        internal Draw3DConfig() { }

        /// <summary>Master switch (see <see cref="NoireDraw3D.Enabled"/>).</summary>
        public bool Enabled
        {
            get => NoireDraw3D.Enabled;
            set => NoireDraw3D.Enabled = value;
        }

        /// <summary>0–1 opacity applied to the whole 3D layer at composite time (see <see cref="NoireDraw3D.LayerOpacity"/>).</summary>
        public float LayerOpacity
        {
            get => NoireDraw3D.LayerOpacity;
            set => NoireDraw3D.LayerOpacity = value;
        }

        /// <summary>Keep the 3D layer rendering while the plugin UI is hidden (see <see cref="NoireDraw3D.KeepDrawingWhenUiHidden"/>).</summary>
        public bool KeepDrawingWhenUiHidden
        {
            get => NoireDraw3D.KeepDrawingWhenUiHidden;
            set => NoireDraw3D.KeepDrawingWhenUiHidden = value;
        }

        /// <summary>The grouped native-UI knobs (see <see cref="NoireDraw3D.NativeUi"/>).</summary>
        public NativeUiConfig NativeUi => NoireDraw3D.NativeUi;

        /// <summary>Lighting parameters for lit materials (see <see cref="NoireDraw3D.Lighting"/>).</summary>
        public Draw3DLighting Lighting => NoireDraw3D.Lighting;

        /// <summary>Performance knobs: model level-of-detail and culling (see <see cref="NoireDraw3D.Performance"/>).</summary>
        public Draw3DPerformance Performance => NoireDraw3D.Performance;

        /// <summary>The interaction knobs (see <see cref="NoireDraw3D.Interaction"/>).</summary>
        public Draw3DInteraction Interaction => NoireDraw3D.Interaction;
    }
}
