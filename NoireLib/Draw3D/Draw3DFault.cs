using NoireLib.Draw3D.Enums;
using System;

namespace NoireLib.Draw3D;

/// <summary>
/// A self-disable notification (see <see cref="NoireDraw3D.OnFault"/>): something failed, the narrowest
/// responsible feature was disabled, and everything else keeps rendering.
/// </summary>
/// <param name="Kind">The ladder rung the fault landed on.</param>
/// <param name="Message">Human-readable description of what was disabled and why.</param>
/// <param name="Exception">The triggering exception, when there was one.</param>
public readonly record struct Draw3DFault(Draw3DFaultKind Kind, string Message, Exception? Exception);
