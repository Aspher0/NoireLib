using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoireLib.UI;

/// <summary>
/// Dialogs you await.<br/>
/// Asking the user a question is a question, so it reads like one: <c>if (await NoireModal.ConfirmAsync(...))</c>.
/// There is no popup-open boolean to declare, no "pending action" field to stash the answer against, and no callback
/// that runs three frames later somewhere else in the file.
/// </summary>
/// <remarks>
/// Dialogs queue: raising two shows the first, then the second. The queue is safe to add to from any thread.<br/>
/// Never block on one of these from the draw or framework thread. The task completes on the draw thread, so waiting on
/// it there waits for a frame that cannot start until the wait ends. Await it, or hang the whole game.<br/>
/// Every dialog still waiting when NoireLib is disposed is completed as cancelled, so nothing awaiting one is left
/// suspended forever by a plugin unload.
/// </remarks>
/// <example>
/// <code>
/// if (await NoireModal.ConfirmAsync("Delete preset", $"Delete '{name}'? This cannot be undone.",
///         new ModalOptions { Danger = true, HoldSeconds = 1f }))
///     DeletePreset(name);
///
/// var newName = await NoireModal.PromptAsync("Rename", "What should it be called?", name);
/// if (newName != null)
///     Rename(newName);
/// </code>
/// </example>
[NoireFacade]
public static class NoireModal
{
    internal const int CancelledResult = -1;

    private static readonly object SyncRoot = new();
    private static readonly List<ModalRequest> Queue = new();

    /// <summary>
    /// How many dialogs are open or waiting their turn.
    /// </summary>
    public static int PendingCount
    {
        get
        {
            lock (SyncRoot)
                return Queue.Count;
        }
    }

    /// <summary>
    /// The drawable that presents the dialogs. It draws itself, so an awaited dialog always appears.
    /// </summary>
    /// <remarks>
    /// A dialog nobody draws never completes, and the await behind it never returns, which is a hang with no visible
    /// cause. The host therefore opts itself into automatic drawing rather than following the
    /// <see cref="NoireUI.AutoDraw"/> master default. Set its <see cref="NoireDrawable.AutoDraw"/> to
    /// <see langword="false"/> and call <see cref="Draw"/> yourself to control where in your draw order it lands.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
    public static NoireModalHost Host => NoireModalHost.Instance;

    #region Asking

    /// <summary>
    /// Asks the user to confirm something.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">What is being asked. Rich content is fully supported.</param>
    /// <param name="options">How the dialog behaves and looks.</param>
    /// <returns>True when the user confirmed, false when they declined or dismissed the dialog.</returns>
    public static async Task<bool> ConfirmAsync(string title, NoireContent message, ModalOptions? options = null)
    {
        options ??= new ModalOptions();

        if (TryReadRemembered(options.RememberKey, out var remembered))
            return remembered;

        var request = Enqueue(ModalKind.Confirm, title, message, options, null);
        var result = await request.Completion.Task.ConfigureAwait(false);

        return result == 1;
    }

    /// <summary>
    /// Asks the user for a line of text.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">What is being asked. Rich content is fully supported.</param>
    /// <param name="initialValue">What the field starts with.</param>
    /// <param name="options">How the dialog behaves and looks.</param>
    /// <returns>The value the user confirmed, or <see langword="null"/> when they cancelled.</returns>
    public static async Task<string?> PromptAsync(string title, NoireContent message, string initialValue = "", PromptOptions? options = null)
    {
        options ??= new PromptOptions();

        var request = Enqueue(ModalKind.Prompt, title, message, options, null);
        request.Value = initialValue ?? string.Empty;

        var result = await request.Completion.Task.ConfigureAwait(false);
        return result == 1 ? request.Value : null;
    }

    /// <summary>
    /// Asks the user to pick one of several options.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">What is being asked. Rich content is fully supported.</param>
    /// <param name="choices">The options, drawn as buttons in the order given.</param>
    /// <param name="options">How the dialog behaves and looks.</param>
    /// <returns>The index of the chosen option, or -1 when the user cancelled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="choices"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="choices"/> is empty.</exception>
    public static async Task<int> ChoiceAsync(string title, NoireContent message, IReadOnlyList<string> choices, ModalOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(choices);

        if (choices.Count == 0)
            throw new ArgumentException("A choice dialog needs at least one option.", nameof(choices));

        var request = Enqueue(ModalKind.Choice, title, message, options ?? new ModalOptions(), choices);
        return await request.Completion.Task.ConfigureAwait(false);
    }

