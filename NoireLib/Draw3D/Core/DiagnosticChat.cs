using NoireLib.Helpers;
using System;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Terminal chat output for the render-thread diagnostics. Draw3D's render body runs on the render thread, and its
/// default path runs mid-frame from inside one of the game's own D3D calls; printing to chat from there re-enters the
/// game's chat system underneath itself, which is not something the game tolerates. Every report routes through here
/// so the print lands on the framework thread instead, one frame later at worst.
/// <br/>
/// Diagnostics always log the full report through <see cref="NoireLogger"/> as well, so a print that cannot be
/// delivered (NoireLib torn down between arming and reporting) costs the summary line, never the findings.
/// </summary>
internal static class DiagnosticChat
{
    /// <summary>
    /// Queues a diagnostic summary line for the in-game chat, marshalled to the framework thread. Safe to call from the
    /// render thread, including from inside a game D3D detour. Fire-and-forget: it never throws and never blocks.
    /// </summary>
    /// <param name="message">The summary line to print.</param>
    public static void Print(string message)
    {
        try
        {
            _ = AsyncHelper.RunOnFrameworkThreadAsync(() =>
            {
                // Swallowed inside the marshalled action so the returned task never faults unobserved.
                try
                {
                    NoireService.ChatGui.Print(message);
                }
                catch (Exception ex)
                {
                    NoireLogger.LogError(ex, "Draw3D: a diagnostic chat report could not be printed; it is in the log.", "Draw3D");
                }
            });
        }
        catch (Exception ex)
        {
            // Reaching the framework thread at all can fail when NoireLib is being torn down.
            NoireLogger.LogError(ex, "Draw3D: a diagnostic chat report could not be scheduled; it is in the log.", "Draw3D");
        }
    }
}
