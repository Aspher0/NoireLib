using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>A short message attached to a window, with a progress line. No drawing.</summary>
public sealed class NoirePill
{
    private float shownAt = float.NegativeInfinity;

    /// <summary>The message.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>A game icon shown before the message, or 0.</summary>
    public uint GameIcon { get; private set; }

    /// <summary>A second game icon, shown after an arrow from the first, or 0.</summary>
    public uint ToGameIcon { get; private set; }

    /// <summary>The accent color, or <see langword="null"/> for the theme's.</summary>
    public Vector4? Accent { get; private set; }

    /// <summary>How long the message stays, in seconds.</summary>
    public float Duration { get; private set; }

    /// <summary>Whether the message is showing.</summary>
    public bool Active => Age < Duration;

    /// <summary>How far through its life the message is, from 0 to 1.</summary>
    public float Progress => Duration <= 0f ? 1f : Math.Clamp(Age / Duration, 0f, 1f);

    /// <summary>Seconds since the message was shown.</summary>
    public float Age => NoireUI.Time - shownAt;

    /// <summary>Shows a message, replacing the current one.</summary>
    /// <param name="text">The message.</param>
    /// <param name="gameIcon">A game icon before it, or 0.</param>
    /// <param name="toGameIcon">A game icon after an arrow, or 0.</param>
    /// <param name="accent">The accent color, or <see langword="null"/> for the theme's.</param>
    /// <param name="seconds">How long it stays.</param>
    public void Show(string text, uint gameIcon = 0, uint toGameIcon = 0, Vector4? accent = null, float seconds = 2.6f)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        GameIcon = gameIcon;
        ToGameIcon = toGameIcon;
        Accent = accent;
        Duration = seconds;
        shownAt = NoireUI.Time;
    }

    /// <summary>Hides the message at once.</summary>
    public void Hide() => shownAt = float.NegativeInfinity;
}
