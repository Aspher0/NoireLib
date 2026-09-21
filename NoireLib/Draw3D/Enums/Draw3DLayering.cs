namespace NoireLib.Draw3D.Enums;

/// <summary>When the finished 3D layer is blitted into the game's frame, relative to the game's UI.</summary>
public enum Draw3DLayering
{
    /// <summary>Blits the layer before the game's native UI, which reads on top (the default). See <see cref="NameplateOcclusion"/>.</summary>
    UnderGameUi = 0,

    /// <summary>Blits the layer at present time over the game's UI unless <c>NativeUi.KeepUiOnTop</c> masks it. Also the fallback when the injection could not run.</summary>
    OverEverything = 1,
}
