using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Helper class for converting different formats of colors including HEX, Vector3 and Vector4.
/// </summary>
public static class ColorHelper
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

    /// <summary>
    /// Converts a Vector4 representing RGBA values between 0 and 1 to a uint color value used by ImGui.
    /// </summary>
    /// <param name="color">The Vector4 color to convert.</param>
    /// <returns>The uint representation of the Vector4 color provided.</returns>
    public static uint Vector4ToUint(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    /// <summary>
    /// Converts a hexadecimal color string to its equivalent 32-bit unsigned integer representation.
    /// </summary>
    /// <param name="hex">The HEX value of the color. Format: "#123456". "#" Optionnal. If no alpha value was provided, it will be set to 1 (255).</param>
    /// <returns>A uint representation of the HEX color string provided.</returns>
    /// <exception cref="ArgumentException">Thrown when the HEX string is null, empty, or not in a valid format.</exception>
    public static uint HexToUint(string hex)
    {
        Vector4 color = HexToVector4(hex);
        return Vector4ToUint(color);
    }

    /// <summary>
    /// Converts a Vector3 representing RGB values between 0 and 1 to a uint color value used by ImGui, with alpha set to 1 (255).
    /// </summary>
    /// <param name="color">The Vector3 color to convert.</param>
    /// <returns>A uint representation of the Vector3 color provided, with alpha set to 1 (255).</returns>
    public static uint Vector3ToUint(Vector3 color)
    {
        Vector4 colorWithAlpha = Vector3ToVector4(color);
        return Vector4ToUint(colorWithAlpha);
    }

    /// <summary>
    /// Blends two colors together, including their alpha.
    /// </summary>
    /// <param name="from">The color returned when <paramref name="amount"/> is 0.</param>
    /// <param name="to">The color returned when <paramref name="amount"/> is 1.</param>
    /// <param name="amount">How far to blend, from 0 to 1. Values outside that range are clamped.</param>
    /// <returns>The blended color.</returns>
    public static Vector4 Mix(Vector4 from, Vector4 to, float amount)
        => Vector4.Lerp(from, to, Math.Clamp(amount, 0f, 1f));

    /// <summary>
    /// Moves a color towards white, leaving its alpha untouched.
    /// </summary>
    /// <param name="color">The color to lighten.</param>
    /// <param name="amount">How far towards white to move, from 0 (unchanged) to 1 (white).</param>
    /// <returns>The lightened color.</returns>
    public static Vector4 Lighten(Vector4 color, float amount)
    {
        var t = Math.Clamp(amount, 0f, 1f);
        return new Vector4(
            color.X + (1f - color.X) * t,
            color.Y + (1f - color.Y) * t,
            color.Z + (1f - color.Z) * t,
            color.W);
    }

    /// <summary>
    /// Moves a color towards black, leaving its alpha untouched.
    /// </summary>
    /// <param name="color">The color to darken.</param>
    /// <param name="amount">How far towards black to move, from 0 (unchanged) to 1 (black).</param>
    /// <returns>The darkened color.</returns>
    public static Vector4 Darken(Vector4 color, float amount)
    {
        var t = 1f - Math.Clamp(amount, 0f, 1f);
        return new Vector4(color.X * t, color.Y * t, color.Z * t, color.W);
    }

    /// <summary>
    /// Returns the same color at a different opacity.
    /// </summary>
    /// <param name="color">The color to change.</param>
    /// <param name="alpha">The opacity to use, from 0 to 1.</param>
    /// <returns>The color at the given opacity.</returns>
    public static Vector4 WithAlpha(Vector4 color, float alpha)
        => new(color.X, color.Y, color.Z, Math.Clamp(alpha, 0f, 1f));

    /// <summary>
    /// Scales the opacity of a color, keeping whatever transparency it already had.<br/>
    /// Use this rather than <see cref="WithAlpha"/> to fade something out, so an already translucent color does not
    /// become more opaque than it started.
    /// </summary>
    /// <param name="color">The color to fade.</param>
    /// <param name="factor">The multiplier to apply to the alpha channel.</param>
    /// <returns>The faded color.</returns>
    public static Vector4 ScaleAlpha(Vector4 color, float factor)
        => new(color.X, color.Y, color.Z, Math.Clamp(color.W * factor, 0f, 1f));

    /// <summary>
    /// Gets the perceived brightness of a color, from 0 (black) to 1 (white).<br/>
    /// Uses the Rec. 709 weighting, so it tracks how bright a color looks rather than the average of its channels:
    /// pure green reads far brighter than pure blue, which a plain average would miss.
    /// </summary>
    /// <param name="color">The color to measure. Its alpha is ignored.</param>
    /// <returns>The perceived brightness.</returns>
    public static float Luminance(Vector4 color)
        => 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z;

    /// <summary>
    /// Whether a color reads as dark, and so wants light text on top of it.
    /// </summary>
    /// <param name="color">The color to test. Its alpha is ignored.</param>
    /// <returns>True when the color is dark.</returns>
    public static bool IsDark(Vector4 color) => Luminance(color) < 0.5f;

    /// <summary>
    /// Picks whichever of two foreground colors is legible on a background.
    /// </summary>
    /// <param name="background">The background the text sits on.</param>
    /// <param name="onDark">The color to use on a dark background. Defaults to near-white.</param>
    /// <param name="onLight">The color to use on a light background. Defaults to near-black.</param>
    /// <returns>The legible foreground color.</returns>
    public static Vector4 Readable(Vector4 background, Vector4? onDark = null, Vector4? onLight = null)
        => IsDark(background)
            ? onDark ?? new Vector4(0.96f, 0.96f, 0.96f, 1f)
            : onLight ?? new Vector4(0.06f, 0.06f, 0.06f, 1f);
}
