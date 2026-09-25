using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using NoireLib.Internal.Helpers;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;

namespace NoireLib.UI;

/// <summary>
/// The in-game translation editor: each declared text beside its translation, live edits, a save to the user's folder
/// and a copy of the language file.
/// </summary>
public sealed class NoireTranslationEditor : NoireWindow
{
    private const string WindowId = "###noire-translate";
    private const ImGuiTableFlags TableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;

    private static NoireTranslationEditor? instance;

    private readonly List<TranslationRow> rows = [];

    // What each language held when first opened and after each save: what Revert restores and Save compares against.
    private readonly Dictionary<string, Dictionary<string, SavedTranslation>> savedByLanguage = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct SavedTranslation(string Text, string? From);
    private string language = string.Empty;
    private string newLanguage = string.Empty;
    private string? checkedLanguage;
    private bool newLanguageValid;
    private string search = string.Empty;
    private bool searchContains = true;
    private bool missingOnly = true;
    private bool dirty;
    private int builtRevision = -1;
    private volatile string status = string.Empty;
    private volatile bool savingFile;
    private int titleRevision = -1;
    private IReadOnlyList<NoireLanguageInfo>? labelsFor;
    private string[] languageLabels = [];

    // The column widths the last drawn row measured; the heights of the rows follow them.
    private float sourceWidth = 300f;
    private float translationWidth = 300f;

    // The color every occurrence of the search is marked in, resolved once per frame.
    private uint markColor;

    internal NoireTranslationEditor()
        : base(NoireStrings.TranslateTitle.Text + WindowId)
    {
        Size = new Vector2(900f, 620f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    internal string Language => language;

    internal int RowCount => rows.Count;

    /// <summary>Opens the editor, on the active language when it is not the source language.</summary>
    public static void Open()
    {
        if (instance == null)
        {
            if (NoireService.NoireWindowSystem is not { } windows)
                return;

            instance = new NoireTranslationEditor();
            windows.AddWindow(instance);
            NoireLibMain.RegisterOnDispose("NoireTranslationEditor", static () => instance = null);
        }

        if (instance.language.Length == 0 && NoireLanguages.Localizer is { } localizer
            && !string.Equals(localizer.CurrentLocale, localizer.SourceLanguage, StringComparison.OrdinalIgnoreCase)
            && !NoireLanguages.IsPseudo(localizer.CurrentLocale))
        {
            instance.Select(localizer.CurrentLocale);
        }

        instance.IsOpen = true;
        instance.BringToFront();
    }

    /// <summary>Follows the language in the title.</summary>
    public override void PreDraw()
    {
        if (titleRevision == NoireLanguages.Revision)
            return;

        titleRevision = NoireLanguages.Revision;
        WindowName = NoireStrings.TranslateTitle.Text + WindowId;
    }

    /// <summary>Draws the language bar, the filters, the texts and the save and preview buttons.</summary>
    public override void Draw()
    {
        if (!UiDraw.Available || NoireLanguages.Localizer is not { } localizer)
            return;

        DrawLanguageBar(localizer);

        if (language.Length == 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(NoireStrings.TranslatePickLanguage.Text);
            ImGui.PopTextWrapPos();
            return;
        }

        DrawFilters();
        Rebuild(localizer);
        DrawTable(localizer);
        DrawFooter(localizer);
    }

    // Picks the language to edit; an unknown or invalid code is ignored.
    internal void Select(string code)
    {
        if (!TryNormalize(code, out var normalized))
            return;

        language = normalized;
        builtRevision = -1;
        dirty = NoireLanguages.Localizer is { } localizer && HasUnsavedChanges(localizer);
        status = string.Empty;
    }

    // An empty text removes the translation. A translation written against a source is up to date with it.
    internal void Edit(NoireLocalizer localizer, string key, string text, string? source = null)
    {
        if (text.Length == 0)
        {
            localizer.RemoveTranslation(language, key);
        }
        else
        {
            localizer.AddTranslation(language, key, text);

            if (source != null)
                localizer.SetTranslatedFrom(language, key, source);
        }

        builtRevision = NoireLanguages.Revision;
        dirty = true;
    }

    // Keeps an outdated translation as it is, now counted as made from the current source.
    internal void Validate(NoireLocalizer localizer, string key, string source)
    {
        localizer.SetTranslatedFrom(language, key, source);
        builtRevision = NoireLanguages.Revision;
        dirty = true;
    }

    // Writes the language to the user's folder; the path, or null when it failed.
    internal string? Save(NoireLocalizer localizer, string? folder = null)
    {
        var path = folder == null ? localizer.SaveUserLanguageFile(language) : localizer.SaveUserLanguageFile(language, folder);
        status = path != null ? NoireStrings.TranslateSaved.With("path", path) : NoireStrings.TranslateSaveFailed.Text;

        if (path != null)
        {
            dirty = false;
            TakeSaved(localizer);
        }

        return path;
    }

    // Puts a translation back as it was last saved, text and source alike.
    internal void Revert(NoireLocalizer localizer, string key)
    {
        var saved = Saved(localizer).GetValueOrDefault(key);

        if (saved.Text is not { Length: > 0 })
        {
            localizer.RemoveTranslation(language, key);
        }
        else
        {
            localizer.AddTranslation(language, key, saved.Text);
            localizer.SetTranslatedFrom(language, key, saved.From);
        }

        builtRevision = NoireLanguages.Revision;
        dirty = HasUnsavedChanges(localizer);
    }

    // What the edited language held when it was last saved, captured the first time it is asked for.
    private Dictionary<string, SavedTranslation> Saved(NoireLocalizer localizer)
    {
        if (!savedByLanguage.TryGetValue(language, out var saved))
            saved = TakeSaved(localizer);

        return saved;
    }

    private Dictionary<string, SavedTranslation> TakeSaved(NoireLocalizer localizer)
    {
        var saved = new Dictionary<string, SavedTranslation>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, text) in localizer.GetLocaleTranslations(language))
            saved[key] = new SavedTranslation(text, localizer.TranslatedFrom(language, key));

        savedByLanguage[language] = saved;
        return saved;
    }

