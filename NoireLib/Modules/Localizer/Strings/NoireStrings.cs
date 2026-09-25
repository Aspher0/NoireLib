namespace NoireLib.Localizer;

/// <summary>
/// NoireLib's own texts, translated in the plugin's language files like its own; every key starts with <c>noire.</c>.
/// </summary>
public static class NoireStrings
{
    /// <summary>The label of a language picker.</summary>
    public static readonly NoireString LanguageLabel = new("noire.language", "Language");

    /// <summary>The cancel button of a confirmation.</summary>
    public static readonly NoireString Cancel = new("noire.cancel", "Cancel");

    /// <summary>Closes the layout editor.</summary>
    public static readonly NoireString Done = new("noire.done", "Done");

    /// <summary>A confirm button while its countdown runs; fills <c>{label}</c> and <c>{seconds}</c>.</summary>
    public static readonly NoireString CountdownLabel = new("noire.countdown", "{label} ({seconds})");

    /// <summary>The tooltip of the window menu's title bar button.</summary>
    public static readonly NoireString WindowOptions = new("noire.window.options", "Window options");

    /// <summary>The window menu's heading above its sliders.</summary>
    public static readonly NoireString MenuWindowHeading = new("noire.menu.window", "WINDOW");

    /// <summary>The window menu's heading above its behaviour switches.</summary>
    public static readonly NoireString MenuBehaviourHeading = new("noire.menu.behaviour", "BEHAVIOUR");

    /// <summary>The window menu's heading above its stay-visible switches.</summary>
    public static readonly NoireString MenuVisibilityHeading = new("noire.menu.visibility", "STAY VISIBLE");

    /// <summary>The window menu's opacity slider.</summary>
    public static readonly NoireString MenuOpacity = new("noire.menu.opacity", "Opacity");

    /// <summary>The window menu's text size slider.</summary>
    public static readonly NoireString MenuTextSize = new("noire.menu.text_size", "Text size");

    /// <summary>The window menu's always-on-top switch.</summary>
    public static readonly NoireString MenuAlwaysOnTop = new("noire.menu.on_top", "Always on top");

    /// <summary>The window menu's reduced-motion switch.</summary>
    public static readonly NoireString MenuReducedMotion = new("noire.menu.reduced_motion", "Reduced motion");

    /// <summary>The window menu's position lock.</summary>
    public static readonly NoireString MenuLockPosition = new("noire.menu.lock_position", "Lock position");

    /// <summary>The window menu's click-through switch.</summary>
    public static readonly NoireString MenuClickThrough = new("noire.menu.click_through", "Click through");

    /// <summary>The window menu's width lock.</summary>
    public static readonly NoireString MenuLockWidth = new("noire.menu.lock_width", "Lock width");

    /// <summary>The window menu's height lock.</summary>
    public static readonly NoireString MenuLockHeight = new("noire.menu.lock_height", "Lock height");

    /// <summary>The window menu's switch keeping the window in gpose.</summary>
    public static readonly NoireString MenuInGpose = new("noire.menu.in_gpose", "In gpose");

    /// <summary>The window menu's switch keeping the window while the game UI is hidden.</summary>
    public static readonly NoireString MenuUiHidden = new("noire.menu.ui_hidden", "UI hidden");

    /// <summary>The window menu's switch keeping the window in cutscenes.</summary>
    public static readonly NoireString MenuInCutscene = new("noire.menu.in_cutscene", "In cutscene");

    /// <summary>The window menu's switch keeping the window when the game hides its UI.</summary>
    public static readonly NoireString MenuAutoHide = new("noire.menu.auto_hide", "Auto hide");

    /// <summary>The hint of the gpose switch.</summary>
    public static readonly NoireString MenuInGposeHint = new("noire.menu.in_gpose.hint", "Keeps the window open while gpose is active.");

    /// <summary>The hint of the hidden UI switch.</summary>
    public static readonly NoireString MenuUiHiddenHint = new("noire.menu.ui_hidden.hint", "Keeps the window open when you hide the game UI.");

    /// <summary>The hint of the cutscene switch.</summary>
    public static readonly NoireString MenuInCutsceneHint = new("noire.menu.in_cutscene.hint", "Keeps the window open during cutscenes.");

