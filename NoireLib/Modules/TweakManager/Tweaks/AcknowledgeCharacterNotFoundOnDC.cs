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
[TweakKeyMigration("NoireLib_Tweak_AknowledgeCharacterNotFoundOnDC")]
public class AcknowledgeCharacterNotFoundOnDC : TweakBase
{
    /// <inheritdoc/>
    public override string InternalKey => "NoireLib_Tweak_AcknowledgeCharacterNotFoundOnDC";

    /// <inheritdoc/>
    public override string Name => "Acknowledge Character Not Found On Login";

    /// <inheritdoc/>
    public override string Description => "Automatically closes the addon that notifies when the last logged out character is not found on this data-center." +
        "\nEspecially needed when you run multiple instances of the game and log in to different accounts.";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Tags => ["Addon"];

    private readonly EventWrapper<IFramework.OnUpdateDelegate> frameworkUpdateWrapper;

    private readonly string lobbyNoticeText;

    /// <summary>
    /// Creates a new instance of the <see cref="AcknowledgeCharacterNotFoundOnDC"/> tweak.
    /// </summary>
    public AcknowledgeCharacterNotFoundOnDC()
    {
        frameworkUpdateWrapper = new(NoireService.Framework, nameof(IFramework.Update), ListenFrameworkUpdate);

        // The notice is a fixed sheet string, so it is resolved once here rather than extracted and
        // allocated again on every frame the comparison runs.
        lobbyNoticeText = ExcelSheetHelper.GetRow<Lobby>(1237) // The character you last logged out with in this play environment...
            .Text.ExtractText().RemoveNewlines();
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

    /// <inheritdoc/>
    protected override void DisposeManaged()
    {
        // The wrapper holds this tweak alive through its own disposal registration, so disabling it is
        // not enough to let a tweak that has been unregistered go away.
        frameworkUpdateWrapper.Dispose();
    }

    private unsafe void ListenFrameworkUpdate(IFramework framework)
    {
        var addon = AddonHelper.GetReadyAddon("SelectOk");

        if (!addon)
            return;

        var textNode = addon.GetNode(1, 2).TextNodePointer;

        if (textNode == null)
            return;

        var seStringAddon = SeStringHelper.Utf8StringPtrToPlainText(&textNode->NodeText).RemoveNewlines();

        if (seStringAddon == lobbyNoticeText)
        {
            ThrottleHelper.Throttle($"{InternalKey}_SendCallback", 200.Milliseconds(), () =>
            {
                NoireLogger.LogDebug(this, "Sending callback to addon SelectOk.");
                addon.SendCallback(0);
            });
        }
    }
}
