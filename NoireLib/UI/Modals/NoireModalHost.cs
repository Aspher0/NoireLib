using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>Presents the dialogs raised through <see cref="NoireModal"/>, one at a time.</summary>
public sealed class NoireModalHost : NoireDrawable
{
    private const string PopupId = "###NoireModalPopup";

    private static readonly object InstanceLock = new();

    private static readonly PromptOptions PromptDefaults = new();

    // Its tone is written right before it is drawn. One dialog is drawn at a time on one thread.
    private static readonly ButtonStyle ConfirmStyle = new();

    private static NoireModalHost? instance;

    private NoireModalHost()
        : base("ModalHost", "Modal")
    {
        // An awaited dialog nobody drew would never return.
        AutoDraw = true;
        Register();
    }

    internal static NoireModalHost Instance
    {
        get
        {
            if (instance is { IsDisposed: false })
                return instance;

            lock (InstanceLock)
            {
                if (instance is { IsDisposed: false })
                    return instance;

                instance = new NoireModalHost();
                return instance;
            }
        }
    }

    /// <inheritdoc/>
    protected override void DrawCore()
    {
        var request = NoireModal.Current;
        if (request == null)
            return;

        if (request.Options.CustomDraw && !request.FellBack)
        {
            var frame = NoireUI.FrameCount;

            if (request.FirstSeenFrame == ModalRequest.NoFrame)
                request.FirstSeenFrame = frame;

            if (DefersToCaller(request.FirstSeenFrame, request.PresentedFrame, frame))
                return;

            request.FellBack = true;
        }

        if (!request.PopupOpened)
        {
            request.PopupOpened = true;

            if (!request.Opened)
            {
                request.Opened = true;
                request.OpenedAt = NoireUI.Time;
            }

            ImGui.OpenPopup(PopupId);
        }

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        var dialogWidth = request.Options.ScaledWidth;
        ImGui.SetNextWindowSizeConstraints(new Vector2(dialogWidth, 0f), new Vector2(dialogWidth, float.MaxValue));

        var title = string.IsNullOrEmpty(request.Title) ? " " : request.Title;

        var open = true;
        var flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize;
        var shown = request.Options.CloseButton
            ? ImGui.BeginPopupModal(title + PopupId, ref open, flags)
            : ImGui.BeginPopupModal(title + PopupId, flags);

        if (shown && !open)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            NoireModal.Complete(request, NoireModal.CancelledResult);
            return;
        }

        if (shown)
        {
            try
            {
                UiScope.Run(nameof(NoireModalHost), request, static r => DrawContents(r));
            }
            finally
            {
                ImGui.EndPopup();
            }

            return;
        }

        // Closed with Escape or by clicking away. That is a decline.
        NoireModal.Complete(request, NoireModal.CancelledResult);
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        NoireModal.CancelAll();

