using NoireLib.Changelog;
using NoireLib.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NoireLib.Localizer;

/// <summary>
/// A plugin's language files as the build keeps them: a blank template of every key under its source text, and each
/// language file brought up to date. The <c>NoireLib.LanguageTemplate.targets</c> build step calls it.
/// </summary>
public static class NoireLanguageTemplate
{
    private const string Header =
        "# Language file template, written by the build. Copy it to <code>.lang, such as de.lang, and write each\n" +
        "# translation after its \"=\". A key left empty shows its source text. \"# Source:\" is the text a translation\n" +
        "# was made from: leave it, the plugin uses it to tell when a source text changes. Your own comments are kept.\n" +
        "# A plural key ending in .one or .other can take the forms the language needs: .zero, .one, .two, .few, .many, .other.\n" +
        "# \"@credits = Your name\" credits you in the plugin's language settings.\n";

    /// <summary>
    /// Builds the template of an assembly: the texts every type of it declares, NoireLib's own texts, and the texts of
    /// every changelog version it defines, each under its source text and translated to nothing.
    /// </summary>
    /// <param name="assembly">The plugin's assembly.</param>
    /// <param name="problems">Receives a line for each type that could not be read; its texts are missing.</param>
    /// <returns>The <c>.lang</c> file content.</returns>
    public static string Build(Assembly assembly, ICollection<string>? problems = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var sources = SourcesOf(assembly, null, problems);
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in sources.Keys)
            table[key] = string.Empty;

        // The header is the first key's notes: it stays on top when the template is copied and updated.
        var first = table.Keys.Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return LanguageFile.Write(table, key => sources.GetValueOrDefault(key), null,
            key => string.Equals(key, first, StringComparison.OrdinalIgnoreCase) ? Header : null);
    }

    /// <summary>
    /// Brings a language file up to date: missing keys added empty, edited changelog texts carried, outdated and unused
    /// translations flagged. Running it on its own output changes nothing.
    /// </summary>
    /// <param name="assembly">The plugin's assembly.</param>
    /// <param name="language">The file's language code, which decides its plural forms.</param>
    /// <param name="fileText">The file's current content.</param>
    /// <param name="problems">Receives a line per type that could not be read.</param>
    /// <returns>The updated <c>.lang</c> content.</returns>
    public static string Update(Assembly assembly, string language, string fileText, ICollection<string>? problems = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(fileText);

        var sources = SourcesOf(assembly, language, problems);
        var basis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var table = LanguageFile.Parse(fileText, basis, notes);

        ChangelogCarry.Carry(sources, table, basis);

        foreach (var key in sources.Keys)
            table.TryAdd(key, string.Empty);

        return LanguageFile.Write(table, key => sources.GetValueOrDefault(key), key => basis.GetValueOrDefault(key), key => notes.GetValueOrDefault(key));
    }

    // Every key the assembly and NoireLib declare with its source text; a plural lists the forms of the language, or
    // the source language's forms for the template.
    private static Dictionary<string, string> SourcesOf(Assembly assembly, string? language, ICollection<string>? problems)
    {
        Declare(assembly, problems);

        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var text in NoireLanguages.Strings)
            sources[text.Key] = text.Source;

        foreach (var plural in NoireLanguages.Plurals)
        {
            if (language == null)
            {
                foreach (var form in plural.Forms)
                    sources[plural.Key + "." + PluralRules.Suffix(form.Key)] = form.Value;

                continue;
            }

            foreach (var category in PluralRules.CategoriesOf(language))
            {
                var source = plural.Forms.TryGetValue(category, out var form) ? form : plural.Forms[PluralCategory.Other];
                sources[plural.Key + "." + PluralRules.Suffix(category)] = source;
            }
        }

        return sources;
    }

    private static void Declare(Assembly assembly, ICollection<string>? problems)
    {
        RuntimeHelpers.RunClassConstructor(typeof(NoireStrings).TypeHandle);

        var types = TypesOf(assembly, problems);

        foreach (var type in types)
        {
            if (DeclaresTexts(type))
                Initialize(type, problems);
        }

        // A setting with choices, such as an enum, declares its choices' labels the first time they are listed.
        foreach (var type in types)
        {
            foreach (var field in StaticFields(type))
            {
                if (!typeof(INoireChoice).IsAssignableFrom(field.FieldType))
                    continue;

                try
                {
                    if (field.GetValue(null) is INoireChoice choice)
                        _ = choice.Labels;
                }
                catch (Exception ex)
                {
                    problems?.Add($"The choices of {type.FullName}.{field.Name} could not be read: {ex.GetBaseException().Message}");
                }
            }
        }

        var texts = new ChangelogTexts();

        foreach (var type in types)
        {
            if (type.IsAbstract || type.ContainsGenericParameters || !typeof(IChangelogVersion).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                continue;

            try
            {
                foreach (var version in ((IChangelogVersion)Activator.CreateInstance(type)!).GetVersions())
                    texts.Localize(version);
            }
            catch (Exception ex)
            {
                problems?.Add($"The changelog {type.FullName} could not be read: {ex.GetBaseException().Message}");
            }
        }
    }

    private static Type[] TypesOf(Assembly assembly, ICollection<string>? problems)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            problems?.Add($"Some types of {assembly.GetName().Name} could not be loaded: {ex.LoaderExceptions.FirstOrDefault()?.Message}");
            return [.. ex.Types.Where(static type => type != null).Select(static type => type!)];
        }
    }

    // A type holding declared texts or settings in its static fields, whose static constructor declares them.
    private static bool DeclaresTexts(Type type)
    {
        foreach (var field in StaticFields(type))
        {
            var fieldType = field.FieldType.IsArray ? field.FieldType.GetElementType()! : field.FieldType;

            if (fieldType == typeof(NoireString) || fieldType == typeof(NoirePlural) || typeof(INoireChoice).IsAssignableFrom(fieldType))
                return true;
        }

        return false;
    }

    private static FieldInfo[] StaticFields(Type type)
        => type.ContainsGenericParameters ? [] : type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

    private static void Initialize(Type type, ICollection<string>? problems)
    {
        try
        {
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }
        catch (Exception ex)
        {
            problems?.Add($"{type.FullName} could not be initialized: {ex.GetBaseException().Message}");
        }
    }
}
