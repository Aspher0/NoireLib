using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using NoireLib.Events;
using NoireLib.Helpers;
using NoireLib.Helpers.ObjectExtensions;
using System.Collections.Generic;

namespace NoireLib.TweakManager;

/// <summary>
/// A tweak to automatically close the addon that tells you that the last logged in character has not been found on this data-center.
/// </summary>
/// <remarks>
/// Kind of obsolete or, well, very inefficient.<br/>
/// Actually all that is needed to do is to get the Lobby instance and there is some property that can be set in there.<br/>
/// This is more so a test than anything else.
/// </remarks>
public class AknowledgeCharacterNotFoundOnDC : TweakBase
{
    /// <inheritdoc/>
    public override string InternalKey => "NoireLib_Tweak_AknowledgeCharacterNotFoundOnDC";

    /// <inheritdoc/>
    public override string Name => "Aknowledge Character Not Found On Login";

    /// <inheritdoc/>
    public override string Description => "Automatically closes the addon that notifies when the last logged out character is not found on this data-center." +
        "\nEspecially needed when you run multiple instances of the game and log in to different accounts.";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Tags => ["Addon"];

    private EventWrapper<IFramework.OnUpdateDelegate> frameworkUpdateWrapper;

    private Lobby lobbyAddonText;

    public AknowledgeCharacterNotFoundOnDC()
    {
        frameworkUpdateWrapper = new(NoireService.Framework, nameof(IFramework.Update), ListenFrameworkUpdate);
        lobbyAddonText = ExcelSheetHelper.GetRow<Lobby>(1237); // The character you last logged out with in this play environment...
    }

    /// <inheritdoc/>
    protected override void OnEnable()
    {
        frameworkUpdateWrapper.Enable();
    }

    /// <inheritdoc/>
    protected override void OnDisable()
    {
        frameworkUpdateWrapper.Disable();
    }

    private unsafe void ListenFrameworkUpdate(IFramework framework)
    {
        if (!AddonHelper.TryGetReadyAddonWrapper("SelectOk", out var addon))
            return;

        if (!addon.TryGetTextNode(out var textNode, 1, 2))
            return;

        var seStringAddon = SeStringHelper.Utf8StringPtrToPlainText(&textNode->NodeText).RemoveNewlines();
        var stringLobby = lobbyAddonText.Text.ExtractText().RemoveNewlines();

        if (seStringAddon == stringLobby)
        {
            ThrottleHelper.Throttle($"{InternalKey}_SendCallback", 200.Milliseconds(), () =>
            {
                NoireLogger.LogDebug(this, "Sending callback to addon SelectOk.");
                addon.SendCallback(true, 0);
            });
        }
    }
}
