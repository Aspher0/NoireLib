using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>How a skin lays out settings pages and forms. The page decides what is shown; the skin decides how.</summary>
public interface ISettingsSkin
{
    /// <summary>The page tabs.</summary>
    /// <param name="pages">The tabs.</param>
    /// <param name="current">The page shown.</param>
    /// <returns>The page picked.</returns>
    int Tabs(ReadOnlySpan<TabItem> pages, int current);

    /// <summary>A section title above a group of rows.</summary>
    /// <param name="title">The title.</param>
    void Section(string title);

    /// <summary>Starts a group of rows, all given up front so the columns can fit the longest name.</summary>
    /// <param name="rows">The rows of the group.</param>
    void BeginRows(ReadOnlySpan<SettingRowInfo> rows);

    /// <summary>One row: its name, help, alarm, modified marker and reset.</summary>
    /// <param name="row">What the row shows.</param>
    /// <param name="control">The control the row holds.</param>
    /// <param name="resetClicked">True on the frame the user asks for the default back.</param>
    /// <returns>Where the row's control goes.</returns>
    SettingRowArea Row(in SettingRowInfo row, SettingControl control, out bool resetClicked);

    /// <summary>Ends the group of rows.</summary>
    void EndRows();

    /// <summary>A true or false control in a row.</summary>
    /// <param name="area">The row's control area.</param>
    /// <param name="id">An id unique in the group.</param>
    /// <param name="value">The value.</param>
    /// <returns>True on the frame the value changes.</returns>
    bool Toggle(in SettingRowArea area, string id, ref bool value);

    /// <summary>A choice control in a row.</summary>
    /// <param name="area">The row's control area.</param>
    /// <param name="id">An id unique in the group.</param>
    /// <param name="options">The option labels.</param>
    /// <param name="index">The selected option.</param>
    /// <returns>True on the frame the selection changes.</returns>
    bool Choice(in SettingRowArea area, string id, ReadOnlySpan<string> options, ref int index);

    /// <summary>A whole number control in a row.</summary>
    /// <param name="area">The row's control area.</param>
    /// <param name="id">An id unique in the group.</param>
    /// <param name="value">The value.</param>
    /// <param name="min">The lowest value.</param>
    /// <param name="max">The highest value.</param>
    /// <returns>True on the frame the value changes.</returns>
    bool Number(in SettingRowArea area, string id, ref int value, int min, int max);

    /// <summary>A duration control in a row.</summary>
    /// <param name="area">The row's control area.</param>
    /// <param name="id">An id unique in the group.</param>
    /// <param name="value">The value.</param>
    /// <param name="min">The shortest value.</param>
    /// <param name="max">The longest value.</param>
    /// <returns>True on the frame the value changes.</returns>
    bool Duration(in SettingRowArea area, string id, ref TimeSpan value, TimeSpan min, TimeSpan max);

    /// <summary>The skin picker: one card per skin with its <see cref="NoireSkin.DrawPreview"/>.</summary>
    /// <param name="skins">The registered skins.</param>
    /// <param name="active">The active skin.</param>
    /// <param name="picked">The skin clicked, or <paramref name="active"/>.</param>
    /// <returns>True on the frame another skin is picked.</returns>
    bool SkinPicker(IReadOnlyList<NoireSkin> skins, NoireSkin active, out NoireSkin picked);
}
