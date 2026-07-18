using FluentAssertions;
using NoireLib.UI;
using System;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for the non-drawing contract of <see cref="NoireContent"/>: emptiness, fluent chaining, the implicit
/// conversion from a string, and the argument guards on the segment builders. Rendering itself needs an ImGui context and
/// is exercised in the demo plugin, not here.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireContentTests
{
    [Fact]
    public void NewContentIsEmpty()
    {
        new NoireContent().IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AddingASegmentMakesItNonEmpty()
    {
        new NoireContent().AddText("hello").IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void BuildersReturnTheSameInstanceForChaining()
    {
        var content = new NoireContent();

        content.AddText("a").Should().BeSameAs(content);
        content.AddKeyCap("Ctrl").Should().BeSameAs(content);
        content.AddText(() => "live").Should().BeSameAs(content);
    }

    [Fact]
    public void ImplicitStringConversionYieldsNonEmptyContent()
    {
        NoireContent content = "converted";
        content.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void DynamicTextRejectsANullProvider()
    {
        var act = () => new NoireContent().AddText((Func<string>)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CustomSegmentRejectsANullAction()
    {
        var act = () => new NoireContent().AddCustom(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ImageSegmentRejectsANullSource()
    {
        var act = () => new NoireContent().AddImage((UiImageSource)null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
