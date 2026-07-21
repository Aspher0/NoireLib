using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UI;

/// <summary>
/// A searchable, icon-rich picker over any sheet of game data, in one line.<br/>
/// Point it at a row type, say how to read a name off a row, and it is a dropdown over every item, emote, mount, world
/// or duty in the game, filtered fuzzily and drawn with icons.
/// </summary>
/// <remarks>
/// Reading a sheet is not free and this does not pretend otherwise: the rows are read once, on a background thread,
/// and the picker says so while it is happening rather than freezing the frame that opened it. Nothing here touches
/// the object table, which is the game state that genuinely has to be read on the framework thread; Excel data is
/// static content on disk.<br/>
/// The <see cref="Combo"/> underneath is fully public. Everything <see cref="NoireComboBox{T}"/> can do is reachable
/// from here, including a renderer of your own, and this is only the assembly of it.
/// </remarks>
/// <typeparam name="TRow">The Excel row type, for example <c>Lumina.Excel.Sheets.Item</c>.</typeparam>
/// <example>
/// <code>
/// var items = new NoireExcelPicker&lt;Item&gt;("itemPicker", row =&gt; row.Name.ExtractText())
/// {
///     Icon = row =&gt; row.Icon,
///     Include = row =&gt; row.ItemSearchCategory.RowId != 0,   // things a player can actually hold
/// };
///
/// if (items.Draw())
///     config.ItemId = items.SelectedRowId;
/// </code>
/// </example>
public sealed class NoireExcelPicker<TRow> where TRow : struct, IExcelRow<TRow>
{
    /// <summary>
    /// Serializes sheet reads across every picker in the plugin.
    /// </summary>
    /// <remarks>
    /// Lumina loads a sheet's pages on demand, so two pickers reading two sheets at once would be two threads walking
    /// that lazy loading concurrently. One at a time costs nothing worth measuring here, because a picker reads its
    /// sheet once for the life of the plugin.
    /// </remarks>
    private static readonly SemaphoreSlim SheetGate = new(1, 1);

    private readonly Dictionary<uint, UiImageSource> icons = new();
    private readonly List<ExcelPickerEntry<TRow>> entries = new();

    private ClientLanguage? loadedLanguage;
    private bool loadRequested;
    private int loading;

    /// <summary>
    /// Creates a picker.
    /// </summary>
    /// <param name="id">A stable id for the widget. When <see langword="null"/>, a random one is generated.</param>
    /// <param name="display">
    /// How a row's name is read. When <see langword="null"/>, the row's <c>ToString()</c> is used, which is almost
    /// never what you want: a sheet row usually needs something like <c>row.Name.ExtractText()</c>.
    /// </param>
    public NoireExcelPicker(string? id = null, Func<TRow, string>? display = null)
    {
        Display = display ?? (row => row.ToString() ?? string.Empty);

        Combo = new NoireComboBox<ExcelPickerEntry<TRow>>(id, displayFunc: entry => entry.Display)
        {
            FilterEnabled = true,
            FilterHint = "Search...",
            PreviewPlaceholder = "Select...",
            VisibleItemCount = 12,
            ItemRenderer = DrawRow,
        };
    }

    #region Configuration

    /// <summary>
    /// The combo this picker is assembled from, fully usable on its own.
    /// </summary>
    /// <remarks>
    /// Reach through it for anything this surface does not name: the wheel-cycle shortcut, the filter's pinning,
    /// a renderer of your own, the visible option count. The picker only fills it and draws it.
    /// </remarks>
    public NoireComboBox<ExcelPickerEntry<TRow>> Combo { get; }

    /// <summary>
    /// How a row's name is read. Changing it takes effect on the next <see cref="Reload"/>.
    /// </summary>
    public Func<TRow, string> Display { get; set; }

    /// <summary>
    /// How a row's icon id is read. When <see langword="null"/>, no icons are drawn.
    /// </summary>
    public Func<TRow, uint>? Icon { get; set; }

    /// <summary>
    /// Which rows the picker offers. When <see langword="null"/>, all of them.
    /// </summary>
    /// <remarks>
    /// A predicate rather than a fixed set of categories, so the decision stays the consumer's: a picker over
    /// equippable items, over emotes the player has unlocked, or over worlds on one data centre is this callback and
    /// nothing else.
    /// </remarks>
    public Func<TRow, bool>? Include { get; set; }

