using System;
using System.Numerics;

namespace NoireLib.Helpers.Colors;

/// <summary>
/// Helper class for converting different formats of colors including HEX, Vector3 and Vector4.
/// </summary>
public class ColorHelper
{
    /// <summary>
    /// Converts a HEX color string to a Vector3 representing RGB values between 0 and 1.
    /// </summary>
    /// <param name="hex">The HEX value of the color. Format: "#123456". "#" Optionnal. Alpha value will be ignored if provided.</param>
    /// <returns>A Vector3 representation of the HEX string provided.</returns>
    /// <exception cref="ArgumentException">Thrown when the HEX string is null, empty, or not in a valid format.</exception>
    public static Vector3 HexToVector3(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new ArgumentException("HEX color string cannot be null or empty.", nameof(hex));

        hex = hex.TrimStart('#');

        if (hex.Length != 6 && hex.Length != 8)
            throw new ArgumentException("HEX color string must be 6 or 8 characters long (excluding '#').", nameof(hex));

        try
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new Vector3(r / 255f, g / 255f, b / 255f);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("HEX color string contains invalid characters.", nameof(hex), ex);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException("HEX color string contains values that are out of range.", nameof(hex), ex);
        }
    }

    /// <summary>
    /// Converts a HEX color string to a Vector4 representing RGBA values between 0 and 1.
    /// </summary>
    /// <param name="hex">The HEX value of the color. Format: "#123456". "#" Optionnal. If no alpha value was provided, it will be set to 1 (255).</param>
    /// <returns>A Vector4 representation of the HEX string provided.</returns>
    /// <exception cref="ArgumentException">Thrown when the HEX string is null, empty, or not in a valid format.</exception>
    public static Vector4 HexToVector4(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new ArgumentException("HEX color string cannot be null or empty.", nameof(hex));

        hex = hex.TrimStart('#');

        if (hex.Length != 6 && hex.Length != 8)
            throw new ArgumentException("HEX color string must be 6 or 8 characters long (excluding '#').", nameof(hex));

        try
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            byte a = hex.Length == 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : (byte)255;
            return new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("HEX color string contains invalid characters.", nameof(hex), ex);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException("HEX color string contains values that are out of range.", nameof(hex), ex);
        }
    }

    /// <summary>
    /// Converts a Vector3 representing RGB values between 0 and 1 to a HEX color string. Alpha value will be ignored.<br/>
    /// For a HEX color string with Alpha value, use <see cref="Vector3ToHexAlpha"/>.
    /// </summary>
    /// <param name="color">The Vector3 color to convert.</param>
    /// <returns>The HEX representation of the Vector3 color provided. Example: "#123456".</returns>
    public static string Vector3ToHex(Vector3 color)
    {
        int r = (int)(color.X * 255);
        int g = (int)(color.Y * 255);
        int b = (int)(color.Z * 255);
        return $"#{r:X2}{g:X2}{b:X2}".ToUpper();
    }

    /// <summary>
    /// Converts a Vector3 representing RGB values between 0 and 1 to a HEX color string with Alpha value set to 1 (255).
    /// </summary>
    /// <param name="color">The Vector3 color to convert.</param>
    /// <returns>The HEX representation of the Vector3 color provided. Example: "#123456FF".</returns>
    public static string Vector3ToHexAlpha(Vector3 color)
    {
        int r = (int)(color.X * 255);
        int g = (int)(color.Y * 255);
        int b = (int)(color.Z * 255);
        int a = 255;
        return $"#{r:X2}{g:X2}{b:X2}{a:X2}".ToUpper();
    }

    /// <summary>
    /// Converts a Vector3 to a Vector4 by adding an alpha channel.
    /// </summary>
    /// <param name="color">The Vector3 color to convert.</param>
    /// <param name="alpha">The alpha value to set. Default is 1 (255).</param>
    /// <returns>The Vector4 representation of the Vector3 color provided, with the specified alpha value.</returns>
    public static Vector4 Vector3ToVector4(Vector3 color, float alpha = 1f) => new(color.X, color.Y, color.Z, alpha);

    /// <summary>
    /// Converts a Vector4 representing RGBA values between 0 and 1 to a HEX color string. Alpha value will be ignored.<br/>
    /// For a HEX color string with Alpha value, use <see cref="Vector4ToHexAlpha"/>.
    /// </summary>
    /// <param name="color">The Vector4 color to convert.</param>
    /// <returns>The HEX representation of the Vector4 color provided. Example: "#123456".</returns>
    public static string Vector4ToHex(Vector4 color)
    {
        int r = (int)(color.X * 255);
        int g = (int)(color.Y * 255);
        int b = (int)(color.Z * 255);
        return $"#{r:X2}{g:X2}{b:X2}".ToUpper();
    }

    /// <summary>
    /// Converts a Vector4 representing RGBA values between 0 and 1 to a HEX color string with alpha value.
    /// </summary>
    /// <param name="color">The Vector4 color to convert.</param>
    /// <returns>The HEX representation of the Vector4 color provided. Example: "#123456FF".</returns>
    public static string Vector4ToHexAlpha(Vector4 color)
    {
        int r = (int)(color.X * 255);
        int g = (int)(color.Y * 255);
        int b = (int)(color.Z * 255);
        int a = (int)(color.W * 255);
        return $"#{r:X2}{g:X2}{b:X2}{a:X2}".ToUpper();
    }

    /// <summary>
    /// Converts a Vector4 to a Vector3 by dropping the alpha channel.
    /// </summary>
    /// <param name="color">The Vector4 color to convert.</param>
    /// <returns>The Vector3 representation of the Vector4 color provided.</returns>
    public static Vector3 Vector4ToVector3(Vector4 color) => new(color.X, color.Y, color.Z);
}