    /// <summary>The hint of the auto hide switch.</summary>
    public static readonly NoireString MenuAutoHideHint = new("noire.menu.auto_hide.hint", "Keeps the window open whenever the game hides its own UI.");

    /// <summary>The window menu's note while click through is on.</summary>
    public static readonly NoireString MenuClickThroughNote = new("noire.menu.click_through.note", "The window lets clicks through to the game. Its title bar stays clickable.");

    /// <summary>The name of the stock skin.</summary>
    public static readonly NoireString SkinStock = new("noire.skin.stock", "Plain");

    /// <summary>The accent colour.</summary>
    public static readonly NoireString ColorAccent = new("noire.colors.accent", "Accent");

    /// <summary>The danger colour.</summary>
    public static readonly NoireString ColorDanger = new("noire.colors.danger", "Danger");

    /// <summary>The layout editor's title.</summary>
    public static readonly NoireString LayoutTitle = new("noire.layout.title", "Layout");

    /// <summary>The layout editor's title bar buttons section.</summary>
    public static readonly NoireString LayoutButtons = new("noire.layout.buttons", "Title bar buttons");

    /// <summary>Restores the default layout from the layout editor.</summary>
    public static readonly NoireString LayoutReset = new("noire.layout.default", "Default layout");

    /// <summary>A status after something was applied.</summary>
    public static readonly NoireString Applied = new("noire.applied", "Applied.");

    /// <summary>A status after something was copied to the clipboard.</summary>
    public static readonly NoireString Copied = new("noire.copied", "Copied.");

    /// <summary>Restores one colour.</summary>
    public static readonly NoireString Restore = new("noire.restore", "Restore");

    /// <summary>The hint of the settings search field.</summary>
    public static readonly NoireString SearchSettings = new("noire.settings.search", "Search settings");

    /// <summary>Shown when a settings search matches nothing.</summary>
    public static readonly NoireString NoSettingMatches = new("noire.settings.none", "No setting matches.");

    /// <summary>The reset button of a modified setting.</summary>
    public static readonly NoireString ResetToDefault = new("noire.settings.reset", "Reset to default");

    /// <summary>Copies the modified settings as a code.</summary>
    public static readonly NoireString ExportSettings = new("noire.settings.export", "Export settings");

    /// <summary>A status after the settings were exported.</summary>
    public static readonly NoireString ExportedSettings = new("noire.settings.exported", "Settings copied to the clipboard.");

    /// <summary>Applies a settings code from the clipboard.</summary>
    public static readonly NoireString ImportSettings = new("noire.settings.import", "Import from clipboard");

    /// <summary>A status after settings were imported; fills <c>{count}</c>.</summary>
    public static readonly NoireString ImportedSettings = new("noire.settings.imported", "{count} settings imported.");

    /// <summary>A status when the clipboard holds no code of the right kind.</summary>
    public static readonly NoireString ImportFailed = new("noire.settings.import_failed", "The clipboard holds no valid code.");

    /// <summary>A language with its translation progress; fills <c>{name}</c> and <c>{percent}</c>.</summary>
    public static readonly NoireString LanguageProgress = new("noire.language.progress", "{name} ({percent}%)");

    /// <summary>Opens the translation editor.</summary>
    public static readonly NoireString Translate = new("noire.language.translate", "Translate...");

    /// <summary>The label of the skin picker.</summary>
    public static readonly NoireString SkinLabel = new("noire.skin", "Interface");

    /// <summary>Opens the colour editor.</summary>
    public static readonly NoireString EditColors = new("noire.colors.edit", "Edit colours");

    /// <summary>Restores every colour of the active skin.</summary>
    public static readonly NoireString ResetColors = new("noire.colors.reset", "Restore all colours");

    /// <summary>The colour editor's title.</summary>
    public static readonly NoireString ColorsTitle = new("noire.colors.title", "Colours");

    /// <summary>Copies the colours as a code.</summary>
    public static readonly NoireString CopyColors = new("noire.colors.copy", "Copy as code");

    /// <summary>Applies a colour code from the clipboard.</summary>
    public static readonly NoireString ImportColors = new("noire.colors.import", "Import from clipboard");

    /// <summary>Opens a window's layout editor; fills <c>{window}</c>.</summary>
    public static readonly NoireString ArrangeWindow = new("noire.layout.arrange", "Arrange {window}");

