using FluentAssertions;
using NoireLib.Configuration;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks which characters are merged into every face, and that fonts rebuild only when a loaded text brings a new one.
/// </summary>
[Collection(LocalizerStateCollection.Name)]
public sealed class NoireScriptFontsTests : IDisposable
{
    private readonly List<NoireLocalizer> localizers = [];

    public NoireScriptFontsTests() => ResetPersistedConfiguration();

    public void Dispose()
    {
        foreach (var localizer in localizers)
            localizer.Dispose();

        ResetPersistedConfiguration();
    }

    [Fact]
    public void RangesOf_KeepsOnlyTheCjkCharacters_InRuns()
    {
        var ranges = NoireScriptFonts.RangesOf(["Bypass 中文", "日本語 é", "中中"]);

        ranges.Should().Equal((ushort)'中', (ushort)'中', (ushort)'文', (ushort)'文', (ushort)'日', (ushort)'日',
            (ushort)'本', (ushort)'本', (ushort)'語', (ushort)'語', 0);
    }

    [Fact]
    public void RangesOf_JoinsConsecutiveCharacters()
        => NoireScriptFonts.RangesOf(["ぁあぃ"]).Should().Equal((ushort)'ぁ', (ushort)'ぃ', 0);

    [Fact]
    public void RangesOf_LatinOnly_IsNull()
        => NoireScriptFonts.RangesOf(["English", "Français", "[Ƥşḗŭḓǿ]"]).Should().BeNull();

    [Fact]
    public void Generation_StaysWhenTheLanguageChanges_AndMovesWhenATextBringsANewCharacter()
    {
        var localizer = new NoireLocalizer(active: true, enableLogging: false, defaultLocale: "en-US");
        localizers.Add(localizer);
        localizer.AddTranslation("ja", "test.hello", "こんにちは");

        var loaded = NoireScriptFonts.Generation;
        NoireScriptFonts.Ranges.Should().NotBeNull();

        localizer.SetCurrentLocale("ja");
        localizer.SetCurrentLocale("fr");
        localizer.SetCurrentLocale("en");
        NoireScriptFonts.Generation.Should().Be(loaded, "every loaded language is merged up front");

        localizer.AddTranslation("ja", "test.hello", "こんにちは!");
        NoireScriptFonts.Generation.Should().Be(loaded, "no new character");

        localizer.AddTranslation("ja", "test.bye", "さようなら");
        NoireScriptFonts.Generation.Should().NotBe(loaded);
    }

    private static void ResetPersistedConfiguration()
    {
        NoireConfigManager.UnloadConfig<LocalizerConfigInstance>();
        var config = new LocalizerConfigInstance();
        NoireConfigManager.AddConfigToCache(typeof(LocalizerConfigInstance), config);
        config.SelectedLocale = null;
        config.DefaultLocaleSource = DefaultLocaleSource.Custom;
        config.CustomDefaultLocale = "en-US";
        config.HasCustomDefaultLocaleSelection = false;
    }
}
