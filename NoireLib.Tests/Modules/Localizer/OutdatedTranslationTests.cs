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

/// <summary>Locks outdated translations: flagged not hidden, cleared by a check or an edit, and carried by the build.</summary>
[Collection(LocalizerStateCollection.Name)]
public sealed class OutdatedTranslationTests : IDisposable
{
    private static readonly NoireString Greeting = new("test.outdated.greeting", "Hello there");
    private static readonly NoireString Named = new("test.outdated.named", "Hi {name}");

    private readonly List<NoireLocalizer> localizers = [];

    public OutdatedTranslationTests() => ResetPersistedConfiguration();

    public void Dispose()
    {
        foreach (var localizer in localizers)
            localizer.Dispose();

        ResetPersistedConfiguration();
    }

    [Fact]
    public void Parse_ReadsTheSourceLines_AndKeepsEveryOtherComment_WithTheKeyUnderIt()
    {
        var basis = new Dictionary<string, string>();
        var notes = new Dictionary<string, string>();

        LanguageFile.Parse("# French, by Anna\n# Source: Hello\nhello = Bonjour\n# Unused: the plugin no longer has this text.\n# Source: Bye\n\nbye = Au revoir\n", basis, notes);

        basis.Should().Equal(new Dictionary<string, string> { ["hello"] = "Hello", ["bye"] = "Bye" });
        notes.Should().Equal(new Dictionary<string, string> { ["hello"] = "# French, by Anna\n" }, "the writer's own notes are not the translator's");
    }

    [Fact]
    public void Write_FlagsAChangedSource_AndAKeyNoLongerDeclared_ButNotTheCredits()
    {
        var table = new Dictionary<string, string> { ["@credits"] = "Anna", ["a"] = "Un", ["b"] = "Deux", ["gone"] = "Parti" };
        var basis = new Dictionary<string, string> { ["a"] = "One", ["b"] = "Two", ["gone"] = "Gone" };
        var sources = new Dictionary<string, string> { ["a"] = "One", ["b"] = "Two!" };

        var text = LanguageFile.Write(table, key => sources.GetValueOrDefault(key), key => basis.GetValueOrDefault(key),
            key => key == "a" ? "# Mine\n" : null);

        text.Should().Be(
            "@credits = Anna\n" +
            "# Mine\n# Source: One\na = Un\n" +
            "# Source: Two\n# Outdated, the source is now: Two!\nb = Deux\n" +
            "# Source: Gone\n# Unused: the plugin no longer has this text.\ngone = Parti\n");
    }

    [Fact]
    public void AnOutdatedTranslation_StillShows_IsCounted_AndIsUpToDateOnceChecked()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", "# Mine\n# Source: Hello\ntest.outdated.greeting = Bonjour\n", userFile: false);
        localizer.SetCurrentLocale("fr");

        Greeting.Text.Should().Be("Bonjour", "an outdated translation is still better than none");
        localizer.Languages.Single(l => l.Code == "fr").Outdated.Should().Be(1);
        localizer.ExportLanguageFile("fr").Should().Contain("# Mine\n# Source: Hello\n# Outdated, the source is now: Hello there\n");

        localizer.SetTranslatedFrom("fr", Greeting.Key, Greeting.Source);

