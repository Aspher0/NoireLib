using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.Models.Colors;

/// <summary>
/// A model representing a color in multiple formats (Vector3, Vector4, HEX).
/// </summary>
public class ColorBuilder
{
    /// <summary>
    /// The Vector3 representation of the color, hence without alpha channel.
    /// </summary>
    public Vector3 Vector3 { get; set; } = new Vector3(1, 1, 1);

    /// <summary>
    /// The Vector4 representation of the color, including alpha channel.
    /// </summary>
    public Vector4 Vector4 { get; set; } = new Vector4(1, 1, 1, 1);

    /// <summary>
    /// The HEX color without alpha channel.
    /// </summary>
    public string Hex { get; set; } = "#FFFFFF";

    /// <summary>
    /// The HEX color with alpha channel included.
    /// </summary>
    public string HexAlpha { get; set; } = "#FFFFFFFF";

    /// <summary>
    /// Creates a ColorBuilder instance from a HEX color string.
    /// </summary>
    /// <param name="hex">The HEX value of the color. Format: "#RRGGBBAA". "#" Optionnal.</param>
    /// <exception cref="ArgumentException">Thrown when the HEX string is null, empty, or not in a valid format.</exception>
    public ColorBuilder(string hex)
    {
        UpdateFromHex(hex);
    }

    /// <summary>
    /// Creates a ColorBuilder instance from a Vector3 color value.
    /// </summary>
    /// <param name="vector">The Vector3 color to convert.</param>
    public ColorBuilder(Vector3 vector)
    {
        UpdateFromVector3(vector);
    }

    /// <summary>
    /// Creates a ColorBuilder instance from a Vector4 color value.
    /// </summary>
    /// <param name="vector">The Vector4 color to convert.</param>
    public ColorBuilder(Vector4 vector)
    {
        UpdateFromVector4(vector);
    }

    /// <summary>
    /// Updates the ColorBuilder instance from a HEX color string.
    /// </summary>
    /// <param name="hex">The HEX value of the color. Format: "#RRGGBBAA". "#" Optionnal.</param>
    /// <exception cref="ArgumentException">Thrown when the HEX string is null, empty, or not in a valid format.</exception>
    public void UpdateFromHex(string hex)
    {
        try
        {
            Vector3 = ColorHelper.HexToVector3(hex);
            Vector4 = ColorHelper.HexToVector4(hex);
            hex = hex.TrimStart('#');
            if (hex.Length == 8)
            {
                HexAlpha = $"#{hex.ToUpper()}";
                Hex = $"#{hex.Substring(0, 6).ToUpper()}";
            }
            else if (hex.Length == 6)
            {
                Hex = $"#{hex.ToUpper()}";
                HexAlpha = $"#{hex.ToUpper()}FF";
            }
            else
                throw new Exception("HEX color string must be 6 or 8 characters long (excluding '#').");
        }
        catch (Exception)
        {
            NoireLogger.LogError<ColorBuilder>($"Invalid HEX color string: {hex}");
        }
    }

    /// <summary>
    /// Updates the ColorBuilder instance from a Vector3 color value.
    /// </summary>
    /// <param name="vector">The Vector3 color to convert.</param>
    public void UpdateFromVector3(Vector3 vector)
    {
        Vector4 = ColorHelper.Vector3ToVector4(vector);
        Hex = ColorHelper.Vector3ToHex(vector);
        HexAlpha = ColorHelper.Vector3ToHexAlpha(vector);
        Vector3 = vector;
    }

    /// <summary>
    /// Updates the ColorBuilder instance from a Vector4 color value.
    /// </summary>
    /// <param name="vector">The Vector4 color to convert.</param>
    public void UpdateFromVector4(Vector4 vector)
    {
        Vector3 = ColorHelper.Vector4ToVector3(vector);
        Hex = ColorHelper.Vector4ToHex(vector);
        HexAlpha = ColorHelper.Vector4ToHexAlpha(vector);
        Vector4 = vector;
    }
}
