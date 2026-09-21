using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a tour looks and which of its controls the user gets.
/// </summary>
public sealed class TourOptions
{
    /// <summary>
    /// How opaque the dimmed screen is, from zero to one.
    /// </summary>
    public float DimAlpha { get; set; } = 0.72f;

    /// <summary>
    /// The color of the dimmed screen. When <see langword="null"/>, the theme's shadow color is used.
    /// </summary>
    public Vector4? DimColor { get; set; }

    /// <summary>
    /// The color of the spotlight outline. When <see langword="null"/>, the theme's accent color is used.
    /// </summary>
    public Vector4? SpotlightColor { get; set; }

    /// <summary>
    /// The thickness of the spotlight outline, at 100%. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float SpotlightThickness { get; set; } = 2f;

    /// <summary>
    /// The corner radius of a rounded spotlight, at 100%. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float SpotlightRounding { get; set; } = 6f;

    /// <summary>
    /// Whether the spotlight breathes. Ignored while <see cref="NoireUI.ReducedMotion"/> is on.
    /// </summary>
    public bool Pulse { get; set; } = true;

    /// <summary>
    /// The width of the card, at 100%. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float CardWidth { get; set; } = 320f;

    /// <summary>
    /// The gap between the spotlight and the card, at 100%. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float CardGap { get; set; } = 12f;

    /// <summary>
    /// Whether the card shows how far along the tour is.
    /// </summary>
    public bool ShowCounter { get; set; } = true;

    /// <summary>
    /// Whether the card offers a button back to the previous step.
    /// </summary>
    public bool ShowBack { get; set; } = true;

    /// <summary>
    /// Whether the card offers a button that passes over a step waiting for the user to act.
    /// </summary>
    public bool ShowSkip { get; set; } = true;

    /// <summary>
    /// Whether the card offers the button that ends the tour.
    /// </summary>
    public bool ShowStop { get; set; } = true;

    /// <summary>
    /// Whether <see cref="NoireTour.IsInteractive"/> answers false for every widget the step does not name.
    /// </summary>
    public bool BlockOtherWidgets { get; set; } = true;

    /// <summary>
    /// What the card says when the widget has been scrolled out of view. When <see langword="null"/>, a sensible
    /// default is used.
    /// </summary>
    public string? ScrollHint { get; set; }

    /// <summary>
    /// The label of the button moving to the next step. When <see langword="null"/>, a sensible default is used.
    /// </summary>
    public string? NextLabel { get; set; }

    /// <summary>
    /// The label of the button moving back. When <see langword="null"/>, a sensible default is used.
    /// </summary>
    public string? BackLabel { get; set; }

    /// <summary>
    /// The label of the button passing over a step. When <see langword="null"/>, a sensible default is used.
    /// </summary>
    public string? SkipLabel { get; set; }

    /// <summary>
    /// The tooltip of the button ending the tour. When <see langword="null"/>, a sensible default is used.
    /// </summary>
    public string? StopLabel { get; set; }

    /// <summary>
    /// The label of the button closing the last step. When <see langword="null"/>, a sensible default is used.
    /// </summary>
    public string? FinishLabel { get; set; }

    /// <summary>
    /// Runs when the user walks past the last step.
    /// </summary>
    public Action? OnCompleted { get; set; }

    /// <summary>
    /// Runs when the tour ends without reaching the end, including through <see cref="NoireTour.Stop"/>.
    /// </summary>
    public Action? OnStopped { get; set; }

    /// <summary>
    /// Runs with the index of the step the tour just moved to.
    /// </summary>
    public Action<int>? OnStepChanged { get; set; }
}
