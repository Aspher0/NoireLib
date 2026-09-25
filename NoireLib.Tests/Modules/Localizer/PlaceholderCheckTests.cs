using FluentAssertions;
using NoireLib.Localizer;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the placeholder check of translations: every {name} of the source must be kept as it is, and none invented,
/// since a text fills its placeholders by name. The language file notes each mistake for translators working from files.
/// </summary>
public sealed class PlaceholderCheckTests
{
    [Fact]
    public void Compare_FindsARenamedPlaceholder_AsMissingAndUnknown()
    {
        var (missing, unknown) = PlaceholderCheck.Compare("{target} plays through {source}.", "{cible} joue par {source}.");

        missing.Should().Equal("{target}");
        unknown.Should().Equal("{cible}");
    }

    [Fact]
    public void Compare_AcceptsReorderedAndRepeatedPlaceholders()
    {
        var (missing, unknown) = PlaceholderCheck.Compare("{a} and {b}", "{b}, puis {a} et encore {a}");

        missing.Should().BeEmpty();
        unknown.Should().BeEmpty();
    }

    [Fact]
    public void Write_NotesTheMistakes_AndParseDropsTheNotes()
    {
        var table = new Dictionary<string, string> { ["k"] = "{cible} bloquée" };
        var sources = new Dictionary<string, string> { ["k"] = "{target} blocked" };

        var text = LanguageFile.Write(table, key => sources.GetValueOrDefault(key));

        text.Should().Be("# Source: {target} blocked\n# Missing tags: {target}\n# Unknown tags: {cible}\nk = {cible} bloquée\n");

        var notes = new Dictionary<string, string>();
        LanguageFile.Parse(text, notes: notes);
        notes.Should().BeEmpty("the check is written again on every write, never kept as a translator's comment");
    }
}
