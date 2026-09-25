using NoireLib.Configuration;
using NoireLib.Localizer;
using System;

namespace NoireLib.UI;

internal enum SettingEntryKind
{
    Section,
    Toggle,
    Choice,
    Number,
    Duration,
    CustomRow,
    Notice,
    Custom,
    Language,
    Appearance,
}

// One declared line of a settings page, pooled and reset each frame.
internal sealed class SettingEntry
{
    public SettingEntryKind Kind;
    public NoireString? Title;
    public INoireSetting? Setting;
    public NoireString? Name;
    public NoireString? Help;
    public Action? OnChange;
    public NoireConfirm? Confirm;
    public Func<bool>? AlarmWhen;
    public NoireString? Alarm;
    public Func<string?>? AlarmText;
    public Func<bool>? DisabledWhen;
    public NoireString? DisabledReason;
    public Func<bool>? HiddenWhen;
    public NoticeTone Tone;
    public Action<NoireSettingsPage>? Custom;
    public SettingRowDraw? Draw;

    // Kept across resets and reused: a row declares the same value type every frame.
    public ConfirmTrigger? Trigger;

    public bool IsRow => Kind is SettingEntryKind.Toggle or SettingEntryKind.Choice or SettingEntryKind.Number or SettingEntryKind.Duration or SettingEntryKind.CustomRow;

    public SettingControl Control => Kind switch
    {
        SettingEntryKind.Toggle => SettingControl.Toggle,
        SettingEntryKind.Choice => SettingControl.Choice,
        SettingEntryKind.Number => SettingControl.Number,
        SettingEntryKind.Duration => SettingControl.Duration,
        _ => SettingControl.Custom,
    };

    public string Id => Setting?.Name ?? Name?.Key ?? string.Empty;

    public void Reset(SettingEntryKind kind)
    {
        Kind = kind;
        Title = null;
        Setting = null;
        Name = null;
        Help = null;
        OnChange = null;
        Confirm = null;
        AlarmWhen = null;
        Alarm = null;
        AlarmText = null;
        DisabledWhen = null;
        DisabledReason = null;
        HiddenWhen = null;
        Custom = null;
        Draw = null;
    }

    public bool NeedsConfirmation(object? value) => Confirm != null && Trigger != null && Trigger.Matches(value);

    public void SetTrigger<TValue>(TValue when)
    {
        if (Trigger is ConfirmTrigger<TValue> typed)
            typed.Value = when;
        else
            Trigger = new ConfirmTrigger<TValue> { Value = when };
    }
}

internal abstract class ConfirmTrigger
{
    public abstract bool Matches(object? value);
}

// Holds the value that asks for a confirmation without boxing it each frame.
internal sealed class ConfirmTrigger<TValue> : ConfirmTrigger
{
    public TValue Value = default!;

    public override bool Matches(object? value) => value is TValue typed && System.Collections.Generic.EqualityComparer<TValue>.Default.Equals(typed, Value);
}
