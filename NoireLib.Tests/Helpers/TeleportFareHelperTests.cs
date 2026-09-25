using FluentAssertions;
using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks the teleport fare against the client's own Telepo.GetTeleportCost, on hand-built streams and the real sheets.</summary>
public sealed class TeleportFareHelperTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper output = output;

    private const int LimsaX = 9924;
    private const int LimsaY = 8856;
    private const int UldahX = 11540;
    private const int UldahY = 9608;
    private const long OldWorldWrap = 9250;

    private static TeleportFareHelper.Stream Stream(int x, int y, uint relay = 0, int term = 200)
        => new(x, y, term, relay);

    private static Func<uint, TeleportFareHelper.Relay?> OneRelay(long wrap = OldWorldWrap)
        => _ => new TeleportFareHelper.Relay(wrap, []);

    private static int Fare(TeleportFareHelper.Stream from, TeleportFareHelper.Stream to, long wrap = OldWorldWrap)
        => TeleportFareHelper.Fare(1, 2, TeleportDiscount.None, id => id == 1 ? from : to, OneRelay(wrap));

    [Fact]
    public void ARideWithinOneRegion_IsPricedFromTheAetherstreamDistance()
    {
        // floor(sqrt(1616^2 + 752^2)) = 1782, then (1000 * 1782 / 1000 + 500) / 5 = 456.
        Fare(Stream(LimsaX, LimsaY), Stream(UldahX, UldahY)).Should().Be(456);
    }

    [Fact]
    public void TheSameTerritory_CostsTheFloorFare()
    {
        Fare(Stream(LimsaX, LimsaY), Stream(LimsaX, LimsaY)).Should().Be(100);
    }

    [Fact]
    public void TheFareIsSymmetric()
    {
        Fare(Stream(LimsaX, LimsaY), Stream(UldahX, UldahY))
            .Should().Be(Fare(Stream(UldahX, UldahY), Stream(LimsaX, LimsaY)));
    }

    [Fact]
    public void PastTheThreshold_OnlyHalfOfEachFurtherGilIsCharged()
    {
        var far = Fare(Stream(0, 0), Stream(0, 40000));

        far.Should().BeGreaterThan(TeleportFareHelper.HalfPriceThreshold);
        far.Should().BeLessThan(4600, "everything above a thousand is charged at half");
    }

    [Fact]
    public void TheXAxisWraps_SoAPairPastTheWrapIsChargedTheShortWayRound()
    {
        var wrapped = Fare(Stream(0, 0), Stream((int)(2 * OldWorldWrap) - 1616, 752));
        var direct = Fare(Stream(LimsaX, LimsaY), Stream(UldahX, UldahY));

        wrapped.Should().Be(direct, "crossing the seam is the same ride as the short way round");
    }

    [Theory]
    [InlineData(TeleportDiscount.None, 456)]
    [InlineData(TeleportDiscount.Favoured, 228)]
    [InlineData(TeleportDiscount.ResidentDistrict, 114)]
    public void ADiscount_DividesTheFare(TeleportDiscount discount, int expected)
    {
        TeleportFareHelper.Fare(
            1, 2, discount,
            id => id == 1 ? Stream(LimsaX, LimsaY) : Stream(UldahX, UldahY),
            OneRelay()).Should().Be(expected);
    }

    [Theory]
    [InlineData(100, 456)]
    [InlineData(70, 319)]
    [InlineData(50, 228)]
    [InlineData(0, 0)]
    public void ADiscountMultiplier_TakesItsPercentageOfTheFare(int multiplier, int expected)
    {
        TeleportFareHelper.Fare(
            1, 2, TeleportDiscount.None,
            id => id == 1 ? Stream(LimsaX, LimsaY) : Stream(UldahX, UldahY),
            OneRelay(), multiplier).Should().Be(expected);
    }

    [Fact]
    public void ADiscountAndAMultiplier_BothApply()
    {
        // Half first, then the percentage, like the client.
        TeleportFareHelper.Fare(
            1, 2, TeleportDiscount.Favoured,
            id => id == 1 ? Stream(LimsaX, LimsaY) : Stream(UldahX, UldahY),
            OneRelay(), 50).Should().Be(114);
    }

    [Fact]
    public void AnUnknownTerritory_IsTheFallbackFare()
    {
        TeleportFareHelper.Fare(1, 2, TeleportDiscount.None, _ => null, OneRelay())
            .Should().Be(TeleportFareHelper.UnknownTerritoryFare);
    }

    /// <summary>Fares read off the game's teleport menu in Limsa Lominsa Lower Decks, without discounts.</summary>
    [Theory]
    [InlineData(180u, TeleportDiscount.None, 263, "Camp Overlook, Outer La Noscea")]
    [InlineData(147u, TeleportDiscount.None, 413, "Ceruleum Processing Plant, Northern Thanalan")]
    [InlineData(641u, TeleportDiscount.ResidentDistrict, 376, "a private estate in Shirogane, past the halving")]
    [InlineData(341u, TeleportDiscount.ResidentDistrict, 114, "an apartment in the Goblet")]
    public void AgainstTheGame_TheFareIsExactToTheGil(uint destination, TeleportDiscount discount, int expected, string what)
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var sheets = SheetLookups(game);

        TeleportFareHelper.Fare(129, destination, discount, sheets.Stream, sheets.Relay)
            .Should().Be(expected, "the game charges {0} gil for {1}", expected, what);
    }

    [Fact]
    public void AgainstTheRealSheets_TheFaresAreSaneAndSymmetric()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var sheets = SheetLookups(game);

        int Fare(uint from, uint to) => TeleportFareHelper.Fare(from, to, TeleportDiscount.None, sheets.Stream, sheets.Relay);

        Fare(129, 130).Should().Be(456);

        uint[] hubs = [128, 129, 130, 132, 133, 140, 155, 418, 478, 612, 621, 813, 819, 956, 957, 962, 1185, 1186];
        foreach (var from in hubs)
        {
            foreach (var to in hubs)
            {
                var fare = Fare(from, to);
                fare.Should().BeInRange(1, 4000, "a teleport from {0} to {1} is a real fare", from, to);
                fare.Should().NotBe(TeleportFareHelper.UnknownTerritoryFare, "both territories carry an aetherstream row");
                Fare(to, from).Should().Be(fare, "the aetherstream is symmetric between {0} and {1}", from, to);
            }
        }

        output.WriteLine($"Limsa->Ul'dah {Fare(129, 130)}, Limsa->Gridania {Fare(129, 132)}, "
            + $"Limsa->Crystarium {Fare(129, 819)}, Limsa->Tuliyollal {Fare(129, 1185)}");
    }

    // The two sheets are read positionally. A column taken from the wrong place fails here.
    private static (Func<uint, TeleportFareHelper.Stream?> Stream, Func<uint, TeleportFareHelper.Relay?> Relay) SheetLookups(GameData game)
    {
        var telepo = game.Excel.GetSheet<RawRow>(Language.English, "TerritoryTypeTelepo");
        var relays = game.GetExcelSheet<TelepoRelay>(Language.English);

        return (Stream, Relay);

        TeleportFareHelper.Stream? Stream(uint territory)
            => telepo.TryGetRow(territory, out var row)
                ? new TeleportFareHelper.Stream(row.ReadUInt16Column(0), row.ReadUInt16Column(1), row.ReadUInt16Column(2), row.ReadUInt8Column(3))
                : null;

        TeleportFareHelper.Relay? Relay(uint id)
        {
            if (!relays.TryGetRow(id, out var row))
                return null;

            var crossings = new List<(uint Enter, uint Exit, int Cost)>();
            foreach (var crossing in row.Relays)
                crossings.Add((crossing.EnterTerritory.RowId, crossing.ExitTerritory.RowId, crossing.Cost));

            return new TeleportFareHelper.Relay(row.Unknown_70, crossings);
        }
    }
}
