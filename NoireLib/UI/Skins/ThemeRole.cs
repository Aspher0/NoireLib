using NoireLib.Localizer;

namespace NoireLib.UI;

/// <summary>A colour the user may change for a skin. Each skin lists a short set; the colours it derives follow them.</summary>
/// <param name="Key">A <see cref="ThemeColor"/> name, or a custom key the skin reads with <see cref="NoireTheme.Resolve(string, System.Numerics.Vector4)"/>.</param>
/// <param name="Label">The name shown to the user.</param>
public sealed record ThemeRole(string Key, NoireString Label);
