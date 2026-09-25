using FluentAssertions;
using NoireLib.Helpers;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks the color operations: lightening keeps alpha, fading scales it, and an image's accent skips dark and transparent regions.</summary>
public class ColorHelperOperationsTests
{
    private static readonly Vector4 HalfTransparentBlue = new(0.2f, 0.4f, 0.8f, 0.5f);

    [Fact]
    public void Lighten_MovesTowardsWhite_AndLeavesAlphaAlone()
    {
        var result = ColorHelper.Lighten(HalfTransparentBlue, 0.5f);

        result.X.Should().BeGreaterThan(HalfTransparentBlue.X);
        result.Y.Should().BeGreaterThan(HalfTransparentBlue.Y);
        result.Z.Should().BeGreaterThan(HalfTransparentBlue.Z);
        result.W.Should().Be(0.5f, "because lightening a color must not quietly make it more opaque");
    }

    [Fact]
    public void Darken_MovesTowardsBlack_AndLeavesAlphaAlone()
    {
        var result = ColorHelper.Darken(HalfTransparentBlue, 0.5f);

        result.X.Should().BeLessThan(HalfTransparentBlue.X);
        result.W.Should().Be(0.5f);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(2f)]
    public void LightenAndDarken_ClampTheirAmount(float amount)
    {
        var lightened = ColorHelper.Lighten(HalfTransparentBlue, amount);
        var darkened = ColorHelper.Darken(HalfTransparentBlue, amount);

        lightened.X.Should().BeInRange(0f, 1f);
        darkened.X.Should().BeInRange(0f, 1f);
    }

    [Fact]
    public void Lighten_ByOne_ReachesWhite()
    {
        var result = ColorHelper.Lighten(new Vector4(0f, 0f, 0f, 1f), 1f);

        result.Should().Be(new Vector4(1f, 1f, 1f, 1f));
    }

    [Fact]
    public void Mix_InterpolatesEverythingIncludingAlpha()
    {
        var result = ColorHelper.Mix(new Vector4(0f, 0f, 0f, 0f), new Vector4(1f, 1f, 1f, 1f), 0.25f);

        result.Should().Be(new Vector4(0.25f, 0.25f, 0.25f, 0.25f));
    }

    [Fact]
    public void ScaleAlpha_FadesRelativeToWhatIsAlreadyThere()
    {
        var result = ColorHelper.ScaleAlpha(HalfTransparentBlue, 0.5f);

        result.W.Should().BeApproximately(0.25f, 0.0001f, "because fading a translucent color must not make it more opaque");
    }

    [Fact]
    public void WithAlpha_ReplacesTheAlpha()
    {
        ColorHelper.WithAlpha(HalfTransparentBlue, 1f).W.Should().Be(1f);
    }

    [Fact]
    public void Luminance_WeightsGreenAboveBlue()
    {
        var green = ColorHelper.Luminance(new Vector4(0f, 1f, 0f, 1f));
        var blue = ColorHelper.Luminance(new Vector4(0f, 0f, 1f, 1f));

        green.Should().BeGreaterThan(blue, "because perceived brightness is not the average of the channels");
    }

    [Theory]
    [InlineData(0f, 0f, 0f, true)]
    [InlineData(1f, 1f, 1f, false)]
    public void IsDark_TracksPerceivedBrightness(float r, float g, float b, bool expected)
    {
        ColorHelper.IsDark(new Vector4(r, g, b, 1f)).Should().Be(expected);
    }

    [Fact]
    public void Readable_PicksTheLegibleForeground()
    {
        ColorHelper.Luminance(ColorHelper.Readable(new Vector4(0f, 0f, 0f, 1f))).Should().BeGreaterThan(0.5f);
        ColorHelper.Luminance(ColorHelper.Readable(new Vector4(1f, 1f, 1f, 1f))).Should().BeLessThan(0.5f);
    }

    [Fact]
    public void RgbToVector4_ReadsRedFromTheHighByte()
        => ColorHelper.RgbToVector4(0xff8000, 0.5f).Should().Be(new Vector4(1f, 128f / 255f, 0f, 0.5f));

    [Fact]
    public void GetVividColor_OfASaturatedImage_KeepsItsHue()
    {
        var color = ColorHelper.GetVividColor(Image(width: 32, height: 32, blue: 30, green: 40, red: 220, alpha: 255), 32, 32);

        color.Should().NotBeNull();
        color!.Value.X.Should().BeGreaterThan(color.Value.Y);
        color.Value.X.Should().BeGreaterThan(color.Value.Z);
        color.Value.W.Should().Be(1f);
    }

    [Fact]
    public void GetVividColor_OfATransparentOrDarkImage_IsNull()
    {
        ColorHelper.GetVividColor(Image(32, 32, 200, 200, 200, 0), 32, 32).Should().BeNull("because nothing in it is opaque");
        ColorHelper.GetVividColor(Image(32, 32, 10, 10, 10, 255), 32, 32).Should().BeNull("because nothing in it is bright");
    }

    [Fact]
    public void GetVividColor_OfATruncatedBuffer_IsNull()
        => ColorHelper.GetVividColor(new byte[16], 32, 32).Should().BeNull();

    private static byte[] Image(int width, int height, byte blue, byte green, byte red, byte alpha)
    {
        var pixels = new byte[width * height * 4];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = alpha;
        }

        return pixels;
    }
}
