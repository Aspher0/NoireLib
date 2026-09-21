using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Everything one server-info bar entry can be configured with. Only <see cref="Title"/> is required. Each
/// remaining value maps to a property of the entry Dalamud hands back.
/// </summary>
public sealed class DtrEntryOptions
{
    /// <summary>The entry's title in Dalamud's settings. Unique to the plugin.</summary>
    public required string Title { get; init; }

    /// <summary>The text the entry starts with. Ignored when <see cref="States"/> is set.</summary>
    public SeString? Text { get; init; }

    /// <summary>The tooltip shown on hover.</summary>
    public SeString? Tooltip { get; init; }

    /// <summary>Whether the entry is shown at all.</summary>
    public bool Shown { get; init; } = true;

    /// <summary>The width the entry asks for. Its text stops jittering as the value changes.</summary>
    public ushort MinimumWidth { get; init; }

    /// <summary>
    /// Produces the entry's text. It is polled on the framework thread every <see cref="RefreshInterval"/> and the
    /// entry is written only when the result differs from what it already shows. An unchanged value costs
    /// nothing. Null leaves the text to <see cref="Text"/> and to whatever the caller assigns.
    /// </summary>
    public Func<SeString>? Source { get; init; }

    /// <summary>How often <see cref="Source"/> is polled.</summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Runs on a left click.</summary>
    public Action<DtrInteractionEvent>? OnLeftClick { get; init; }

    /// <summary>Runs on a right click.</summary>
    public Action<DtrInteractionEvent>? OnRightClick { get; init; }

    /// <summary>
    /// The states the entry cycles through, in order, each click advancing to the next and wrapping at the end.
    /// The first is the state the entry starts in. Leave null for an entry that does not cycle.
    /// </summary>
    public IReadOnlyList<DtrEntryState>? States { get; init; }

    /// <summary>Which mouse button advances the cycle. The other one still reaches its own callback.</summary>
    public MouseClickType CycleOn { get; init; } = MouseClickType.Left;

    /// <summary>Runs after the cycle advances, with the state it landed on.</summary>
    public Action<DtrEntryState>? OnStateChanged { get; init; }
}
