using NoireLib.Helpers;
using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// One notification: a message, how serious it is, how long it stays, and anything the user can do about it.<br/>
/// Create one through the static helpers and it appears in <see cref="NoireToastArea.Default"/>; hand one to a
/// <see cref="NoireToastArea"/> yourself to place it somewhere else.
/// </summary>
/// <remarks>
/// Showing a toast is safe from any thread. Nothing here touches ImGui: the toast is queued, and the area picks it up
/// on the next frame it draws, which is also when its clock starts. A toast created while the interface is hidden
/// therefore still gets its full duration once it becomes visible, rather than expiring unseen.
/// </remarks>
/// <example>
/// <code>
/// NoireToast.Success("Preset saved");
/// NoireToast.Error("Could not reach the server").WithAction("Retry", _ => Retry());
/// NoireToast.Undo("3 presets deleted", () => RestoreDeleted());
/// </code>
/// </example>
[NoireFacade]
public sealed class NoireToast
{
    private readonly List<ToastAction> actions = new();

    /// <summary>
    /// Creates a toast without showing it.
    /// </summary>
    /// <param name="content">The message.</param>
    /// <param name="severity">What the toast is telling the user.</param>
    /// <param name="id">An optional unique identifier, used to keep the toast's animation keyed to it. When
    /// <see langword="null"/>, a random one is generated.</param>
    public NoireToast(NoireContent content, ToastSeverity severity = ToastSeverity.Info, string? id = null)
    {
        Content = content ?? new NoireContent();
        Severity = severity;
        Id = string.IsNullOrWhiteSpace(id) ? RandomGenerator.GenerateGuidString() : id;
        Duration = DefaultDurationFor(severity);
    }

    /// <summary>
    /// The unique identifier of this toast.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The message. Rich content is fully supported: text, dynamic text, icons, images, keycaps and arbitrary widgets.
    /// </summary>
    public NoireContent Content { get; set; }

    /// <summary>
    /// An optional heading shown above the message, in the severity color.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// What the toast is telling the user, which decides its stripe, its icon and its default duration.
    /// </summary>
    public ToastSeverity Severity { get; set; }

    /// <summary>
    /// How long the toast stays before dismissing itself. <see cref="TimeSpan.Zero"/> makes it stay until it is
    /// dismissed, which is what an error with an action wants.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// A live progress reading from 0 to 1, drawn as a bar under the message.<br/>
    /// Set it for work in flight and give the toast a zero <see cref="Duration"/>, then call <see cref="Dismiss"/> when
    /// the work finishes. Polled on the draw thread, so it must be cheap and must not touch game objects.
    /// </summary>
    public Func<float>? Progress { get; set; }

    /// <summary>
    /// The buttons on the toast. See <see cref="WithAction(string, Action{NoireToast}, ButtonTone)"/>.
    /// </summary>
    public IList<ToastAction> Actions => actions;

    /// <summary>
    /// Whether the toast shows a close button. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Closable { get; set; } = true;

    /// <summary>
    /// Whether hovering the toast pauses its countdown, so a message cannot expire while it is being read.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool PauseOnHover { get; set; } = true;

    /// <summary>
    /// Invoked when the body of the toast is clicked.
    /// </summary>
    public Action<NoireToast>? OnClick { get; set; }

    /// <summary>
    /// Invoked once when the toast goes away, whichever way it went: expired, dismissed, or closed by the user.
    /// </summary>
    public Action<NoireToast>? OnDismissed { get; set; }

    /// <summary>
    /// Whether the toast has been asked to go away. It stays on screen for the length of its exit animation afterwards.
    /// </summary>
    public bool IsDismissed { get; private set; }

    /// <summary>
    /// The area currently showing this toast, or <see langword="null"/> when it has not been shown yet.
    /// </summary>
    public NoireToastArea? Area { get; internal set; }

    /// <summary>
    /// How many seconds of the toast's duration are left. Counts down only while the toast is drawn, and pauses while
    /// it is hovered when <see cref="PauseOnHover"/> is set.
    /// </summary>
    internal float Remaining { get; set; } = -1f;

    /// <summary>
    /// Whether the toast has been drawn at least once, which is when its clock starts.
    /// </summary>
    internal bool Started { get; set; }

    /// <summary>
    /// How present the toast is this frame, from 0 (gone) to 1 (fully arrived). Drives its opacity, its slide and how
    /// much vertical room it takes in the stack.
    /// </summary>
    internal float Presence { get; set; }

    /// <summary>
    /// How much vertical room the toast takes in the stack this frame, which is its measured height scaled by
    /// <see cref="Presence"/> so the stack closes up smoothly behind it.
    /// </summary>
    internal float Reserved { get; set; }

    /// <summary>
    /// The height this toast measured last frame, used to paint its background before its contents are laid out.
    /// </summary>
    /// <remarks>
    /// An immediate-mode pass cannot know how tall a block is until it has drawn it, and a background drawn afterwards
    /// would cover it. Reusing last frame's measurement means a toast whose content changes size is painted one frame
    /// behind, which self-corrects immediately and is invisible in practice.
    /// </remarks>
    internal float LastHeight { get; set; }

