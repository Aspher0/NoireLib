using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Threading;

namespace NoireLib.Helpers;

/// <summary>
/// Resolves the cost columns a special shop does not fill with an Item row. A tomestone line names a Tomestones row
/// and a scrip line names a scrip slot.
/// </summary>
public static class ShopCurrencyHelper
{
    /// <summary>The scrip each slot charges. A slot is repointed when an expansion brings a new pair.</summary>
    public static IReadOnlyDictionary<uint, uint> ScripSlots { get; } = new Dictionary<uint, uint>
    {
        [2] = 33913,
        [4] = 33914,
        [6] = 41784,
        [7] = 41785,
    };

    private static readonly object TomestoneLock = new();

    private static IReadOnlyDictionary<uint, uint>? cachedTomestones;

    /// <summary>The tomestone each Tomestones row charges, read from TomestonesItem.</summary>
    public static IReadOnlyDictionary<uint, uint> TomestoneTiers
    {
        get
        {
            if (Volatile.Read(ref cachedTomestones) is { } cached)
                return cached;

            lock (TomestoneLock)
                return cachedTomestones ??= ReadTomestoneTiers();
        }
    }

    /// <summary>The Item row a cost column charges.</summary>
    /// <param name="kind">What the column names.</param>
    /// <param name="value">The column's value: an Item row, a Tomestones row or a scrip slot.</param>
    /// <returns>The Item row id, or zero when the slot names no item.</returns>
    public static uint Resolve(ShopCostKind kind, uint value) => kind switch
    {
        ShopCostKind.Tomestone => TomestoneTiers.TryGetValue(value, out var tomestone) ? tomestone : 0,
        ShopCostKind.Scrip => ScripSlots.TryGetValue(value, out var scrip) ? scrip : 0,
        _ => value,
    };

    /// <summary>The kind a special shop's cost type column names.</summary>
    /// <param name="costType">The CostType column of one cost.</param>
    /// <returns>The kind.</returns>
    public static ShopCostKind KindOf(byte costType) => costType switch
    {
        1 => ShopCostKind.HighQualityItem,
        2 => ShopCostKind.Tomestone,
        3 => ShopCostKind.Scrip,
        _ => ShopCostKind.Item,
    };

    private static IReadOnlyDictionary<uint, uint> ReadTomestoneTiers()
    {
        var found = SafeExecutor.ExecuteSafely(() =>
        {
            var tiers = new Dictionary<uint, uint>();
            var sheet = ExcelSheetHelper.GetSheet<TomestonesItem>();

            if (sheet == null)
                return tiers;

            foreach (var row in sheet)
            {
                if (row.Item.RowId != 0 && !tiers.ContainsKey(row.Tomestones.RowId))
                    tiers[row.Tomestones.RowId] = row.Item.RowId;
            }

            return tiers;
        }, []);

        return found ?? new Dictionary<uint, uint>();
    }
}
