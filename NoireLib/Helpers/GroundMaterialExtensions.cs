using NoireLib.Enums;

namespace NoireLib.Helpers;

/// <summary>Reads over the game's footing table, including which material ids share a meaning.</summary>
public static class GroundMaterialExtensions
{
    // The game's 22-entry table of 8-byte strings, in order. Its footstep path is
    // sound/foot/foot/fs_{name}_{a}_{b}_{shoes|boots}.scd.
    private static readonly string[] SoundNames =
    [
        "None", "dart", "grass", "sand", "stone", "wood", "metal", "gravel", "leaf", "powder", "carpet",
        "snow", "water", "water", "soil", "soil", "soil", "soil", "soil", "water", "grass", "metal",
    ];

    /// <summary>The name the game uses for this material when it builds the footstep sound path.</summary>
    /// <param name="material">The material.</param>
    /// <returns>The name, or null for a value outside the game's table.</returns>
    public static string? SoundName(this GroundMaterial material)
    {
        var index = (int)material;

        return index >= 0 && index < SoundNames.Length ? SoundNames[index] : null;
    }

    /// <summary>Whether a character standing on this material is standing in water.</summary>
    /// <param name="material">The material.</param>
    /// <returns>True for any of the water variants.</returns>
    public static bool IsWater(this GroundMaterial material)
        => material is GroundMaterial.Water or GroundMaterial.Water2 or GroundMaterial.Water3;

    /// <summary>Whether this material is one of the soil variants.</summary>
    /// <param name="material">The material.</param>
    /// <returns>True for any of the soil variants.</returns>
    public static bool IsSoil(this GroundMaterial material)
        => material is GroundMaterial.Soil or GroundMaterial.Soil2 or GroundMaterial.Soil3
            or GroundMaterial.Soil4 or GroundMaterial.Soil5;
}
