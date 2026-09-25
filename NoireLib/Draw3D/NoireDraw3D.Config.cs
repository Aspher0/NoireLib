using NoireLib.Draw3D.Enums;
using System;

namespace NoireLib.Draw3D;

/// <summary>
/// The grouped configuration surface of the Draw3D hub: the native-UI settings under <see cref="NativeUi"/> and the
/// batch <see cref="Configure"/> entry point.
/// </summary>
public static partial class NoireDraw3D
{
    /// <summary>Gets the native-UI layering settings.</summary>
    public static NativeUiConfig NativeUi { get; } = new();

    /// <summary>
    /// Applies a batch of settings through a view that writes straight through to the live render and
    /// <see cref="Interaction"/> settings.
    /// </summary>
    /// <param name="configure">The callback receiving the config view to mutate.</param>
    public static void Configure(Action<Draw3DConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureInitialized();
        configure(Draw3DConfig.Instance);
    }

    /// <summary>
    /// The native-UI layering settings, reached via <see cref="NoireDraw3D.NativeUi"/>, where <see cref="KeepUiOnTop"/>
    /// and <see cref="NameplateDim"/> apply only under <see cref="Draw3DLayering.OverEverything"/>.
    /// </summary>
    public sealed class NativeUiConfig
    {
        internal NativeUiConfig() { }

        /// <summary>
        /// Gets or sets where the finished layer lands in the game's frame, <see cref="Draw3DLayering.UnderGameUi"/>
        /// by default.
        /// </summary>
        public Draw3DLayering Layering
        {
            get => layering;
            set => SetLayering(value);
        }

        /// <summary>
        /// Gets or sets whether the native UI stays on top, masked per pixel by the present buffer before and after it draws.
        /// True by default. Reported by <c>/noire3d uimask</c>.
        /// </summary>
        public bool KeepUiOnTop
        {
            get => keepUiOnTop;
            set => SetKeepUiOnTop(value);
        }

        /// <summary>
        /// Gets or sets whether nameplates are occluded by 3D objects in front of them. <see cref="NameplateOcclusion.DepthAware"/>
        /// by default. <see cref="NameplateOcclusion.Covered"/> needs <see cref="Draw3DLayering.OverEverything"/>.
        /// </summary>
        public NameplateOcclusion Nameplates
        {
            get => nameplateOcclusion;
            set => nameplateOcclusion = value;
        }

        /// <summary>
        /// Gets or sets how much a covered nameplate still shows through the layer, from 0 (the default, fully covered)
        /// toward 1, applied only over everything with <see cref="KeepUiOnTop"/> on.
        /// </summary>
        public float NameplateDim
        {
            get => nameplateDimFactor;
            set => nameplateDimFactor = value;
        }
    }

    /// <summary>
    /// A live view over the Draw3D settings for <see cref="Configure"/>, every property reading and writing the live
    /// setting directly.
    /// </summary>
    public sealed class Draw3DConfig
    {
        internal static readonly Draw3DConfig Instance = new();

        internal Draw3DConfig() { }

        /// <summary>Gets or sets the master switch (see <see cref="NoireDraw3D.Enabled"/>).</summary>
        public bool Enabled
        {
            get => NoireDraw3D.Enabled;
            set => NoireDraw3D.Enabled = value;
        }

        /// <summary>Gets or sets the 0-1 opacity of the whole 3D layer (see <see cref="NoireDraw3D.LayerOpacity"/>).</summary>
        public float LayerOpacity
        {
            get => NoireDraw3D.LayerOpacity;
            set => NoireDraw3D.LayerOpacity = value;
        }

        /// <summary>Gets or sets whether water and later surfaces hide the layer (see <see cref="NoireDraw3D.TranslucentOcclusion"/>).</summary>
        public TranslucentOcclusion TranslucentOcclusion
        {
            get => NoireDraw3D.TranslucentOcclusion;
            set => NoireDraw3D.TranslucentOcclusion = value;
        }

        /// <summary>Gets or sets whether the 3D layer keeps rendering while the game UI is hidden (see <see cref="NoireDraw3D.KeepDrawingWhenUiHidden"/>).</summary>
        public bool KeepDrawingWhenUiHidden
        {
            get => NoireDraw3D.KeepDrawingWhenUiHidden;
            set => NoireDraw3D.KeepDrawingWhenUiHidden = value;
        }

        /// <summary>Gets the native-UI layering settings (see <see cref="NoireDraw3D.NativeUi"/>).</summary>
        public NativeUiConfig NativeUi => NoireDraw3D.NativeUi;

        /// <summary>Gets the lighting parameters for lit materials (see <see cref="NoireDraw3D.Lighting"/>).</summary>
        public Draw3DLighting Lighting => NoireDraw3D.Lighting;

        /// <summary>Gets the level-of-detail and culling settings (see <see cref="NoireDraw3D.Performance"/>).</summary>
        public Draw3DPerformance Performance => NoireDraw3D.Performance;

        /// <summary>Gets the interaction settings (see <see cref="NoireDraw3D.Interaction"/>).</summary>
        public Draw3DInteraction Interaction => NoireDraw3D.Interaction;
    }
}