    /// <summary>Restores a window's default layout.</summary>
    public static readonly NoireString ResetLayout = new("noire.layout.reset", "Restore the layout");

    /// <summary>The translation editor's title.</summary>
    public static readonly NoireString TranslateTitle = new("noire.translate.title", "Translate");

    /// <summary>Shown until a language is picked in the translation editor.</summary>
    public static readonly NoireString TranslatePickLanguage = new("noire.translate.pick", "Pick a language, or type a language code to start a new one.");

    /// <summary>Starts a new language in the translation editor.</summary>
    public static readonly NoireString TranslateNew = new("noire.translate.new", "Start");

    /// <summary>The hint of the translation editor's search field.</summary>
    public static readonly NoireString TranslateSearch = new("noire.translate.search", "Search keys and texts");

    /// <summary>Shows only the texts a language does not translate.</summary>
    public static readonly NoireString TranslateMissingOnly = new("noire.translate.missing", "Missing or outdated");

    /// <summary>Marks a translation made from a source text that has changed since.</summary>
    public static readonly NoireString TranslateOutdated = new("noire.translate.outdated", "Source changed");

    /// <summary>The hover of an outdated translation; fills <c>{text}</c> with the source it was made from.</summary>
    public static readonly NoireString TranslateOutdatedFrom = new("noire.translate.outdated_from", "Translated from: {text}");

    /// <summary>A translation lacks placeholders of its source; fills <c>{tags}</c>.</summary>
    public static readonly NoireString TranslateMissingTags = new("noire.translate.missing_tags", "Missing tags: {tags}");

    /// <summary>A translation has placeholders its source does not, such as a renamed one; fills <c>{tags}</c>.</summary>
    public static readonly NoireString TranslateUnknownTags = new("noire.translate.unknown_tags", "Unknown tags: {tags}");

    /// <summary>Confirms an outdated translation still fits the new source text.</summary>
    public static readonly NoireString TranslateValidate = new("noire.translate.validate", "Still correct");

    /// <summary>Puts back the translation as it was last saved.</summary>
    public static readonly NoireString TranslateRevert = new("noire.translate.revert", "Revert");

    /// <summary>The hover of the revert button; fills <c>{text}</c> with the saved translation.</summary>
    public static readonly NoireString TranslateRevertTo = new("noire.translate.revert_to", "Saved text: {text}");

    /// <summary>The hover of the revert button when the text was saved without a translation.</summary>
    public static readonly NoireString TranslateRevertToEmpty = new("noire.translate.revert_to_empty", "Saved without a translation");

    /// <summary>How many translations of a language are outdated.</summary>
    public static readonly NoirePlural LanguageOutdated = new("noire.language.outdated", "{count} outdated", "{count} outdated");

    /// <summary>The key column.</summary>
    public static readonly NoireString TranslateKey = new("noire.translate.key", "Key");

    /// <summary>The source text column.</summary>
    public static readonly NoireString TranslateSource = new("noire.translate.source", "Original");

    /// <summary>The translation column.</summary>
    public static readonly NoireString TranslateTranslation = new("noire.translate.translation", "Translation");

    /// <summary>Saves the language to the user's language file.</summary>
    public static readonly NoireString TranslateSave = new("noire.translate.save", "Save");

    /// <summary>A status after a save; fills <c>{path}</c>.</summary>
    public static readonly NoireString TranslateSaved = new("noire.translate.saved", "Saved to {path}.");

    /// <summary>A status when the language file could not be written.</summary>
    public static readonly NoireString TranslateSaveFailed = new("noire.translate.save_failed", "The file could not be written.");

    /// <summary>Makes the search find its text anywhere rather than only as a whole word.</summary>
    public static readonly NoireString TranslateContains = new("noire.translate.contains", "Contains");

    /// <summary>Opens the list of the people each language file credits.</summary>
    public static readonly NoireString TranslationCredits = new("noire.translate.credits", "Translation credits");

    /// <summary>Unfolds something folded, such as the translation credits.</summary>
    public static readonly NoireString Show = new("noire.show", "Show");

    /// <summary>Folds it back.</summary>
    public static readonly NoireString Hide = new("noire.hide", "Hide");

