using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
using ContentType = FFXIVClientStructs.FFXIV.Client.Game.Event.ContentType;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FluentAssertions;
using NoireLib.Helpers;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the pure rules in the shop, duty, class job, text command and world helpers: the ones that index a catalog,
/// pick the cheapest price, fold a set of flags, reduce a typed command line, or answer a query the sheets cannot
/// serve. Every one of them is a function over its inputs, so none of these needs a game. The sheet reads around them
/// are exercised in game, and their no-game behaviour is asserted here instead.
/// </summary>
public sealed class ShopDutyJobHelperTests
{
    #region Shop costs and offers

    [Fact]
    public void ShopCost_RecognisesGilByItsItemId()
    {
        new ShopCost(ShopHelper.GilItemId, 500).IsGil.Should().BeTrue();
        new ShopCost(28, 5).IsGil.Should().BeFalse();
    }

    [Fact]
    public void ShopOffer_ReadsItsGilCostOutOfItsCosts()
    {
        var offer = Offer(itemId: 4850, costs: [new ShopCost(ShopHelper.GilItemId, 1200)]);

        offer.GilCost.Should().Be(1200);
        offer.IsGilPurchase.Should().BeTrue();
    }

    [Fact]
    public void ShopOffer_IsNotAGilPurchaseWhenGilIsOnlyPartOfThePrice()
    {
        // A special shop can charge gil alongside a token, and that is not something a plain gil purchase can pay.
        var offer = Offer(costs: [new ShopCost(ShopHelper.GilItemId, 100), new ShopCost(28, 3)]);

        offer.GilCost.Should().Be(100);
        offer.IsGilPurchase.Should().BeFalse();
    }

    [Fact]
    public void ShopOffer_HasNoGilCostWhenNothingIsPaidInGil()
    {
        var offer = Offer(costs: [new ShopCost(28, 3)]);

        offer.GilCost.Should().Be(0);
        offer.IsGilPurchase.Should().BeFalse();
    }

    #endregion

    #region The catalog

    [Fact]
    public void Catalog_FindsEveryShopSellingAnItem()
    {
        var catalog = SampleCatalog();

        catalog.ShopsSelling(4850).Should().BeEquivalentTo([262100u, 262200u]);
        catalog.ShopsSelling(9999).Should().BeEmpty();
    }

    [Fact]
    public void Catalog_ReturnsEveryOfferForAnItemAcrossShops()
    {
        var offers = SampleCatalog().OffersFor(4850);

        offers.Should().HaveCount(2);
        offers.Should().OnlyContain(offer => offer.ItemId == 4850);
    }

    [Fact]
    public void Catalog_PicksTheCheapestGilPriceAndTheShopChargingIt()
    {
        var cheapest = SampleCatalog().CheapestGilPrice(4850);

        cheapest.Should().NotBeNull();
        cheapest!.Value.ShopId.Should().Be(262200u);
        cheapest.Value.GilCost.Should().Be(800u);
    }

    [Fact]
    public void Catalog_HasNoCheapestGilPriceForATokenOnlyItem()
    {
        // The token offer is in the catalog and findable, it just cannot be paid for with gil.
        var catalog = SampleCatalog();

        catalog.ShopsSelling(30000).Should().ContainSingle();
        catalog.CheapestGilPrice(30000).Should().BeNull();
    }

    [Fact]
    public void EmptyCatalog_MissesEveryLookup()
    {
        ShopCatalog.Empty.ShopsSelling(4850).Should().BeEmpty();
        ShopCatalog.Empty.OffersFor(4850).Should().BeEmpty();
        ShopCatalog.Empty.CheapestGilPrice(4850).Should().BeNull();
    }

    [Theory]
    [InlineData(0x40000u, EventHandlerContent.Shop)]
    [InlineData(0x40483u, EventHandlerContent.Shop)]
    [InlineData(0x1B0000u, EventHandlerContent.SpecialShop)]
    [InlineData(0x1B061Fu, EventHandlerContent.SpecialShop)]
    public void KindOf_ReadsTheHandlerContentOutOfTheRowIdItself(uint shopId, EventHandlerContent expected)
        => ShopHelper.KindOf(shopId).Should().Be(expected);

