namespace NoireLib.Helpers;

/// <summary>What the EObj sheet says about an event object beyond its name and handler.</summary>
/// <param name="PopType">The sheet's pop type.</param>
/// <param name="Invisibility">The sheet's invisibility value, zero for a visible object.</param>
/// <param name="IsTargetable">The sheet's target flag.</param>
/// <param name="EyeCollision">The sheet's eye collision flag.</param>
/// <param name="DirectorControl">The sheet's director control flag, set for objects a duty or content director drives.</param>
/// <param name="SgbPath">The SGB scene the row names through ExportedSG, empty when it names none.</param>
public sealed record EventObjectDetails(
    byte PopType,
    byte Invisibility,
    bool IsTargetable,
    bool EyeCollision,
    bool DirectorControl,
    string SgbPath);
