namespace NoireLib.Helpers;

/// <summary>One class or job as the game defines it.</summary>
/// <param name="RowId">The ClassJob row id.</param>
/// <param name="Name">The job's name in the client language.</param>
/// <param name="Abbreviation">The job's three-letter abbreviation in the client language.</param>
/// <param name="NameEnglish">The job's English name, which is the same on every client and so is what to match on.</param>
/// <param name="Role">What the job does in a party.</param>
/// <param name="JobIndex">
/// The row's index among the <b>battle jobs</b>, 1 upwards, or zero when it is not one. A crafter, a gatherer and a
/// battle class all sit outside this numbering, so a zero here does not mean "not a job": it means "not a battle job".
/// </param>
/// <param name="BattleClassIndex">The row's index among the battle classes, or -1 when it is not one.</param>
/// <param name="ParentId">
/// The class the row advances from, which is its own id for anything that advances from nothing. Half the battle jobs
/// have a base class (paladin advances from gladiator) and half were introduced as jobs outright, so this equalling
/// the row's own id says nothing about whether the row is a job.
/// </param>
/// <param name="ExpArrayIndex">Where the character's level for this job sits in the game's own level array, or -1 when it has none.</param>
/// <param name="HandOrLandIndex">The row's index among the crafters or gatherers, or -1 when it is neither.</param>
/// <param name="StartingLevel">The level a character has on first taking the class.</param>
/// <param name="UnlockQuestId">The quest that unlocks the job, or zero for a starting class.</param>
/// <param name="SoulCrystalItemId">The soul crystal that turns the class into the job, or zero.</param>
/// <param name="DisciplineCategoryId">
/// The <c>ClassJobCategory</c> the sheet points this job at, which is the discipline rather than the job: every
/// disciple of the hand shares one, every disciple of the land another. It is the game's own answer to "is this a
/// crafter", so read its name with <see cref="ClassJobHelper.CategoryName"/> or compare two jobs' values, rather
/// than comparing it to a number written down here.
/// </param>
/// <param name="IsLimited">Whether the job is a limited job.</param>
public sealed record ClassJobInfo(
    uint RowId,
    string Name,
    string Abbreviation,
    string NameEnglish,
    ClassJobRole Role,
    byte JobIndex,
    sbyte BattleClassIndex,
    uint ParentId,
    sbyte ExpArrayIndex,
    sbyte HandOrLandIndex,
    byte StartingLevel,
    uint UnlockQuestId,
    uint SoulCrystalItemId,
    uint DisciplineCategoryId,
    bool IsLimited)
{
    /// <summary>
    /// Whether the row has a place in the battle job numbering. Crafters and gatherers answer false; see
    /// <see cref="IsHandOrLand"/>.
    /// </summary>
    public bool IsBattleJob => JobIndex != 0;

    /// <summary>Whether the row has a place in the class numbering and none in the job numbering.</summary>
    public bool IsBattleClass => BattleClassIndex >= 0 && JobIndex == 0;

    /// <summary>Whether the job deals damage, melee or ranged.</summary>
    public bool IsDps => Role is ClassJobRole.MeleeDps or ClassJobRole.RangedDps;

    /// <summary>
    /// Whether the row has an index among the crafters and gatherers. Which of the two comes from
    /// <see cref="DisciplineCategoryId"/>.
    /// </summary>
    public bool IsHandOrLand => HandOrLandIndex >= 0;
}
