using FluentAssertions;
using Lumina.Excel.Sheets;
using NoireLib.Helpers;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins how a warp's <c>WarpLogic</c> arguments are read. Three warps in the game are gated there and nowhere
/// else, so a reader that only looks at <c>WarpCondition</c> calls them free passage and a router plans straight
/// through a locked door.
/// </summary>
public sealed class WarpHelperTests(ITestOutputHelper output)
{
    private static WarpLogicInfo Logic(params (string Function, uint Argument)[] parameters)
        => new(
            11,
            "WarpInnCrystarium",
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            parameters.Select(p => new WarpLogicParam(p.Function, p.Argument)).ToList());

    // ---- classifying an argument, which is pure logic and runs with no game ----

    [Fact]
    public void NamesContentGate_AQuestArgument_IsAGate()
    {
        WarpHelper.NamesContentGate(Logic(("QST_LUCKMA401", 68836))).Should().BeTrue();
        WarpHelper.NamesContentGate(Logic(("QUEST_WEDDING", 67114))).Should().BeTrue();
    }

    [Fact]
    public void NamesContentGate_AnItemArgument_IsAGate()
        => WarpHelper.NamesContentGate(Logic(("ITEM_CHILD_TICKET", 8699))).Should().BeTrue();

    /// <summary>
    /// A sequence number is the threshold a quest gate is measured against, not a gate of its own, and it shares
    /// the quest prefix. Reading it as a gate would make every sequence constant look like content.
    /// </summary>
    [Fact]
    public void NamesContentGate_AQuestSequence_IsNotAGateOnItsOwn()
    {
        WarpHelper.NamesContentGate(Logic(("QST_SEQ_11", 11))).Should().BeFalse();
        WarpHelper.NamesContentGate(Logic(("QST_SEQ_FINISH", 255))).Should().BeFalse();
    }

    /// <summary>
    /// The rental-chocobo desks carry a tutorial popup id and are gated by the gil and class level on their
    /// condition. Calling the popup a gate would report them as needing content nobody needs.
    /// </summary>
    [Fact]
    public void NamesContentGate_ATutorialId_IsNotAGate()
        => WarpHelper.NamesContentGate(Logic(("HOWTO_ABOUT_RENTAL_CHOCOBO", 81))).Should().BeFalse();

    [Fact]
    public void NamesContentGate_NoArguments_IsNotAGate()
    {
        WarpHelper.NamesContentGate(Logic()).Should().BeFalse();
        WarpHelper.NamesContentGate(default(WarpDefinition)).Should().BeFalse();
    }

    /// <summary>A quest among sequence numbers still gates, which is the real shape of the two inn rows.</summary>
    [Fact]
    public void NamesContentGate_AQuestAmongSequences_IsAGate()
    {
        WarpHelper.NamesContentGate(Logic(
                ("QST_SEQ_1", 1), ("QST_SEQ_FINISH", 255), ("QST_KINGMD108", 70455)))
            .Should().BeTrue();
    }

    [Fact]
    public void NamesContentGate_ReadsADefinitionsOwnArguments()
    {
        var definition = new WarpDefinition(
            0, WarpTriggerKind.EventNpc, 131316, 843, 0, 0, 0, [], 0, 11,
            [new WarpLogicParam("QST_LUCKMA401", 68836)]);

        WarpHelper.NamesContentGate(definition).Should().BeTrue();
    }

    // ---- the data this rests on, pinned against the archives; skipped with no installation ----

