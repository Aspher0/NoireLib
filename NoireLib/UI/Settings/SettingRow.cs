using NoireLib.Localizer;
using System;

namespace NoireLib.UI;

/// <summary>A declared settings row, for the extras written after it on the same line; they apply to the frame they are declared in.</summary>
public readonly struct SettingRow
{
    private readonly SettingEntry entry;

    internal SettingRow(SettingEntry entry) => this.entry = entry;

    /// <summary>Runs a callback after the user changes or resets the value.</summary>
    /// <param name="changed">The callback.</param>
    /// <returns>The row.</returns>
    public SettingRow OnChange(Action changed)
    {
        entry.OnChange = changed;
        return this;
    }

    /// <summary>Asks a confirmation over the settings window before the value becomes <paramref name="when"/>.</summary>
    /// <typeparam name="TValue">The setting's type.</typeparam>
    /// <param name="when">The value that needs a confirmation.</param>
    /// <param name="confirm">The confirmation.</param>
    /// <returns>The row.</returns>
    public SettingRow Confirm<TValue>(TValue when, NoireConfirm confirm)
    {
        ArgumentNullException.ThrowIfNull(confirm);

        entry.SetTrigger(when);
        entry.Confirm = confirm;
        return this;
    }

    /// <summary>Shows the name in the danger colour, with a warning on hover, while a condition holds.</summary>
    /// <param name="when">The condition, read each frame.</param>
    /// <param name="alarm">The warning.</param>
    /// <returns>The row.</returns>
    public SettingRow Alarm(Func<bool> when, NoireString alarm)
    {
        entry.AlarmWhen = when;
        entry.Alarm = alarm;
        return this;
    }

    /// <summary>Shows the name in the danger colour, with the text returned as a warning on hover, while that text is not <see langword="null"/>.</summary>
    /// <param name="alarm">The warning, read each frame; <see langword="null"/> while there is none.</param>
    /// <returns>The row.</returns>
    public SettingRow Alarm(Func<string?> alarm)
    {
        entry.AlarmText = alarm;
        return this;
    }

    /// <summary>Disables the row while a condition holds, with a reason on hover.</summary>
    /// <param name="when">The condition, read each frame.</param>
    /// <param name="reason">The reason.</param>
    /// <returns>The row.</returns>
    public SettingRow DisabledWhen(Func<bool> when, NoireString reason)
    {
        entry.DisabledWhen = when;
        entry.DisabledReason = reason;
        return this;
    }

    /// <summary>Leaves the row out while a condition holds, search results included.</summary>
    /// <param name="when">The condition, read each frame.</param>
    /// <returns>The row.</returns>
    public SettingRow HiddenWhen(Func<bool> when)
    {
        entry.HiddenWhen = when;
        return this;
    }
}
