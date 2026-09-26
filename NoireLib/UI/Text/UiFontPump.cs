using Dalamud.Plugin.Services;
using NoireLib.Localizer;
using System;
using System.Threading;

namespace NoireLib.UI;

internal static class UiFontPump
{
    private const string DisposeCallbackKey = "NoireLib.UI.UiFontPump";

    private static int hooked;

    internal static void Ensure()
    {
        if (!NoireService.IsInitialized() || Interlocked.Exchange(ref hooked, 1) == 1)
            return;

        NoireService.Framework.Update += OnUpdate;

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeCallbackKey))
            NoireLibMain.RegisterOnDispose(DisposeCallbackKey, Unhook);
    }

    private static void Unhook()
    {
        if (Interlocked.Exchange(ref hooked, 0) == 0)
            return;

        if (NoireService.IsInitialized())
            NoireService.Framework.Update -= OnUpdate;
    }

    private static void OnUpdate(IFramework framework)
    {
        if (Volatile.Read(ref hooked) == 0 || !NoireService.IsInitialized())
            return;

        try
        {
            NoireFontLadder.TickAll();
            NoireScriptFonts.UpdateStage();
            NoireLanguagePicker.Commit();
            UiFaceAtlas.Maintain();
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireFont), "The font upkeep failed.", ex);
        }
    }
}