    /// <summary>
    /// Whether rows whose name is empty are dropped. On by default.
    /// </summary>
    /// <remarks>
    /// Most game sheets are mostly blank: unused ids, placeholders and internal entries all carry an empty name, and
    /// a picker that lists them is thousands of unselectable rows deep.
    /// </remarks>
    public bool SkipEmptyNames { get; set; } = true;

    /// <summary>
    /// The language the sheet is read in. When <see langword="null"/>, the client's own.<br/>
    /// Changing it reloads the picker, because the names are what changed.
    /// </summary>
    public ClientLanguage? Language { get; set; }

    /// <summary>
    /// The size of the icon beside each row, at 100%. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float IconSize { get; set; } = 20f;

    /// <summary>
    /// The text shown in the closed picker while the sheet is being read.
    /// </summary>
    public string LoadingText { get; set; } = "Loading...";

    /// <summary>
    /// The hint shown in the search box. Forwarded to <see cref="Combo"/>.
    /// </summary>
    public string FilterHint
    {
        get => Combo.FilterHint;
        set => Combo.FilterHint = value;
    }

    /// <summary>
    /// The text shown when nothing is selected. Forwarded to <see cref="Combo"/>.
    /// </summary>
    public string PreviewPlaceholder
    {
        get => Combo.PreviewPlaceholder;
        set => Combo.PreviewPlaceholder = value;
    }

    /// <summary>
    /// Invoked when the selection changes, with the newly selected row.
    /// </summary>
    public Action<TRow?>? OnSelectionChanged { get; set; }

    #endregion

    #region State

    /// <summary>Whether the sheet has been read and the picker is usable.</summary>
    public bool IsLoaded => loadedLanguage.HasValue && loading == 0;

    /// <summary>Whether the sheet is being read right now.</summary>
    public bool IsLoading => loading != 0;

    /// <summary>How many rows the picker is offering.</summary>
    public int Count => entries.Count;

    /// <summary>The selected row, or <see langword="null"/> when nothing is selected.</summary>
    public TRow? Selected => Combo.SelectedIndex >= 0 ? Combo.SelectedItem.Row : null;

    /// <summary>
    /// The selected row's id, which is the value worth persisting. <see langword="null"/> when nothing is selected.
    /// </summary>
    public uint? SelectedRowId => Combo.SelectedIndex >= 0 ? Combo.SelectedItem.RowId : null;

    #endregion

    #region Drawing

    /// <summary>
    /// Draws the picker, reading the sheet the first time it is called.
    /// </summary>
    /// <returns>True on the frame the selection changes.</returns>
    public bool Draw()
    {
        using var profile = UiProfile.Widget(nameof(NoireExcelPicker<TRow>), Combo.Id);

        EnsureLoaded();

        if (!IsLoaded)
        {
            DrawLoadingPlaceholder();
            return false;
        }

        Combo.ItemHeight = MathF.Max(IconSize, 0f) > 0f && Icon != null ? IconSize + 2f : null;

        return Combo.Draw();
    }

    /// <summary>
    /// Draws a disabled stand-in in the picker's place while the sheet is being read, so the layout does not jump when
    /// it arrives.
    /// </summary>
    private void DrawLoadingPlaceholder()
    {
        using var disabled = ImRaii.Disabled();

        if (Combo.Width.HasValue)
            ImGui.SetNextItemWidth(Combo.Width.Value);

        if (ImGui.BeginCombo(UiIds.For("###NoireExcelPicker_", Combo.Id), LoadingText))
            ImGui.EndCombo();
    }

    /// <summary>
    /// Draws one row: its icon, then the combo's own label so the filter highlighting still applies.
    /// </summary>
    private void DrawRow(UiComboItemDraw<ExcelPickerEntry<TRow>> option)
    {
        var size = NoireUI.Scaled(IconSize);

        if (Icon != null && option.Item.IconId != 0 && size > 0f)
        {
            var wrap = ResolveIcon(option.Item.IconId).GetWrap();

            if (wrap != null)
            {
                ImGui.Image(wrap.Handle, new Vector2(size, size));
                ImGui.SameLine(0f, NoireUI.Scaled(6f));
            }
            else
            {
                // Reserved rather than skipped, so a texture that has not finished loading does not shuffle the row
                // sideways and then back on the frame it arrives.
                ImGui.Dummy(new Vector2(size, size));
                ImGui.SameLine(0f, NoireUI.Scaled(6f));
            }
        }

        // Centred against the icon rather than sitting on the row's top edge, which is what makes a taller row read as
        // one line instead of two things that happen to overlap.
        var offset = (size - NoireText.LineHeight()) * 0.5f;

        if (Icon != null && offset > 0f)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);

