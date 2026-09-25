using System;
using System.Collections.Generic;

namespace NoireLib.Localizer;

/// <summary>A text depending on a count, translated under <c>key.one</c>, <c>key.other</c> and each other CLDR category.</summary>
public sealed class NoirePlural
{
    /// <summary>Declares a plural text with a singular and a plural form.</summary>
    /// <param name="key">The stable key.</param>
    /// <param name="one">The singular form, for example <c>{count} target</c>.</param>
    /// <param name="other">The plural form, for example <c>{count} targets</c>.</param>
    public NoirePlural(string key, string one, string other)
        : this(key, new Dictionary<PluralCategory, string> { [PluralCategory.One] = one, [PluralCategory.Other] = other })
    {
    }

    /// <summary>Declares a plural text with every form the source language uses.</summary>
    /// <param name="key">The stable key.</param>
    /// <param name="forms">The forms by category. <see cref="PluralCategory.Other"/> is required.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="forms"/> has no <see cref="PluralCategory.Other"/> form.</exception>
    public NoirePlural(string key, IReadOnlyDictionary<PluralCategory, string> forms)
    {
        if (!forms.ContainsKey(PluralCategory.Other))
            throw new ArgumentException("A plural text needs an Other form.", nameof(forms));

        Key = key;
        Forms = forms;
        NoireLanguages.Register(this);
    }

    /// <summary>The stable key.</summary>
    public string Key { get; }

    /// <summary>The source language's forms by category.</summary>
    public IReadOnlyDictionary<PluralCategory, string> Forms { get; }

    /// <summary>
    /// The text for a count in the active language with <c>{count}</c> filled, cached per count and language.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <returns>The filled text.</returns>
    public string For(int count) => FormatCache.Plural(this, count);

    // The unfilled form for a count in the active language.
    internal string FormFor(int count) => NoireLanguages.PluralForm(this, count);

    // The source form for a count, picked with the source language's rules.
    internal string SourceForm(int count)
    {
        var source = NoireLanguages.Localizer?.SourceLanguage ?? NoireLocalizer.DefaultSourceLanguage;
        return Forms.TryGetValue(PluralRules.For(source, count), out var form) ? form : Forms[PluralCategory.Other];
    }
}