    #endregion

    #region Drawing and lifetime

    /// <summary>
    /// Draws the dialog at the front of the queue, if there is one.<br/>
    /// Only needed when <see cref="Host"/> has had its automatic drawing turned off.
    /// </summary>
    public static void Draw() => Host.Draw();

    /// <summary>
    /// Cancels every open and waiting dialog, completing everything awaiting one.
    /// </summary>
    public static void CancelAll()
    {
        ModalRequest[] pending;

        lock (SyncRoot)
        {
            pending = Queue.ToArray();
            Queue.Clear();
        }

        foreach (var request in pending)
            request.Resolve(CancelledResult);
    }

    /// <summary>
    /// Forgets a remembered answer, so the dialog using that <see cref="ModalOptions.RememberKey"/> asks again.
    /// </summary>
    /// <param name="rememberKey">The key the answer was stored under.</param>
    /// <returns>True when an answer was stored and has now been removed.</returns>
    public static bool Forget(string rememberKey)
        => !string.IsNullOrWhiteSpace(rememberKey) && NoireUiState.Remove(StateKeyFor(rememberKey));

    #endregion

    /// <summary>
    /// The dialog currently being shown, or <see langword="null"/> when the queue is empty.
    /// </summary>
    internal static ModalRequest? Current
    {
        get
        {
            lock (SyncRoot)
                return Queue.Count > 0 ? Queue[0] : null;
        }
    }

    /// <summary>
    /// Completes a dialog and takes it off the queue.
    /// </summary>
    /// <param name="request">The dialog to finish.</param>
    /// <param name="result">1 for confirmed, a zero-based index for a choice, or
    /// <see cref="CancelledResult"/> for cancelled.</param>
    internal static void Complete(ModalRequest request, int result)
    {
        lock (SyncRoot)
            Queue.Remove(request);

        if (result != CancelledResult && !string.IsNullOrWhiteSpace(request.Options.RememberKey) && request.Remember)
            NoireUiState.Set(StateKeyFor(request.Options.RememberKey!), result == 1);

        request.Resolve(result);
    }

    internal static string StateKeyFor(string rememberKey) => $"Modal.{rememberKey}.answer";

    private static ModalRequest Enqueue(ModalKind kind, string title, NoireContent message, ModalOptions options, IReadOnlyList<string>? choices)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Touching the host here is what guarantees a dialog is presented: it creates and registers the drawable on the
        // first ask, so an awaited dialog cannot sit in a queue nobody draws. There is nothing to draw onto before
        // NoireLib is initialized, so the dialog simply queues until there is.
        if (NoireService.IsInitialized())
            _ = Host;

        var request = new ModalRequest(kind, title ?? string.Empty, message, options, choices);

        lock (SyncRoot)
            Queue.Add(request);

        return request;
    }

    private static bool TryReadRemembered(string? rememberKey, out bool answer)
    {
        answer = false;

        if (string.IsNullOrWhiteSpace(rememberKey))
            return false;

        return NoireUiState.TryGet(StateKeyFor(rememberKey), out answer);
    }
}

/// <summary>
/// Which kind of question a queued dialog is asking.
/// </summary>
internal enum ModalKind
{
    Confirm,
    Prompt,
    Choice,
}

/// <summary>
/// One queued dialog: what it asks, how it should look, and the task waiting on its answer.
/// </summary>
internal sealed class ModalRequest
{
    public ModalRequest(ModalKind kind, string title, NoireContent message, ModalOptions options, IReadOnlyList<string>? choices)
    {
        Kind = kind;
        Title = title;
        Message = message;
        Options = options;
        Choices = choices;

        // Continuations must not run inside the draw loop that completed the dialog: an await that immediately drew
        // more UI, or blocked, would be running in the middle of someone else's frame.
        Completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public ModalKind Kind { get; }

    public string Title { get; }

    public NoireContent Message { get; }

    public ModalOptions Options { get; }

    public IReadOnlyList<string>? Choices { get; }

    public TaskCompletionSource<int> Completion { get; }

    /// <summary>The current value of a prompt's field.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Whether the user ticked "don't ask again".</summary>
    public bool Remember { get; set; }

    /// <summary>Whether the popup has been opened, which happens on the first frame the dialog is drawn.</summary>
    public bool Opened { get; set; }

    /// <summary>Whether the prompt's field has been focused, which happens on the first frame it is drawn.</summary>
    public bool Focused { get; set; }

    public void Resolve(int result) => Completion.TrySetResult(result);
}