    [Theory]
    [InlineData(0u)]
    [InlineData(0x320000u)]
    [InlineData(0x3A0000u)]
    public void KindOf_RejectsAnIdWhoseHandlerContentIsNotAShopItReads(uint handlerId)
        => ShopHelper.KindOf(handlerId).Should().BeNull();

    [Fact]
    public void ShopHelper_AnswersNothingForRowZeroWithoutAGame()
    {
        ShopHelper.KindOf(0).Should().BeNull();
        ShopHelper.ReadShop(0).Should().BeNull();
        ShopHelper.ReadOffers(0).Should().BeEmpty();
        ShopHelper.ReadGrandCompanyOffers(GrandCompany.None).Should().BeEmpty();
        ShopHelper.SealItemId(GrandCompany.None).Should().Be(0);
    }

    [Theory]
    [InlineData(GrandCompany.Maelstrom, ShopHelper.StormSealItemId)]
    [InlineData(GrandCompany.TwinAdder, ShopHelper.SerpentSealItemId)]
    [InlineData(GrandCompany.ImmortalFlames, ShopHelper.FlameSealItemId)]
    public void SealItemId_AnswersWithoutAGame(GrandCompany company, uint expected)
    {
        // The currencies are Item rows, so they answer from static sheet data with nothing running: no client, no
        // logged-in character, no inventory.
        ShopHelper.SealItemId(company).Should().Be(expected);
        ShopHelper.GilItemId.Should().Be(1);
    }

    #endregion

    #region Duties

    [Fact]
    public void DutyInfo_KnowsWhetherAnyRouletteDrawsIt()
    {
        Duty(roulettes: []).IsInAnyRoulette.Should().BeFalse();
        Duty(roulettes: [1]).IsInAnyRoulette.Should().BeTrue();
    }

    [Fact]
    public void DutyInfo_HoldsTheRouletteRowIdsTheGameItselfNumbers()
    {
        // A roulette is a ContentRoulette row, not a name written down here: row 1 is Leveling and row 9 is Mentor on
        // every client, and a roulette added in a later patch is just another row id.
        var duty = Duty(roulettes: [1, 9]);

        duty.IsInRoulette(1).Should().BeTrue();
        duty.IsInRoulette(9).Should().BeTrue();
        duty.IsInRoulette(5).Should().BeFalse();
    }

    [Fact]
    public void DutyInfo_IsOnlyInstanceContentWhenItsLinkTypeSaysSo()
    {
        // The content column points into whichever sheet the link type names, so a non-instanced duty's content id is
        // a row in some other sheet and must not be handed to an instance content lookup.
        Duty(contentId: 20, contentLinkType: ContentType.Instance).IsInstanceContent.Should().BeTrue();
        Duty(contentId: 20, contentLinkType: ContentType.Public).IsInstanceContent.Should().BeFalse();
        Duty(contentId: 0, contentLinkType: ContentType.Instance).IsInstanceContent.Should().BeFalse();
    }

    [Fact]
    public void DutyHelper_AnswersNothingForRowZeroWithoutAGame()
    {
        DutyHelper.Read(0).Should().BeNull();
        DutyHelper.Name(0).Should().BeEmpty();
        DutyHelper.IsUnlocked(0).Should().BeFalse();
        DutyHelper.IsCompleted(0).Should().BeFalse();
        DutyHelper.InRoulette(0).Should().BeEmpty();
        DutyHelper.RouletteName(0).Should().BeEmpty();
        DutyHelper.InTerritory(0).Should().BeEmpty();

        var (unlocked, completed) = DutyHelper.ReadProgress([]);
        unlocked.Should().BeEmpty();
        completed.Should().BeEmpty();
    }

    #endregion

    #region Classes and jobs

    [Fact]
    public void ClassJobInfo_IsABattleJobOnlyOnceItHasAPlaceInThatNumbering()
    {
        Job(jobIndex: 0).IsBattleJob.Should().BeFalse();
        Job(jobIndex: 1).IsBattleJob.Should().BeTrue();
    }

