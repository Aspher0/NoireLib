using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// A <c>WarpLogic</c> row: the script that runs a warp, the confirmation it shows, and the named arguments it is
/// given.
/// The row names a compiled Lua script at <c>game_script/warp/&lt;ScriptName&gt;.luab</c> and its parameters are that
/// script's own gating constants, which for three warps are the only gate <c>WarpCondition</c> does not carry.
/// </summary>
/// <param name="RowId">The WarpLogic row id.</param>
/// <param name="ScriptName">
/// The row's <c>WarpName</c>, which is the stem of its script file, empty for the two generic rows that name no
/// script of their own.
/// </param>
/// <param name="Question">The confirmation prompt, localised, or empty when the row shows none.</param>
/// <param name="ResponseYes">The affirmative answer's text, localised.</param>
/// <param name="ResponseNo">The negative answer's text, localised.</param>
/// <param name="CanSkipCutscene">Whether the row lets its cutscene be skipped.</param>
/// <param name="Params">The named arguments, in the row's own slot order, with the empty slots dropped.</param>
public readonly record struct WarpLogicInfo(
    uint RowId,
    string ScriptName,
    string Question,
    string ResponseYes,
    string ResponseNo,
    bool CanSkipCutscene,
    IReadOnlyList<WarpLogicParam> Params);
