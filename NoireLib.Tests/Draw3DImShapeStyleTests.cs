using FluentAssertions;
using NoireLib.Draw3D.Im;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks <see cref="ImShapeStyle"/>'s defaults against the struct-default trap: a property initializer only runs for
/// <c>new()</c>, never for <c>default</c>, and every <c>ImDraw3D.Draw*</c> overload turns an omitted style into
/// <c>default</c>. When the defaults lived in initializers, a marker drawn without a style silently got
/// <c>FillOpacity = 0</c> and <c>OutlineWidth = 0</c> and painted nothing at all, and a flat curve got
/// <c>Segments = 0</c> and collapsed to a triangle. Both spellings must agree.
/// </summary>
public class Draw3DImShapeStyleTests
{
    [Fact]
    public void DefaultAndNew_AreEquivalent()
    {
        ImShapeStyle zeroed = default;
        var constructed = new ImShapeStyle();

        zeroed.Should().Be(constructed);
    }

    /// <summary>The four defaults that are not the zero value: zero fill or zero outline means a decal paints nothing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonZeroDefaults_SurviveBothSpellings(bool useDefaultKeyword)
    {
        var style = useDefaultKeyword ? default : new ImShapeStyle();

        style.OutlineWidth.Should().Be(0.08f);
        style.FillOpacity.Should().Be(0.6f);
        style.DecalHeight.Should().Be(4f);
        style.Segments.Should().Be(64);
    }

    /// <summary>The zero-valued defaults, so a future initializer cannot quietly change them.</summary>
    [Fact]
    public void ZeroValuedDefaults_AreZero()
    {
        var style = new ImShapeStyle();

        style.Placement.Should().Be(ImShapePlacement.Grounded);
        style.DepthFade.Should().Be(0f);
        style.Layer.Should().Be(0);
        style.Additive.Should().BeFalse();
        style.IgnoreDepth.Should().BeFalse();
        style.OnTopOfObjects.Should().BeFalse();
        style.ExcludeVolumes.Should().BeNull();
    }

    /// <summary>An explicit 0 must stay 0 - defaulting on read must not swallow a caller turning the outline or fill off.</summary>
    [Fact]
    public void ExplicitZero_IsNotTreatedAsUnset()
    {
        var style = new ImShapeStyle { OutlineWidth = 0f, FillOpacity = 0f, DecalHeight = 0f, Segments = 0 };

        style.OutlineWidth.Should().Be(0f);
        style.FillOpacity.Should().Be(0f);
        style.DecalHeight.Should().Be(0f);
        style.Segments.Should().Be(0);
    }

    /// <summary>`with` must carry the untouched defaults through, not reset them to zero.</summary>
    [Fact]
    public void With_KeepsUntouchedDefaults()
    {
        ImShapeStyle style = default;

        var tweaked = style with { Layer = 3 };

        tweaked.Layer.Should().Be(3);
        tweaked.OutlineWidth.Should().Be(0.08f);
        tweaked.FillOpacity.Should().Be(0.6f);
        tweaked.DecalHeight.Should().Be(4f);
        tweaked.Segments.Should().Be(64);
    }
}
