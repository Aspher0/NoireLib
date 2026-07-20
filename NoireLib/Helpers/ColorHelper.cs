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
    /// Converts a display color to linear light, where multiplying two colors means what it physically means.
    /// </summary>
    /// <remarks>
    /// <b>Convert once, at the point where the color's origin is known.</b> A color picked in a UI, read from a
    /// hex string or taken from the game's dye table is display-encoded and belongs here first; a shader
    /// constant is already linear and must not be passed through this at all. Both look like three floats in
    /// 0..1 and nothing downstream can tell them apart, so a second conversion is silent and lands the color
    /// noticeably dark rather than obviously wrong.
    /// </remarks>
    /// <param name="color">A display-encoded color, each channel in 0..1.</param>
    /// <returns>The same color in linear light.</returns>
    public static Vector3 SrgbToLinear(Vector3 color) => new(
        SrgbToLinear(color.X),
        SrgbToLinear(color.Y),
        SrgbToLinear(color.Z));

    /// <summary>Converts a color in linear light back to a display encoding.</summary>
    /// <param name="color">A color in linear light, each channel in 0..1.</param>
    /// <returns>The same color display-encoded.</returns>
    public static Vector3 LinearToSrgb(Vector3 color) => new(
        LinearToSrgb(color.X),
        LinearToSrgb(color.Y),
        LinearToSrgb(color.Z));

    /// <summary>Converts one display-encoded channel to linear light.</summary>
    /// <param name="channel">The channel value, in 0..1.</param>
    /// <returns>The channel in linear light.</returns>
    public static float SrgbToLinear(float channel)
    {
        channel = Math.Clamp(channel, 0f, 1f);
        return channel <= 0.04045f ? channel / 12.92f : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }

    /// <summary>Converts one channel in linear light to a display encoding.</summary>
    /// <param name="channel">The channel value, in 0..1.</param>
    /// <returns>The channel display-encoded.</returns>
    public static float LinearToSrgb(float channel)
    {
        channel = Math.Clamp(channel, 0f, 1f);
        return channel <= 0.0031308f ? channel * 12.92f : (1.055f * MathF.Pow(channel, 1f / 2.4f)) - 0.055f;
    }

    /// <summary>
    /// Converts a HEX color string to a Vector3 representing RGB values between 0 and 1.
    /// </summary>
    /// <param name="hex">The HEX value of the color. Format: "#123456", "#1234", "#123" or "#12345678". "#" Optionnal. Alpha value will be ignored if provided.</param>
    /// <returns>A Vector3 representation of the HEX string provided.</returns>
    /// <exception cref="ArgumentException">Thrown when the HEX string is null, empty, or not in a valid format.</exception>
    public static Vector3 HexToVector3(string hex)
        => Vector4ToVector3(HexToVector4(hex));

    /// <summary>
    /// Reads a HEX color string without throwing when it is not one.
    /// </summary>
    /// <remarks>
    /// This is the form to use behind a text field. A hex being typed is invalid for most of the keystrokes it takes to
    /// write, so the throwing form would raise an exception on nearly every frame of the entry.<br/>
    /// Accepts the three-digit and four-digit shorthands as well as the full six and eight, with or without the "#",
    /// because those are what people paste.
    /// </remarks>
    /// <param name="hex">The HEX value of the color, for example "#123456", "#123", or "1234abcd".</param>
    /// <param name="color">The color, or <see cref="Vector4.Zero"/> when the string was not a HEX color.</param>
    /// <returns>True when the string was read.</returns>
    public static bool TryHexToVector4(string? hex, out Vector4 color)
    {
        color = Vector4.Zero;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var digits = hex.AsSpan().Trim().TrimStart('#');

        if (digits.Length is not (3 or 4 or 6 or 8))
            return false;

        // The shorthands repeat each digit rather than padding it, so "#f00" is pure red and not a very dark one.
        var shorthand = digits.Length <= 4;
        Span<byte> channels = stackalloc byte[4];
        channels[3] = 255;

        for (var i = 0; i < digits.Length; i++)
        {
            if (!TryReadNibble(digits[i], out var nibble))
                return false;

            if (shorthand)
                channels[i] = (byte)((nibble << 4) | nibble);
            else if ((i & 1) == 0)
                channels[i >> 1] = (byte)(nibble << 4);
            else
                channels[i >> 1] |= nibble;
        }

        color = new Vector4(channels[0] / 255f, channels[1] / 255f, channels[2] / 255f, channels[3] / 255f);
        return true;
    }

    /// <inheritdoc cref="TryHexToVector4(string?, out Vector4)"/>
    /// <param name="hex">The HEX value of the color. Any alpha in it is ignored.</param>
    /// <param name="color">The color, or <see cref="Vector3.Zero"/> when the string was not a HEX color.</param>
    /// <returns>True when the string was read.</returns>
    public static bool TryHexToVector3(string? hex, out Vector3 color)
    {
        if (TryHexToVector4(hex, out var rgba))
        {
            color = Vector4ToVector3(rgba);
            return true;
        }

        color = Vector3.Zero;
        return false;
    }

    /// <summary>
    /// Reads one hexadecimal digit.
    /// </summary>
    private static bool TryReadNibble(char digit, out byte value)
    {
        value = digit switch
        {
            >= '0' and <= '9' => (byte)(digit - '0'),
            >= 'a' and <= 'f' => (byte)(digit - 'a' + 10),
            >= 'A' and <= 'F' => (byte)(digit - 'A' + 10),
            _ => byte.MaxValue,
        };

        return value != byte.MaxValue;
    }

    /// <summary>
    /// Converts a HEX color string to a Vector4 representing RGBA values between 0 and 1.
    /// </summary>
    /// <param name="hex">The HEX value of the color. Format: "#123456", "#1234", "#123" or "#12345678". "#" Optionnal. If no alpha value was provided, it will be set to 1 (255).</param>
    /// <returns>A Vector4 representation of the HEX string provided.</returns>
    /// <exception cref="ArgumentException">Thrown when the HEX string is null, empty, or not in a valid format.</exception>
    public static Vector4 HexToVector4(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new ArgumentException("HEX color string cannot be null or empty.", nameof(hex));

        if (!TryHexToVector4(hex, out var color))
            throw new ArgumentException("HEX color string must be 3, 4, 6 or 8 hexadecimal digits (excluding '#').", nameof(hex));

        return color;
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
    /// <param name="hex">The HEX value of the color. Format: "#123456", "#1234", "#123" or "#12345678". "#" Optionnal. If no alpha value was provided, it will be set to 1 (255).</param>
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
