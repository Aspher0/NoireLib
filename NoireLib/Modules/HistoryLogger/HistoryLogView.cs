using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.HistoryLogger;

/// <summary>
/// A searched, filtered, sorted and paged view over the entries of a <see cref="NoireHistoryLogger"/>. The state
/// behind a log window.<br/>
/// Results are cached and rebuilt only when the entries or a setting of the view change. Reading them every frame
/// does not allocate. An empty category or level selection means no filter.
/// </summary>
public sealed class HistoryLogView
{
    private static readonly int LevelCount = Enum.GetValues<HistoryLogLevel>().Length;

    private readonly HashSet<string> selectedCategories = new();
    private readonly HashSet<HistoryLogLevel> selectedLevels = new();
    private readonly int[] levelCounts = new int[LevelCount];

    private IReadOnlyList<HistoryLogEntry> allEntries = Array.Empty<HistoryLogEntry>();
    private List<HistoryLogEntry> entries = new();
    private List<HistoryLogEntry> pageEntries = new();
    private List<string> categories = new();

    private string searchText = string.Empty;
    private HistoryLogSortColumn sortColumn = HistoryLogSortColumn.Time;
    private bool sortDescending = true;
    private int itemsPerPage = 100;
    private int page = 1;

    private bool hasEntries;
    private int cachedEntriesVersion;
    private int settingsVersion;
    private int cachedSettingsVersion = -1;
    private int revision;
    private int pageRevision = -1;
    private int pageCachedPage = -1;
    private int pageCachedItemsPerPage = -1;

    /// <summary>
    /// Creates a view over <paramref name="logger"/> showing every entry, newest first, 100 per page.
    /// </summary>
    /// <param name="logger">The logger whose entries the view reads.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
    public HistoryLogView(NoireHistoryLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Logger = logger;
    }

    /// <summary>
    /// The logger this view reads.
    /// </summary>
    public NoireHistoryLogger Logger { get; }

    /// <summary>
    /// Text matched case-insensitively against each entry's message, category and source, ignoring surrounding spaces.
    /// Changing it returns to the first page.
    /// </summary>
    public string SearchText
    {
        get => searchText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(searchText, value, StringComparison.Ordinal))
                return;

