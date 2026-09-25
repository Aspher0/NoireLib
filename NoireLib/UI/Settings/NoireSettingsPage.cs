using Dalamud.Bindings.ImGui;
using NoireLib.Configuration;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// What a settings page declares each frame, in order. Rows are drawn once the page's method returns, grouped under
/// their sections, filtered by the search and laid out by the active skin's <see cref="ISettingsSkin"/>.
/// </summary>
public sealed class NoireSettingsPage
{
    private const float LanguageWidth = 240f;

    private readonly List<SettingEntry> entries = [];
    private readonly List<int> group = [];
    private SettingRowInfo[] infos = new SettingRowInfo[16];
    private int count;
    private string[] languageLabels = [];
    private IReadOnlyList<NoireLanguageInfo>? languageSource;
    private int languageRevision = -1;
    private bool showCredits;

    internal NoireSettingsPage()
    {
    }

    /// <summary>The active skin, for rows only one skin shows.</summary>
    public NoireSkin Skin => NoireSkins.Active;

    /// <summary>The active skin's controls, for custom content.</summary>
    public IControlSkin Controls => NoireSkins.Active.Controls;

    internal string Search { get; private set; } = string.Empty;

    // The window drawing the page; confirmations are asked over it.
    internal NoireSkinnedWindowBase? Window { get; private set; }

    internal bool HasMatches { get; private set; }

    /// <summary>A section title, hidden when a search leaves it no row.</summary>
    /// <param name="title">The title.</param>
    public void Section(NoireString title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Next(SettingEntryKind.Section).Title = title;
    }

    /// <summary>A true or false setting.</summary>
    /// <param name="setting">The setting.</param>
    /// <param name="name">The label.</param>
    /// <param name="help">The help text.</param>
    /// <returns>The row, for its extras.</returns>
    public SettingRow Toggle(NoireSetting<bool> setting, NoireString name, NoireString? help = null)
        => Row(SettingEntryKind.Toggle, setting, name, help);

    /// <summary>An enum setting, its values in declaration order, each labelled with the text declared under <c>&lt;Enum&gt;.&lt;Value&gt;</c>.</summary>
    /// <typeparam name="TEnum">The enum.</typeparam>
    /// <param name="setting">The setting.</param>
    /// <param name="name">The label.</param>
    /// <param name="help">The help text.</param>
    /// <returns>The row, for its extras.</returns>
    public SettingRow Choice<TEnum>(NoireSetting<TEnum> setting, NoireString name, NoireString? help = null) where TEnum : struct, Enum
        => Row(SettingEntryKind.Choice, setting, name, help);

    /// <summary>A whole number setting, bounded by its rules.</summary>
    /// <param name="setting">The setting.</param>
    /// <param name="name">The label.</param>
    /// <param name="help">The help text.</param>
    /// <returns>The row, for its extras.</returns>
    public SettingRow Number(NoireSetting<int> setting, NoireString name, NoireString? help = null)
        => Row(SettingEntryKind.Number, setting, name, help);

    /// <summary>A duration setting, bounded by its rules.</summary>
    /// <param name="setting">The setting.</param>
    /// <param name="name">The label.</param>
    /// <param name="help">The help text.</param>
    /// <returns>The row, for its extras.</returns>
    public SettingRow Duration(NoireSetting<TimeSpan> setting, NoireString name, NoireString? help = null)
        => Row(SettingEntryKind.Duration, setting, name, help);

    /// <summary>A row whose control the plugin draws, in the area the skin gives.</summary>
    /// <param name="name">The label; its key is the row's id.</param>
    /// <param name="help">The help text.</param>
    /// <param name="draw">Draws the control.</param>
    /// <returns>The row, for its extras.</returns>
    public SettingRow CustomRow(NoireString name, NoireString? help, SettingRowDraw draw)
    {
        ArgumentNullException.ThrowIfNull(draw);

        var row = Row(SettingEntryKind.CustomRow, null, name, help);
        entries[count - 1].Draw = draw;
        return row;
    }

    /// <summary>A notice between rows, hidden while searching.</summary>
    /// <param name="tone">The tone.</param>
    /// <param name="text">The text.</param>
    /// <param name="detail">A muted second paragraph.</param>
    public void Notice(NoticeTone tone, NoireString text, NoireString? detail = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var entry = Next(SettingEntryKind.Notice);
        entry.Tone = tone;
        entry.Title = text;
        entry.Help = detail;
    }

    /// <summary>The language picker, each language with its translation progress, and a button opening <see cref="NoireTranslationEditor"/>.</summary>
    public void Language() => Next(SettingEntryKind.Language);

    /// <summary>The skin picker, the colour editor and the layout of every skinned window.</summary>
    public void Appearance() => Next(SettingEntryKind.Appearance);

    /// <summary>Anything else, drawn in place with <see cref="Controls"/>; hidden while searching.</summary>
    /// <param name="draw">Draws the content.</param>
    public void Custom(Action<NoireSettingsPage> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        Next(SettingEntryKind.Custom).Custom = draw;
    }