    /// <summary>One language of the credits; fills <c>{language}</c> and <c>{names}</c>.</summary>
    public static readonly NoireString TranslationCreditLine = new("noire.translate.credit_line", "{language}: {names}");

    /// <summary>Saves the language file where the user picks, through the system's save dialog.</summary>
    public static readonly NoireString TranslateSaveFile = new("noire.translate.save_file", "Save to a file...");

    /// <summary>Switches the plugin to the language being edited.</summary>
    public static readonly NoireString TranslatePreview = new("noire.translate.preview", "Show the plugin in this language");

    /// <summary>The skinned changelog window's title.</summary>
    public static readonly NoireString ChangelogTitle = new("noire.changelog.title", "Changelog");

    /// <summary>The changelog's version selector, in the layout editor.</summary>
    public static readonly NoireString ChangelogVersions = new("noire.changelog.versions", "Versions");

    /// <summary>The changelog's entries, in the layout editor.</summary>
    public static readonly NoireString ChangelogEntries = new("noire.changelog.entries", "Entries");

    /// <summary>The changelog's footer, in the layout editor.</summary>
    public static readonly NoireString ChangelogFooter = new("noire.changelog.footer", "Footer");

    /// <summary>The skinned log window's title.</summary>
    public static readonly NoireString LogsTitle = new("noire.logs.title", "All logs");

    /// <summary>The log window's filters, in the layout editor.</summary>
    public static readonly NoireString LogsFilters = new("noire.logs.filters", "Filters");

    /// <summary>The log window's entries, in the layout editor.</summary>
    public static readonly NoireString LogsEntries = new("noire.logs.entries", "Entries");

    /// <summary>Closes the window.</summary>
    public static readonly NoireString Close = new("noire.close", "Close");

    /// <summary>The changelog's version picker label.</summary>
    public static readonly NoireString ChangelogSelectVersion = new("noire.changelog.select_version", "Select a version:");

    /// <summary>Shown for a version without entries.</summary>
    public static readonly NoireString ChangelogNone = new("noire.changelog.none", "No changelog available for this version.");

    /// <summary>The log window's filter panel heading.</summary>
    public static readonly NoireString LogsFiltersStorage = new("noire.logs.filters_storage", "Filters and storage");

    /// <summary>The log window's panel for adding an entry by hand.</summary>
    public static readonly NoireString LogsManualEntry = new("noire.logs.manual_entry", "Manual entry");

    /// <summary>Folds the filter panel.</summary>
    public static readonly NoireString LogsCollapsePanel = new("noire.logs.collapse_panel", "Collapse the panel");

    /// <summary>Unfolds the filter panel.</summary>
    public static readonly NoireString LogsExpandPanel = new("noire.logs.expand_panel", "Expand the panel");

    /// <summary>The category filter with nothing picked.</summary>
    public static readonly NoireString LogsAllCategories = new("noire.logs.all_categories", "All categories");

    /// <summary>The level filter with nothing picked.</summary>
    public static readonly NoireString LogsAllLevels = new("noire.logs.all_levels", "All levels");

    /// <summary>Keeps the entries in the database across sessions.</summary>
    public static readonly NoireString LogsPersist = new("noire.logs.persist", "Save to the database");

    /// <summary>Tints each row by its level.</summary>
    public static readonly NoireString LogsShowColors = new("noire.logs.show_colors", "Show colors");

    /// <summary>Lets each line of an entry be selected on its own.</summary>
    public static readonly NoireString LogsSplitLines = new("noire.logs.split_lines", "Split lines");

    /// <summary>Hides the category column.</summary>
    public static readonly NoireString LogsHideCategory = new("noire.logs.hide_category", "Hide the category");

    /// <summary>Hides the source column.</summary>
    public static readonly NoireString LogsHideSource = new("noire.logs.hide_source", "Hide the source");

    /// <summary>Reloads the entries.</summary>
    public static readonly NoireString LogsRefresh = new("noire.logs.refresh", "Refresh the entries");

    /// <summary>Clears the entries held in memory.</summary>
    public static readonly NoireString LogsClearMemory = new("noire.logs.clear_memory", "Clear the entries in memory");

    /// <summary>Clears the entries saved in the database.</summary>
    public static readonly NoireString LogsClearDatabase = new("noire.logs.clear_database", "Clear the database entries");