            searchText = value;
            Invalidate(resetPage: true);
        }
    }

    /// <summary>
    /// The categories shown. Empty shows every category.
    /// </summary>
    public IReadOnlySet<string> SelectedCategories => selectedCategories;

    /// <summary>
    /// The levels shown. Empty shows every level.
    /// </summary>
    public IReadOnlySet<HistoryLogLevel> SelectedLevels => selectedLevels;

    /// <summary>
    /// The field entries are sorted by.
    /// </summary>
    public HistoryLogSortColumn SortColumn
    {
        get => sortColumn;
        set
        {
            if (sortColumn == value)
                return;

            sortColumn = value;
            Invalidate(resetPage: false);
        }
    }

    /// <summary>
    /// Whether entries are sorted in descending order (newest first for <see cref="HistoryLogSortColumn.Time"/>).
    /// </summary>
    public bool SortDescending
    {
        get => sortDescending;
        set
        {
            if (sortDescending == value)
                return;

            sortDescending = value;
            Invalidate(resetPage: false);
        }
    }

    /// <summary>
    /// The number of entries on a page, at least 1. Changing it returns to the first page.
    /// </summary>
    public int ItemsPerPage
    {
        get => itemsPerPage;
        set
        {
            value = Math.Max(1, value);
            if (itemsPerPage == value)
                return;

            itemsPerPage = value;
            page = 1;
        }
    }

    /// <summary>
    /// The current page, 1-based, kept between 1 and <see cref="PageCount"/>.
    /// </summary>
    public int Page
    {
        get
        {
            page = Math.Clamp(page, 1, PageCount);
            return page;
        }
        set => page = Math.Clamp(value, 1, PageCount);
    }

    /// <summary>
    /// The number of pages the matching entries fill, at least 1.
    /// </summary>
    public int PageCount => Math.Max(1, (Entries.Count + itemsPerPage - 1) / itemsPerPage);

    /// <summary>
    /// The index in <see cref="Entries"/> of the first entry on the current page.
    /// </summary>
    public int PageStartIndex => (Page - 1) * itemsPerPage;

    /// <summary>
    /// The entries on the current page, in sort order.
    /// </summary>
    public IReadOnlyList<HistoryLogEntry> PageEntries
    {
        get
        {
            var current = Page;
            if (pageRevision != revision || pageCachedPage != current || pageCachedItemsPerPage != itemsPerPage)
            {
                var start = (current - 1) * itemsPerPage;
                var end = Math.Min(start + itemsPerPage, entries.Count);
                var slice = new List<HistoryLogEntry>(Math.Max(0, end - start));
                for (var i = start; i < end; i++)
                    slice.Add(entries[i]);

                pageEntries = slice;
                pageRevision = revision;
                pageCachedPage = current;
                pageCachedItemsPerPage = itemsPerPage;
            }

            return pageEntries;
        }
    }

    /// <summary>
    /// Every entry matching the search and filters, in sort order.
    /// </summary>
    public IReadOnlyList<HistoryLogEntry> Entries
    {
        get
        {
            EnsureCurrent();
            return entries;
        }
    }

    /// <summary>
    /// The number of entries the logger holds, before any filter.
    /// </summary>
    public int TotalCount
    {
        get
        {
            EnsureCurrent();
            return allEntries.Count;
        }
    }

    /// <summary>
    /// The distinct categories of every entry the logger holds, sorted.
    /// </summary>
    public IReadOnlyList<string> Categories
    {
        get
        {
            EnsureCurrent();
            return categories;
        }
    }

    /// <summary>
    /// A number that changes every time <see cref="Entries"/> is rebuilt. Compare it to know when text built from the entries is stale.
    /// </summary>
    public int Revision
    {
        get
        {
            EnsureCurrent();
            return revision;
        }
    }

    /// <summary>
    /// Counts the entries of <paramref name="level"/> the logger holds, before any filter.
    /// </summary>
    /// <param name="level">The level to count.</param>
    /// <returns>The number of entries at that level.</returns>
    public int CountOf(HistoryLogLevel level)
    {
        EnsureCurrent();
        var index = (int)level;
        return index >= 0 && index < levelCounts.Length ? levelCounts[index] : 0;
    }

    /// <summary>
    /// Whether <paramref name="category"/> is part of the category filter.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns><see langword="true"/> if the category is selected.</returns>
    public bool IsCategorySelected(string category) => selectedCategories.Contains(category);

    /// <summary>
    /// Adds <paramref name="category"/> to or removes it from the category filter, returning to the first page.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <param name="selected">Whether the category is shown.</param>
    public void SetCategorySelected(string category, bool selected)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (selected ? selectedCategories.Add(category) : selectedCategories.Remove(category))
            Invalidate(resetPage: true);
    }

    /// <summary>
    /// Flips whether <paramref name="category"/> is part of the category filter.
    /// </summary>
    /// <param name="category">The category.</param>
    public void ToggleCategory(string category) => SetCategorySelected(category, !IsCategorySelected(category));

    /// <summary>
    /// Empties the category filter so every category shows, returning to the first page.
    /// </summary>
    public void ClearCategoryFilter()
    {
        selectedCategories.Clear();
        Invalidate(resetPage: true);
    }

    /// <summary>
    /// Whether <paramref name="level"/> is part of the level filter.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <returns><see langword="true"/> if the level is selected.</returns>
    public bool IsLevelSelected(HistoryLogLevel level) => selectedLevels.Contains(level);

    /// <summary>
    /// Adds <paramref name="level"/> to or removes it from the level filter, returning to the first page.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="selected">Whether the level is shown.</param>
    public void SetLevelSelected(HistoryLogLevel level, bool selected)
    {
        if (selected ? selectedLevels.Add(level) : selectedLevels.Remove(level))
            Invalidate(resetPage: true);
    }

    /// <summary>
    /// Flips whether <paramref name="level"/> is part of the level filter.
    /// </summary>
    /// <param name="level">The level.</param>
    public void ToggleLevel(HistoryLogLevel level) => SetLevelSelected(level, !IsLevelSelected(level));

    /// <summary>
    /// Empties the level filter so every level shows, returning to the first page.
    /// </summary>
    public void ClearLevelFilter()
    {
        selectedLevels.Clear();
        Invalidate(resetPage: true);
    }

    private void Invalidate(bool resetPage)
    {
        settingsVersion++;
        if (resetPage)
            page = 1;
    }

    private void EnsureCurrent()
    {
        var entriesVersion = Logger.EntriesVersion;
        var entriesChanged = !hasEntries || entriesVersion != cachedEntriesVersion;

        if (!entriesChanged && cachedSettingsVersion == settingsVersion)
            return;

        if (entriesChanged)
        {
            allEntries = Logger.GetEntriesSnapshot();
            hasEntries = true;
            cachedEntriesVersion = entriesVersion;

            Array.Clear(levelCounts);
            foreach (var entry in allEntries)
            {
                var index = (int)entry.Level;
                if (index >= 0 && index < levelCounts.Length)
                    levelCounts[index]++;
            }

            categories = allEntries
                .Select(entry => entry.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category)
                .ToList();
        }

        entries = Filter(allEntries);
        cachedSettingsVersion = settingsVersion;
        revision++;
    }

    private List<HistoryLogEntry> Filter(IReadOnlyList<HistoryLogEntry> source)
    {
        IEnumerable<HistoryLogEntry> query = source;

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.Trim();
            query = query.Where(entry =>
                entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (entry.Source?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (selectedCategories.Count > 0)
            query = query.Where(entry => selectedCategories.Contains(entry.Category));

        if (selectedLevels.Count > 0)
            query = query.Where(entry => selectedLevels.Contains(entry.Level));

        // OrderBy is stable. Equal keys keep the oldest-first order.
        query = (sortColumn, sortDescending) switch
        {
            (HistoryLogSortColumn.Time, true) => query.OrderByDescending(entry => entry.Timestamp),
            (HistoryLogSortColumn.Time, false) => query.OrderBy(entry => entry.Timestamp),
            (HistoryLogSortColumn.Level, true) => query.OrderByDescending(entry => entry.Level),
            (HistoryLogSortColumn.Level, false) => query.OrderBy(entry => entry.Level),
            (HistoryLogSortColumn.Category, true) => query.OrderByDescending(entry => entry.Category),
            (HistoryLogSortColumn.Category, false) => query.OrderBy(entry => entry.Category),
            (HistoryLogSortColumn.Source, true) => query.OrderByDescending(entry => entry.Source),
            (HistoryLogSortColumn.Source, false) => query.OrderBy(entry => entry.Source),
            _ => query,
        };

        return query.ToList();
    }
}
