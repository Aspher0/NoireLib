using FluentAssertions;
using NoireLib.Helpers;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks how a special shop's cost column is read. Tomestones and scrips hold a slot, not an Item row.</summary>
public sealed class ShopCostTests
{
    [Fact]
    public void KindOf_MapsTheCostTypeColumn()
    {
        ShopCurrencyHelper.KindOf(0).Should().Be(ShopCostKind.Item);
        ShopCurrencyHelper.KindOf(1).Should().Be(ShopCostKind.HighQualityItem);
        ShopCurrencyHelper.KindOf(2).Should().Be(ShopCostKind.Tomestone);
        ShopCurrencyHelper.KindOf(3).Should().Be(ShopCostKind.Scrip);
    }

    [Fact]
    public void Resolve_ScripSlotsNameTheirItem()
    {
        ShopCurrencyHelper.Resolve(ShopCostKind.Scrip, 6).Should().Be(41784u);
        ShopCurrencyHelper.Resolve(ShopCostKind.Scrip, 7).Should().Be(41785u);
        ShopCurrencyHelper.Resolve(ShopCostKind.Scrip, 99).Should().Be(0u, "because no scrip sits in that slot");
    }

    [Fact]
    public void Resolve_AnItemColumnPassesThrough()
        => ShopCurrencyHelper.Resolve(ShopCostKind.Item, 12994).Should().Be(12994u);

    [Fact]
    public void IsGil_ATomestoneTierOneIsNotGil()
    {
        new ShopCost(28, 345, 0, ShopCostKind.Tomestone, 1).IsGil.Should().BeFalse();
        new ShopCost(ShopHelper.GilItemId, 345).IsGil.Should().BeTrue();
    }

    [Fact]
    public void IsCurrency_ATomestoneAndAScripAreCurrencies()
    {
        new ShopCost(28, 345, 0, ShopCostKind.Tomestone, 1).IsCurrency.Should().BeTrue();
        new ShopCost(41784, 500, 0, ShopCostKind.Scrip, 6).IsCurrency.Should().BeTrue();
        new ShopCost(0, 100, 0, ShopCostKind.CompanyCredit).IsCurrency.Should().BeTrue();
    }

    [Fact]
    public void NeedsHighQuality_OnlyTheHighQualityKind()
    {
        new ShopCost(5379, 1, 0, ShopCostKind.HighQualityItem).NeedsHighQuality.Should().BeTrue();
        new ShopCost(5379, 1).NeedsHighQuality.Should().BeFalse();
    }
}
