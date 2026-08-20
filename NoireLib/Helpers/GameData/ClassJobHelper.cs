using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.Helpers;

/// <summary>
/// Reads the classes and jobs: what each one is called, what it does in a party, and what level the character has it
/// at. The sheet reads work without a character; the level reads describe a loaded one and answer zero without it.
/// </summary>
public static unsafe class ClassJobHelper
{
    private static IReadOnlyList<ClassJobInfo>? cachedJobs;
    private static IReadOnlyList<PropertyInfo>? cachedCategoryColumns;
    private static IReadOnlyDictionary<uint, IReadOnlySet<uint>>? cachedCategoryIndex;

    #region The sheet

    /// <summary>Reads one class or job.</summary>
    /// <param name="classJobId">The ClassJob row id.</param>
    /// <returns>The class or job, or null when the id names none.</returns>
    public static ClassJobInfo? Read(uint classJobId)
    {
        return SafeExecutor.ExecuteSafely<ClassJobInfo?>(
            () => ExcelSheetHelper.TryGetRow<ClassJob>(classJobId, out var row) && row.HasValue
                ? Describe(row.Value)
                : null,
            null);
    }

    /// <summary>
    /// Every class and job the sheet actually names, cached since it cannot change while the client runs. Rows the
    /// game leaves blank, which is how a future job is reserved before it ships, are skipped.
    /// </summary>
    /// <returns>The classes and jobs, in ascending row order.</returns>
    public static IReadOnlyList<ClassJobInfo> ReadAll()
    {
        if (cachedJobs != null)
            return cachedJobs;

        var jobs = SafeExecutor.ExecuteSafely(() =>
        {
            var found = new List<ClassJobInfo>();
            var sheet = ExcelSheetHelper.GetSheet<ClassJob>();
            if (sheet == null)
                return found;

            foreach (var row in sheet)
            {
                if (row.RowId != 0 && row.Abbreviation.ByteLength > 0)
                    found.Add(Describe(row));
            }

            return found;
        }, []) ?? [];

        return cachedJobs = jobs;
    }

    /// <summary>A job's abbreviation, which is the label almost every UI wants.</summary>
    /// <param name="classJobId">The ClassJob row id.</param>
    /// <returns>The abbreviation, or an empty string.</returns>
    public static string Abbreviation(uint classJobId) => Read(classJobId)?.Abbreviation ?? string.Empty;

    /// <summary>A job's name in the client language.</summary>
    /// <param name="classJobId">The ClassJob row id.</param>
    /// <returns>The name, or an empty string.</returns>
    public static string Name(uint classJobId) => Read(classJobId)?.Name ?? string.Empty;

