using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FluentAssertions;
using NoireLib.Changelog;
using NoireLib.Configuration;
using NoireLib.Helpers;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Changelog texts are declared under version and text keys, and shown copies follow the active language.</summary>
[Collection(LocalizerStateCollection.Name)]
public sealed class ChangelogTextsTests : IDisposable
{
    private readonly List<NoireLocalizer> localizers = [];

    public ChangelogTextsTests() => ResetPersistedConfiguration();

    public void Dispose()
    {
        foreach (var localizer in localizers)
            localizer.Dispose();

        ResetPersistedConfiguration();
    }

    [Fact]
    public void Localize_DeclaresEveryText_UnderItsVersionAndItsChecksum()
    {
        new ChangelogTexts().Localize(MakeVersion());

        var keys = NoireLanguages.Strings.Select(text => text.Key).ToArray();

        keys.Should().Contain([KeyOf("What's new"), KeyOf("A version."), KeyOf("Fixes"), KeyOf("Fixed"), KeyOf("Open")]);
    }

    [Fact]
    public void AnEntryAddedToATranslatedVersion_LeavesTheOtherTranslationsInPlace()
    {
        var localizer = MakeActive();
        localizer.AddTranslation("fr", KeyOf("Fixed"), "Réparé");
        localizer.SetCurrentLocale("fr");

        var version = MakeVersion();
        var grown = version with { Entries = [new ChangelogEntry { Text = "Added", HasBullet = true }, .. version.Entries] };
        var shown = new ChangelogTexts().Localize(grown);

        shown.Entries[2].Text.Should().Be("Réparé", "the key follows the text, not its position");
        shown.Entries[0].Text.Should().Be("Added");
    }

    [Fact]
    public void Shown_ReadsTheActiveLanguage_AndKeepsItsCopiesUntilTheLanguageChanges()
    {
        var localizer = MakeActive();
        localizer.AddTranslation("fr", KeyOf("What's new"), "Nouveautés");
        localizer.AddTranslation("fr", KeyOf("Fixed"), "Réparé");

        var texts = new ChangelogTexts();
        ChangelogVersion[] sorted = [MakeVersion()];

        var english = texts.Shown(sorted);
        english[0].Title.Should().Be("What's new");
        texts.Shown(sorted).Should().BeSameAs(english, "the copies stay the same while nothing changes");
        texts.ShownOf(sorted, sorted[0]).Should().BeSameAs(english[0]);

        localizer.SetCurrentLocale("fr");
        var french = texts.Shown(sorted)[0];

        french.Title.Should().Be("Nouveautés");
        french.Entries[1].Text.Should().Be("Réparé");
        french.Entries[0].Text.Should().Be("Fixes", "a text with no translation shows as written");
        french.Entries[1].ButtonAction.Should().BeSameAs(sorted[0].Entries[1].ButtonAction);
        french.Entries[2].IsSeparator.Should().BeTrue();
    }

    private static string KeyOf(string text) => "changelog.9.1.0.0." + Crc32Helper.Compute(Encoding.UTF8.GetBytes(text)).ToString("x8");

    private NoireLocalizer MakeActive()
    {
        var localizer = new NoireLocalizer(active: true, enableLogging: false, defaultLocale: "en-US");
        localizers.Add(localizer);
        return localizer;
    }

    private static readonly Action<ImGuiMouseButton> Open = static _ => { };

    private static ChangelogVersion MakeVersion() => new()
    {
        Version = new Version(9, 1, 0, 0),
        Date = "01-01-2026",
        Title = "What's new",
        Description = "A version.",
        Entries =
        [
            new ChangelogEntry { Text = "Fixes", IsHeader = true, Icon = FontAwesomeIcon.Bug },
            new ChangelogEntry { Text = "Fixed", HasBullet = true, ButtonText = "Open", ButtonAction = Open },
            new ChangelogEntry { IsSeparator = true },
        ],
    };

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