    /// <summary>
    /// The whole reason the arguments are read: these warps name no quest on their condition and are gated
    /// entirely by their logic row. If a patch moves the gate into the condition this test says so.
    /// </summary>
    [Fact]
    public void TheArchives_HoldWarpsGatedOnlyByTheirLogicArguments()
    {
        var game = GameDataFixture.TryOpen();
        if (game == null)
            return;

        var gating = new Dictionary<uint, List<string>>();
        foreach (var row in game.GetExcelSheet<WarpLogic>())
        {
            var named = row.WarpParams
                .Select(p => p.Function.ExtractText())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            if (named.Count > 0)
                gating[row.RowId] = named;
        }

        gating.Should().HaveCount(4, "four WarpLogic rows carry arguments: 2, 8, 11 and 16");

        var onlyGate = new List<uint>();
        foreach (var warp in game.GetExcelSheet<Warp>())
        {
            if (!gating.TryGetValue(warp.WarpLogic.RowId, out var named))
                continue;

            var quests = warp.WarpCondition.ValueNullable is { } condition
                ? new[]
                {
                    condition.RequiredQuest1.RowId, condition.RequiredQuest2.RowId,
                    condition.RequiredQuest3.RowId, condition.RequiredQuest4.RowId,
                }.Count(q => q != 0)
                : 0;

            var content = named.Any(f => !f.StartsWith("QST_SEQ_", System.StringComparison.Ordinal)
                                         && (f.StartsWith("QST_", System.StringComparison.Ordinal)
                                             || f.StartsWith("QUEST_", System.StringComparison.Ordinal)
                                             || f.StartsWith("ITEM_", System.StringComparison.Ordinal)));

            if (content && quests == 0)
                onlyGate.Add(warp.RowId);

            output.WriteLine($"  warp {warp.RowId} logic {warp.WarpLogic.RowId} "
                             + $"condition quests {quests} content gate {content}");
        }

        onlyGate.Should().BeEquivalentTo(new uint[] { 131176, 131316, 131576 },
            "the wedding desk and the Crystarium and Tuliyollal inns are gated only by their logic arguments");
    }

    /// <summary>
    /// The third warp wiring: an EObj joined to a Warp row only through <c>WKSWarp</c>, which neither the direct
    /// handler nor the array handler reaches. Read positionally, so a schema update that names those columns has
    /// to be noticed.
    /// </summary>
    [Fact]
    public void TheArchives_WireTheCosmicElevatorsThroughTheirOwnTable()
    {
        var game = GameDataFixture.TryOpen();
        if (game == null)
            return;

        var warps = game.GetExcelSheet<Warp>();
        var objects = game.GetExcelSheet<EObjName>();
        var paired = new List<(uint Object, uint Warp, string Name)>();

        foreach (var row in game.GetExcelSheet<WKSWarp>())
        {
            if (row.Unknown0 == 0 || row.Unknown1 == 0)
                continue;

            warps.HasRow(row.Unknown1).Should().BeTrue(
                $"WKSWarp row {row.RowId}'s second column should be a Warp row");

            paired.Add((row.Unknown0, row.Unknown1,
                objects.GetRowOrDefault(row.Unknown0)?.Singular.ExtractText() ?? string.Empty));
        }

        foreach (var (objectId, warpId, name) in paired)
            output.WriteLine($"  EObj {objectId} '{name}' -> warp {warpId}");

        paired.Should().HaveCount(4, "four elevators are wired this way");
        paired.Select(p => p.Warp).Should().BeEquivalentTo(new uint[] { 131622, 131623, 131624, 131625 });
        paired.Should().OnlyContain(p => p.Name == "elevator",
            "every EObj this table names is an elevator, which is what makes the pairing legible");
    }

    /// <summary>
    /// The argument names are the script's own constants, which is why they can be read as a gate at all. Each
    /// one should appear verbatim in the constant table of the script its row names.
    /// </summary>
    [Fact]
    public void TheArchives_NameEachArgumentInsideItsOwnScript()
    {
        var game = GameDataFixture.TryOpen();
        if (game == null)
            return;

        var matched = 0;
        var missing = new List<string>();

        foreach (var row in game.GetExcelSheet<WarpLogic>())
        {
            var script = row.WarpName.ExtractText();
            if (string.IsNullOrEmpty(script))
                continue;

            byte[]? data;
            try
            {
                data = game.GetFile($"game_script/warp/{script}.luab")?.Data;
            }
            catch
            {
                continue;
            }

            if (data == null)
                continue;

            var text = System.Text.Encoding.ASCII.GetString(data);
            foreach (var param in row.WarpParams)
            {
                var function = param.Function.ExtractText();
                if (string.IsNullOrEmpty(function))
                    continue;

                if (text.Contains(function, System.StringComparison.Ordinal))
                    matched++;
                else
                    missing.Add($"{script}/{function}");
            }
        }

        output.WriteLine($"{matched} arguments found in their own script; missing: {string.Join(", ", missing)}");

        matched.Should().Be(15, "fifteen of the sixteen arguments are constants of the script their row names");
        missing.Should().BeEquivalentTo(new[] { "WarpWeddingPlaceDesk/ITEM_CHILD_TICKET" },
            "the child ticket is the one argument its script does not name");
    }
}
