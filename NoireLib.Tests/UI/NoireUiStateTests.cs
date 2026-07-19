using FluentAssertions;
using NoireLib.UI;
using System;
using System.IO;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the persisted widget memory: the in-memory contract, the round trip through the file, and the two behaviours
/// that would otherwise fail silently (a value of the wrong shape reading as absent, and an unchanged write not
/// dirtying the file).
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireUiStateTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"NoireUiStateTests_{Guid.NewGuid():N}.json");

    public NoireUiStateTests()
    {
        NoireUiState.FilePath = path;
        NoireUiState.Clear();
        NoireUiState.Save();
    }

    public void Dispose()
    {
        NoireUiState.FilePath = null;
        NoireUiState.Reload();

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private sealed class Layout
    {
        public string Sort { get; set; } = string.Empty;

        public bool Collapsed { get; set; }
    }

    [Fact]
    public void Get_WithNoEntry_ReturnsTheFallback()
    {
        NoireUiState.Get("missing", 42).Should().Be(42);
        NoireUiState.TryGet<int>("missing", out _).Should().BeFalse();
    }

    [Fact]
    public void SetThenGet_RoundTripsAValue()
    {
        NoireUiState.Set("overlay.main.position", new Vector2(120f, 40f));

        NoireUiState.Get<Vector2>("overlay.main.position").Should().Be(new Vector2(120f, 40f));
    }

    [Fact]
    public void SetThenGet_RoundTripsAnObject()
    {
        NoireUiState.Set("window.rows", new Layout { Sort = "name", Collapsed = true });

        var stored = NoireUiState.Get<Layout>("window.rows");

        stored!.Sort.Should().Be("name");
        stored.Collapsed.Should().BeTrue();
    }

    [Fact]
    public void TryGet_OfAValueOfTheWrongShape_ReadsAsAbsentRatherThanThrowing()
    {
        NoireUiState.Set("window.rows", 17);

        NoireUiState.TryGet<Layout>("window.rows", out _)
            .Should().BeFalse("the file is editable by hand, and one bad entry must not take a window down");
    }

    [Fact]
    public void Set_WithAnUnchangedValue_DoesNotDirtyTheFile()
    {
        NoireUiState.Set("window.rows", 3);
        NoireUiState.Save();
        NoireUiState.HasUnsavedChanges.Should().BeFalse();

        NoireUiState.Set("window.rows", 3);

        NoireUiState.HasUnsavedChanges.Should().BeFalse("a widget writing the same value every frame must not write the disk every frame");
    }

    [Fact]
    public void Set_WithABlankKey_Throws()
    {
        var act = () => NoireUiState.Set("  ", 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Remove_DropsTheEntry()
    {
        NoireUiState.Set("a", 1);

        NoireUiState.Remove("a").Should().BeTrue();
        NoireUiState.TryGet<int>("a", out _).Should().BeFalse();
        NoireUiState.Remove("a").Should().BeFalse();
    }

    [Fact]
    public void RemoveAll_DropsOnlyTheMatchingPrefix()
    {
        NoireUiState.Set("overlay.main.position", 1);
        NoireUiState.Set("overlay.main.size", 2);
        NoireUiState.Set("window.rows", 3);

        NoireUiState.RemoveAll("overlay.main.").Should().Be(2);

        NoireUiState.Count.Should().Be(1);
        NoireUiState.Get<int>("window.rows").Should().Be(3);
    }

    [Fact]
    public void SaveThenReload_ReadsTheValuesBackFromTheFile()
    {
        NoireUiState.Set("overlay.main.position", new Vector2(10f, 20f));
        NoireUiState.Set("window.rows", new Layout { Sort = "date", Collapsed = true });
        NoireUiState.Save();

        File.Exists(path).Should().BeTrue();

        NoireUiState.Reload();
        NoireUiState.IsLoaded.Should().BeFalse("reloading defers the read until something asks for a value");

        NoireUiState.Get<Vector2>("overlay.main.position").Should().Be(new Vector2(10f, 20f));
        NoireUiState.Get<Layout>("window.rows")!.Sort.Should().Be("date");
    }

    [Fact]
    public void Save_WithNothingPending_DoesNothing()
    {
        NoireUiState.Set("a", 1);
        NoireUiState.Save();

        var writtenAt = File.GetLastWriteTimeUtc(path);

        NoireUiState.Save();

        File.GetLastWriteTimeUtc(path).Should().Be(writtenAt);
    }

    [Fact]
    public void Clear_EmptiesTheStoreAndSurvivesASave()
    {
        NoireUiState.Set("a", 1);
        NoireUiState.Save();

        NoireUiState.Clear();
        NoireUiState.Save();
        NoireUiState.Reload();

        NoireUiState.Count.Should().Be(0);
    }
}