        option.DrawLabel();
    }

    private UiImageSource ResolveIcon(uint iconId)
    {
        if (icons.TryGetValue(iconId, out var cached))
            return cached;

        var source = UiImageSource.FromGameIcon(iconId);
        icons[iconId] = source;
        return source;
    }

    #endregion

    #region Selection

    /// <summary>
    /// Selects a row by its id, which is how a persisted value is restored.
    /// </summary>
    /// <remarks>
    /// Safe to call before the sheet has been read: the request is remembered and applied when the rows arrive, so a
    /// plugin restoring a saved id on load does not have to wait for anything.
    /// </remarks>
    /// <param name="rowId">The row id to select.</param>
    /// <returns>True when the row was found and selected.</returns>
    public bool Select(uint rowId)
    {
        pendingSelection = rowId;

        if (!IsLoaded)
            return false;

        return ApplyPendingSelection();
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection()
    {
        pendingSelection = null;
        Combo.ClearSelection();
    }

    private uint? pendingSelection;

    private bool ApplyPendingSelection()
    {
        if (pendingSelection is not { } rowId)
            return false;

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].RowId != rowId)
                continue;

            pendingSelection = null;
            return Combo.Select(i);
        }

        pendingSelection = null;
        return false;
    }

    #endregion

    #region Loading

    /// <summary>
    /// Reads the sheet again, for when <see cref="Display"/>, <see cref="Include"/> or <see cref="Icon"/> has changed.
    /// </summary>
    /// <remarks>
    /// A language change reloads on its own; this is for the other three, which the picker cannot notice being
    /// reassigned.
    /// </remarks>
    public void Reload()
    {
        loadedLanguage = null;
        loadRequested = false;
    }

    /// <summary>
    /// Starts reading the sheet if it has not been read for the current language.
    /// </summary>
    private void EnsureLoaded()
    {
        var language = Language ?? (NoireService.IsInitialized() ? NoireService.ClientState.ClientLanguage : ClientLanguage.English);

        if (loadedLanguage == language)
            return;

        if (loadRequested)
            return;

        loadRequested = true;
        Interlocked.Exchange(ref loading, 1);

        _ = LoadAsync(language);
    }

    /// <summary>
    /// Reads the sheet off the draw thread and hands the rows back on it.
    /// </summary>
    private async Task LoadAsync(ClientLanguage language)
    {
        List<ExcelPickerEntry<TRow>>? built = null;

        try
        {
            built = await AsyncHelper.RunInBackgroundAsync(() => BuildEntries(language)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to read the '{typeof(TRow).Name}' sheet for a picker.", nameof(NoireExcelPicker<TRow>));
        }

        // Back on the draw thread before touching anything the drawing reads, so a frame never sees a half-filled
        // list.
        NoireUI.RunOnDraw(() =>
        {
            entries.Clear();

            if (built != null)
                entries.AddRange(built);

            Combo.SetItems(entries);
            loadedLanguage = language;
            Interlocked.Exchange(ref loading, 0);

            ApplyPendingSelection();
        });
    }

    /// <summary>
    /// Materializes the sheet into entries: the display text built once, the icon id read once, the rows the consumer
    /// does not want dropped.
    /// </summary>
    private List<ExcelPickerEntry<TRow>> BuildEntries(ClientLanguage language)
    {
        var built = new List<ExcelPickerEntry<TRow>>();

        SheetGate.Wait();

        try
        {
            var sheet = ExcelSheetHelper.GetSheet<TRow>(language);

            if (sheet == null)
                return built;

            foreach (var row in sheet)
            {
                if (Include != null && !Include(row))
                    continue;

                var display = Display(row) ?? string.Empty;

                if (SkipEmptyNames && string.IsNullOrWhiteSpace(display))
                    continue;

                built.Add(new ExcelPickerEntry<TRow>(row.RowId, row, display, Icon?.Invoke(row) ?? 0u));
            }
        }
        finally
        {
            SheetGate.Release();
        }

        return built;
    }

    #endregion
}
