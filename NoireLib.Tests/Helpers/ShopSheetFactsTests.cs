using FluentAssertions;
using Lumina;
using Lumina.Excel.Sheets;
using NoireLib.Helpers;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins the sheet facts the buyable item rules rest on: which rows the game files as a currency, and how many shop
/// rows carrying lines an NPC actually opens. A patch that moves one of these numbers fails a test, never silently
/// moves items in the picker.<br/>
/// These read the installed game archives and skip cleanly without one.
/// </summary>
public sealed class ShopSheetFactsTests
{
    private const int HandlerContentShift = 16;

    // The currency rows in 7.5.
    private const int CurrencyRowCount = 78;

    // The development shop row. No NPC opens it.
    private const uint DevelopmentShopId = 0x40000;

    #region Currencies

    [Fact]
    public void TheCurrencySortCategoryHoldsEveryCurrencyAndNothingElse()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var currencies = CurrencyRows(game);

        currencies.Should().HaveCount(CurrencyRowCount);
        currencies.Should().Contain(ShopHelper.GilItemId);
        currencies.Should().Contain(ShopHelper.StormSealItemId);
    }

    [Fact]
    public void TheCurrencyInterfaceCategoryIsASubsetOfTheSortCategory()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var bySort = new HashSet<uint>(Items(game)
            .Where(static item => item.ItemSortCategory.RowId == ShopHelper.CurrencySortCategoryId)
            .Select(static item => item.RowId));

        var byInterface = Items(game)
            .Where(static item => item.ItemUICategory.RowId == ShopHelper.CurrencyUiCategoryId)
            .Select(static item => item.RowId)
            .ToList();

        byInterface.Should().NotBeEmpty();
        byInterface.Should().BeSubsetOf(bySort);
        byInterface.Count.Should().BeLessThan(bySort.Count, "the sort category also holds gil and the tomestones");
    }

    [Theory]
    [InlineData("Gil", true)]
    [InlineData("Storm Seal", true)]
    [InlineData("Allagan Tomestone of Poetics", true)]
    [InlineData("Venture", true)]
    [InlineData("MGP", true)]
    [InlineData("Fire Crystal", false)]
    [InlineData("Gysahl Greens", false)]
    public void TheRuleTellsACurrencyFromAnOrdinaryItem(string englishName, bool expected)
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var item = Items(game).FirstOrDefault(row => row.Name.ExtractText() == englishName);

        item.RowId.Should().NotBe(0, $"'{englishName}' must exist in the Item sheet");
        IsCurrency(item).Should().Be(expected);
    }

    #endregion

    #region Shops an NPC opens

    [Fact]
    public void SomeShopRowsCarryingLinesAreReachedByNoNpc()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var selling = ShopRowsCarryingLines(game);
        var reachable = HandlersNpcsRun(game);

        selling.Should().Contain(DevelopmentShopId);
        selling.Count.Should().BeGreaterThan(2000);
        selling.Count(shopId => !reachable.Contains(shopId)).Should().BeGreaterThan(0);
        reachable.Should().NotContain(DevelopmentShopId);
    }

    #endregion

    private static IEnumerable<Item> Items(GameData game) => game.GetExcelSheet<Item>()!.Where(static item => item.RowId != 0);

    private static bool IsCurrency(Item item)
        => item.ItemSortCategory.RowId == ShopHelper.CurrencySortCategoryId || item.ItemUICategory.RowId == ShopHelper.CurrencyUiCategoryId;

    private static List<uint> CurrencyRows(GameData game) => [.. Items(game).Where(IsCurrency).Select(static item => item.RowId)];

    private static List<uint> ShopRowsCarryingLines(GameData game)
    {
        var shops = new List<uint>();
        var lines = game.GetSubrowExcelSheet<GilShopItem>()!;

        foreach (var shop in game.GetExcelSheet<GilShop>()!)
        {
            if (shop.RowId != 0 && lines.TryGetRow(shop.RowId, out var subrows) && subrows.Count > 0)
                shops.Add(shop.RowId);
        }

        foreach (var shop in game.GetExcelSheet<SpecialShop>()!)
        {
            if (shop.RowId != 0 && shop.Item.Any(static entry => entry.ReceiveItems.Any(static received => received.Item.RowId != 0)))
                shops.Add(shop.RowId);
        }

        return shops;
    }

    private static HashSet<uint> HandlersNpcsRun(GameData game)
    {
        var direct = new HashSet<uint>();

        foreach (var npc in game.GetExcelSheet<ENpcBase>()!)
        {
            foreach (var data in npc.ENpcData)
            {
                if (data.RowId != 0)
                    direct.Add(data.RowId);
            }
        }

        var reached = new HashSet<uint>(direct);

        foreach (var topic in game.GetExcelSheet<TopicSelect>()!)
        {
            if (!direct.Contains(topic.RowId))
                continue;

            foreach (var shop in topic.Shop)
            {
                if (shop.RowId != 0)
                    reached.Add(shop.RowId);
            }
        }

        foreach (var pre in game.GetExcelSheet<PreHandler>()!)
        {
            if (direct.Contains(pre.RowId) && pre.Target.RowId != 0)
                reached.Add(pre.Target.RowId);
        }

        foreach (var array in game.GetExcelSheet<ArrayEventHandler>()!)
        {
            if (!direct.Contains(array.RowId))
                continue;

            foreach (var entry in array.Data)
            {
                if (entry.RowId != 0)
                    reached.Add(entry.RowId);
            }
        }

        foreach (var talk in game.GetExcelSheet<CustomTalk>()!)
        {
            if (!direct.Contains(talk.RowId))
                continue;

            foreach (var script in talk.Script)
            {
                if (script.ScriptArg >= 1u << HandlerContentShift)
                    reached.Add(script.ScriptArg);
            }
        }

        return reached;
    }
}
