using FluentAssertions;
using NoireLib.Helpers;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace NoireLib.Tests;

public class FileDialogHelperTests
{
    [Fact]
    public void BuildWindowsFilter_ShouldConvertDalamudStyleExtensions()
    {
        var filter = InvokePrivate<string>("BuildWindowsFilter", "Images{.png,.jpg},.txt");

        filter.Should().Be("Images (*.png, *.jpg)|*.png;*.jpg|TXT files (*.txt)|*.txt|All files (*.*)|*.*");
    }

    [Fact]
    public void BuildDefaultFileName_ShouldAppendExtensionWhenMissing()
    {
        var fileName = InvokePrivate<string>("BuildDefaultFileName", "export", ".json");

        fileName.Should().Be("export.json");
    }

    [Fact]
    public void ParsePaths_ShouldSplitMultipleSeparators_AndRemoveDuplicates()
    {
        var paths = InvokePrivate<List<string>>("ParsePaths", "a.txt\r\nb.txt|a.txt\n");

        paths.Should().Equal("a.txt", "b.txt");
    }

    private static T InvokePrivate<T>(string methodName, params object?[] arguments)
    {
        var method = typeof(FileDialogHelper).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (T)method!.Invoke(null, arguments)!;
    }
}
