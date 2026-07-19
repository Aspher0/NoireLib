using FluentAssertions;
using NoireLib.Helpers;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the theme's resolution rules: an unset color falls through rather than forcing a default, a generated palette
/// stays legible, derivation follows the surface rather than a fixed direction, and a theme survives a share-code round
/// trip.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireThemeTests
{
    private static readonly Vector4 Gold = new(0.784f, 0.663f, 0.416f, 1f);

    [Fact]
    public void Resolve_ReturnsTheShippedDefault_WhenNothingIsSet()
    {
        var theme = new NoireTheme();

        theme.Accent.Should().BeNull("because an unset token has to stay unset so it can fall through");
        theme.Resolve(ThemeColor.Accent).Should().NotBe(default(Vector4), "because resolving always produces a usable color");
    }

    [Fact]
    public void Resolve_PrefersTheExplicitValue()
    {
        var theme = new NoireTheme { Accent = Gold };

        theme.Resolve(ThemeColor.Accent).Should().Be(Gold);
    }

    [Fact]
    public void SettingAColorToNull_RemovesTheOverride()
    {
        var theme = new NoireTheme { Accent = Gold };

        theme.Accent = null;

        theme.Colors.Should().NotContainKey(ThemeColor.Accent, "because a null assignment means 'fall through', not 'transparent'");
    }

    [Fact]
    public void FromAccent_BuildsALegiblePalette()
    {
        var theme = NoireTheme.FromAccent(Gold);

        theme.Resolve(ThemeColor.Accent).Should().Be(Gold);
        theme.IsDark.Should().BeTrue("because a dark palette was asked for");

        var surface = theme.Resolve(ThemeColor.Surface);
        var text = theme.Resolve(ThemeColor.Text);

        var contrast = ColorHelper.Luminance(text) - ColorHelper.Luminance(surface);
        contrast.Should().BeGreaterThan(0.4f, "because generated text has to stay readable on its own generated surface");
    }

    [Fact]
    public void FromAccent_Light_PicksDarkText()
    {
        var theme = NoireTheme.FromAccent(Gold, dark: false);

        theme.IsDark.Should().BeFalse();
        ColorHelper.Luminance(theme.Resolve(ThemeColor.Text)).Should().BeLessThan(0.5f);
    }

    [Fact]
    public void Hover_PerSurface_FollowsTheThemeRatherThanTheColor()
    {
        var dark = NoireTheme.FromAccent(Gold);
        var light = NoireTheme.FromAccent(Gold, dark: false);

        dark.TintSource = ThemeTintSource.Surface;
        light.TintSource = ThemeTintSource.Surface;

        var baseColor = new Vector4(0.5f, 0.5f, 0.5f, 1f);

        ColorHelper.Luminance(dark.Hover(baseColor)).Should().BeGreaterThan(ColorHelper.Luminance(baseColor));
        ColorHelper.Luminance(light.Hover(baseColor)).Should().BeLessThan(ColorHelper.Luminance(baseColor));
    }

    [Fact]
    public void Hover_PerItem_MovesEveryColorAwayFromItself()
    {
        var theme = NoireTheme.FromAccent(Gold);
        theme.TintSource.Should().Be(ThemeTintSource.Item, "because deriving per colour is the default");

        var darkButton = new Vector4(0.14f, 0.14f, 0.16f, 1f);
        var paleAccent = new Vector4(0.86f, 0.80f, 0.62f, 1f);

        ColorHelper.Luminance(theme.Hover(darkButton)).Should()
            .BeGreaterThan(ColorHelper.Luminance(darkButton), "because a dark button has nowhere to go but brighter");

        ColorHelper.Luminance(theme.Hover(paleAccent)).Should()
            .BeLessThan(ColorHelper.Luminance(paleAccent), "because brightening an already pale colour washes it out instead of reading as a hover");
    }

    [Theory]
    [InlineData(ThemeTintSource.Lighten, true)]
    [InlineData(ThemeTintSource.Darken, false)]
    public void Hover_ForcedDirection_IgnoresBothTheColorAndTheSurface(ThemeTintSource source, bool expectBrighter)
    {
        var theme = NoireTheme.FromAccent(Gold);
        theme.TintSource = source;

        var dark = new Vector4(0.1f, 0.1f, 0.1f, 1f);
        var light = new Vector4(0.9f, 0.9f, 0.9f, 1f);

        foreach (var color in new[] { dark, light })
        {
            var moved = ColorHelper.Luminance(theme.Hover(color));
            var original = ColorHelper.Luminance(color);

            if (expectBrighter)
                moved.Should().BeGreaterThanOrEqualTo(original);
            else
                moved.Should().BeLessThanOrEqualTo(original);
        }
    }

    [Fact]
    public void ShareCode_RoundTripsTheTintSource()
    {
        var theme = NoireTheme.FromAccent(Gold);
        theme.TintSource = ThemeTintSource.Darken;

        var decoded = NoireTheme.FromShareCode(theme.ToShareCode());

        decoded.Success.Should().BeTrue(decoded.Message);
        decoded.Value!.TintSource.Should().Be(ThemeTintSource.Darken);
    }

    [Fact]
    public void Active_MovesFurtherThanHover_InTheSameDirection()
    {
        var theme = NoireTheme.FromAccent(Gold);
        var baseColor = new Vector4(0.4f, 0.4f, 0.4f, 1f);

        var hover = ColorHelper.Luminance(theme.Hover(baseColor));
        var active = ColorHelper.Luminance(theme.Active(baseColor));

        active.Should().BeGreaterThan(hover, "because pressing should read as a further step, not a different state");
    }

    [Fact]
    public void Muted_FadesWithoutChangingTheHue()
    {
        var theme = new NoireTheme { MutedAlpha = 0.5f };
        var muted = theme.Muted(Gold);

        muted.X.Should().Be(Gold.X);
        muted.Y.Should().Be(Gold.Y);
        muted.Z.Should().Be(Gold.Z);
        muted.W.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void On_PicksAForegroundLegibleAgainstTheFill()
    {
        var theme = NoireTheme.FromAccent(Gold);

        var onWhite = theme.On(new Vector4(1f, 1f, 1f, 1f));
        var onBlack = theme.On(new Vector4(0f, 0f, 0f, 1f));

        ColorHelper.Luminance(onWhite).Should().BeLessThan(0.5f);
        ColorHelper.Luminance(onBlack).Should().BeGreaterThan(0.5f);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var theme = NoireTheme.FromAccent(Gold);
        var clone = theme.Clone();

        clone.Accent = new Vector4(1f, 0f, 0f, 1f);
        clone.CustomColors["stripe"] = Gold;

        theme.Resolve(ThemeColor.Accent).Should().Be(Gold);
        theme.CustomColors.Should().NotContainKey("stripe");
    }

    [Fact]
    public void ShareCode_RoundTripsThePalette()
    {
        var theme = NoireTheme.FromAccent(Gold);
        theme.Rounding = 0f;
        theme.HoverShift = 0.3f;
        theme.CustomColors["deco.hairline"] = new Vector4(0.5f, 0.4f, 0.2f, 0.8f);

        var decoded = NoireTheme.FromShareCode(theme.ToShareCode());

        decoded.Success.Should().BeTrue(decoded.Message);
        decoded.Value.Should().NotBeNull();
        decoded.Value!.Rounding.Should().Be(0f, "because zero rounding is a real choice, not an absent one");
        decoded.Value.HoverShift.Should().BeApproximately(0.3f, 0.0001f);
        var accent = decoded.Value.Resolve(ThemeColor.Accent);
        accent.X.Should().BeApproximately(Gold.X, 0.01f, "because the palette travels as HEX, which rounds to 8 bits per channel");
        accent.Y.Should().BeApproximately(Gold.Y, 0.01f);
        accent.Z.Should().BeApproximately(Gold.Z, 0.01f);
        decoded.Value.CustomColors.Should().ContainKey("deco.hairline");
    }

    [Fact]
    public void ShareCode_RefusesACodeOfAnotherKind()
    {
        var other = ShareCodeHelper.Encode("noire.preset", new { Name = "not a theme" });

        var decoded = NoireTheme.FromShareCode(other);

        decoded.Success.Should().BeFalse();
        decoded.Error.Should().Be(ShareCodeError.WrongKind);
    }

    [Fact]
    public void ShareCode_RefusesJunk()
    {
        var decoded = NoireTheme.FromShareCode("this is not a share code");

        decoded.Success.Should().BeFalse();
        decoded.Message.Should().NotBeEmpty("because the message is what the user is shown");
    }

    [Fact]
    public void Snapshot_SkipsAColorNameThisVersionDoesNotKnow()
    {
        var snapshot = new ThemeSnapshot();
        snapshot.Colors["Accent"] = "#C8A96A";
        snapshot.Colors["SomethingFromTheFuture"] = "#112233";
        snapshot.Colors["Danger"] = "not a color";

        var theme = snapshot.ToTheme();

        theme.Colors.Should().ContainKey(ThemeColor.Accent);
        theme.Colors.Should().NotContainKey(ThemeColor.Danger, "because a value that does not parse is skipped rather than failing the whole theme");
        theme.Colors.Should().HaveCount(1);
    }
}