    internal void Begin(NoireSkinnedWindowBase? window, string search)
    {
        Window = window;
        Search = search;
        count = 0;
        HasMatches = false;
    }

    // Whether the search leaves anything on this page, asked before drawing so results get a page heading.
    internal bool HasAnyMatch()
    {
        for (var i = 0; i < count; i++)
        {
            var entry = entries[i];

            if (entry.IsRow ? Matches(entry) : MatchesLabel(entry))
                return true;
        }

        return false;
    }

    // Draws what the page declared: sections with their groups of rows, and what sits between them.
    internal void Flush()
    {
        var skin = NoireSkins.Active.Settings;
        NoireString? section = null;

        for (var i = 0; i <= count; i++)
        {
            var entry = i < count ? entries[i] : null;

            if (entry != null && entry.IsRow)
            {
                if (Matches(entry))
                    group.Add(i);

                continue;
            }

            if (group.Count > 0)
            {
                DrawSection(skin, ref section);
                DrawGroup(skin);
            }

            if (entry == null)
                break;

            switch (entry.Kind)
            {
                case SettingEntryKind.Section:
                    section = entry.Title;
                    break;
                case SettingEntryKind.Notice when Search.Length == 0:
                    DrawSection(skin, ref section);
                    Controls.Notice(entry.Tone, entry.Title!.Text, entry.Help?.Text);
                    break;
                case SettingEntryKind.Custom when Search.Length == 0:
                    DrawSection(skin, ref section);
                    entry.Custom!(this);
                    break;
                case SettingEntryKind.Language when MatchesLabel(entry):
                    DrawSection(skin, ref section);
                    DrawLanguage();
                    HasMatches = true;
                    break;
                case SettingEntryKind.Appearance when MatchesLabel(entry):
                    DrawSection(skin, ref section);
                    DrawAppearance();
                    HasMatches = true;
                    break;
            }
        }
    }

    // Writes a value, or asks the window's confirmation first when the row asks for one on that value.
    internal void Apply(SettingEntry entry, object? value)
    {
        if (entry.NeedsConfirmation(value) && Window != null)
        {
            AskThenApply(Window.Ask, entry.Confirm!, entry.Setting!, entry.OnChange, value);
            return;
        }

        entry.Setting!.Boxed = value;
        entry.OnChange?.Invoke();
    }

    // Separate from Apply: the closure is only built on the frame a confirmation is asked.
    private static void AskThenApply(NoireAsk ask, NoireConfirm confirm, INoireSetting setting, Action? changed, object? value)
        => ask.Ask(confirm, confirmed =>
        {
            if (!confirmed)
                return;

            setting.Boxed = value;
            changed?.Invoke();
        });

    private static void DrawSection(ISettingsSkin skin, ref NoireString? section)
    {
        if (section == null)
            return;

        skin.Section(section.Text);
        section = null;
    }

    private void DrawGroup(ISettingsSkin skin)
    {
        if (infos.Length < group.Count)
            infos = new SettingRowInfo[group.Count * 2];

        for (var i = 0; i < group.Count; i++)
            infos[i] = Info(entries[group[i]]);

        skin.BeginRows(infos.AsSpan(0, group.Count));

        for (var i = 0; i < group.Count; i++)
        {
            var entry = entries[group[i]];
            var area = skin.Row(infos[i], entry.Control, out var reset);

            if (reset && entry.Setting != null)
            {
                entry.Setting.Reset();
                entry.OnChange?.Invoke();
            }

            DrawControl(skin, entry, area);
        }

        skin.EndRows();
        group.Clear();
        HasMatches = true;
    }

    private void DrawControl(ISettingsSkin skin, SettingEntry entry, in SettingRowArea area)
    {
        ImGui.SetCursorScreenPos(area.Min);
        var id = entry.Id;

        switch (entry.Kind)
        {
            case SettingEntryKind.Toggle:
            {
                var value = ((NoireSetting<bool>)entry.Setting!).Value;

                if (skin.Toggle(area, id, ref value))
                    Apply(entry, value);

                break;
            }

            case SettingEntryKind.Choice:
            {
                var choice = (INoireChoice)entry.Setting!;
                var index = choice.Index;

                if (skin.Choice(area, id, choice.Labels, ref index))
                    Apply(entry, choice.ValueAt(index));

                break;
            }

            case SettingEntryKind.Number:
            {
                var setting = (NoireSetting<int>)entry.Setting!;
                var rules = setting.Rules;
                var value = setting.Value;

                if (skin.Number(area, id, ref value, rules.HasMin ? rules.Min : int.MinValue, rules.HasMax ? rules.Max : int.MaxValue))
                    Apply(entry, value);

                break;
            }

            case SettingEntryKind.Duration:
            {
                var setting = (NoireSetting<TimeSpan>)entry.Setting!;
                var rules = setting.Rules;
                var value = setting.Value;

                if (skin.Duration(area, id, ref value, rules.HasMin ? rules.Min : TimeSpan.Zero, rules.HasMax ? rules.Max : TimeSpan.MaxValue))
                    Apply(entry, value);

                break;
            }

            case SettingEntryKind.CustomRow:
                entry.Draw!(in area);
                break;
        }
    }

