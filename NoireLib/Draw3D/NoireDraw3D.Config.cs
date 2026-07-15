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
    /// The grouped native-UI configuration, reached via <see cref="NoireDraw3D.NativeUi"/>. Every property forwards to
    /// the same live setting the (now deprecated) flat <c>NoireDraw3D.*</c> properties used.
    /// </summary>
    public sealed class NativeUiConfig
    {
        internal NativeUiConfig() { }

        /// <summary>The game's native UI always draws on top of the 3D layer (per-pixel, letter-exact). Default true. Off puts the whole layer above the game UI.</summary>
        public bool Protect
        {
            get => protectGameUi;
            set => protectGameUi = value;
        }

        /// <summary>How nameplates layer against 3D content (letter-granular). Default <see cref="NativeUiProtectionMode.DepthAware"/>. Only meaningful while <see cref="Protect"/> is on.</summary>
        public NativeUiProtectionMode Protection
        {
            get => nativeUiProtectionMode;
            set => nativeUiProtectionMode = value;
        }

        /// <summary>How much a nameplate behind your content still shows through it: 0 (default) fully covered, toward 1 faintly readable.</summary>
        public float DimFactor
        {
            get => nativeUiProtectionDimFactor;
            set => nativeUiProtectionDimFactor = value;
        }

        /// <summary>Composite the 3D layer before the game draws its native UI, so HUD/nameplates read on top per-pixel (experimental). Default true; fail-soft.</summary>
        public bool RenderUnder
        {
            get => renderUnderNativeUi;
            set => SetRenderUnderNativeUi(value);
        }

        /// <summary>Write the layer's opaque depth into the game's scene depth so nameplates are occluded by 3D objects in front of characters (needs <see cref="RenderUnder"/>). Default true; fail-soft.</summary>
        public bool DepthWrite
        {
            get => nativeUiDepthWrite;
            set => nativeUiDepthWrite = value;
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

        /// <summary>The interaction knobs (see <see cref="NoireDraw3D.Interaction"/>).</summary>
        public Draw3DInteraction Interaction => NoireDraw3D.Interaction;
    }
}
