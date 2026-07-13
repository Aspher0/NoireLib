namespace NoireLib.Draw3D.Enums;

/// <summary>
/// When the camera matrices are sampled for a frame.
/// </summary>
public enum CameraSourceMode
{
    /// <summary>Sample at draw time, inside the present callback (default).</summary>
    DrawTime = 0,

    /// <summary>Sample during <c>IFramework.Update</c> and consume the latest complete snapshot at draw. An A/B experiment for fast camera pans.</summary>
    FrameworkSnapshot = 1,
}