        localizer.Languages.Single(l => l.Code == "fr").Outdated.Should().Be(0);
        localizer.ExportLanguageFile("fr").Should().Contain("# Mine\n# Source: Hello there\ntest.outdated.greeting = Bonjour");
    }

    [Fact]
    public void RejectOutdated_IsOffByDefault_AndShowsTheSourceOnceOn()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", "# Source: Hello\ntest.outdated.greeting = Bonjour\n", userFile: false);
        localizer.SetCurrentLocale("fr");

        localizer.RejectOutdated.Should().BeFalse();
        Greeting.Text.Should().Be("Bonjour");

        localizer.SetRejectOutdated(true);

        Greeting.Text.Should().Be("Hello there", "the outdated translation is set aside");
    }

    [Fact]
    public void RejectMissingOrExtraTags_IsOffByDefault_AndShowsTheSourceOnceOn()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", "# Source: Hi {name}\ntest.outdated.named = Salut {nom}\n", userFile: false);
        localizer.SetCurrentLocale("fr");

        localizer.RejectMissingOrExtraTags.Should().BeFalse();
        Named.Text.Should().Be("Salut {nom}");

        localizer.SetRejectMissingOrExtraTags(true);

        Named.With("name", "Anna").Should().Be("Hi Anna", "the translation with a renamed placeholder is set aside");
    }

    [Fact]
    public void Update_AddsTheMissingKeys_AndCarriesAnEditedChangelogText_FlaggedOutdated()
    {
        var oldKey = "changelog.9.2.0.0." + Checksum("Fixed the hotbar.");
        var newKey = "changelog.9.2.0.0." + Checksum(TemplateChangelog.EditedText);
        var file = "# Source: Fixed the hotbar.\n" + oldKey + " = Barre réparée.\n";

        var updated = NoireLanguageTemplate.Update(typeof(OutdatedTranslationTests).Assembly, "fr", file);

        updated.Should().Contain("# Source: Fixed the hotbar.\n# Outdated, the source is now: " + TemplateChangelog.EditedText + "\n"
            + newKey + " = Barre réparée.\n");
        updated.Should().NotContain(oldKey + " =");
        updated.Should().Contain("# Source: Hello there\ntest.outdated.greeting = \n", "a key the file lacks is added, empty");
        NoireLanguageTemplate.Update(typeof(OutdatedTranslationTests).Assembly, "fr", updated).Should().Be(updated, "an update of an updated file changes nothing");
    }

    [Fact]
    public void AtLoad_AnEditedChangelogText_ShowsItsOldTranslation_BeforeTheFilesCatchUp()
    {
        var localizer = MakeActive();
        var oldKey = "changelog.9.2.0.0." + Checksum("Fixed the hotbar.");
        localizer.LoadLanguageText("fr", "# Source: Fixed the hotbar.\n" + oldKey + " = Barre réparée.\n", userFile: false);
        localizer.SetCurrentLocale("fr");
        var version = new TemplateChangelog().GetVersions()[0];

        new ChangelogTexts().Localize(version);
        localizer.CarryChangelogTexts();

        new ChangelogTexts().Localize(version).Entries[0].Text.Should().Be("Barre réparée.");
        localizer.Languages.Single(l => l.Code == "fr").Outdated.Should().Be(1, "it was made from the old text");
    }

    [Fact]
    public void Similarity_IsOneForEqualTexts_AndLowForUnrelatedOnes()
    {
        ChangelogCarry.Similarity("Fixed the hotbar.", "Fixed the hotbar.").Should().Be(1d);
        ChangelogCarry.Similarity("Fixed the hotbar.", "Fixed the hotbar bug.").Should().BeGreaterThan(0.7d);
        ChangelogCarry.Similarity("Fixed the hotbar.", "New icons everywhere").Should().BeLessThan(0.5d);
    }

    private static string Checksum(string text) => Crc32Helper.Compute(Encoding.UTF8.GetBytes(text)).ToString("x8");

    private NoireLocalizer MakeActive()
    {
        var localizer = new NoireLocalizer(active: true, enableLogging: false, defaultLocale: "en-US");
        localizers.Add(localizer);
        return localizer;
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

/// <summary>A changelog the language file update reads from the test assembly; its one entry was edited.</summary>
public sealed class TemplateChangelog : BaseChangelogVersion
{
    internal const string EditedText = "Fixed the hotbar bug.";

    /// <inheritdoc/>
    public override List<ChangelogVersion> GetVersions() =>
    [
        new ChangelogVersion
        {
            Version = new Version(9, 2, 0, 0),
            Date = "01-01-2026",
            Entries = [EntryBullet(EditedText)],
        },
    ];
}
