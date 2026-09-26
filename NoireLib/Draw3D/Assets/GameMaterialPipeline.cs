using NoireLib.Helpers;
using System.Reflection;

namespace NoireLib.Draw3D.Assets;

/// <summary>The pipeline game materials draw with. The color map's alpha is a dye mask.</summary>
public static class GameMaterialPipeline
{
    /// <summary>Name to pass to <see cref="Materials.Material.Custom"/> for this pipeline.</summary>
    public const string Name = "NoireGameMaterial";

    private const string LogPrefix = "[Draw3D] ";

    private static readonly System.Threading.Lock RegisterLock = new();
    private static bool registered;
    private static bool missingSource;
    private static bool warnedNotReady;

    /// <summary>Why the pipeline is unavailable (materials then fall back to the lit shader and ignore dye), or null when it is usable.</summary>
    public static string? Unavailable { get; private set; }

    /// <summary>Whether the pipeline is registered. Materials built earlier must be rebuilt.</summary>
    public static bool Ready => registered;

    /// <summary>Registers the pipeline if needed. A missing shader resource fails permanently. A renderer not started yet is retried.</summary>
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

            Unavailable = "The renderer has not started yet. The pipeline could not be registered.";
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
        => FileHelper.ReadEmbeddedText(Assembly.GetExecutingAssembly(), ResourceName);
}
