using Dalamud.Interface;
using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the textured icon library: one icon kind in force on a button style, carried by Clone and CopyFrom, the
/// built-in marks embedded under the names the registry resolves, and icon buttons that allocate nothing on either the
/// glyph or the texture path.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireIconLibraryTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private static readonly ButtonStyle Branded = new() { NoireIcon = NoireIcon.Kofi, IconSize = 19f };

    private static readonly ButtonStyle Named = new() { IconName = "NoireLib.Tests.Icon" };

    private readonly UiHarness harness;

    public NoireIconLibraryTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void ButtonStyle_KeepsOneIconKindInForce()
    {
        var style = new ButtonStyle { Icon = FontAwesomeIcon.Save };

        style.NoireIcon = NoireIcon.Discord;
        style.Icon.Should().BeNull();

        style.IconName = "custom";
        style.NoireIcon.Should().BeNull();

        style.Icon = FontAwesomeIcon.Heart;
        style.IconName.Should().BeNull();
    }

    [Fact]
    public void ButtonStyle_CloneAndCopyFrom_CarryTheTexturedIcon()
    {
        var source = new ButtonStyle { NoireIcon = NoireIcon.Kofi, IconSize = 19f };
        var target = new ButtonStyle { Icon = FontAwesomeIcon.Save };

        target.CopyFrom(source);
        var clone = source.Clone();

        target.NoireIcon.Should().Be(NoireIcon.Kofi);
        target.Icon.Should().BeNull();
        target.IconSize.Should().Be(19f);
        clone.NoireIcon.Should().Be(NoireIcon.Kofi);
        clone.IconSize.Should().Be(19f);
    }

    [Fact]
    public void BuiltInMarks_AreEmbeddedAtEverySize()
    {
        var assembly = typeof(NoireIcons).Assembly;

        foreach (var stem in new[] { "discord", "kofi" })
        {
            foreach (var size in new[] { 20, 24, 32, 40, 48, 64, 128 })
                assembly.GetManifestResourceInfo($"NoireLib.UI.Icons.{stem}_{size}.png").Should().NotBeNull();
        }
    }

    [Fact]
    public void ManifestResource_MissingName_ResolvesToNothing()
    {
        var source = UiImageSource.FromManifestResource(typeof(NoireIcons).Assembly, "NoireLib.UI.Icons.missing.png");

        source.GetWrap().Should().BeNull();
        source.GetNativeSize().Should().BeNull();
    }

    [Fact]
    public void Registry_RegistersAndUnregisters()
    {
        var source = UiImageSource.FromManifestResource(typeof(NoireIcons).Assembly, "NoireLib.UI.Icons.kofi_20.png");

        NoireIcons.Register("NoireLib.Tests.Registry", source);

        NoireIcons.IsRegistered("NoireLib.Tests.Registry").Should().BeTrue();
        NoireIcons.Source("NoireLib.Tests.Registry").Should().BeSameAs(source);
        NoireIcons.Unregister("NoireLib.Tests.Registry").Should().BeTrue();
        NoireIcons.IsRegistered("NoireLib.Tests.Registry").Should().BeFalse();
    }

    [Fact]
    public void Source_PicksTheSmallestRasterThatCoversTheSize()
    {
        NoireIcons.Source(NoireIcon.Discord, 19f).Should().BeSameAs(NoireIcons.Source(NoireIcon.Discord, 20f));
        NoireIcons.Source(NoireIcon.Discord, 38f).Should().BeSameAs(NoireIcons.Source(NoireIcon.Discord, 40f));
        NoireIcons.Source(NoireIcon.Discord, 500f).Should().BeSameAs(NoireIcons.Source(NoireIcon.Discord, 128f));
    }

    [Fact]
    public void BrandIconButton_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireButtons.Button("Support me on Ko-fi##alloc_brand", Branded);
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void RegisteredIconButton_AllocatesNothing()
    {
        NoireIcons.Register("NoireLib.Tests.Icon", UiImageSource.FromManifestResource(typeof(NoireIcons).Assembly, "NoireLib.UI.Icons.discord_20.png"));

        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireButtons.Button("Discord##alloc_named", Named, new Vector2(120f, 0f));
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }
}