    /// <summary>The hint of the clear buttons.</summary>
    public static readonly NoireString LogsHoldToClear = new("noire.logs.hold_clear", "Hold CTRL and SHIFT to clear.");

    /// <summary>Adds the entry typed by hand.</summary>
    public static readonly NoireString LogsAddEntry = new("noire.logs.add_entry", "Add the entry");

    /// <summary>The hint of the manual entry's category field.</summary>
    public static readonly NoireString LogsCategoryHint = new("noire.logs.category_hint", "Category");

    /// <summary>The hint of the manual entry's message field.</summary>
    public static readonly NoireString LogsMessageHint = new("noire.logs.message_hint", "What happened?");

    /// <summary>The hint of the search field over the entries.</summary>
    public static readonly NoireString LogsSearchHint = new("noire.logs.search_hint", "Search for a message, category, source...");

    /// <summary>The entries on the page; fills <c>{range}</c> and <c>{count}</c>.</summary>
    public static readonly NoireString LogsShowing = new("noire.logs.showing", "Showing {range} of {count}");

    /// <summary>Every entry, filtered or not; fills <c>{total}</c>.</summary>
    public static readonly NoireString LogsTotal = new("noire.logs.total", "({total} in total)");

    /// <summary>After the page size picker.</summary>
    public static readonly NoireString LogsPerPage = new("noire.logs.per_page", "per page");

    /// <summary>A column of the log table.</summary>
    public static readonly NoireString LogsColumnTime = new("noire.logs.column.time", "Time");

    /// <summary>A column of the log table.</summary>
    public static readonly NoireString LogsColumnLevel = new("noire.logs.column.level", "Level");

    /// <summary>A column of the log table.</summary>
    public static readonly NoireString LogsColumnCategory = new("noire.logs.column.category", "Category");

    /// <summary>A column of the log table.</summary>
    public static readonly NoireString LogsColumnMessage = new("noire.logs.column.message", "Message");

    /// <summary>A column of the log table.</summary>
    public static readonly NoireString LogsColumnSource = new("noire.logs.column.source", "Source");

    /// <summary>Fills <c>{count}</c>.</summary>
    public static readonly NoireString LogsDeleteSelected = new("noire.logs.delete_selected", "Delete the selected entries ({count})");

    /// <summary>Deletes one entry.</summary>
    public static readonly NoireString LogsDeleteEntry = new("noire.logs.delete_entry", "Delete the entry");

    /// <summary>Fills <c>{count}</c>.</summary>
    public static readonly NoireString LogsCopyLinesAcross = new("noire.logs.copy_lines_across", "Copy the lines selected across entries ({count})");

    /// <summary>Copies one line to the clipboard.</summary>
    public static readonly NoireString LogsCopyLine = new("noire.logs.copy_line", "Copy the selected line");

    /// <summary>Copies the lines to the clipboard.</summary>
    public static readonly NoireString LogsCopyLines = new("noire.logs.copy_lines", "Copy the selected lines");

    /// <summary>Fills <c>{count}</c>.</summary>
    public static readonly NoireString LogsCopySelected = new("noire.logs.copy_selected", "Copy the selected entries ({count})");

    /// <summary>Copies one entry to the clipboard.</summary>
    public static readonly NoireString LogsCopyEntry = new("noire.logs.copy_entry", "Copy the entry");

    /// <summary>The hint of the delete item.</summary>
    public static readonly NoireString LogsHoldToDelete = new("noire.logs.hold_delete", "Hold CTRL to delete.");

    /// <summary>Fills <c>{count}</c>.</summary>
    public static readonly NoireString LogsCopyMessages = new("noire.logs.copy_messages", "Copy only the messages ({count})");

    /// <summary>Copies one message to the clipboard.</summary>
    public static readonly NoireString LogsCopyMessage = new("noire.logs.copy_message", "Copy only the message");

    /// <summary>How many categories the filter picks.</summary>
    public static readonly NoirePlural LogsCategories = new("noire.logs.categories", "{count} category", "{count} categories");

    /// <summary>How many levels the filter picks.</summary>
    public static readonly NoirePlural LogsLevels = new("noire.logs.levels", "{count} level", "{count} levels");
}
