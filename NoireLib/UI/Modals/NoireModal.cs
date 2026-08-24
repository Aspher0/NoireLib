using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoireLib.UI;

/// <summary>
/// Dialogs you await: <c>if (await NoireModal.ConfirmAsync(...))</c>. Dialogs queue: raising two shows the first, then
/// the second, and the queue is safe to add to from any thread.<br/>
/// Never block on one of these from the draw or framework thread: the task completes on the draw thread, so a wait
/// there hangs the game.
/// </summary>
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
    /// The drawable that presents the dialogs.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
    public static NoireModalHost Host => NoireModalHost.Instance;

    #region Asking

    /// <summary>
    /// Asks the user to confirm something.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">What is being asked.</param>
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
    /// <param name="message">What is being asked.</param>
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
    /// <param name="message">What is being asked.</param>
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
    /// Draws the dialog at the front of the queue, if there is one.
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

    // Null when the queue is empty.
    internal static ModalRequest? Current
    {
        get
        {
            lock (SyncRoot)
                return Queue.Count > 0 ? Queue[0] : null;
        }
    }

    // result is 1 for confirmed, a zero-based index for a choice, or CancelledResult for cancelled.
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

        // Touching the host here creates and registers the drawable on the first ask, so an awaited dialog cannot
        // sit in a queue nobody draws. There is nothing to draw onto before NoireLib is initialized, so the dialog
        // simply queues until there is.
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

internal enum ModalKind
{
    Confirm,
    Prompt,
    Choice,
}

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

    public string Value { get; set; } = string.Empty;

    // Whether the user ticked "don't ask again".
    public bool Remember { get; set; }

    public bool Opened { get; set; }

    // The time the dialog was first drawn at, which ModalOptions.EnableAfterSeconds counts from.
    public float OpenedAt { get; set; }

    public bool Focused { get; set; }

    public void Resolve(int result) => Completion.TrySetResult(result);
}
