using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>The price break the game applies to a teleport.</summary>
public enum TeleportDiscount
{
    /// <summary>Full price.</summary>
    None,

    /// <summary>A favoured destination, charged half.</summary>
    Favoured,

    /// <summary>The character's own residential district, charged a quarter.</summary>
    ResidentDistrict,
}

/// <summary>
/// Prices a teleport between two territories exactly as the client does, from the aetherstream coordinates each
/// territory carries and the relay network that joins the regions. It needs no character and no game state. A
/// fare is available for a pair the character is nowhere near.
/// </summary>
public static class TeleportFareHelper
{
    /// <summary>The fare charged when either territory carries no aetherstream row.</summary>
    public const int UnknownTerritoryFare = 999;

    /// <summary>The fare above which the game charges only half of each further gil.</summary>
    public const int HalfPriceThreshold = 1000;

    // The column the schema calls Expansion is the term that scales the rides, not a game version.
    internal readonly record struct Stream(int X, int Y, int Term, uint Relay);

    // How far the region's X axis wraps, and where it crosses into each other region.
    internal readonly record struct Relay(long Wrap, IReadOnlyList<(uint Enter, uint Exit, int Cost)> Crossings);

    /// <summary>The gil a teleport from one territory to another costs.</summary>
    /// <param name="fromTerritoryId">The TerritoryType row the character is in.</param>
    /// <param name="toTerritoryId">The TerritoryType row the destination aetheryte stands in.</param>
    /// <param name="discount">The price break to apply.</param>
    /// <param name="multiplier">What a discount leaves of the price, as a percentage. 100 for the full fare.</param>
    /// <returns>The fare in gil.</returns>
    public static int Fare(
        uint fromTerritoryId,
        uint toTerritoryId,
        TeleportDiscount discount = TeleportDiscount.None,
        int multiplier = 100)
        => SafeExecutor.ExecuteSafely(
            () => Fare(fromTerritoryId, toTerritoryId, discount, ReadStream, ReadRelay, multiplier),
            UnknownTerritoryFare);

    internal static int Fare(
        uint fromTerritoryId,
        uint toTerritoryId,
        TeleportDiscount discount,
        Func<uint, Stream?> stream,
        Func<uint, Relay?> relay,
        int multiplier = 100)
    {
        if (stream(fromTerritoryId) is not { } from || stream(toTerritoryId) is not { } to)
            return UnknownTerritoryFare;

        var raw = from.Relay == to.Relay
            ? Ride(from, to, relay(from.Relay)?.Wrap ?? 0)
            : Crossed(from, to, stream, relay);

        if (raw > HalfPriceThreshold)
            raw = HalfPriceThreshold + ((raw - HalfPriceThreshold) / 2);

        return raw / Divisor(discount) * multiplier / 100;
    }

    private static int Divisor(TeleportDiscount discount) => discount switch
    {
        TeleportDiscount.ResidentDistrict => 4,
        TeleportDiscount.Favoured => 2,
        _ => 1,
    };

    // The ride to the region's exit, the crossing's fixed price, and the ride from where it lands. The departing region's row is indexed by the arriving region.
    private static int Crossed(Stream from, Stream to, Func<uint, Stream?> stream, Func<uint, Relay?> relay)
    {
        if (relay(from.Relay) is not { } departing || to.Relay >= (uint)departing.Crossings.Count)
            return UnknownTerritoryFare;

        var crossing = departing.Crossings[(int)to.Relay];

        if (stream(crossing.Enter) is not { } enter || stream(crossing.Exit) is not { } exit)
            return UnknownTerritoryFare;

        return Ride(from, enter, departing.Wrap)
            + crossing.Cost
            + Ride(exit, to, relay(exit.Relay)?.Wrap ?? 0);
    }

    // The X axis wraps and is charged the short way round. Integer division and a truncated distance, like the client.
    private static int Ride(Stream from, Stream to, long wrap)
    {
        long dx = Math.Abs(from.X - to.X);
        if (dx > wrap && dx < 2 * wrap)
            dx = (2 * wrap) - dx;

        long dy = from.Y - to.Y;
        var distance = (long)Math.Sqrt((double)((dx * dx) + (dy * dy)));

        var term = from.Term + to.Term;
        var scale = term >= 400 ? term + 600 : 1000;

        return (int)((((scale * distance) / 1000) + 500) / 5);
    }

    private static Stream? ReadStream(uint territoryId)
        => ExcelSheetHelper.TryGetRow<TerritoryTypeTelepo>(territoryId, out var row) && row is { } telepo
            ? new Stream(telepo.X, telepo.Y, telepo.Expansion, telepo.Relay.RowId)
            : null;

    private static Relay? ReadRelay(uint relayId)
    {
        if (!ExcelSheetHelper.TryGetRow<TelepoRelay>(relayId, out var row) || row is not { } relay)
            return null;

        var crossings = new List<(uint Enter, uint Exit, int Cost)>(relay.Relays.Count);
        foreach (var crossing in relay.Relays)
            crossings.Add((crossing.EnterTerritory.RowId, crossing.ExitTerritory.RowId, crossing.Cost));

        return new Relay(relay.Unknown_70, crossings);
    }
}
