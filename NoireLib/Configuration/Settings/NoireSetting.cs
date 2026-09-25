using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace NoireLib.Configuration;

/// <summary>
/// One configuration property with its default and rules, generated for a configuration that names a
/// <see cref="NoireConfigAttribute.SettingsClassName"/>.
/// </summary>
/// <typeparam name="TValue">The property type.</typeparam>
public sealed class NoireSetting<TValue> : INoireSetting, INoireChoice
{
    private readonly Func<TValue> get;
    private readonly Action<TValue> set;
    private TValue[]? choiceValues;
    private NoireString[]? choiceTexts;
    private string[]? choiceLabels;
    private int labelsRevision = -1;

    /// <summary>
    /// Creates a setting. Called by generated code.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="default">The value a fresh configuration has.</param>
    /// <param name="rules">The property's limits.</param>
    /// <param name="get">Reads the property.</param>
    /// <param name="set">Writes the property and saves.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public NoireSetting(string name, TValue @default, NoireRules<TValue> rules, Func<TValue> get, Action<TValue> set)
    {
        Name = name;
        Default = @default;
        Rules = rules;
        this.get = get;
        this.set = set;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>The value a fresh configuration has.</summary>
    public TValue Default { get; }

    /// <summary>The property's limits.</summary>
    public NoireRules<TValue> Rules { get; }

    /// <summary>
    /// The current value. Setting it applies <see cref="Rules"/> and saves the configuration when the value changed.
    /// </summary>
    public TValue Value
    {
        get => get();
        set
        {
            var applied = Rules.Apply(value);

            if (!EqualityComparer<TValue>.Default.Equals(get(), applied))
                set(applied);
        }
    }

    /// <inheritdoc/>
    public bool IsModified => !EqualityComparer<TValue>.Default.Equals(get(), Default);

    /// <inheritdoc/>
    public void Reset() => Value = Default;

    object? INoireSetting.Boxed
    {
        get => Value;
        set => Value = (TValue)value!;
    }

    Type INoireSetting.ValueType => typeof(TValue);

    // Labels are the texts declared under "<Enum>.<Value>", else the value's name, rebuilt after a language change.
    string[] INoireChoice.Labels
    {
        get
        {
            BuildChoices();

            if (labelsRevision != NoireLanguages.Revision)
            {
                for (var i = 0; i < choiceTexts!.Length; i++)
                    choiceLabels![i] = choiceTexts[i].Text;

                labelsRevision = NoireLanguages.Revision;
            }

            return choiceLabels!;
        }
    }

    int INoireChoice.Index
    {
        get
        {
            BuildChoices();
            return Math.Max(0, Array.IndexOf(choiceValues!, Value));
        }
    }

    object? INoireChoice.ValueAt(int index)
    {
        BuildChoices();
        return choiceValues![index];
    }

    private void BuildChoices()
    {
        if (choiceValues != null)
            return;

        var values = typeof(TValue).IsEnum ? DeclaredValues() : [];
        choiceTexts = new NoireString[values.Length];
        choiceLabels = new string[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            var name = values[i]!.ToString()!;
            var key = typeof(TValue).Name + "." + name;
            choiceTexts[i] = NoireLanguages.Find(key) ?? new NoireString(key, name);
        }

        choiceValues = values;
    }

    // Enum.GetValues sorts by value; a choice lists the values in the order the enum declares them.
    private static TValue[] DeclaredValues()
    {
        var fields = typeof(TValue).GetFields(BindingFlags.Public | BindingFlags.Static);
        Array.Sort(fields, static (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

        var values = new TValue[fields.Length];

        for (var i = 0; i < fields.Length; i++)
            values[i] = (TValue)fields[i].GetValue(null)!;

        return values;
    }
}