    // Whether any translation, or the source it was made from, differs from what was last saved.
    private bool HasUnsavedChanges(NoireLocalizer localizer)
    {
        var saved = Saved(localizer);
        var current = localizer.GetLocaleTranslations(language);

        if (current.Count != saved.Count)
            return true;

        foreach (var (key, text) in current)
        {
            if (!saved.TryGetValue(key, out var kept) || !string.Equals(kept.Text, text, StringComparison.Ordinal)
                || !string.Equals(kept.From, localizer.TranslatedFrom(language, key), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void DrawLanguageBar(NoireLocalizer localizer)
    {
        var languages = localizer.Languages;
        RefreshLabels(languages);

        var preview = NoireStrings.LanguageLabel.Text;

        for (var i = 0; i < languages.Count; i++)
        {
            if (string.Equals(languages[i].Code, language, StringComparison.OrdinalIgnoreCase))
                preview = languageLabels[i];
        }

        if (preview == NoireStrings.LanguageLabel.Text && language.Length > 0)
            preview = language;

        ImGui.SetNextItemWidth(NoireUI.Scaled(260f));

        if (ImGui.BeginCombo("##language", preview))
        {
            for (var i = 0; i < languages.Count; i++)
            {
                if (IsSourceOrPseudo(localizer, languages[i].Code))
                    continue;

                ImGui.PushID(i);

                if (ImGui.Selectable(languageLabels[i], string.Equals(languages[i].Code, language, StringComparison.OrdinalIgnoreCase)))
                    Select(languages[i].Code);

                ImGui.PopID();
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(NoireUI.Scaled(100f));
        ImGui.InputTextWithHint("##new", "es, pt-BR", ref newLanguage, 16);

        if (!ReferenceEquals(checkedLanguage, newLanguage))
        {
            checkedLanguage = newLanguage;
            newLanguageValid = TryNormalize(newLanguage, out var normalized) && !IsSourceOrPseudo(localizer, normalized);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!newLanguageValid);

        if (ImGui.Button(NoireStrings.TranslateNew.Text))
        {
            Select(newLanguage);
            newLanguage = string.Empty;
        }

        ImGui.EndDisabled();
    }

    private void DrawFilters()
    {
        var style = ImGui.GetStyle();
        var box = ImGui.GetFrameHeight() + style.ItemInnerSpacing.X + style.ItemSpacing.X;
        var checkboxes = (box * 2f) + ImGui.CalcTextSize(NoireStrings.TranslateContains.Text).X + ImGui.CalcTextSize(NoireStrings.TranslateMissingOnly.Text).X;
        ImGui.SetNextItemWidth(-checkboxes);

        if (ImGui.InputTextWithHint("##search", NoireStrings.TranslateSearch.Text, ref search, 128))
            builtRevision = -1;

        ImGui.SameLine();

        if (ImGui.Checkbox(NoireStrings.TranslateContains.Text, ref searchContains))
            builtRevision = -1;

        ImGui.SameLine();

        if (ImGui.Checkbox(NoireStrings.TranslateMissingOnly.Text, ref missingOnly))
            builtRevision = -1;
    }

    private void DrawTable(NoireLocalizer localizer)
    {
        var height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();

        if (!ImGui.BeginTable("##texts", 3, TableFlags, new Vector2(0f, MathF.Max(1f, height))))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(NoireStrings.TranslateKey.Text, ImGuiTableColumnFlags.WidthFixed, NoireUI.Scaled(200f));
        ImGui.TableSetupColumn(NoireStrings.TranslateSource.Text, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(NoireStrings.TranslateTranslation.Text, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        var mark = NoireTheme.Current.Resolve(ThemeColor.Warning);
        mark.W *= 0.4f;
        markColor = ImGui.GetColorU32(mark);

        // Rows out of view keep their height undrawn. The test spans the whole table: a window pushed off the left keeps its rows.
        var tableLeft = ImGui.GetWindowPos().X;
        var tableRight = tableLeft + ImGui.GetWindowSize().X;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowHeight = RowHeight(row);

            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            ImGui.TableNextColumn();

            var top = ImGui.GetCursorScreenPos().Y;

            if (ImGui.IsRectVisible(new Vector2(tableLeft, top), new Vector2(tableRight, top + rowHeight)))
                DrawRow(localizer, row, rowHeight);
        }

        ImGui.EndTable();
    }

    private float RowHeight(TranslationRow row)
    {
        if (!ReferenceEquals(row.MeasuredText, row.Translation) || row.MeasuredOutdated != (row.OutdatedFrom != null)
            || row.MeasuredModified != row.Modified
            || !ReferenceEquals(row.MeasuredWarning, row.TagWarning) || row.MeasuredSourceWidth != sourceWidth
            || row.MeasuredTranslationWidth != translationWidth || row.MeasuredFont != ImGui.GetFontSize())
        {
            row.MeasuredText = row.Translation;
            row.MeasuredOutdated = row.OutdatedFrom != null;
            row.MeasuredModified = row.Modified;
            row.MeasuredWarning = row.TagWarning;
            row.MeasuredSourceWidth = sourceWidth;
            row.MeasuredTranslationWidth = translationWidth;
            row.MeasuredFont = ImGui.GetFontSize();

            // Wrapped as the translation field wraps: its marks know every line break.
            (row.SourceDisplay, row.SourceBreaks) = SoftWrap.Wrap(row.Source, sourceWidth, NoireTextArea.Advance);
            var source = ImGui.CalcTextSize(row.SourceDisplay).Y;
            var translation = NoireTextArea.HeightFor(row.Translation, translationWidth);
            row.Height = MathF.Max(ImGui.GetFrameHeight(), MathF.Max(source, translation));

            // The key column holds the tag warning, and an outdated row's flag and button, under the key.
            var keyLines = 1 + (row.TagWarning != null ? 1 : 0) + (row.OutdatedFrom != null ? 1 : 0);
            var keyButtons = (row.OutdatedFrom != null ? 1 : 0) + (row.Modified ? 1 : 0);
            var buttonsHeight = keyButtons == 0 ? 0f : ImGui.GetFrameHeight() + (ImGui.GetFrameHeightWithSpacing() * (keyButtons - 1));
            var keyColumn = (ImGui.GetTextLineHeightWithSpacing() * keyLines) + buttonsHeight;
            row.Height = MathF.Max(row.Height, keyColumn);
        }

        return row.Height;
    }

    // Drawn from the key column, which the table already moved to.
    private void DrawRow(NoireLocalizer localizer, TranslationRow row, float height)
    {
        var wholeWord = !searchContains;

        if (!string.Equals(row.MarkedSearch, search, StringComparison.Ordinal) || row.MarkedWholeWord != wholeWord
            || !ReferenceEquals(row.MarkedSourceDisplay, row.SourceDisplay))
        {
            row.MarkedSearch = search;
            row.MarkedWholeWord = wholeWord;
            row.MarkedSourceDisplay = row.SourceDisplay;
            NoireTextArea.Mark(row.Key, row.Key, [], search, wholeWord, row.KeyMarks);
            NoireTextArea.Mark(row.Source, row.SourceDisplay, row.SourceBreaks, search, wholeWord, row.SourceMarks);
        }

        NoireTextArea.DrawMarks(ImGui.GetCursorScreenPos(), row.KeyMarks, markColor);
        ImGui.PushStyleColor(ImGuiCol.Text, NoireTheme.Current.Resolve(ThemeColor.TextMuted));
        ImGui.TextUnformatted(row.Key);
        ImGui.PopStyleColor();

        // Placeholders the translation lost or invented: the text would show a name instead of its value.
        if (row.TagWarning is { } warning)
            ImGui.TextColored(FlagColor(localizer.RejectMissingOrExtraTags), warning);

        // A translation made from an older source: still shown, flagged until it is edited or confirmed.
        if (row.OutdatedFrom is { } from)
        {
            ImGui.TextColored(FlagColor(localizer.RejectOutdated), NoireStrings.TranslateOutdated.Text);

            row.RefreshOutdatedTexts(from);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(row.OutdatedTooltip);

            if (ImGui.SmallButton(row.ValidateLabel))
            {
                Validate(localizer, row.Key, row.Source);
                row.OutdatedFrom = null;
                row.From = row.Source;
            }
        }

        // A translation changed since the last save: put back as saved, with the saved text on hover.
        if (row.Modified)
        {
            if (ImGui.SmallButton(row.RevertLabel))
            {
                Revert(localizer, row.Key);
                row.Translation = row.SavedText;
                row.From = row.SavedFrom;
                row.OutdatedFrom = row.From != null && !string.Equals(row.From, row.Source, StringComparison.Ordinal) ? row.From : null;
                row.CheckTags();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(row.RevertTooltip);
        }

        ImGui.TableNextColumn();
        sourceWidth = ImGui.GetContentRegionAvail().X;
        NoireTextArea.DrawMarks(ImGui.GetCursorScreenPos(), row.SourceMarks, markColor);
        ImGui.TextUnformatted(row.SourceDisplay);

        ImGui.TableNextColumn();
        translationWidth = ImGui.GetContentRegionAvail().X;

        if (NoireTextArea.Draw(row.Id, ref row.Translation, 2048, new Vector2(-1f, height), search, wholeWord, markColor))
        {
            Edit(localizer, row.Key, row.Translation, row.Source);
            row.OutdatedFrom = null;
            row.From = row.Translation.Length > 0 ? row.Source : null;
            row.CheckTags();
        }
    }

    // Orange while the localizer still shows the translation, red when it sets it aside.
    private static Vector4 FlagColor(bool rejected) => NoireTheme.Current.Resolve(rejected ? ThemeColor.Danger : ThemeColor.Warning);

    // The language file written where the user picks, through the system's save dialog.
    private async Task SaveFileAsync(NoireLocalizer localizer, string locale)
    {
        savingFile = true;

        try
        {
            var path = await FileDialogHelper.SaveFileAsync(NoireStrings.TranslateSaveFile.Text, "{.lang}", locale + ".lang", ".lang");

            if (path == null)
                return;

            await File.WriteAllTextAsync(path, localizer.ExportLanguageFile(locale));
            status = NoireStrings.TranslateSaved.With("path", path);
        }
        catch (Exception)
        {
            status = NoireStrings.TranslateSaveFailed.Text;
        }
        finally
        {
            savingFile = false;
        }
    }

    private void DrawFooter(NoireLocalizer localizer)
    {
        ImGui.BeginDisabled(!dirty);

        if (ImGui.Button(NoireStrings.TranslateSave.Text))
            Save(localizer);

        ImGui.EndDisabled();
        ImGui.SameLine();

        ImGui.BeginDisabled(savingFile);

        if (ImGui.Button(NoireStrings.TranslateSaveFile.Text))
            _ = SaveFileAsync(localizer, language);

        ImGui.EndDisabled();

        ImGui.SameLine();

        if (ImGui.Button(NoireStrings.TranslatePreview.Text))
            localizer.SetCurrentLocale(language);

        if (status.Length == 0)
            return;

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, NoireTheme.Current.Resolve(ThemeColor.TextMuted));
        ImGui.TextUnformatted(status);
        ImGui.PopStyleColor();
    }

    // Rebuilt when a filter or a translation changes; an edit typed here marks itself built so its row stays.
    private void Rebuild(NoireLocalizer localizer)
    {
        if (builtRevision == NoireLanguages.Revision)
            return;

        builtRevision = NoireLanguages.Revision;
        rows.Clear();

        var table = localizer.GetLocaleTranslations(language);

        foreach (var text in NoireLanguages.Strings)
            Add(localizer, text.Key, text.Source, table);

        var categories = PluralRules.CategoriesOf(language);

        foreach (var plural in NoireLanguages.Plurals)
        {
            foreach (var category in categories)
            {
                var source = plural.Forms.TryGetValue(category, out var form) ? form : plural.Forms[PluralCategory.Other];
                Add(localizer, plural.Key + "." + PluralRules.Suffix(category), source, table);
            }
        }

        rows.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
    }

    private void Add(NoireLocalizer localizer, string key, string source, IReadOnlyDictionary<string, string> table)
    {
        var translation = table.TryGetValue(key, out var found) ? found : string.Empty;
        var from = translation.Length > 0 ? localizer.TranslatedFrom(language, key) : null;
        var outdated = from != null && !string.Equals(from, source, StringComparison.Ordinal);

        if (missingOnly && translation.Length > 0 && !outdated)
            return;

        if (!Matches(search, searchContains, key, source, translation))
            return;

        var saved = Saved(localizer).GetValueOrDefault(key);
        var row = new TranslationRow(key, source, translation)
        {
            OutdatedFrom = outdated ? from : null,
            From = from,
            SavedText = saved.Text ?? string.Empty,
            SavedFrom = saved.From,
        };

        row.CheckTags();
        rows.Add(row);
    }

    // With contains off, whole words only: "Remplacer" finds "Remplacer une emote" but not "Remplacement".
    internal static bool Matches(string search, bool contains, string key, string source, string translation)
    {
        if (search.Length == 0)
            return true;

        var wholeWord = !contains;
        return TextMatches.Any(key, search, wholeWord) || TextMatches.Any(source, search, wholeWord) || TextMatches.Any(translation, search, wholeWord);
    }

    private void RefreshLabels(IReadOnlyList<NoireLanguageInfo> languages)
    {
        if (ReferenceEquals(labelsFor, languages))
            return;

        labelsFor = languages;
        languageLabels = new string[languages.Count];

        for (var i = 0; i < languages.Count; i++)
        {
            var info = languages[i];
            languageLabels[i] = info.NativeName + " (" + info.Code + ")  " + NoireLanguages.Number(info.Translated) + "/" + NoireLanguages.Number(info.Total)
                + " (" + NoireLanguages.Number(info.Translated * 100 / Math.Max(1, info.Total)) + "%)"
                + (info.Outdated > 0 ? " - " + NoireStrings.LanguageOutdated.For(info.Outdated) : string.Empty);
        }
    }

    private static bool IsSourceOrPseudo(NoireLocalizer localizer, string code)
        => string.Equals(code, localizer.SourceLanguage, StringComparison.OrdinalIgnoreCase)
        || NoireLanguages.IsPseudo(code);

    private static bool TryNormalize(string code, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(code))
            return false;

        try
        {
            var culture = CultureInfo.GetCultureInfo(code.Trim());

            if (culture.Equals(CultureInfo.InvariantCulture) || culture.Name.Length == 0)
                return false;

            normalized = culture.Name;
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private sealed class TranslationRow(string key, string source, string translation)
    {
        public readonly string Key = key;
        public readonly string Source = source;
        public readonly string Id = "##translate." + key;
        public readonly string ValidateId = "##validate." + key;
        public readonly string RevertId = "##revert." + key;

        // The source the translation is currently counted as made from, and the translation and source last saved.
        public string? From;
        public string SavedText = string.Empty;
        public string? SavedFrom;

        public bool Modified => !string.Equals(Translation, SavedText, StringComparison.Ordinal)
            || !string.Equals(From, SavedFrom, StringComparison.Ordinal);

        // The revert button's label and hover, built once per language and saved text rather than on every frame.
        public string RevertLabel
        {
            get
            {
                RefreshRevertTexts();
                return revertLabel;
            }
        }

        public string RevertTooltip
        {
            get
            {
                RefreshRevertTexts();
                return revertTooltip;
            }
        }

        private string revertLabel = string.Empty;
        private string revertTooltip = string.Empty;
        private int revertRevision = -1;
        private string? revertTextsFor;

        private void RefreshRevertTexts()
        {
            if (revertRevision == NoireLanguages.Revision && ReferenceEquals(revertTextsFor, SavedText))
                return;

            revertRevision = NoireLanguages.Revision;
            revertTextsFor = SavedText;
            revertLabel = NoireStrings.TranslateRevert.Text + RevertId;
            revertTooltip = SavedText.Length > 0
                ? NoireStrings.TranslateRevertTo.With("text", SavedText)
                : NoireStrings.TranslateRevertToEmpty.Text;
        }

        // The source an outdated translation was made from; null while the translation is up to date or missing.
        public string? OutdatedFrom;

        // The outdated flag's button label and tooltip, built once per language rather than on every frame.
        public string ValidateLabel = string.Empty;
        public string OutdatedTooltip = string.Empty;
        private int outdatedRevision = -1;
        private string? outdatedTextsFrom;

        public void RefreshOutdatedTexts(string from)
        {
            if (outdatedRevision == NoireLanguages.Revision && ReferenceEquals(outdatedTextsFrom, from))
                return;

            outdatedRevision = NoireLanguages.Revision;
            outdatedTextsFrom = from;
            ValidateLabel = NoireStrings.TranslateValidate.Text + ValidateId;
            OutdatedTooltip = NoireStrings.TranslateOutdatedFrom.With("text", from);
        }
        public string Translation = translation;
        public string? MeasuredText;
        public bool MeasuredOutdated;
        public bool MeasuredModified;
        public string? MeasuredWarning;

        // Which placeholders the translation lost or invented, or null when it keeps them all.
        public string? TagWarning;

        // Built on a rebuild and after an edit, never per frame.
        public void CheckTags()
        {
            if (Translation.Length == 0)
            {
                TagWarning = null;
                return;
            }

            var (missing, unknown) = PlaceholderCheck.Compare(Source, Translation);
            var parts = new List<string>(2);

            if (missing.Length > 0)
                parts.Add(NoireStrings.TranslateMissingTags.With("tags", string.Join(", ", missing)));

            if (unknown.Length > 0)
                parts.Add(NoireStrings.TranslateUnknownTags.With("tags", string.Join(", ", unknown)));

            TagWarning = parts.Count == 0 ? null : string.Join("  ", parts);
        }
        public float MeasuredSourceWidth;
        public float MeasuredTranslationWidth;
        public float MeasuredFont;
        public float Height;

        // The original wrapped at the width of its column.
        public string SourceDisplay = source;
        public int[] SourceBreaks = [];

        // Where the search stands in the key and the original, for the search and the wrapping they were measured for.
        public readonly List<Vector4> KeyMarks = [];
        public readonly List<Vector4> SourceMarks = [];
        public string? MarkedSearch;
        public bool MarkedWholeWord;
        public string? MarkedSourceDisplay;
    }
}
