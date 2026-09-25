using Dalamud.Bindings.ImGui;
using System;

namespace NoireLib.UI;

/// <summary>
/// Rows of name, control and help laid out by the active skin exactly like a settings page, for values that are not
/// configuration. Declare every row up front, draw them in order, then call <see cref="End"/>.
/// </summary>
public ref struct NoireForm
{
    private readonly ISettingsSkin skin;
    private readonly ReadOnlySpan<SettingRowInfo> rows;
    private int next;

    private NoireForm(ReadOnlySpan<SettingRowInfo> rows)
    {
        skin = NoireSkins.Active.Settings;
        this.rows = rows;
        next = 0;
        skin.BeginRows(rows);
    }

    /// <summary>Starts a form with all its rows: the skin fits its columns to them.</summary>
    /// <param name="rows">The rows, in drawing order.</param>
    /// <returns>The form.</returns>
    public static NoireForm Begin(ReadOnlySpan<SettingRowInfo> rows) => new(rows);

    /// <summary>The next row; draw its control at the area returned.</summary>
    /// <param name="control">The control the row holds.</param>
    /// <returns>Where the control goes.</returns>
    /// <exception cref="InvalidOperationException">Thrown past the last declared row.</exception>
    public SettingRowArea Row(SettingControl control)
    {
        if (next >= rows.Length)
            throw new InvalidOperationException("The form has no row left.");

        return skin.Row(rows[next++], control, out _);
    }

    /// <summary>A true or false control in the next row.</summary>
    /// <param name="id">An id unique in the form.</param>
    /// <param name="value">The value.</param>
    /// <returns>True on the frame the value changes.</returns>
    public bool Toggle(string id, ref bool value)
    {
        var area = Row(SettingControl.Toggle);
        ImGui.SetCursorScreenPos(area.Min);
        return skin.Toggle(area, id, ref value);
    }

    /// <summary>Ends the form.</summary>
    public readonly void End() => skin.EndRows();
}
