using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Collects the steps of a tour, each call after <see cref="Step"/> refining the step just added.
/// </summary>
public sealed class TourBuilder
{
    private readonly string id;

    private readonly List<TourStep> steps = new();

    internal TourBuilder(string id) => this.id = id;

    /// <summary>
    /// The options of the tour being built, free to be set directly.
    /// </summary>
    public TourOptions Options { get; } = new();

    /// <summary>
    /// Adds a step pointing at a marked widget.
    /// </summary>
    /// <param name="target">The key the widget is marked with through <see cref="NoireTour.Mark(string)"/>.</param>
    /// <param name="title">The heading of the card.</param>
    /// <param name="body">The body of the card.</param>
    /// <returns>This builder.</returns>
    public TourBuilder Step(string target, string title, string body)
    {
        steps.Add(new TourStep
        {
            Target = target,
            Title = title,
            Body = body,
        });

        return this;
    }

    /// <summary>
    /// Adds a step with no widget, its card centred on screen.
    /// </summary>
    /// <param name="title">The heading of the card.</param>
    /// <param name="body">The body of the card.</param>
    /// <returns>This builder.</returns>
    public TourBuilder Say(string title, string body) => Step(string.Empty, title, body);

    /// <summary>
    /// Adds a step pointing at a screen rectangle of its own.
    /// </summary>
    /// <param name="rect">The rectangle, min in xy and max in zw, read every frame.</param>
    /// <param name="title">The heading of the card.</param>
    /// <param name="body">The body of the card.</param>
    /// <returns>This builder.</returns>
    public TourBuilder StepAt(Func<Vector4> rect, string title, string body)
    {
        Step(string.Empty, title, body);
        steps[^1].Rect = rect;
        return this;
    }

    /// <summary>
    /// Waits for a click on the widget of the step just added.
    /// </summary>
    /// <returns>This builder.</returns>
    public TourBuilder AdvanceOnClick() => Configure(static step => step.Advance = TourAdvance.OnTargetClick);

    /// <summary>
    /// Waits for a condition on the step just added.
    /// </summary>
    /// <param name="isReady">Whether the step is done, read every frame.</param>
    /// <returns>This builder.</returns>
    public TourBuilder AdvanceWhen(Func<bool> isReady)
    {
        steps[^1].Advance = TourAdvance.WhenReady;
        steps[^1].IsReady = isReady;
        return this;
    }

    /// <summary>
    /// Waits for a condition to hold still for a while, on the step just added.
    /// </summary>
    /// <param name="isReady">Whether the step is done, read every frame.</param>
    /// <param name="settleSeconds">How long it has to hold before the step moves on.</param>
    /// <param name="changes">A value whose change restarts the wait, such as the text of the field being watched.</param>
    /// <returns>This builder.</returns>
    public TourBuilder AdvanceWhenSettled(Func<bool> isReady, float settleSeconds, Func<int>? changes = null)
    {
        AdvanceWhen(isReady);
        steps[^1].SettleSeconds = settleSeconds;
        steps[^1].SettleStamp = changes;
        return this;
    }

    /// <summary>
    /// Passes over the step just added when a condition holds as the tour reaches it.
    /// </summary>
    /// <param name="isSkipped">Whether the step has nothing to teach on this run.</param>
    /// <returns>This builder.</returns>
    public TourBuilder SkipWhen(Func<bool> isSkipped)
    {
        steps[^1].IsSkipped = isSkipped;
        return this;
    }

    /// <summary>
    /// Keeps other widgets usable alongside the one the step just added points at.
    /// </summary>
    /// <param name="targets">The keys of the widgets that stay interactive.</param>
    /// <returns>This builder.</returns>
    public TourBuilder AlsoInteractive(params string[] targets)
    {
        steps[^1].AlsoInteractive = targets;
        return this;
    }

    /// <summary>
    /// Sets what the card says in place of the button while the step just added waits for the user to act.
    /// </summary>
    /// <param name="hint">The line of text.</param>
    /// <returns>This builder.</returns>
    public TourBuilder Hint(string hint)
    {
        steps[^1].ActionHint = hint;
        return this;
    }

    /// <summary>
    /// Sets where the card of the step just added sits.
    /// </summary>
    /// <param name="placement">The side of the widget.</param>
    /// <returns>This builder.</returns>
    public TourBuilder Place(TourPlacement placement)
    {
        steps[^1].Placement = placement;
        return this;
    }

    /// <summary>
    /// Sets the shape drawn around the widget of the step just added.
    /// </summary>
    /// <param name="spotlight">The shape.</param>
    /// <returns>This builder.</returns>
    public TourBuilder Shape(TourSpotlight spotlight)
    {
        steps[^1].Spotlight = spotlight;
        return this;
    }

    /// <summary>
    /// Runs callbacks as the step just added appears and is left.
    /// </summary>
    /// <param name="onEnter">Runs when the step appears, ignored when <see langword="null"/>.</param>
    /// <param name="onLeave">Runs when the step is left, ignored when <see langword="null"/>.</param>
    /// <returns>This builder.</returns>
    public TourBuilder On(Action? onEnter, Action? onLeave = null)
    {
        steps[^1].OnEnter = onEnter;
        steps[^1].OnLeave = onLeave;
        return this;
    }

    /// <summary>
    /// Runs when the user walks past the last step.
    /// </summary>
    /// <param name="onCompleted">The callback.</param>
    /// <returns>This builder.</returns>
    public TourBuilder WhenCompleted(Action onCompleted)
    {
        Options.OnCompleted = onCompleted;
        return this;
    }

    /// <summary>
    /// Adds a step already built.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <returns>This builder.</returns>
    public TourBuilder Add(TourStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        steps.Add(step);
        return this;
    }

    /// <summary>
    /// Runs the tour.
    /// </summary>
    public void Start() => NoireTour.Start(id, steps, Options);

    private TourBuilder Configure(Action<TourStep> body)
    {
        body(steps[^1]);
        return this;
    }
}