    private void DrawLanguage()
    {
        var localizer = NoireLanguages.Localizer;

        if (localizer == null)
            return;

        var available = localizer.Languages;

        if (!ReferenceEquals(languageSource, available) || languageRevision != NoireLanguages.Revision)
        {
            languageLabels = new string[available.Count];

            for (var i = 0; i < available.Count; i++)
            {
                var info = available[i];
                languageLabels[i] = (info.Translated >= info.Total
                    ? info.NativeName
                    : NoireStrings.LanguageProgress.With("name", info.NativeName, "percent", NoireLanguages.Number(info.Translated * 100 / Math.Max(1, info.Total))))
                    + (info.Outdated > 0 ? " - " + NoireStrings.LanguageOutdated.For(info.Outdated) : string.Empty);
            }

            languageSource = available;
            languageRevision = NoireLanguages.Revision;
        }

        var current = 0;

        for (var i = 0; i < available.Count; i++)
        {
            if (available[i].IsActive)
                current = i;
        }

        var controls = Controls;

        if (controls.Combo("noire-language", languageLabels, ref current, NoireUI.Scaled(LanguageWidth)))
            localizer.SetCurrentLocale(available[current].Code);

        ImGui.SameLine();

        if (controls.Button("noire-translate", NoireStrings.Translate.Text, NoireIcon.Language, ButtonTone.Ghost))
            NoireTranslationEditor.Open();

        // The people the language files credit, folded under the translate button.
        var credits = NoireLanguages.CreditLines;

        if (credits.Count == 0)
            return;

        ImGui.SameLine();

        if (controls.Button("noire-credits", NoireStrings.TranslationCredits.Text, showCredits ? NoireIcon.Collapse : NoireIcon.Expand, ButtonTone.Ghost))
            showCredits = !showCredits;

        if (!showCredits)
            return;

        using var muted = UiPush.Color(ImGuiCol.Text, NoireTheme.Current.Resolve(ThemeColor.TextMuted));

        foreach (var line in credits)
            ImGui.TextWrapped(line);
    }

    private static void DrawAppearance()
    {
        var active = NoireSkins.Active;
        var controls = active.Controls;

        if (active.Settings.SkinPicker(NoireSkins.All, active, out var picked))
            NoireSkins.Use(picked);

        if (controls.Button("noire-colors", NoireStrings.EditColors.Text, NoireIcon.Palette))
            NoireThemeEditor.Open();

        ImGui.SameLine();

        if (controls.Button("noire-colors-reset", NoireStrings.ResetColors.Text, null, ButtonTone.Ghost))
            NoireSkins.ResetColors(active);

        var windows = NoireSkinnedWindowBase.All;

        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];

            if (!window.HasLayout)
                continue;

            ImGui.PushID(window.Id);

            if (controls.Button("layout", NoireStrings.ArrangeWindow.With("window", window.Title.Text), NoireIcon.Layout, ButtonTone.Ghost))
            {
                window.Open();
                window.EditingLayout = true;
            }

            ImGui.SameLine();

            if (controls.Button("layout-reset", NoireStrings.ResetLayout.Text, null, ButtonTone.Ghost))
                window.ResetLayout();

            ImGui.PopID();
        }
    }

    private SettingRow Row(SettingEntryKind kind, INoireSetting? setting, NoireString name, NoireString? help)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (kind != SettingEntryKind.CustomRow)
            ArgumentNullException.ThrowIfNull(setting);

        var entry = Next(kind);
        entry.Setting = setting;
        entry.Name = name;
        entry.Help = help;
        return new SettingRow(entry);
    }

    private SettingEntry Next(SettingEntryKind kind)
    {
        if (count == entries.Count)
            entries.Add(new SettingEntry());

        var entry = entries[count++];
        entry.Reset(kind);
        return entry;
    }

    private bool Matches(SettingEntry entry)
    {
        if (entry.HiddenWhen?.Invoke() == true)
            return false;

        if (Search.Length == 0)
            return true;

        return entry.Name!.Text.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || (entry.Help?.Text.Contains(Search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (entry.Setting?.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    // The language and appearance entries match a search by their label.
    private bool MatchesLabel(SettingEntry entry)
    {
        var label = entry.Kind switch
        {
            SettingEntryKind.Language => NoireStrings.LanguageLabel,
            SettingEntryKind.Appearance => NoireStrings.SkinLabel,
            _ => null,
        };

        return label != null && (Search.Length == 0 || label.Text.Contains(Search, StringComparison.OrdinalIgnoreCase));
    }

    private static SettingRowInfo Info(SettingEntry entry)
    {
        var disabled = entry.DisabledWhen?.Invoke() ?? false;
        var alarmed = entry.AlarmWhen?.Invoke() ?? false;
        var alarm = alarmed ? entry.Alarm?.Text : entry.AlarmText?.Invoke();

        return new SettingRowInfo(
            entry.Id,
            entry.Name!.Text,
            entry.Help?.Text,
            entry.Setting?.IsModified ?? false,
            disabled,
            disabled ? entry.DisabledReason?.Text : null,
            alarm);
    }
}
