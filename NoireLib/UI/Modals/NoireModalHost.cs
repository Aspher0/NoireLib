using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Presents the dialogs raised through <see cref="NoireModal"/>, one at a time.<br/>
/// There is one of these, reached through <see cref="NoireModal.Host"/>. It exists as a drawable so the dialog can be
/// placed in your own draw order if you want it there, and so it is disposed with the rest of the library.
/// </summary>
public sealed class NoireModalHost : NoireDrawable
{
    private const string PopupId = "###NoireModalPopup";

    private static readonly object InstanceLock = new();
    private static NoireModalHost? instance;

    private NoireModalHost()
        : base("ModalHost", "Modal")
    {
        // A dialog is awaited, so it has to appear whatever the master default says: an await behind a dialog nobody
        // drew would never return, and the symptom is a hang with nothing on screen to explain it.
        AutoDraw = true;
        Register();
    }

    /// <summary>
    /// The one host, created on first use.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
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

        if (!request.Opened)
        {
            request.Opened = true;
            ImGui.OpenPopup(PopupId);
        }

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSizeConstraints(new Vector2(request.Options.Width, 0f), new Vector2(request.Options.Width, float.MaxValue));

        var title = string.IsNullOrEmpty(request.Title) ? " " : request.Title;

        if (ImGui.BeginPopupModal(title + PopupId, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize))
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

        // The popup is gone but nothing resolved it, so it was closed with Escape or by clicking away. That is a
        // decline, and it has to complete the task rather than leave the await hanging.
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
        var width = request.Options.Width - theme.ResolveFramePadding().X * 2f;

        // Resolved through the theme rather than inherited, so a light palette does not leave near-white text on a
        // near-white dialog.
        using var textColor = ImRaii.PushColor(ImGuiCol.Text, theme.Resolve(ThemeColor.Text));

        NoireLayout.WrapText(width, request, static r => r.Message.Draw());

        if (request.Kind == ModalKind.Prompt)
            DrawPromptField(request, width);

        if (!string.IsNullOrWhiteSpace(request.Options.RememberKey))
        {
            ImGui.Spacing();

            var remember = request.Remember;
            if (ImGui.Checkbox(NoireUI.Text("NoireUI.Modal.Remember", request.Options.RememberLabel ?? "Don't ask again"), ref remember))
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
        var options = request.Options as PromptOptions ?? new PromptOptions();

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
        var confirmLabel = NoireUI.Text("NoireUI.Modal.Confirm", options.ConfirmLabel ?? DefaultConfirmLabel(request));
        var cancelLabel = options.CancelLabel ?? NoireUI.Text("NoireUI.Modal.Cancel", "Cancel");

        var confirmEnabled = request.Kind != ModalKind.Prompt
            || (request.Options as PromptOptions)?.AllowEmpty == true
            || !string.IsNullOrWhiteSpace(request.Value);

        var tone = options.Danger ? ButtonTone.Danger : ButtonTone.Accent;
        var hasCancel = cancelLabel.Length > 0;

        var confirmWidth = MeasureButton(confirmLabel);
        var cancelWidth = hasCancel ? MeasureButton(cancelLabel) : 0f;
        var spacing = NoireTheme.Current.ResolveItemSpacing().X;

        AlignRight(confirmWidth + (hasCancel ? cancelWidth + spacing : 0f));

        if (hasCancel)
        {
            if (NoireButtons.Button($"{cancelLabel}##NoireModalCancel", ButtonTone.Ghost, new Vector2(cancelWidth, 0f)))
                NoireModal.Complete(request, NoireModal.CancelledResult);

            ImGui.SameLine(0f, spacing);
        }

        ImGui.BeginDisabled(!confirmEnabled);

        var confirmed = options.HoldSeconds > 0f
            ? NoireButtons.HoldToConfirm($"{confirmLabel}##NoireModalConfirm", options.HoldSeconds, new ButtonStyle { Tone = tone }, new Vector2(confirmWidth, 0f))
            : NoireButtons.Button($"{confirmLabel}##NoireModalConfirm", tone, new Vector2(confirmWidth, 0f));

        ImGui.EndDisabled();

        if (confirmed && confirmEnabled)
            NoireModal.Complete(request, 1);
    }

    private static void DrawChoiceButtons(ModalRequest request)
    {
        var choices = request.Choices!;
        var spacing = NoireTheme.Current.ResolveItemSpacing().X;

        var total = 0f;
        for (var index = 0; index < choices.Count; index++)
            total += MeasureButton(choices[index]) + (index > 0 ? spacing : 0f);

        AlignRight(total);

        for (var index = 0; index < choices.Count; index++)
        {
            if (index > 0)
                ImGui.SameLine(0f, spacing);

            var tone = index == 0
                ? request.Options.Danger ? ButtonTone.Danger : ButtonTone.Accent
                : ButtonTone.Ghost;

            if (NoireButtons.Button($"{choices[index]}##NoireModalChoice{index}", tone, new Vector2(MeasureButton(choices[index]), 0f)))
                NoireModal.Complete(request, index);
        }
    }

    private static string DefaultConfirmLabel(ModalRequest request) => request.Kind switch
    {
        ModalKind.Prompt => "OK",
        _ => "Confirm",
    };

    private static float MeasureButton(string label)
        => MathF.Max(80f, ImGui.CalcTextSize(label).X + NoireTheme.Current.ResolveFramePadding().X * 4f);

    /// <summary>
    /// Moves the cursor so a row of that width ends at the right edge of the dialog.
    /// </summary>
    private static void AlignRight(float width)
    {
        var offset = ImGui.GetContentRegionAvail().X - width;

        if (offset > 0f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
    }
}