    /// <summary>
    /// Finds a class or job by name or abbreviation, matching the client language and the English name alike so a
    /// command typed in either is understood.
    /// </summary>
    /// <param name="text">The name or abbreviation to match, case insensitively.</param>
    /// <returns>The class or job, or null when nothing matches.</returns>
    public static ClassJobInfo? Find(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var wanted = text.Trim();

        foreach (var job in ReadAll())
        {
            if (string.Equals(job.Abbreviation, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(job.Name, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(job.NameEnglish, wanted, StringComparison.OrdinalIgnoreCase))
                return job;
        }

        return null;
    }

    /// <summary>The classes and jobs that fill a role.</summary>
    /// <param name="role">The role to match.</param>
    /// <param name="battleJobsOnly">Whether to skip the battle classes that advance into those jobs.</param>
    /// <returns>The matching classes and jobs, in ascending row order.</returns>
    public static IReadOnlyList<ClassJobInfo> InRole(ClassJobRole role, bool battleJobsOnly = true)
        => Where(job => job.Role == role && (!battleJobsOnly || job.IsBattleJob));

    /// <summary>Every battle job, skipping the classes that advance into them.</summary>
    /// <returns>The battle jobs, in ascending row order.</returns>
    public static IReadOnlyList<ClassJobInfo> BattleJobs() => Where(static job => job.IsBattleJob);

    /// <summary>Every battle class a character levels before advancing into a job.</summary>
    /// <returns>The battle classes, in ascending row order.</returns>
    public static IReadOnlyList<ClassJobInfo> BattleClasses() => Where(static job => job.IsBattleClass);

    /// <summary>Every disciple of the hand and of the land.</summary>
    /// <returns>The crafters and gatherers, in ascending row order.</returns>
    public static IReadOnlyList<ClassJobInfo> HandAndLand() => Where(static job => job.IsHandOrLand);

    /// <summary>
    /// The disciplines the game groups its classes and jobs into, discovered from the sheet rather than listed here,
    /// so they can be shown and picked from by name instead of by a row id a caller has to go and find.
    /// </summary>
    /// <returns>Each discipline's ClassJobCategory row id and name, in ascending row order.</returns>
    public static IReadOnlyList<(uint CategoryId, string Name)> ReadDisciplines()
    {
        var disciplines = new List<(uint CategoryId, string Name)>();
        var seen = new HashSet<uint>();

        foreach (var job in ReadAll())
        {
            if (job.DisciplineCategoryId != 0 && seen.Add(job.DisciplineCategoryId))
                disciplines.Add((job.DisciplineCategoryId, CategoryName(job.DisciplineCategoryId)));
        }

        disciplines.Sort(static (first, second) => first.CategoryId.CompareTo(second.CategoryId));

        return disciplines;
    }

    /// <summary>
    /// Every class and job in a discipline. Take the id from <see cref="ReadDisciplines"/> or from a job's own
    /// <see cref="ClassJobInfo.DisciplineCategoryId"/>.
    /// </summary>
    /// <param name="disciplineCategoryId">The discipline's ClassJobCategory row id.</param>
    /// <returns>The classes and jobs in it, in ascending row order.</returns>
    public static IReadOnlyList<ClassJobInfo> InDiscipline(uint disciplineCategoryId)
        => disciplineCategoryId == 0 ? [] : Where(job => job.DisciplineCategoryId == disciplineCategoryId);

    private static IReadOnlyList<ClassJobInfo> Where(Func<ClassJobInfo, bool> predicate)
    {
        var found = new List<ClassJobInfo>();

        foreach (var job in ReadAll())
        {
            if (predicate(job))
                found.Add(job);
        }

        return found;
    }

    #endregion

    #region Categories

    /// <summary>
    /// Whether a class or job is in a <c>ClassJobCategory</c>, which is how the game writes every "this job may equip
    /// it" and "this job may queue for it" restriction.
    /// </summary>
    /// <param name="categoryId">The ClassJobCategory row id.</param>
    /// <param name="classJobId">The ClassJob row id.</param>
    /// <returns>True when the category includes the class or job.</returns>
    public static bool CategoryIncludes(uint categoryId, uint classJobId)
        => CategoryIndex().TryGetValue(categoryId, out var members) && members.Contains(classJobId);

    /// <summary>Every class and job a category includes.</summary>
    /// <param name="categoryId">The ClassJobCategory row id.</param>
    /// <returns>The ClassJob row ids, in ascending order.</returns>
    public static IReadOnlyList<uint> CategoryMembers(uint categoryId)
    {
        if (!CategoryIndex().TryGetValue(categoryId, out var members))
            return [];

        var ordered = new List<uint>(members);
        ordered.Sort();

        return ordered;
    }

    /// <summary>A category's name, which is the text the game shows on an item's job restriction.</summary>
    /// <param name="categoryId">The ClassJobCategory row id.</param>
    /// <returns>The name, or an empty string.</returns>
    public static string CategoryName(uint categoryId)
    {
        return SafeExecutor.ExecuteSafely(
            () => ExcelSheetHelper.TryGetRow<ClassJobCategory>(categoryId, out var row) && row.HasValue
                ? row.Value.Name.ExtractText()
                : string.Empty,
            string.Empty) ?? string.Empty;
    }

    /// <summary>
    /// Indexes every category's membership once. Column order matches <c>ClassJob</c> row order, so the columns are
    /// read positionally: a job added in a patch needs no code change, and no localised abbreviation is matched.
    /// </summary>
    /// <returns>The ClassJob row ids each category holds.</returns>
    private static IReadOnlyDictionary<uint, IReadOnlySet<uint>> CategoryIndex()
    {
        if (cachedCategoryIndex != null)
            return cachedCategoryIndex;

        var built = SafeExecutor.ExecuteSafely(() =>
        {
            var index = new Dictionary<uint, IReadOnlySet<uint>>();
            var sheet = ExcelSheetHelper.GetSheet<ClassJobCategory>();
            if (sheet == null)
                return index;

            var columns = CategoryColumns();
            var named = NamedClassJobIds();

            foreach (var category in sheet)
            {
                if (category.RowId == 0)
                    continue;

                HashSet<uint>? members = null;

                // The row is boxed once and read through, rather than boxed per column.
                object row = category;

                for (var column = 0; column < columns.Count; column++)
                {
                    var classJobId = (uint)column;

                    if (!named.Contains(classJobId))
                        continue;

                    if (columns[column].GetValue(row) is true)
                        (members ??= []).Add(classJobId);
                }

                if (members != null)
                    index[category.RowId] = members;
            }

            return index;
        }, []) ?? [];

        return cachedCategoryIndex = built;
    }

    private static IReadOnlyList<PropertyInfo> CategoryColumns()
        => cachedCategoryColumns ??= typeof(ClassJobCategory)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(bool))
            .ToArray();

    /// <summary>
    /// The ClassJob rows the sheet actually names. The sheet reserves blank rows ahead of a job shipping, and the
    /// category sheet has columns for them, so membership is only reported for a class or job that exists.
    /// </summary>
    private static IReadOnlySet<uint> NamedClassJobIds()
    {
        var named = new HashSet<uint>();
        var sheet = ExcelSheetHelper.GetSheet<ClassJob>();

        if (sheet == null)
            return named;

        foreach (var row in sheet)
        {
            if (row.Abbreviation.ByteLength > 0)
                named.Add(row.RowId);
        }

        return named;
    }

    #endregion

    #region Character state

    /// <summary>The class or job the character is currently playing.</summary>
    /// <returns>The ClassJob row id, or zero when there is no loaded character.</returns>
    public static uint CurrentId()
    {
        if (!CharacterHelper.IsStateReady)
            return 0;

        return SafeExecutor.ExecuteSafely(() =>
        {
            var playerState = PlayerState.Instance();
            return playerState == null ? 0u : playerState->CurrentClassJobId;
        });
    }

    /// <summary>The class or job the character is currently playing.</summary>
    /// <returns>The class or job, or null when there is no loaded character.</returns>
    public static ClassJobInfo? Current()
    {
        var id = CurrentId();
        return id == 0 ? null : Read(id);
    }

    /// <summary>
    /// The character's level in a class or job. The level is stored per class rather than per job, so asking for a job
    /// answers the level of the class it grew out of, which is the same number the game shows.
    /// </summary>
    /// <param name="classJobId">The ClassJob row id.</param>
    /// <param name="synced">Whether to answer the level the character is synced down to rather than their true level.</param>
    /// <returns>The level, or zero when there is no loaded character or the job holds no level.</returns>
    public static int Level(uint classJobId, bool synced = false)
    {
        if (classJobId == 0 || !CharacterHelper.IsStateReady)
            return 0;

        return SafeExecutor.ExecuteSafely(() =>
        {
            var playerState = PlayerState.Instance();
            return playerState == null ? 0 : playerState->GetClassJobLevel((int)classJobId, synced);
        });
    }

    /// <summary>
    /// The character's level in every class and job that holds one.
    /// </summary>
    /// <param name="minimumLevel">The lowest level worth reporting, so levelled jobs alone can be asked for.</param>
    /// <returns>The level per ClassJob row id.</returns>
    public static IReadOnlyDictionary<uint, int> AllLevels(int minimumLevel = 1)
    {
        var levels = new Dictionary<uint, int>();

        if (!CharacterHelper.IsStateReady)
            return levels;

        SafeExecutor.ExecuteSafely(() =>
        {
            var playerState = PlayerState.Instance();
            if (playerState == null)
                return;

            foreach (var job in ReadAll())
            {
                if (job.ExpArrayIndex < 0)
                    continue;

                var level = playerState->GetClassJobLevel((int)job.RowId, false);
                if (level >= minimumLevel)
                    levels[job.RowId] = level;
            }
        });

        return levels;
    }

    /// <summary>The highest level the character has reached on any class or job.</summary>
    /// <returns>The level, or zero when there is no loaded character.</returns>
    public static int HighestLevel()
    {
        var highest = 0;

        foreach (var level in AllLevels().Values)
        {
            if (level > highest)
                highest = level;
        }

        return highest;
    }

    #endregion

    private static ClassJobInfo Describe(ClassJob row) => new(
        row.RowId,
        row.Name.ExtractText(),
        row.Abbreviation.ExtractText(),
        row.NameEnglish.ExtractText(),
        (ClassJobRole)row.Role,
        row.JobIndex,
        row.BattleClassIndex,
        row.ClassJobParent.RowId,
        row.ExpArrayIndex,
        row.DohDolJobIndex,
        row.StartingLevel,
        row.UnlockQuest.RowId,
        row.ItemSoulCrystal.RowId,
        row.ClassJobCategory.RowId,
        row.IsLimitedJob);
}
