using System;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>Helper class for converting different formats of colors including HEX, Vector3 and Vector4.</summary>
public static class ColorHelper
{
    /// <summary>Converts a display color to linear light. Never convert a color twice.</summary>
    /// <param name="color">A display-encoded color, each channel from 0 to 1.</param>
    /// <returns>The color in linear light.</returns>
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

    /// <summary>Converts a HEX color string to a Vector3 representing RGB values between 0 and 1.</summary>
    /// <param name="hex">Hex color: "#123456", "#1234", "#123" or "#12345678", with or without "#". Alpha is ignored.</param>
    /// <returns>A Vector3 representation of the HEX string provided.</returns>
    /// <exception cref="ArgumentException">Thrown when the HEX string is null, empty, or not in a valid format.</exception>
    public static Vector3 HexToVector3(string hex)
        => Vector4ToVector3(HexToVector4(hex));

    /// <summary>Reads a HEX color of 3, 4, 6 or 8 digits, with or without "#", without throwing.</summary>
    /// <param name="hex">The HEX value, such as "#123456", "#123" or "1234abcd".</param>
    /// <param name="color">The color, or <see cref="Vector4.Zero"/> when the string is not one.</param>
    /// <returns>Whether the string was read.</returns>
    public static bool TryHexToVector4(string? hex, out Vector4 color)
    {
        color = Vector4.Zero;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var digits = hex.AsSpan().Trim().TrimStart('#');

        if (digits.Length is not (3 or 4 or 6 or 8))
            return false;

        // A shorthand repeats each digit: "#f00" is pure red.
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

    /// <summary>Converts a HEX color string to a Vector4 representing RGBA values between 0 and 1.</summary>
    /// <param name="hex">Hex color: "#123456", "#1234", "#123" or "#12345678", with or without "#". Missing alpha defaults to 255.</param>
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

    /// <summary>Converts a Vector3 to a HEX string, without alpha.</summary>
    /// <param name="color">The color, each channel from 0 to 1.</param>
    /// <returns>The HEX string, such as "#123456".</returns>
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

    /// <summary>Converts a Vector3 to a Vector4 by adding an alpha channel.</summary>
    /// <param name="color">The Vector3 color to convert.</param>
    /// <param name="alpha">The alpha value to set. Default is 1 (255).</param>
    /// <returns>The Vector4 representation of the Vector3 color provided, with the specified alpha value.</returns>
    public static Vector4 Vector3ToVector4(Vector3 color, float alpha = 1f) => new(color.X, color.Y, color.Z, alpha);

    /// <summary>Converts a Vector4 to a HEX string, ignoring alpha. See <see cref="Vector4ToHexAlpha"/>.</summary>
    /// <param name="color">The color, each channel from 0 to 1.</param>
    /// <returns>The HEX string, such as "#123456".</returns>
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

    /// <summary>Converts a Vector4 to a Vector3 by dropping the alpha channel.</summary>
    /// <param name="color">The Vector4 color to convert.</param>
    /// <returns>The Vector3 representation of the Vector4 color provided.</returns>
    public static Vector3 Vector4ToVector3(Vector4 color) => new(color.X, color.Y, color.Z);

    /// <summary>
    /// Converts a color written as <c>0xRRGGBB</c>, the order CSS and design tools write hex in, to a Vector4.
    /// </summary>
    /// <param name="rgb">The color, red in the high byte of the three.</param>
    /// <param name="alpha">The alpha, from 0 to 1.</param>
    /// <returns>The color, RGBA from 0 to 1.</returns>
    public static Vector4 RgbToVector4(uint rgb, float alpha = 1f)
        => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    /// <summary>
    /// Converts a Vector4 representing RGBA values between 0 and 1 to a uint color value used by ImGui.
    /// </summary>
    /// <param name="color">The Vector4 color to convert.</param>
    /// <returns>The uint representation of the Vector4 color provided.</returns>
    public static uint Vector4ToUint(Vector4 color)
        => Saturate(color.X) | (Saturate(color.Y) << 8) | (Saturate(color.Z) << 16) | (Saturate(color.W) << 24);

    // Clamped and rounded the way ImGui packs a channel.
    private static uint Saturate(float channel)
        => (uint)((channel < 0f ? 0f : channel > 1f ? 1f : channel) * 255f + 0.5f);

    /// <summary>The alpha channel of a packed ImGui color, from 0 to 1.</summary>
    /// <param name="color">The packed color.</param>
    /// <returns>The alpha, from 0 to 1.</returns>
    public static float UintAlpha(uint color) => ((color >> 24) & 0xFFu) / 255f;

    /// <summary>Converts a hexadecimal color string to its equivalent 32-bit unsigned integer representation.</summary>
    /// <param name="hex">Hex color: "#123456", "#1234", "#123" or "#12345678", with or without "#". Missing alpha defaults to 255.</param>
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

    /// <summary>Builds a color from hue, saturation and value.</summary>
    /// <param name="hue">The hue, 0 and 1 both red. It wraps: an increasing hue cycles.</param>
    /// <param name="saturation">The saturation, from 0 (grey) to 1. Clamped.</param>
    /// <param name="value">The brightness, from 0 (black) to 1. Clamped.</param>
    /// <param name="alpha">The alpha.</param>
    /// <returns>The color, RGBA from 0 to 1.</returns>
    public static Vector4 FromHsv(float hue, float saturation, float value, float alpha = 1f)
    {
        var h = (hue - MathF.Floor(hue)) * 6f;
        var s = Math.Clamp(saturation, 0f, 1f);
        var v = Math.Clamp(value, 0f, 1f);
        var sector = (int)h % 6;
        var f = h - MathF.Floor(h);
        var p = v * (1f - s);
        var q = v * (1f - (s * f));
        var t = v * (1f - (s * (1f - f)));

        return sector switch
        {
            0 => new Vector4(v, t, p, alpha),
            1 => new Vector4(q, v, p, alpha),
            2 => new Vector4(p, v, t, alpha),
            3 => new Vector4(p, q, v, alpha),
            4 => new Vector4(t, p, v, alpha),
            _ => new Vector4(v, p, q, alpha),
        };
    }

    /// <summary>Splits a color into hue, saturation and value.</summary>
    /// <param name="color">The color. Its alpha is ignored.</param>
    /// <returns>The hue from 0 (red) up to 1, the saturation and the value from 0 to 1. A grey has hue 0.</returns>
    public static (float Hue, float Saturation, float Value) ToHsv(Vector4 color)
    {
        var max = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        var min = MathF.Min(color.X, MathF.Min(color.Y, color.Z));
        var delta = max - min;

        if (delta <= 0f)
            return (0f, 0f, max);

        float hue;

        if (max == color.X)
            hue = (color.Y - color.Z) / delta;
        else if (max == color.Y)
            hue = 2f + ((color.Z - color.X) / delta);
        else
            hue = 4f + ((color.X - color.Y) / delta);

        hue /= 6f;

        if (hue < 0f)
            hue += 1f;

        return (hue, max <= 0f ? 0f : delta / max, max);
    }

    /// <summary>
    /// Converts a color to OKLab, a space where equal distances look like equal differences and where mixing two colors
    /// does not pass through a muddy or greyed middle.
    /// </summary>
    /// <param name="color">The color in sRGB, each channel from 0 to 1. The alpha is ignored.</param>
    /// <returns>The lightness (0 to 1) and the two color axes.</returns>
    public static Vector3 ToOklab(Vector4 color)
    {
        var r = SrgbToLinear(color.X);
        var g = SrgbToLinear(color.Y);
        var b = SrgbToLinear(color.Z);

        var l = MathF.Cbrt((0.4122214708f * r) + (0.5363325363f * g) + (0.0514459929f * b));
        var m = MathF.Cbrt((0.2119034982f * r) + (0.6806995451f * g) + (0.1073969566f * b));
        var s = MathF.Cbrt((0.0883024619f * r) + (0.2817188376f * g) + (0.6299787005f * b));

        return new Vector3(
            (0.2104542553f * l) + (0.7936177850f * m) - (0.0040720468f * s),
            (1.9779984951f * l) - (2.4285922050f * m) + (0.4505937099f * s),
            (0.0259040371f * l) + (0.7827717662f * m) - (0.8086757660f * s));
    }

    /// <summary>Converts an OKLab color back to sRGB.</summary>
    /// <param name="lab">The lightness and the two color axes, as <see cref="ToOklab"/> returns them.</param>
    /// <param name="alpha">The alpha of the returned color.</param>
    /// <returns>The color in sRGB, each channel clamped between 0 and 1.</returns>
    public static Vector4 FromOklab(Vector3 lab, float alpha = 1f)
    {
        var l = lab.X + (0.3963377774f * lab.Y) + (0.2158037573f * lab.Z);
        var m = lab.X - (0.1055613458f * lab.Y) - (0.0638541728f * lab.Z);
        var s = lab.X - (0.0894841775f * lab.Y) - (1.2914855480f * lab.Z);

        l = l * l * l;
        m = m * m * m;
        s = s * s * s;

        var r = (4.0767416621f * l) - (3.3077115913f * m) + (0.2309699292f * s);
        var g = (-1.2684380046f * l) + (2.6097574011f * m) - (0.3413193965f * s);
        var b = (-0.0041960863f * l) - (0.7034186147f * m) + (1.7076147010f * s);

        return new Vector4(
            Math.Clamp(LinearToSrgb(r), 0f, 1f),
            Math.Clamp(LinearToSrgb(g), 0f, 1f),
            Math.Clamp(LinearToSrgb(b), 0f, 1f),
            alpha);
    }

    /// <summary>Blends two colors together, including their alpha.</summary>
    /// <param name="from">The color returned when <paramref name="amount"/> is 0.</param>
    /// <param name="to">The color returned when <paramref name="amount"/> is 1.</param>
    /// <param name="amount">How far to blend, from 0 to 1. Values outside that range are clamped.</param>
    /// <returns>The blended color.</returns>
    public static Vector4 Mix(Vector4 from, Vector4 to, float amount)
        => Vector4.Lerp(from, to, Math.Clamp(amount, 0f, 1f));

    /// <summary>Moves a color towards white, leaving its alpha untouched.</summary>
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

    /// <summary>Moves a color towards black, leaving its alpha untouched.</summary>
    /// <param name="color">The color to darken.</param>
    /// <param name="amount">How far towards black to move, from 0 (unchanged) to 1 (black).</param>
    /// <returns>The darkened color.</returns>
    public static Vector4 Darken(Vector4 color, float amount)
    {
        var t = 1f - Math.Clamp(amount, 0f, 1f);
        return new Vector4(color.X * t, color.Y * t, color.Z * t, color.W);
    }

    /// <summary>Returns the same color at a different opacity.</summary>
    /// <param name="color">The color to change.</param>
    /// <param name="alpha">The opacity to use, from 0 to 1.</param>
    /// <returns>The color at the given opacity.</returns>
    public static Vector4 WithAlpha(Vector4 color, float alpha)
        => new(color.X, color.Y, color.Z, Math.Clamp(alpha, 0f, 1f));

    /// <summary>Scales a color's opacity. Use it over <see cref="WithAlpha"/> to fade a translucent color.</summary>
    /// <param name="color">The color to fade.</param>
    /// <param name="factor">The multiplier on the alpha.</param>
    /// <returns>The faded color.</returns>
    public static Vector4 ScaleAlpha(Vector4 color, float factor)
        => new(color.X, color.Y, color.Z, Math.Clamp(color.W * factor, 0f, 1f));

    /// <summary>
    /// Gets the perceived brightness of a color, from 0 (black) to 1 (white), using Rec. 709 weighting rather than a flat channel average.
    /// </summary>
    /// <param name="color">The color to measure. Its alpha is ignored.</param>
    /// <returns>The perceived brightness.</returns>
    public static float Luminance(Vector4 color)
        => 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z;

    /// <summary>Whether a color reads as dark, and so wants light text on top of it.</summary>
    /// <param name="color">The color to test. Its alpha is ignored.</param>
    /// <returns>True when the color is dark.</returns>
    public static bool IsDark(Vector4 color) => Luminance(color) < 0.5f;

    /// <summary>Picks whichever of two foreground colors is legible on a background.</summary>
    /// <param name="background">The background the text sits on.</param>
    /// <param name="onDark">The color to use on a dark background. Defaults to near-white.</param>
    /// <param name="onLight">The color to use on a light background. Defaults to near-black.</param>
    /// <returns>The legible foreground color.</returns>
    public static Vector4 Readable(Vector4 background, Vector4? onDark = null, Vector4? onLight = null)
        => IsDark(background)
            ? onDark ?? new Vector4(0.96f, 0.96f, 0.96f, 1f)
            : onLight ?? new Vector4(0.06f, 0.06f, 0.06f, 1f);

    private const int VividTiles = 24;

    /// <summary>The accent color an image reads as, weighted toward its opaque, bright and saturated regions.</summary>
    /// <param name="bgra">The pixels, row by row, in blue, green, red, alpha order.</param>
    /// <param name="width">The image width, in pixels.</param>
    /// <param name="height">The image height, in pixels.</param>
    /// <returns>The color with full alpha, or null when the image has no opaque, bright region.</returns>
    public static Vector4? GetVividColor(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            return null;

        double r = 0, g = 0, b = 0, w = 0;

        for (var ty = 0; ty < VividTiles; ty++)
        {
            var y0 = ty * height / VividTiles;
            var y1 = Math.Max(y0 + 1, (ty + 1) * height / VividTiles);

            for (var tx = 0; tx < VividTiles; tx++)
            {
                var x0 = tx * width / VividTiles;
                var x1 = Math.Max(x0 + 1, (tx + 1) * width / VividTiles);

                double pr = 0, pg = 0, pb = 0, pa = 0;
                var n = 0;

                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var i = (y * width + x) * 4;
                        var a = bgra[i + 3];
                        pb += bgra[i] * a;
                        pg += bgra[i + 1] * a;
                        pr += bgra[i + 2] * a;
                        pa += a;
                        n++;
                    }
                }

                if (n == 0 || pa <= 0)
                    continue;

                var tileAlpha = Math.Round(pa / n);
                var tileRed = Math.Round(pr / pa);
                var tileGreen = Math.Round(pg / pa);
                var tileBlue = Math.Round(pb / pa);

                if (tileAlpha < 200)
                    continue;

                var max = Math.Max(tileRed, Math.Max(tileGreen, tileBlue));
                var min = Math.Min(tileRed, Math.Min(tileGreen, tileBlue));
                var saturation = max > 0 ? (max - min) / max : 0;
                var lightness = max / 255.0;

                if (lightness < 0.18)
                    continue;

                var weight = Math.Pow(saturation, 2.2) * lightness + 0.002;
                r += tileRed * weight;
                g += tileGreen * weight;
                b += tileBlue * weight;
                w += weight;
            }
        }

        if (w <= 0)
            return null;

        var red = r / w;
        var green = g / w;
        var blue = b / w;
        var top = Math.Max(red, Math.Max(green, blue));

        if (top > 0)
        {
            var gain = Math.Min(1.6, 235.0 / top);
            red = Math.Min(255, red * gain);
            green = Math.Min(255, green * gain);
            blue = Math.Min(255, blue * gain);
        }

        var grey = (red + green + blue) / 3.0;
        red = Math.Clamp(grey + (red - grey) * 1.25, 0, 255);
        green = Math.Clamp(grey + (green - grey) * 1.25, 0, 255);
        blue = Math.Clamp(grey + (blue - grey) * 1.25, 0, 255);

        return new Vector4((float)(red / 255.0), (float)(green / 255.0), (float)(blue / 255.0), 1f);
    }
}
