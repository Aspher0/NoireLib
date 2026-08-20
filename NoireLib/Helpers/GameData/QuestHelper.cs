using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace NoireLib.Helpers;

/// <summary>
/// Reads the logged-in character's quest state. Everything here describes a loaded character, so it is gated on
/// <see cref="CharacterHelper.IsStateReady"/> and answers "nothing" rather than reading through a half-built one.
/// </summary>
public static unsafe class QuestHelper
{
    /// <summary>Whether a quest is complete.</summary>
    /// <param name="questId">The Quest row id.</param>
    /// <returns>True when the quest is complete.</returns>
    public static bool IsComplete(uint questId)
        => questId != 0 && CharacterHelper.IsStateReady && QuestManager.IsQuestComplete(questId);

    /// <summary>Whether a quest is in the character's journal and not yet complete.</summary>
    /// <param name="questId">The Quest row id.</param>
    /// <returns>True when the quest is accepted.</returns>
    public static bool IsAccepted(uint questId)
    {
        if (questId == 0 || !CharacterHelper.IsStateReady)
            return false;

        var manager = QuestManager.Instance();
        return manager != null && manager->IsQuestAccepted(questId);
    }

    /// <summary>
    /// How far an accepted quest has got, being the sequence step the game advances as its steps are done. Zero for a
    /// quest that is not accepted.
    /// </summary>
    /// <param name="questId">The Quest row id.</param>
    /// <returns>The sequence step.</returns>
    public static byte Sequence(uint questId)
        => questId == 0 || !CharacterHelper.IsStateReady ? (byte)0 : QuestManager.GetQuestSequence(questId);

    /// <summary>
    /// Reads a known set of quests in one pass: a completed set, an accepted set, and the step each accepted one has reached.
    /// </summary>
    /// <param name="questIds">The Quest row ids to read.</param>
    /// <returns>The progress, or <see cref="QuestProgress.Empty"/> when there is nothing to read or no character.</returns>
    public static QuestProgress ReadProgress(IReadOnlyCollection<uint> questIds)
    {
        if (questIds.Count == 0 || !CharacterHelper.IsStateReady)
            return QuestProgress.Empty;

        return SafeExecutor.ExecuteSafely(() =>
        {
            var completed = new HashSet<uint>();
            var accepted = new HashSet<uint>();
            var sequence = new Dictionary<uint, byte>();
            var manager = QuestManager.Instance();

            foreach (var questId in questIds)
            {
                if (questId == 0)
                    continue;

                if (QuestManager.IsQuestComplete(questId))
                {
                    completed.Add(questId);
                }
                else if (manager != null && manager->IsQuestAccepted(questId))
                {
                    accepted.Add(questId);
                    sequence[questId] = QuestManager.GetQuestSequence(questId);
                }
            }

            return new QuestProgress(completed, accepted, sequence);
        }, QuestProgress.Empty) ?? QuestProgress.Empty;
    }
}
