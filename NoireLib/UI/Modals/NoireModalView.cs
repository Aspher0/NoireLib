using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// The dialog at the front of the <see cref="NoireModal"/> queue, for a caller drawing it itself (see <see cref="ModalOptions.CustomDraw"/>).
/// </summary>
public sealed class NoireModalView
{
    private readonly ModalRequest request;

    private string? labelBase;
    private int labelSeconds = -1;
    private string? label;

    internal NoireModalView(ModalRequest request)
    {
        this.request = request;
    }

    /// <summary>
    /// The dialog title.
    /// </summary>
    public string Title => request.Title;

    /// <summary>
    /// What is being asked.
    /// </summary>
    public NoireContent Message => request.Message;

    /// <summary>
    /// The options the dialog was raised with.
    /// </summary>
    public ModalOptions Options => request.Options;

    /// <summary>
    /// The options of a choice dialog, <see langword="null"/> for a confirmation or a prompt.
    /// </summary>
    public IReadOnlyList<string>? Choices => request.Choices;

    /// <summary>
    /// Whether the dialog is a prompt, whose answer is <see cref="Value"/>.
    /// </summary>
    public bool IsPrompt => request.Kind == ModalKind.Prompt;

    /// <summary>
    /// The text a prompt holds.
    /// </summary>
    public string Value
    {
        get => request.Value;
        set => request.Value = value ?? string.Empty;
    }

    /// <summary>
    /// The time the dialog was first presented at, in <see cref="NoireUI.Time"/> seconds.
    /// </summary>
    public float OpenedAt => request.OpenedAt;

    /// <summary>
    /// The whole seconds left before the confirming button enables, zero once it has.
    /// </summary>
    public int SecondsUntilEnabled => request.Opened
        ? NoireModalHost.CountdownSeconds(request.OpenedAt, NoireUI.Time, request.Options.EnableAfterSeconds)
        : NoireModalHost.CountdownSeconds(0f, 0f, request.Options.EnableAfterSeconds);

    /// <summary>
    /// Whether the dialog can be confirmed right now.
    /// </summary>
    public bool CanConfirm => SecondsUntilEnabled == 0 && NoireModalHost.PromptAllowsConfirm(request);

    /// <summary>
    /// The confirming button's label, with the countdown appended while it is disabled.
    /// </summary>
    public string ConfirmLabel
    {
        get
        {
            var text = NoireModalHost.ConfirmLabelFor(request);
            var seconds = SecondsUntilEnabled;

            if (!ReferenceEquals(text, labelBase) || seconds != labelSeconds || label == null)
            {
                labelBase = text;
                labelSeconds = seconds;
                label = seconds > 0 ? NoireModalHost.WithCountdown(text, seconds) : text;
            }

            return label;
        }
    }

    /// <summary>
    /// The declining button's label, empty when the dialog has no such button.
    /// </summary>
    public string CancelLabel => NoireModalHost.CancelLabelFor(request);

    /// <summary>Records that the caller drew the dialog this frame. Keeps the built-in popup away and starts the countdown.</summary>
    public void MarkPresented()
    {
        request.PresentedFrame = NoireUI.FrameCount;

        if (request.Opened)
            return;

        request.Opened = true;
        request.OpenedAt = NoireUI.Time;
    }

    /// <summary>
    /// Confirms the dialog, ignored while <see cref="CanConfirm"/> is false.
    /// </summary>
    /// <returns>True when the dialog was confirmed.</returns>
    public bool Confirm()
    {
        if (!CanConfirm)
            return false;

        NoireModal.Complete(request, 1);
        return true;
    }

    /// <summary>
    /// Declines the dialog.
    /// </summary>
    public void Cancel() => NoireModal.Complete(request, NoireModal.CancelledResult);

    /// <summary>
    /// Picks one of the <see cref="Choices"/> of a choice dialog.
    /// </summary>
    /// <param name="index">The zero-based index of the choice.</param>
    public void Choose(int index)
    {
        if (request.Choices is { } choices && index >= 0 && index < choices.Count)
            NoireModal.Complete(request, index);
    }
}