    [Fact]
    public void ClassJobInfo_DoesNotCallACrafterOrGathererABattleJob()
    {
        // A crafter and a gatherer sit outside the battle job numbering entirely, so their zero there means "not a
        // battle job", never "not a job". They are recognised by their own index instead.
        var crafter = Job(jobIndex: 0, handOrLandIndex: 0, battleClassIndex: -1);

        crafter.IsBattleJob.Should().BeFalse();
        crafter.IsBattleClass.Should().BeFalse();
        crafter.IsHandOrLand.Should().BeTrue();
    }

    [Fact]
    public void ClassJobInfo_SeparatesABattleClassFromTheJobItAdvancesInto()
    {
        // A base class has a place in the class numbering and none in the job numbering; a job that was introduced
        // outright has both, so the class test has to exclude anything already numbered as a job.
        Job(jobIndex: 0, battleClassIndex: 0).IsBattleClass.Should().BeTrue();
        Job(jobIndex: 11, battleClassIndex: 9).IsBattleClass.Should().BeFalse();
        Job(jobIndex: 1, battleClassIndex: -1).IsBattleClass.Should().BeFalse();
    }

    [Theory]
    [InlineData(ClassJobRole.MeleeDps, true)]
    [InlineData(ClassJobRole.RangedDps, true)]
    [InlineData(ClassJobRole.Tank, false)]
    [InlineData(ClassJobRole.Healer, false)]
    [InlineData(ClassJobRole.None, false)]
    public void ClassJobInfo_TreatsBothDamageRolesAsDps(ClassJobRole role, bool expected)
        => Job(role: role).IsDps.Should().Be(expected);

    [Fact]
    public void ClassJobHelper_AnswersNothingForRowZeroWithoutAGame()
    {
        ClassJobHelper.Read(0).Should().BeNull();
        ClassJobHelper.Name(0).Should().BeEmpty();
        ClassJobHelper.Abbreviation(0).Should().BeEmpty();
        ClassJobHelper.Find("   ").Should().BeNull();
        ClassJobHelper.Level(0).Should().Be(0);
        ClassJobHelper.CategoryIncludes(0, 19).Should().BeFalse();
        ClassJobHelper.CategoryMembers(0).Should().BeEmpty();
        ClassJobHelper.CategoryName(0).Should().BeEmpty();
    }

    [Fact]
    public void ClassJobInfo_IsHandOrLandOnlyWithAnIndexAmongThem()
    {
        // The game gives every crafter and gatherer an index among them and every battle job -1, which is the test
        // with nothing written down: no row id, no abbreviation, no category number.
        Job(handOrLandIndex: -1).IsHandOrLand.Should().BeFalse();
        Job(handOrLandIndex: 0).IsHandOrLand.Should().BeTrue();
        Job(handOrLandIndex: 7).IsHandOrLand.Should().BeTrue();
    }

    [Fact]
    public void CategoryIncludes_RejectsAClassJobTheCategorySheetHasNoColumnFor()
    {
        // The sheet holds one column per class and job, so an id past the last column can only ever be false and must
        // not be looked up as though it had one.
        ClassJobHelper.CategoryIncludes(1, 4000).Should().BeFalse();
    }

    #endregion

    #region Text commands

