using System.IO;
using System.Reflection;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// The shading pipeline game materials are drawn with, registered on first use.<br/>
/// It exists because these materials use the color map's alpha as a dyeable mask rather than as
/// coverage: the surface must be drawn opaque, and any color applied to it must be confined to the
/// masked area or it darkens the detail the texture already colored correctly.
/// </summary>
public static class GameMaterialPipeline
{
    /// <summary>Name to pass to <see cref="Materials.Material.Custom"/> for this pipeline.</summary>
    public const string Name = "NoireGameMaterial";

    private const string LogPrefix = "Draw3D";

    private static readonly System.Threading.Lock RegisterLock = new();
    private static bool registered;
    private static bool missingSource;
    private static bool warnedNotReady;

    /// <summary>
    /// Why the pipeline is unavailable, or null when it is usable.<br/>
    /// Materials fall back to the standard lit shader when this is set, which draws the same texture without
    /// confining the tint, so a dye applied through <see cref="GameMaterial.ToGameShaded"/> has no visible
    /// effect. Surface this wherever that difference would otherwise read as the dye doing nothing.
    /// </summary>
    public static string? Unavailable { get; private set; }

    /// <summary>
    /// Whether the pipeline is registered, so a material built now draws with it rather than falling back.<br/>
    /// <b>A caller that keeps built materials around has to watch this.</b> Registration fails while the
    /// renderer has no device, and <see cref="GameMaterial.ToGameShaded"/> then returns a plain lit material -
    /// same texture, but no dye, no normal map and no specular. That material is not repaired when the device
    /// arrives, because nothing owns it any more. Rebuild on the transition from false to true, or the first
    /// object of a session is quietly flatter than every one after it.<br/>
    /// Unlike <see cref="Unavailable"/> this is meaningful before the first registration attempt, which is
    /// exactly when the decision has to be made.
    /// </summary>
    public static bool Ready => registered;

    /// <summary>
    /// Registers the pipeline if it is not already, and reports whether it is usable.<br/>
    /// Failure disables only this pipeline; callers fall back to the standard lit shader. A missing shader
    /// resource is permanent and is not retried, while a renderer that has not started yet is retried on
    /// every call, so the first material built before the device exists does not disable the pipeline for
    /// the rest of the session.
    /// </summary>
    public static bool EnsureRegistered()
    {
        if (registered)
            return true;

        if (missingSource)
            return false;

        lock (RegisterLock)
        {
            if (registered)
                return true;

            if (missingSource)
                return false;

            var source = ReadSource();
            if (source is null)
            {
                missingSource = true;
                Unavailable = $"The shader '{ResourceName}' is not embedded in this build of NoireLib.";
                NoireLogger.LogError($"Game material pipeline disabled: {Unavailable}", LogPrefix);
                return false;
            }

            registered = NoireDraw3D.RegisterPipeline(Name, source);
            if (registered)
            {
                Unavailable = null;
                return true;
            }

            // The renderer has no device yet. This is ordinary during startup and resolves itself, so it is
            // reported once rather than on every material built until then.
            Unavailable = "The renderer has not started yet, so the pipeline could not be registered.";
            if (!warnedNotReady)
            {
                warnedNotReady = true;
                NoireLogger.LogWarning($"Game material pipeline not registered yet: {Unavailable} Materials built now fall back to the lit shader.", LogPrefix);
            }

            return false;
        }
    }

    private static string ResourceName
        => $"{typeof(GameMaterialPipeline).Namespace!.Replace(".Assets", ".Shaders")}.GameMaterial.hlsl";

    private static string? ReadSource()
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
