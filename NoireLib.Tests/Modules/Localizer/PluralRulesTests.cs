using FluentAssertions;
using NoireLib.Localizer;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the CLDR cardinal categories for whole numbers in the languages the localizer knows, and that the categories a
/// translation must provide cover every category a count can land in, Many for exact millions excepted.
/// </summary>
public sealed class PluralRulesTests
{
    [Theory]
    [InlineData("en", 0, PluralCategory.Other)]
    [InlineData("en", 1, PluralCategory.One)]
    [InlineData("en-GB", 2, PluralCategory.Other)]
    [InlineData("de", 1, PluralCategory.One)]
    [InlineData("fr", 0, PluralCategory.One)]
    [InlineData("fr", 1, PluralCategory.One)]
    [InlineData("fr", 2, PluralCategory.Other)]
    [InlineData("fr", 1000000, PluralCategory.Many)]
    [InlineData("pt", 0, PluralCategory.One)]
    [InlineData("pt-PT", 0, PluralCategory.Other)]
    [InlineData("es", 1000000, PluralCategory.Many)]
    [InlineData("ja", 1, PluralCategory.Other)]
    [InlineData("zh-TW", 1, PluralCategory.Other)]
    [InlineData("ko", 5, PluralCategory.Other)]
    [InlineData("ru", 1, PluralCategory.One)]
    [InlineData("ru", 21, PluralCategory.One)]
    [InlineData("ru", 11, PluralCategory.Many)]
    [InlineData("ru", 3, PluralCategory.Few)]
    [InlineData("ru", 13, PluralCategory.Many)]
    [InlineData("ru", 25, PluralCategory.Many)]
    [InlineData("uk", 22, PluralCategory.Few)]
    [InlineData("pl", 1, PluralCategory.One)]
    [InlineData("pl", 21, PluralCategory.Many)]
    [InlineData("pl", 22, PluralCategory.Few)]
    [InlineData("cs", 3, PluralCategory.Few)]
    [InlineData("cs", 5, PluralCategory.Other)]
    [InlineData("hr", 21, PluralCategory.One)]
    [InlineData("lt", 11, PluralCategory.Other)]
    [InlineData("lt", 21, PluralCategory.One)]
    [InlineData("lt", 25, PluralCategory.Few)]
    [InlineData("ro", 0, PluralCategory.Few)]
    [InlineData("ro", 20, PluralCategory.Other)]
    [InlineData("ro", 101, PluralCategory.Few)]
    [InlineData("sl", 102, PluralCategory.Two)]
    [InlineData("he", 2, PluralCategory.Two)]
    [InlineData("ar", 0, PluralCategory.Zero)]
    [InlineData("ar", 2, PluralCategory.Two)]
    [InlineData("ar", 105, PluralCategory.Few)]
    [InlineData("ar", 111, PluralCategory.Many)]
    [InlineData("ar", 100, PluralCategory.Other)]
    [InlineData("en", -1, PluralCategory.One)]
    public void For_ReturnsTheCldrCategory(string language, int count, PluralCategory expected)
    {
        PluralRules.For(language, count).Should().Be(expected);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("ja")]
    [InlineData("ru")]
    [InlineData("pl")]
    [InlineData("cs")]
    [InlineData("lt")]
    [InlineData("ro")]
    [InlineData("sl")]
    [InlineData("he")]
    [InlineData("ar")]
    public void CategoriesOf_CoversEveryCategoryACountReaches(string language)
    {
        var categories = PluralRules.CategoriesOf(language);

        for (var count = 0; count <= 250; count++)
            categories.Should().Contain(PluralRules.For(language, count), "count {0} in {1} must have a form to translate", count, language);
    }
}
