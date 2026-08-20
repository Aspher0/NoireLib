using NoireLib.Helpers;
using System.Reflection;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// The shading pipeline PBR glTF materials are drawn with, registered on first use. The importer routes only
/// materials using metallic-roughness, a normal map, emissive or an alpha cutoff here, and leaves plain
/// base-color materials on the instancing-friendly lit path.
/// </summary>
public static class GltfPbrPipeline
{
    /// <summary>Name to pass to <see cref="Materials.Material.Custom"/> for this pipeline.</summary>
    public const string Name = "NoireGltfPbr";

    private const string LogPrefix = "Draw3D";

    private static readonly System.Threading.Lock RegisterLock = new();
    private static bool registered;
    private static bool missingSource;
    private static bool warnedNotReady;

    /// <summary>
    /// Why the pipeline is unavailable, or null when it is usable; materials fall back to the lit shader while set.
    /// </summary>
    public static string? Unavailable { get; private set; }

    /// <summary>
    /// Whether the pipeline is registered; a material built while this is false keeps the fallback shader for good.
    /// </summary>
    public static bool Ready => registered;

    /// <summary>
    /// Registers the pipeline if it is not already registered.
    /// </summary>
    /// <returns>True when the pipeline is usable. A missing shader resource is permanent; a renderer that has
    /// not started yet is retried on the next call.</returns>
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
                NoireLogger.LogError($"glTF PBR pipeline disabled: {Unavailable}", LogPrefix);
                return false;
            }

            registered = NoireDraw3D.RegisterPipeline(Name, source);
            if (registered)
            {
                Unavailable = null;
                return true;
            }

            // The renderer has no device yet, which is ordinary during startup, so this reports once only.
            Unavailable = "The renderer has not started yet, so the pipeline could not be registered.";
            if (!warnedNotReady)
            {
                warnedNotReady = true;
                NoireLogger.LogWarning($"glTF PBR pipeline not registered yet: {Unavailable} Materials built now fall back to the lit shader.", LogPrefix);
            }

            return false;
        }
    }

    private static string ResourceName
        => $"{typeof(GltfPbrPipeline).Namespace!.Replace(".Assets", ".Shaders")}.GltfPbr.hlsl";

    private static string? ReadSource()
        => FileHelper.ReadEmbeddedText(Assembly.GetExecutingAssembly(), ResourceName);
}
