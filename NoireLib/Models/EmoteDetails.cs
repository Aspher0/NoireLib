using NoireLib.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Models;

/// <summary>
/// Display details about an emote, resolved by
/// <see cref="Helpers.EmoteHelper.TryGetEmoteDetails(uint, out EmoteDetails?)"/>.
/// </summary>
public class EmoteDetails
{
    /// <summary>
    /// The row ID of the emote.
    /// </summary>
    public uint EmoteId { get; init; }

    /// <summary>
    /// The display name of the emote, in the current client language.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The text command of the emote in the current client language, empty when it has none.
    /// </summary>
    public string TextCommand { get; init; } = string.Empty;

    /// <summary>
    /// The icon ID of the emote.
    /// </summary>
    public uint Icon { get; init; }

    /// <summary>
    /// The sort order of the emote in the emote list.
    /// </summary>
    public ushort Order { get; init; }

    /// <summary>
    /// The unlock link of the emote, zero when it is always available.
    /// </summary>
    public uint UnlockLink { get; init; }

    /// <summary>
    /// The category of the emote.
    /// </summary>
    public EmoteCategory Category { get; init; }

    /// <summary>
    /// The character states the emote can be performed in, as reported by the game client.
    /// </summary>
    public EmoteCondition Conditions { get; init; }

    /// <summary>
    /// The individual states set in <see cref="Conditions"/>, as display names.
    /// </summary>
    public IReadOnlyList<string> ConditionNames =>
        Enum.GetValues<EmoteCondition>()
            .Where(c => c != EmoteCondition.None && Conditions.HasFlag(c))
            .Select(GetConditionDisplayName)
            .ToList();

    /// <summary>
    /// Joins the states in <see cref="Conditions"/> into one display string.
    /// </summary>
    /// <returns>The comma-separated state names, or "None".</returns>
    public string ConditionsToString()
        => Conditions == EmoteCondition.None ? "None" : string.Join(", ", ConditionNames);

    /// <summary>
    /// The display name of a single emote condition flag.
    /// </summary>
    /// <param name="condition">The condition flag to name, expected to be a single flag.</param>
    /// <returns>The display name of the condition.</returns>
    public static string GetConditionDisplayName(EmoteCondition condition) => condition switch
    {
        EmoteCondition.None => "None",
        EmoteCondition.Standing => "Standing",
        EmoteCondition.Swimming => "Swimming",
        EmoteCondition.Diving => "Diving",
        EmoteCondition.SittingOnGround => "Sitting on ground",
        EmoteCondition.SittingInChair => "Sitting in chair",
        EmoteCondition.Mounted => "Mounted",
        EmoteCondition.HoldingUmbrella => "Holding umbrella",
        EmoteCondition.HoldingTorch => "Holding torch",
        EmoteCondition.WearingFashionAccessory => "Wearing fashion accessory",
        EmoteCondition.Fishing => "Fishing",
        _ => condition.ToString(),
    };

    /// <summary>
    /// A one-line summary of the emote, for logging.
    /// </summary>
    /// <returns>The name, text command, id, category and usable states.</returns>
    public override string ToString()
        => $"{Name}{(TextCommand.Length > 0 ? $" ({TextCommand})" : "")} [Id: {EmoteId}, Category: {Category}] - Usable while: {ConditionsToString()}";
}