    [Theory]
    [InlineData("/dance", "dance")]
    [InlineData("dance", "dance")]
    [InlineData("  /DANCE  ", "dance")]
    [InlineData("/dance motion", "dance")]
    [InlineData("/ac \"Fire\"", "ac")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize_ReducesALineToItsCommandWord(string? text, string expected)
        => TextCommandHelper.Normalize(text).Should().Be(expected);

    [Fact]
    public void Normalize_AcceptsTheFullWidthSlashTheJapaneseClientUses()
        => TextCommandHelper.Normalize("／dance").Should().Be("dance");

    [Fact]
    public void Spellings_ListsOnlyTheFormsTheCommandActuallyHas()
    {
        var command = new TextCommandInfo(1, "/beckon", "/beck", string.Empty, string.Empty, "Beckon.");

        command.Spellings().Should().BeEquivalentTo(["/beckon", "/beck"], options => options.WithStrictOrdering());
    }

    [Fact]
    public void Matches_AcceptsEverySpellingTheClientWouldAccept()
    {
        var command = new TextCommandInfo(1, "/beckon", "/beck", "/wave", "/wv", "Beckon.");

        TextCommandHelper.Matches(command, "beckon").Should().BeTrue();
        TextCommandHelper.Matches(command, "beck").Should().BeTrue();
        TextCommandHelper.Matches(command, "wave").Should().BeTrue();
        TextCommandHelper.Matches(command, "wv").Should().BeTrue();
        TextCommandHelper.Matches(command, "dance").Should().BeFalse();
    }

    [Fact]
    public void TextCommandHelper_AnswersNothingForRowZeroWithoutAGame()
    {
        TextCommandHelper.Read(0).Should().BeNull();
        TextCommandHelper.Find(string.Empty).Should().BeNull();
        TextCommandHelper.Localize("/dance").Should().BeNull();
    }

    #endregion

    #region Worlds

    [Fact]
    public void WorldHelper_AnswersNothingForRowZeroWithoutAGame()
    {
        WorldHelper.Read(0).Should().BeNull();
        WorldHelper.Name(0).Should().BeEmpty();
        WorldHelper.Find("  ").Should().BeNull();
        WorldHelper.DataCenterName(0).Should().BeEmpty();
        WorldHelper.ShareDataCenter(0, 0).Should().BeFalse();
        WorldHelper.IsVisiting().Should().BeFalse();
        WorldHelper.IsTravelling().Should().BeFalse();
    }

    #endregion

    #region Sample data

    private static ShopOffer Offer(
        uint shopId = 262100,
        EventHandlerContent kind = EventHandlerContent.Shop,
        uint itemId = 4850,
        IReadOnlyList<ShopCost>? costs = null)
        => new(shopId, kind, itemId, 1, false, costs ?? [new ShopCost(ShopHelper.GilItemId, 1000)], [], 0, 0);

    /// <summary>
    /// Two gil shops selling the same item at different prices, and a token shop selling something else, which is the
    /// smallest shape that exercises every catalog query.
    /// </summary>
    private static ShopCatalog SampleCatalog()
    {
        var first = Offer(262100, EventHandlerContent.Shop, 4850, [new ShopCost(ShopHelper.GilItemId, 1000)]);
        var second = Offer(262200, EventHandlerContent.Shop, 4850, [new ShopCost(ShopHelper.GilItemId, 800)]);
        var third = Offer(262200, EventHandlerContent.Shop, 5000, [new ShopCost(ShopHelper.GilItemId, 60)]);
        var token = Offer(1769500, EventHandlerContent.SpecialShop, 30000, [new ShopCost(28, 5)]);

        return new ShopCatalog(
            new Dictionary<uint, IReadOnlyList<uint>>
            {
                [4850] = [262100u, 262200u],
                [5000] = [262200u],
                [30000] = [1769500u],
            },
            new Dictionary<uint, IReadOnlyList<ShopOffer>>
            {
                [262100] = [first],
                [262200] = [second, third],
                [1769500] = [token],
            },
            new Dictionary<uint, EventHandlerContent>
            {
                [262100] = EventHandlerContent.Shop,
                [262200] = EventHandlerContent.Shop,
                [1769500] = EventHandlerContent.SpecialShop,
            });
    }

    private static DutyInfo Duty(
        IReadOnlyList<uint>? roulettes = null,
        uint contentId = 20,
        ContentType contentLinkType = ContentType.Instance)
        => new(
            1036, "Sastasha", "Sastasha", 1036, 2, contentId, contentLinkType,
            15, 0, 0, 0, 4, 68, roulettes ?? [], true, false, false, false);

    private static ClassJobInfo Job(
        byte jobIndex = 1,
        ClassJobRole role = ClassJobRole.Tank,
        sbyte handOrLandIndex = -1,
        sbyte battleClassIndex = -1)
        => new(19, "Paladin", "PLD", "Paladin", role, jobIndex, battleClassIndex, 1, 0, handOrLandIndex, 30, 0, 0, 30, false);

    #endregion
}
