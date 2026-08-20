using System.Collections.Generic;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// One race/gender combination and the human skeleton id the game animates it with. Tribes of the same race share a
/// skeleton, except Hyur, where Midlander and Highlander have separate ones.
/// </summary>
public class RaceGenderData
{
    /// <summary> Display name, such as "Miqo'te F". </summary>
    public string Name { get; }

    /// <summary> The human skeleton id, such as "c0801". </summary>
    public string Id { get; }

    /// <summary> Pairs a display name with a skeleton id. </summary>
    /// <param name="name"> Display name, such as "Miqo'te F". </param>
    /// <param name="id"> The human skeleton id, such as "c0801". </param>
    public RaceGenderData(string name, string id)
    {
        Name = name;
        Id = id;
    }

    /// <summary> Every playable race/gender combination and its skeleton id. </summary>
    public static readonly List<RaceGenderData> AllRaces =
    [
        new("Midlander M", "c0101"),
        new("Midlander F", "c0201"),
        new("Highlander M", "c0301"),
        new("Highlander F", "c0401"),
        new("Elezen M", "c0501"),
        new("Elezen F", "c0601"),
        new("Miqo'te M", "c0701"),
        new("Miqo'te F", "c0801"),
        new("Roegadyn M", "c0901"),
        new("Roegadyn F", "c1001"),
        new("Lalafell M", "c1101"),
        new("Lalafell F", "c1201"),
        new("Au Ra M", "c1301"),
        new("Au Ra F", "c1401"),
        new("Hrothgar M", "c1501"),
        new("Hrothgar F", "c1601"),
        new("Viera M", "c1701"),
        new("Viera F", "c1801")
    ];

    /// <summary>
    /// Resolves the human skeleton id for a character's customize bytes.
    /// </summary>
    /// <param name="race">
    /// Customize race byte: Hyur 1, Elezen 2, Lalafell 3, Miqo'te 4, Roegadyn 5, Au Ra 6, Hrothgar 7, Viera 8.
    /// </param>
    /// <param name="tribe">
    /// Customize tribe byte, consulted only when <paramref name="race"/> is Hyur: Midlander 1, Highlander 2.
    /// </param>
    /// <param name="gender">Customize gender byte: 0 male, 1 female.</param>
    /// <returns> The skeleton id, such as "c0801". An unrecognized race falls back to Midlander's pair. </returns>
    public static string SkeletonFromCustomize(byte race, byte tribe, byte gender)
    {
        var maleCode = race switch
        {
            1 => tribe == 2 ? 3 : 1, // Hyur: Highlander is tribe 2; any other tribe byte reads as Midlander.
            2 => 5,                  // Elezen
            3 => 11,                 // Lalafell
            4 => 7,                  // Miqo'te
            5 => 9,                  // Roegadyn
            6 => 13,                 // Au Ra
            7 => 15,                 // Hrothgar
            8 => 17,                 // Viera
            _ => 1,                  // Unrecognized race: fall back to Midlander rather than guess.
        };

        var code = gender == 1 ? maleCode + 1 : maleCode;
        return $"c{code:D2}01";
    }
}
