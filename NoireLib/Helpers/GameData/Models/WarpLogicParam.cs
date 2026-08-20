namespace NoireLib.Helpers;

/// <summary>
/// One named argument a <c>WarpLogic</c> row hands to the warp's script.
/// The argument column is an untyped row reference, so what it identifies has to be read from the name; the prefixes
/// the game uses are listed on <see cref="WarpHelper.NamesContentGate(WarpLogicInfo)"/>.
/// </summary>
/// <param name="Function">The constant's name, as in <c>QST_LUCKMA401</c> or <c>QST_SEQ_FINISH</c>.</param>
/// <param name="Argument">The value, which is a row id for a quest or item and a plain number for a sequence.</param>
public readonly record struct WarpLogicParam(string Function, uint Argument);
