using System;

namespace NoireLib.Helpers;

/// <summary>One clickable line in a dev bar menu.</summary>
public sealed class DevBarItem
{
    /// <summary>The text on the line.</summary>
    public required string Label { get; init; }

    /// <summary>Runs when the line is clicked.</summary>
    public required Action OnClick { get; init; }

    /// <summary>The shortcut drawn right-aligned on the line. It is a label only. Nothing binds it to a key.</summary>
    public string? Shortcut { get; init; }

    /// <summary>Whether the line can be clicked, asked every frame. Null leaves it always clickable.</summary>
    public Func<bool>? Enabled { get; init; }

    /// <summary>Whether the line draws its tick, asked every frame. Null leaves it unticked.</summary>
    public Func<bool>? Selected { get; init; }

    /// <summary>Whether a separator is drawn above the line.</summary>
    public bool SeparatorBefore { get; init; }
}
