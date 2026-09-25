using FluentAssertions;
using NoireLib.Configuration;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the translation editor's writes: an edit is live in the localizer, an emptied translation is removed, a save
/// writes the language file a fresh localizer reads back unchanged, and a revert puts back what was last saved.
/// </summary>
[Collection(LocalizerStateCollection.Name)]
public sealed class NoireTranslationEditorTests : IDisposable
{
    private static readonly NoireString Greeting = new("test.editor.greeting", "Hello");
    private static readonly NoireString Farewell = new("test.editor.farewell", "Goodbye");

    private readonly List<NoireLocalizer> localizers = [];
    private readonly string tempDirectory;

    public NoireTranslationEditorTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibTranslationEditorTests", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Edit_IsLive_AndShowsInTheDeclaredText()
    {
        var localizer = MakeActive();
        localizer.SetCurrentLocale("fr");
        var editor = new NoireTranslationEditor();
        editor.Select("fr");

        editor.Edit(localizer, Greeting.Key, "Bonjour");

        editor.Language.Should().Be("fr");
        Greeting.Text.Should().Be("Bonjour");
    }

    [Fact]
    public void Edit_WithAnEmptyText_RemovesTheTranslation()
    {
        var localizer = MakeActive();
        var editor = new NoireTranslationEditor();
        editor.Select("fr");
        editor.Edit(localizer, Farewell.Key, "Au revoir");

        editor.Edit(localizer, Farewell.Key, string.Empty);

        localizer.GetLocaleTranslations("fr").ContainsKey(Farewell.Key).Should().BeFalse();
    }

    [Fact]
    public void Save_WritesTheLanguageFile_AndAFreshLocalizerReadsItBack()
    {
        var localizer = MakeActive();
        var editor = new NoireTranslationEditor();
        editor.Select("fr");
        editor.Edit(localizer, Greeting.Key, "Bonjour");
        editor.Edit(localizer, Farewell.Key, "Au revoir\nà bientôt");

        var path = editor.Save(localizer, tempDirectory);

        path.Should().Be(Path.Combine(tempDirectory, "fr.lang"));
        File.ReadAllText(path!).Should().Contain("test.editor.greeting = Bonjour").And.Contain("# Source: Hello");

        localizer.Dispose();
        localizers.Remove(localizer);

        var fresh = MakeActive();
        fresh.ReloadUserLanguageFile(path!);
        var table = fresh.GetLocaleTranslations("fr");

        table[Greeting.Key].Should().Be("Bonjour");
        table[Farewell.Key].Should().Be("Au revoir\nà bientôt");
    }

    [Fact]
    public void Revert_PutsBackTheSavedTranslation()
    {
        var localizer = MakeActive();
        localizer.AddTranslation("fr", Greeting.Key, "Bonjour");
        localizer.SetCurrentLocale("fr");
        var editor = new NoireTranslationEditor();
        editor.Select("fr");
        editor.Edit(localizer, Greeting.Key, "Salut", Greeting.Source);

        editor.Revert(localizer, Greeting.Key);

        Greeting.Text.Should().Be("Bonjour");
        localizer.TranslatedFrom("fr", Greeting.Key).Should().BeNull("the saved translation was made from no recorded source");
        HasUnsavedChanges(editor).Should().BeFalse();
    }

    [Fact]
    public void Revert_OfATextSavedWithoutATranslation_RemovesIt()
    {
        var localizer = MakeActive();
        var editor = new NoireTranslationEditor();
        editor.Select("fr");
        editor.Edit(localizer, Farewell.Key, "Au revoir", Farewell.Source);

        editor.Revert(localizer, Farewell.Key);

        localizer.GetLocaleTranslations("fr").ContainsKey(Farewell.Key).Should().BeFalse();
        HasUnsavedChanges(editor).Should().BeFalse();
    }

    [Fact]
    public void Revert_OfOneOfTwoChanges_KeepsTheOtherUnsaved()
    {
        var localizer = MakeActive();
        var editor = new NoireTranslationEditor();
        editor.Select("fr");
        editor.Edit(localizer, Greeting.Key, "Bonjour", Greeting.Source);
        editor.Edit(localizer, Farewell.Key, "Au revoir", Farewell.Source);

        editor.Revert(localizer, Greeting.Key);

        HasUnsavedChanges(editor).Should().BeTrue();
        localizer.GetLocaleTranslations("fr")[Farewell.Key].Should().Be("Au revoir");
    }

    [Fact]
    public void Revert_AfterASave_PutsBackWhatWasSaved()
    {
        var localizer = MakeActive();
        localizer.SetCurrentLocale("fr");
        var editor = new NoireTranslationEditor();
        editor.Select("fr");
        editor.Edit(localizer, Greeting.Key, "Bonjour", Greeting.Source);
        editor.Save(localizer, tempDirectory);
        editor.Edit(localizer, Greeting.Key, "Salut", Greeting.Source);

        editor.Revert(localizer, Greeting.Key);

        Greeting.Text.Should().Be("Bonjour");
        HasUnsavedChanges(editor).Should().BeFalse();
    }

    private static bool HasUnsavedChanges(NoireTranslationEditor editor)
        => (bool)typeof(NoireTranslationEditor)
            .GetField("dirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(editor)!;

    [Fact]
    public void Select_IgnoresACodeThatIsNotALanguage()
    {
        var editor = new NoireTranslationEditor();

        editor.Select("not a language");
        editor.Language.Should().BeEmpty();

        editor.Select("pt-br");
        editor.Language.Should().Be("pt-BR");
    }

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

    [Theory]
    [InlineData("contourne", true, true)]
    [InlineData("contourne", false, false)]
    [InlineData("Contourner", false, true)]
    public void Search_FindsATranslation(string search, bool contains, bool found)
    {
        var localizer = MakeActive();
        var editor = new NoireTranslationEditor();
        editor.Select("fr");
        editor.Edit(localizer, Greeting.Key, "Contourner les emotes");
        SetField(editor, "search", search);
        SetField(editor, "searchContains", contains);
        SetField(editor, "missingOnly", false);
        SetField(editor, "builtRevision", -1);

        typeof(NoireTranslationEditor).GetMethod("Rebuild", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(editor, [localizer]);

        editor.RowCount.Should().Be(found ? 1 : 0);
    }

    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(target, value);

    [Fact]
    public void Matches_ContainsFindsAPart_OffFindsOnlyAWholeWord()
    {
        NoireTranslationEditor.Matches("contourne", true, "k", "Bypass", "Contourner les emotes").Should().BeTrue();
        NoireTranslationEditor.Matches("contourne", false, "k", "Bypass", "Contourner les emotes").Should().BeFalse();
        NoireTranslationEditor.Matches("Remplacer", false, "k", "Override", "+ Remplacer une emote...").Should().BeTrue();
        NoireTranslationEditor.Matches("remplacer", false, "k", "Override", "Remplacements d'emotes").Should().BeFalse();
        NoireTranslationEditor.Matches("swap", false, "chat.swap.failed", "s", "t").Should().BeTrue();
        NoireTranslationEditor.Matches(string.Empty, false, "k", "s", "t").Should().BeTrue();
    }
}