        if (ReferenceEquals(instance, this))
            instance = null;
    }

    private static void DrawContents(ModalRequest request)
    {
        var theme = NoireTheme.Current;
        var width = request.Options.ScaledWidth - theme.ResolveFramePadding().X * 2f;

        // A light palette must not leave near-white text on a near-white dialog.
        using var textColor = UiPush.Color(ImGuiCol.Text, theme.Resolve(ThemeColor.Text));

        NoireLayout.WrapText(width, request, static r => r.Message.Draw());

        if (request.Kind == ModalKind.Prompt)
            DrawPromptField(request, width);

        if (!string.IsNullOrWhiteSpace(request.Options.RememberKey))
        {
            ImGui.Spacing();

            var remember = request.Remember;
            if (ImGui.Checkbox(NoireUI.Localize("NoireUI.Modal.Remember", request.Options.RememberLabel ?? "Don't ask again"), ref remember))
                request.Remember = remember;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (request.Kind == ModalKind.Choice)
            DrawChoiceButtons(request);
        else
            DrawConfirmButtons(request);
    }

    private static void DrawPromptField(ModalRequest request, float width)
    {
        var options = request.Options as PromptOptions ?? PromptDefaults;

        ImGui.Spacing();
        ImGui.SetNextItemWidth(width);

        if (!request.Focused)
        {
            request.Focused = true;
            ImGui.SetKeyboardFocusHere();
        }

        var value = request.Value;
        if (ImGui.InputTextWithHint("##NoireModalPrompt", options.Placeholder ?? string.Empty, ref value, options.MaxLength))
            request.Value = value;
    }

    private static void DrawConfirmButtons(ModalRequest request)
    {
        var options = request.Options;
        var confirmLabel = ConfirmLabelFor(request);
        var cancelLabel = CancelLabelFor(request);
        var confirmEnabled = PromptAllowsConfirm(request);

        var countdown = CountdownSeconds(request.OpenedAt, NoireUI.Time, options.EnableAfterSeconds);

        if (countdown > 0)
        {
            confirmEnabled = false;
            confirmLabel = WithCountdown(confirmLabel, countdown);
        }

        var tone = options.Danger ? ButtonTone.Danger : ButtonTone.Accent;
        var hasCancel = cancelLabel.Length > 0;

        var confirmWidth = MeasureButton(WidestLabel(confirmLabel, options.EnableAfterSeconds));
        var cancelWidth = hasCancel ? MeasureButton(cancelLabel) : 0f;
        var spacing = NoireTheme.Current.ResolveItemSpacing().X;

        AlignRight(confirmWidth + (hasCancel ? cancelWidth + spacing : 0f));

        if (hasCancel)
        {
            if (NoireButtons.Button(UiIds.Labelled(cancelLabel, "##NoireModalCancel", string.Empty), ButtonTone.Ghost, new Vector2(cancelWidth, 0f)))
                NoireModal.Complete(request, NoireModal.CancelledResult);

            ImGui.SameLine(0f, spacing);
        }

        ImGui.BeginDisabled(!confirmEnabled);

        ConfirmStyle.Tone = tone;

        var confirmed = options.HoldSeconds > 0f
            ? NoireButtons.HoldToConfirm(UiIds.Labelled(confirmLabel, "##NoireModalConfirm", string.Empty), options.HoldSeconds, ConfirmStyle, new Vector2(confirmWidth, 0f))
            : NoireButtons.Button(UiIds.Labelled(confirmLabel, "##NoireModalConfirm", string.Empty), tone, new Vector2(confirmWidth, 0f));

        ImGui.EndDisabled();

        if (confirmed && confirmEnabled)
            NoireModal.Complete(request, 1);
    }

    private static void DrawChoiceButtons(ModalRequest request)
    {
        var choices = request.Choices!;
        var spacing = NoireTheme.Current.ResolveItemSpacing().X;
        var cancelLabel = CancelLabelFor(request);
        var hasCancel = cancelLabel.Length > 0;

        var total = hasCancel ? MeasureButton(cancelLabel) + spacing : 0f;
        for (var index = 0; index < choices.Count; index++)
            total += MeasureButton(choices[index]) + (index > 0 ? spacing : 0f);

        AlignRight(total);

        if (hasCancel)
        {
            if (NoireButtons.Button(UiIds.Labelled(cancelLabel, "##NoireModalCancel", string.Empty), ButtonTone.Ghost, new Vector2(MeasureButton(cancelLabel), 0f)))
                NoireModal.Complete(request, NoireModal.CancelledResult);

            ImGui.SameLine(0f, spacing);
        }

        for (var index = 0; index < choices.Count; index++)
        {
            if (index > 0)
                ImGui.SameLine(0f, spacing);

            var tone = index == 0
                ? request.Options.Danger ? ButtonTone.Danger : ButtonTone.Accent
                : ButtonTone.Ghost;

            if (NoireButtons.Button(UiIds.Labelled(choices[index], "##NoireModalChoice", string.Empty, string.Empty, index), tone, new Vector2(MeasureButton(choices[index]), 0f)))
                NoireModal.Complete(request, index);
        }
    }

    // Defers while presented within the last two frames, and for its first two frames.
    internal static bool DefersToCaller(int firstSeenFrame, int presentedFrame, int frame)
        => frame - presentedFrame <= 2 || frame - firstSeenFrame <= 2;

    internal static string ConfirmLabelFor(ModalRequest request)
        => NoireUI.Localize("NoireUI.Modal.Confirm", request.Options.ConfirmLabel ?? DefaultConfirmLabel(request));

    internal static string CancelLabelFor(ModalRequest request)
        => request.Options.CancelLabel ?? NoireUI.Localize("NoireUI.Modal.Cancel", "Cancel");

    internal static bool PromptAllowsConfirm(ModalRequest request)
        => request.Kind != ModalKind.Prompt
            || (request.Options as PromptOptions)?.AllowEmpty == true
            || !string.IsNullOrWhiteSpace(request.Value);

    internal static int CountdownSeconds(float openedAt, float now, float enableAfterSeconds)
    {
        if (enableAfterSeconds <= 0f)
            return 0;

        var remaining = enableAfterSeconds - (now - openedAt);

        if (remaining > enableAfterSeconds)
            remaining = enableAfterSeconds;

        return remaining <= 0f ? 0 : (int)MathF.Ceiling(remaining);
    }

    internal static string WithCountdown(string label, int seconds) => $"{label} ({seconds})";

    internal static string WidestLabel(string label, float enableAfterSeconds)
        => enableAfterSeconds <= 0f ? label : WithCountdown(label, (int)MathF.Ceiling(enableAfterSeconds));

    private static string DefaultConfirmLabel(ModalRequest request) => request.Kind switch
    {
        ModalKind.Prompt => "OK",
        _ => "Confirm",
    };

    private static float MeasureButton(string label)
        => MathF.Max(NoireUI.Scaled(80f), NoireText.CalcSizeInCurrentFont(label).X + NoireTheme.Current.ResolveFramePadding().X * 4f);

    private static void AlignRight(float width)
    {
        var offset = ImGui.GetContentRegionAvail().X - width;

        if (offset > 0f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
    }
}
