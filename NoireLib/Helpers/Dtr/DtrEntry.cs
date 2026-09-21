using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using System;

namespace NoireLib.Helpers;

/// <summary>
/// A live server-info bar entry. Disposing it removes the entry from the bar, and disposing it again does nothing.
/// <br/>
/// Assigning <see cref="Text"/> or <see cref="Tooltip"/> writes through to the bar only when the value differs from
/// what the entry already shows. A caller assigning the same string every frame costs nothing.
/// </summary>
public sealed class DtrEntry : IDisposable
{
    private readonly IDtrBarEntry bar;
    private readonly Action refresh;
    private readonly Throttler? throttler;

    private SeString? text;
    private SeString? tooltip;
    private int stateIndex;

    internal DtrEntry(IDtrBarEntry bar, DtrEntryOptions options)
    {
        this.bar = bar;
        Options = options;
        refresh = Refresh;

        if (options.Source != null)
            throttler = new Throttler(options.RefreshInterval);

        bar.Shown = options.Shown;
        bar.MinimumWidth = options.MinimumWidth;

        // Dalamud draws an entry with a click action differently.
        if (options.OnLeftClick != null || options.OnRightClick != null || options.States is { Count: > 0 })
            bar.OnClick = Clicked;

        if (options.States is { Count: > 0 })
            Apply(options.States[0]);
        else
        {
            Text = options.Text ?? new SeString();
            Tooltip = options.Tooltip;
        }
    }

    /// <summary>The options the entry was registered with.</summary>
    public DtrEntryOptions Options { get; }

    /// <summary>The entry's title, also the key Dalamud holds it under.</summary>
    public string Title => Options.Title;

    /// <summary>Whether the entry has been removed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>The state the entry currently shows, or null when it does not cycle.</summary>
    public DtrEntryState? State
        => Options.States is { Count: > 0 } states ? states[stateIndex] : null;

    /// <summary>The text the entry shows.</summary>
    public SeString Text
    {
        get => text ?? new SeString();
        set
        {
            if (IsDisposed || Same(text, value))
                return;

            text = value;
            bar.Text = value;
        }
    }

    /// <summary>The tooltip the entry shows on hover.</summary>
    public SeString? Tooltip
    {
        get => tooltip;
        set
        {
            if (IsDisposed || Same(tooltip, value))
                return;

            tooltip = value;
            bar.Tooltip = value;
        }
    }

    /// <summary>Whether the entry is shown. The user can still hide it from Dalamud's settings.</summary>
    public bool Shown
    {
        get => !IsDisposed && bar.Shown;
        set
        {
            if (!IsDisposed)
                bar.Shown = value;
        }
    }

    /// <summary>The Dalamud entry underneath, for anything this wrapper does not expose.</summary>
    public IDtrBarEntry Bar => bar;

    /// <summary>
    /// Moves the entry to a named state, whether or not it was reached by clicking.
    /// <see cref="DtrEntryOptions.OnStateChanged"/> is not called, since the caller already knows.
    /// </summary>
    /// <param name="name">The state's name.</param>
    /// <returns>True when a state of that name exists.</returns>
    public bool SetState(string name)
    {
        if (IsDisposed || Options.States is not { Count: > 0 } states)
            return false;

        for (var index = 0; index < states.Count; index++)
        {
            if (!string.Equals(states[index].Name, name, StringComparison.Ordinal))
                continue;

            stateIndex = index;
            Apply(states[index]);
            return true;
        }

        return false;
    }

    /// <summary>Removes the entry from the bar.</summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        throttler?.Dispose();
        SafeExecutor.ExecuteSafely(bar.Remove);
        DtrEntryHelper.Forget(this);
    }

    // Framework thread, while a source is set. An unchanged value is not written.
    internal void Tick()
    {
        if (IsDisposed || throttler == null)
            return;

        throttler.Throttle(refresh);
    }

    private void Refresh()
    {
        var produced = SafeExecutor.ExecuteSafely(Options.Source!, null);
        if (produced != null)
            Text = produced;
    }

    private void Apply(DtrEntryState state)
    {
        Text = state.Text;
        Tooltip = state.Tooltip ?? Options.Tooltip;
    }

    private void Clicked(DtrInteractionEvent interaction)
    {
        SafeExecutor.ExecuteSafely(() =>
        {
            if (Options.States is { Count: > 0 } states && interaction.ClickType == Options.CycleOn)
            {
                stateIndex = (stateIndex + 1) % states.Count;
                Apply(states[stateIndex]);
                Options.OnStateChanged?.Invoke(states[stateIndex]);
            }

            switch (interaction.ClickType)
            {
                case MouseClickType.Left:
                    Options.OnLeftClick?.Invoke(interaction);
                    break;

                case MouseClickType.Right:
                    Options.OnRightClick?.Invoke(interaction);
                    break;
            }
        });
    }

    // SeString has no value equality.
    private static bool Same(SeString? left, SeString? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left == null || right == null)
            return false;

        return string.Equals(left.TextValue, right.TextValue, StringComparison.Ordinal);
    }
}
