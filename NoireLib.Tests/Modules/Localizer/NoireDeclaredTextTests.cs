using FluentAssertions;
using NoireLib.Configuration;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks declared-text resolution, plurals, the pseudo-language, layered language files and allocation-free reads.</summary>
[Collection(LocalizerStateCollection.Name)]
public sealed class NoireDeclaredTextTests : IDisposable
{
    private static readonly NoireString Greeting = new("test.declared.greeting", "Hello");
    private static readonly NoireString Welcome = new("test.declared.welcome", "Welcome, {name}");
    private static readonly NoireString Pair = new("test.declared.pair", "{a} and {b}");
    private static readonly NoirePlural Targets = new("test.declared.targets", "{count} target", "{count} targets");

    private readonly List<NoireLocalizer> localizers = [];
    private readonly string tempDirectory;

    public NoireDeclaredTextTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibDeclaredTextTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        ResetPersistedConfiguration();
    }

    public void Dispose()
    {
        foreach (var localizer in localizers)
            localizer.Dispose();

        ResetPersistedConfiguration();

        try
        {
            Directory.Delete(tempDirectory, true);
        }
        catch (IOException)
        {
            // A leftover temporary directory must not fail a test run.
        }
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

    private NoireLocalizer MakeActive(string defaultLocale = "en-US")
    {
        var localizer = new NoireLocalizer(active: true, enableLogging: false, defaultLocale: defaultLocale);
        localizers.Add(localizer);
        return localizer;
    }

    private const string French = """
        # French, a comment of the translator's own: kept with the key under it, never read as its source.
        test.declared.greeting = Bonjour
        test.declared.welcome = Bienvenue, {name}
        test.declared.pair = {b} et {a}
        test.declared.targets.one = {count} cible
        test.declared.targets.other = {count} cibles
        """;

    [Fact]
    public void Text_WithNoActiveLocalizer_IsTheSource()
    {
        Greeting.Text.Should().Be("Hello");
        NoireLanguages.Localizer.Should().BeNull();
    }

    [Fact]
    public void Text_FollowsTheActiveLanguage_AndItsRegions()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", French, userFile: false);

        localizer.SetCurrentLocale("fr");
        Greeting.Text.Should().Be("Bonjour");

        localizer.SetCurrentLocale("fr-CA");
        Greeting.Text.Should().Be("Bonjour", "a region falls back to its parent language");

        localizer.SetCurrentLocale("de");
        Greeting.Text.Should().Be("Hello");
    }

    [Fact]
    public void Text_MissingFromTheActiveLanguage_ShowsTheSourceRatherThanTheDefaultLocale()
    {
        var localizer = MakeActive(defaultLocale: "fr");
        localizer.LoadLanguageText("fr", French, userFile: false);
        localizer.SetCurrentLocale("de");

        Greeting.Text.Should().Be("Hello", "a declared text falls back to the language it was written in");
    }

    [Fact]
    public void Text_UsesACorrectionFromTheSourceLanguageTable()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("en", "test.declared.greeting = Hi there", userFile: true);
        localizer.SetCurrentLocale("de");

        Greeting.Text.Should().Be("Hi there");
    }

    [Fact]
    public void With_FillsPlaceholders_AndIsCachedPerLanguage()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", French, userFile: false);

        var english = Welcome.With("name", "Aspher");
        english.Should().Be("Welcome, Aspher");
        Welcome.With("name", "Aspher").Should().BeSameAs(english);

        localizer.SetCurrentLocale("fr");
        Welcome.With("name", "Aspher").Should().Be("Bienvenue, Aspher");
        Pair.With("a", "1", "b", "2").Should().Be("2 et 1", "a translation may reorder its placeholders");
    }

    [Fact]
    public void Plural_PicksTheCategoryOfTheActiveLanguage()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", French, userFile: false);

        Targets.For(0).Should().Be("0 targets");
        Targets.For(1).Should().Be("1 target");

        localizer.SetCurrentLocale("fr");
        Targets.For(0).Should().Be("0 cible", "French puts zero in One");
        Targets.For(2).Should().Be("2 cibles");
        Targets.For(1234).Should().Be(1234.ToString("N0", CultureInfo.GetCultureInfo("fr")) + " cibles");
    }

    [Fact]
    public void Plural_WithAMissingCategory_UsesTheLanguagesOtherForm()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("ru", "test.declared.targets.one = {count} tsel\ntest.declared.targets.other = {count} tseley", userFile: false);
        localizer.SetCurrentLocale("ru");

        Targets.For(21).Should().Be("21 tsel");
        Targets.For(3).Should().Be("3 tseley", "the Russian Other form stands in for a missing Few");
    }

    [Fact]
    public void Pseudo_AccentsBracketsAndPads_ButKeepsPlaceholders()
    {
        var localizer = MakeActive();
        localizer.SetCurrentLocale(NoireLanguages.Pseudo);

        var pseudo = Welcome.Text;
        pseudo.Should().StartWith("[").And.EndWith("]").And.Contain("{name}");
        pseudo.Should().Contain("Wélçómé");
        pseudo.Length.Should().BeGreaterThanOrEqualTo("Welcome, {name}".Length * 13 / 10);
        Welcome.With("name", "Aspher").Should().Contain("Aspher");
        Targets.For(2).Should().StartWith("[2 tárgéts");
    }

    [Fact]
    public void PseudoLong_WritesTheTextTwice_AndKeepsPlaceholders()
    {
        var localizer = MakeActive();
        localizer.SetCurrentLocale(NoireLanguages.PseudoLong);

        Welcome.Text.Should().Be("[Wélçómé, {name} Wélçómé, {name}]");
        Welcome.With("name", "Aspher").Should().Be("[Wélçómé, Aspher Wélçómé, Aspher]");
        NoireLanguages.IsPseudo(localizer.CurrentLocale).Should().BeTrue();
        localizer.MissingKeys(NoireLanguages.PseudoLong).Should().BeEmpty();
    }

    [Fact]
    public void Message_ShowsTheActiveLanguage_AndRecordsTheLogLanguage()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", French, userFile: false);
        localizer.SetCurrentLocale("fr");

        var message = new NoireMessage(Welcome, "name", "Aspher");

        message.Display.Should().Be("Bienvenue, Aspher");
        message.Record.Should().Be("Welcome, Aspher");

        localizer.SetLogLanguage("fr");
        message.Record.Should().Be("Bienvenue, Aspher");
    }

    [Fact]
    public void UserFile_OverridesTheEmbeddedLine_AndRevertsWhenDeleted()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", French, userFile: false);
        localizer.SetCurrentLocale("fr");

        var path = Path.Combine(tempDirectory, "fr.lang");
        File.WriteAllText(path, "test.declared.greeting = Salut");
        localizer.ReloadUserLanguageFile(path);

        Greeting.Text.Should().Be("Salut");
        Welcome.With("name", "A").Should().Be("Bienvenue, A", "a user file only needs the lines it changes");

        File.Delete(path);
        localizer.ReloadUserLanguageFile(path);

        Greeting.Text.Should().Be("Bonjour");
    }

    [Fact]
    public void ReloadingAFile_DropsTheLinesItNoLongerHas()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", French, userFile: false);
        localizer.SetCurrentLocale("fr");

        localizer.LoadLanguageText("fr", "test.declared.welcome = Bienvenue, {name}", userFile: false);

        Greeting.Text.Should().Be("Hello");
    }

    [Fact]
    public void LoadingAFile_MovesTheRevisionOnce()
    {
        var localizer = MakeActive();
        var before = NoireLanguages.Revision;

        localizer.LoadLanguageText("fr", French, userFile: false);

        NoireLanguages.Revision.Should().Be(before + 1, "a whole file is one change");
    }

    [Fact]
    public void MissingKeys_ListsTextsAndPluralForms_AndNothingForTheSource()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", "test.declared.greeting = Bonjour\ntest.declared.targets.one = {count} cible", userFile: false);

        var missing = localizer.MissingKeys("fr").Where(k => k.StartsWith("test.declared.", StringComparison.Ordinal)).ToList();

        missing.Should().Equal("test.declared.pair", "test.declared.targets.other", "test.declared.welcome");
        localizer.MissingKeys("en-GB").Should().BeEmpty();
        localizer.MissingKeys(NoireLanguages.Pseudo).Should().BeEmpty();
    }

    [Fact]
    public void Credits_ComeFromTheLanguageFile_AndSurviveAnExport()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", "@credits = Anna,  Ben ,\n" + French, userFile: false);

        var french = localizer.Languages.Single(l => l.Code == "fr");

        french.Credits.Should().Equal("Anna", "Ben");
        localizer.Languages[0].Credits.Should().BeEmpty("the source language names no one");
        NoireLanguages.CreditLines.Should().ContainSingle().Which.Should().EndWith("Anna, Ben");
        localizer.ExportLanguageFile("fr").Should().Contain("@credits = Anna,  Ben ,");
    }

    [Fact]
    public void Languages_ListsSourceLoadedAndPseudo_AndMarksTheActiveOne()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", French, userFile: false);
        localizer.SetCurrentLocale("fr-FR");

        var languages = localizer.Languages;

        languages[0].Code.Should().Be("en");
        languages[^2].Code.Should().Be(NoireLanguages.Pseudo);
        languages[^1].Code.Should().Be(NoireLanguages.PseudoLong);
        var french = languages.Single(l => l.Code == "fr");
        french.IsActive.Should().BeTrue("fr-FR shows the fr texts");
        french.NativeName.Should().StartWith("F");
        french.Total.Should().Be(languages[0].Total);
        languages.Count(l => l.IsActive).Should().Be(1);
        localizer.Languages.Should().BeSameAs(languages, "the list is cached until something changes");
    }

    [Fact]
    public void ExportLanguageFile_RoundTripsThroughTheParser()
    {
        // The declared texts register when this class's static fields initialize, which nothing else in this test forces.
        _ = Greeting.Key;
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", French, userFile: false);

        var exported = localizer.ExportLanguageFile("fr");

        exported.Should().Contain("# Source: Hello\ntest.declared.greeting = Bonjour");
        LanguageFile.Parse(exported)["test.declared.targets.other"].Should().Be("{count} cibles");
    }

    [Fact]
    public void Number_IsWrittenTheActiveLanguagesWay_AndCachedWhenSmall()
    {
        var localizer = MakeActive();
        localizer.SetCurrentLocale("fr");

        NoireLanguages.Number(12345).Should().Be(12345.ToString("N0", CultureInfo.GetCultureInfo("fr")));
        NoireLanguages.Number(42).Should().BeSameAs(NoireLanguages.Number(42));
    }

    [Fact]
    public void WarmReads_AllocateNothing()
    {
        var localizer = MakeActive();
        localizer.LoadLanguageText("fr", French, userFile: false);
        localizer.SetCurrentLocale("fr");

        for (var i = 0; i < 2; i++)
        {
            _ = Greeting.Text;
            _ = Welcome.With("name", "Aspher");
            _ = Pair.With("a", "1", "b", "2");
            _ = Targets.For(3);
            _ = NoireLanguages.Number(7);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 100; i++)
        {
            _ = Greeting.Text;
            _ = Welcome.With("name", "Aspher");
            _ = Pair.With("a", "1", "b", "2");
            _ = Targets.For(3);
            _ = NoireLanguages.Number(7);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }
}
