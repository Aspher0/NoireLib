using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// One stop of a tour: the widget to spotlight, what to say about it, and what moves the tour on.
/// </summary>
public sealed class TourStep
{
    /// <summary>
    /// The key the widget is marked with through <see cref="NoireTour.Mark(string)"/>, empty for a step with no widget.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// The heading of the card.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The body of the card.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// The shape drawn around the widget.
    /// </summary>
    public TourSpotlight Spotlight { get; set; } = TourSpotlight.Rounded;

    /// <summary>
    /// What moves the tour to the next step.
    /// </summary>
    public TourAdvance Advance { get; set; } = TourAdvance.Manual;

    /// <summary>
    /// Where the card sits relative to the widget.
    /// </summary>
    public TourPlacement Placement { get; set; } = TourPlacement.Auto;

    /// <summary>
    /// How far the spotlight reaches past the widget, at 100%. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float Padding { get; set; } = 6f;

    /// <summary>
    /// A screen rectangle to spotlight (min in xy, max in zw), taking over from <see cref="Target"/> when it is set.
    /// </summary>
    public Func<Vector4>? Rect { get; set; }

    /// <summary>
    /// Whether the step is done, read every frame while <see cref="Advance"/> is <see cref="TourAdvance.WhenReady"/>.
    /// </summary>
    public Func<bool>? IsReady { get; set; }

    /// <summary>
    /// How long <see cref="IsReady"/> has to hold before the step moves on, in seconds. Zero moves on at once.
    /// </summary>
    public float SettleSeconds { get; set; }

    /// <summary>
    /// A value whose change restarts the <see cref="SettleSeconds"/> wait, such as the text of the field the step
    /// watches.
    /// </summary>
    public Func<int>? SettleStamp { get; set; }

    /// <summary>
    /// Whether the step has nothing to teach on this run and is passed over, read when the tour reaches it.
    /// </summary>
    public Func<bool>? IsSkipped { get; set; }

    /// <summary>
    /// The keys of the widgets that stay interactive alongside <see cref="Target"/> while the step is up.
    /// </summary>
    public string[]? AlsoInteractive { get; set; }

    /// <summary>
    /// What the card says in place of the button when the step waits for the user to act. When <see langword="null"/>,
    /// a sensible default is used.
    /// </summary>
    public string? ActionHint { get; set; }

    /// <summary>
    /// Runs when the step appears.
    /// </summary>
    public Action? OnEnter { get; set; }

    /// <summary>
    /// Runs when the step is left, whichever direction the tour goes.
    /// </summary>
    public Action? OnLeave { get; set; }
}
