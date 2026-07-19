using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks <see cref="NoireUiSession"/>: the widget memory that lasts exactly as long as the plugin does.
/// <br/>
/// The two properties worth being strict about are the ones that follow from there being no file. Any type may be
/// stored, including ones that do not serialize, and a value stored under a key as one type must read as absent rather
/// than throw when another widget asks for it as something else.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireUiSessionTests : IDisposable
{
    public NoireUiSessionTests() => NoireUiSession.Clear();

    public void Dispose() => NoireUiSession.Clear();

    private sealed class NotSerializable
    {
        public Action? Callback { get; init; }
    }

    #region Storing and reading

    [Fact]
    public void Set_ThenGet_ReturnsWhatWasStored()
    {
        NoireUiSession.Set("a.b", "value");

        NoireUiSession.Get<string>("a.b").Should().Be("value");
    }

    [Fact]
    public void Get_ReturnsTheFallback_WhenNothingIsStored()
    {
        NoireUiSession.Get("missing", 42).Should().Be(42);
    }

    [Fact]
    public void TryGet_ReportsAbsence_RatherThanThrowing_WhenTheTypeDisagrees()
    {
        NoireUiSession.Set("a.b", "text");

        NoireUiSession.TryGet<int>("a.b", out var value).Should().BeFalse(
            "because one widget's mistake about a key must not take another widget down");
        value.Should().Be(0);
    }

    [Fact]
    public void Set_HoldsTypesThatDoNotSerialize()
    {
        var held = new NotSerializable { Callback = () => { } };

        NoireUiSession.Set("a.b", held);

        NoireUiSession.Get<NotSerializable>("a.b").Should().BeSameAs(held,
            "because nothing is round-tripped through JSON, so a reference type comes back as the same instance");
    }

    [Fact]
    public void Set_HoldsStructs()
    {
        NoireUiSession.Set("a.b", new Vector2(3f, 4f));

        NoireUiSession.Get<Vector2>("a.b").Should().Be(new Vector2(3f, 4f));
    }

    [Fact]
    public void TryGet_TellsNothingApartFromNever_WhenNullIsStoredDeliberately()
    {
        NoireUiSession.Set<string?>("a.b", null);

        NoireUiSession.TryGet<string>("a.b", out var value).Should().BeTrue(
            "because remembering 'nothing' has to be distinguishable from never having been set");
        value.Should().BeNull();

        NoireUiSession.TryGet<string>("never.set", out _).Should().BeFalse();
    }

    [Fact]
    public void Set_OverwritesAnExistingEntry()
    {
        NoireUiSession.Set("a.b", 1);
        NoireUiSession.Set("a.b", 2);

        NoireUiSession.Get<int>("a.b").Should().Be(2);
        NoireUiSession.Count.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Set_RefusesABlankKey(string key)
    {
        var act = () => NoireUiSession.Set(key, 1);

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Forgetting

    [Fact]
    public void Remove_ForgetsOneEntry()
    {
        NoireUiSession.Set("a.b", 1);

        NoireUiSession.Remove("a.b").Should().BeTrue();
        NoireUiSession.Remove("a.b").Should().BeFalse();
        NoireUiSession.Count.Should().Be(0);
    }

    [Fact]
    public void RemoveAll_ForgetsAWholePrefix()
    {
        NoireUiSession.Set("window.roster.search", "a");
        NoireUiSession.Set("window.roster.tab", 2);
        NoireUiSession.Set("window.other.search", "b");

        NoireUiSession.RemoveAll("window.roster.").Should().Be(2);

        NoireUiSession.Count.Should().Be(1);
        NoireUiSession.Get<string>("window.other.search").Should().Be("b");
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        NoireUiSession.Set("a", 1);
        NoireUiSession.Set("b", 2);

        NoireUiSession.Clear();

        NoireUiSession.Count.Should().Be(0);
        NoireUiSession.GetKeys().Should().BeEmpty();
    }

    #endregion
}
