using FluentAssertions;
using NoireLib.Animations.Helpers;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the customize-bytes-to-skeleton-id mapping a swap feature reads a target character with:
/// every id <see cref="RaceGenderData.SkeletonFromCustomize"/> can derive has to be one of the 18
/// <see cref="RaceGenderData.AllRaces"/> already declares, or a swap would silently point at a
/// skeleton nothing in a mod was ever built for.
/// </summary>
public class RaceGenderDataTests
{
    [Theory]
    [InlineData((byte)1, (byte)1, (byte)0, "c0101")]  // Hyur, Midlander, Male
    [InlineData((byte)1, (byte)2, (byte)1, "c0401")]  // Hyur, Highlander, Female
    [InlineData((byte)4, (byte)7, (byte)1, "c0801")]  // Miqo'te, Seeker of the Sun, Female
    [InlineData((byte)3, (byte)5, (byte)0, "c1101")]  // Lalafell, Plainsfolk, Male
    [InlineData((byte)8, (byte)15, (byte)1, "c1801")] // Viera, Rava, Female
    public void SkeletonFromCustomize_MapsTheBriefsNamedCases(byte race, byte tribe, byte gender, string expected)
        => RaceGenderData.SkeletonFromCustomize(race, tribe, gender).Should().Be(expected);

    /// <summary>
    /// Every race the game has, crossed with both genders, using one representative tribe per race
    /// (a race's two tribes share a skeleton; Hyur is the only race whose tribe changes it at all).
    /// The 18 ids this derives must be exactly the 18 <see cref="RaceGenderData.AllRaces"/> lists -
    /// not merely a subset of them, and none of them repeated.
    /// </summary>
    [Fact]
    public void SkeletonFromCustomize_EveryGeneratedIdExistsInAllRacesAndCoversItExactly()
    {
        (byte Race, byte Tribe)[] raceTribes =
        [
            (1, 1),  // Hyur / Midlander
            (1, 2),  // Hyur / Highlander
            (2, 3),  // Elezen / Wildwood
            (3, 5),  // Lalafell / Plainsfolk
            (4, 7),  // Miqo'te / Seeker of the Sun
            (5, 9),  // Roegadyn / Sea Wolf
            (6, 11), // Au Ra / Raen
            (7, 13), // Hrothgar / Helions
            (8, 15), // Viera / Rava
        ];

        var generated = new List<string>();
        foreach (var (race, tribe) in raceTribes)
        {
            generated.Add(RaceGenderData.SkeletonFromCustomize(race, tribe, 0));
            generated.Add(RaceGenderData.SkeletonFromCustomize(race, tribe, 1));
        }

        generated.Should().HaveCount(18);
        generated.Should().OnlyHaveUniqueItems();
        generated.Should().BeEquivalentTo(RaceGenderData.AllRaces.Select(r => r.Id));
    }
}