    #region Building

    /// <summary>
    /// Adds a button to the toast.
    /// </summary>
    /// <param name="label">The button label.</param>
    /// <param name="onInvoke">What the button does.</param>
    /// <param name="tone">How the button is colored.</param>
    /// <returns>This <see cref="NoireToast"/> instance, for chaining.</returns>
    public NoireToast WithAction(string label, Action<NoireToast> onInvoke, ButtonTone tone = ButtonTone.Ghost)
    {
        actions.Add(new ToastAction(label, onInvoke) { Tone = tone });
        return this;
    }

    /// <summary>
    /// Sets the toast's heading.
    /// </summary>
    /// <param name="title">The heading.</param>
    /// <returns>This <see cref="NoireToast"/> instance, for chaining.</returns>
    public NoireToast WithTitle(string title)
    {
        Title = title;
        return this;
    }

    /// <summary>
    /// Sets how long the toast stays.
    /// </summary>
    /// <param name="seconds">The duration in seconds. Zero makes the toast stay until dismissed.</param>
    /// <returns>This <see cref="NoireToast"/> instance, for chaining.</returns>
    public NoireToast WithDuration(double seconds)
    {
        Duration = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        Remaining = -1f;
        return this;
    }

    /// <summary>
    /// Gives the toast a live progress bar and makes it stay until it is dismissed.
    /// </summary>
    /// <param name="progress">Returns how far the work has got, from 0 to 1.</param>
    /// <returns>This <see cref="NoireToast"/> instance, for chaining.</returns>
    public NoireToast WithProgress(Func<float> progress)
    {
        Progress = progress;
        Duration = TimeSpan.Zero;
        return this;
    }

    #endregion

    /// <summary>
    /// Asks the toast to go away. It plays its exit animation and is then removed.<br/>
    /// Safe to call from any thread, and safe to call more than once.
    /// </summary>
    public void Dismiss() => IsDismissed = true;

    /// <summary>
    /// Shows this toast in an area.
    /// </summary>
    /// <param name="area">The area to show it in. When <see langword="null"/>, <see cref="NoireToastArea.Default"/> is
    /// used.</param>
    /// <returns>This <see cref="NoireToast"/> instance, for chaining.</returns>
    public NoireToast Show(NoireToastArea? area = null)
    {
        (area ?? NoireToastArea.Default).Add(this);
        return this;
    }

    #region Static helpers

    /// <summary>
    /// Shows a toast in <see cref="NoireToastArea.Default"/>.
    /// </summary>
    /// <param name="content">The message.</param>
    /// <param name="severity">What the toast is telling the user.</param>
    /// <returns>The toast, so it can be configured further or dismissed later.</returns>
    public static NoireToast Show(NoireContent content, ToastSeverity severity = ToastSeverity.Info)
        => new NoireToast(content, severity).Show();

    /// <summary>Shows an informational toast.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The toast.</returns>
    public static NoireToast Info(string message) => Show(message, ToastSeverity.Info);

    /// <summary>Shows a success toast.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The toast.</returns>
    public static NoireToast Success(string message) => Show(message, ToastSeverity.Success);

    /// <summary>Shows a warning toast.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The toast.</returns>
    public static NoireToast Warning(string message) => Show(message, ToastSeverity.Warning);

    /// <summary>Shows an error toast.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The toast.</returns>
    public static NoireToast Error(string message) => Show(message, ToastSeverity.Error);

    /// <summary>
    /// Shows a toast offering to undo what just happened.<br/>
    /// This is the pattern that replaces a confirmation dialog for a reversible action: do the thing immediately, and
    /// offer a way back for a few seconds instead of asking first.
    /// </summary>
    /// <param name="message">What happened, phrased in the past tense.</param>
    /// <param name="onUndo">How to put it back.</param>
    /// <param name="seconds">How long the offer stands.</param>
    /// <returns>The toast.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="onUndo"/> is <see langword="null"/>.</exception>
    public static NoireToast Undo(string message, Action onUndo, double seconds = 6d)
    {
        ArgumentNullException.ThrowIfNull(onUndo);

        return new NoireToast(message)
            .WithDuration(seconds)
            .WithAction(NoireUI.Localize("NoireUI.Toast.Undo", "Undo"), _ => onUndo(), ButtonTone.Accent)
            .Show();
    }

    #endregion

    /// <summary>
    /// How long a toast of each severity stays by default. An error stays noticeably longer, because it is the one a
    /// user cannot afford to have already scrolled past.
    /// </summary>
    private static TimeSpan DefaultDurationFor(ToastSeverity severity) => severity switch
    {
        ToastSeverity.Error => TimeSpan.FromSeconds(10d),
        ToastSeverity.Warning => TimeSpan.FromSeconds(7d),
        _ => TimeSpan.FromSeconds(4d),
    };

    /// <summary>
    /// Runs the dismissal callback once, reporting anything it throws.
    /// </summary>
    internal void NotifyDismissed()
    {
        var callback = OnDismissed;
        OnDismissed = null;

        if (callback == null)
            return;

        try
        {
            callback(this);
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireToast), "A toast's dismissal callback threw.", ex);
        }
    }
}
