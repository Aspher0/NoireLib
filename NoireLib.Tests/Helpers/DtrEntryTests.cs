using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Drives a server-info bar entry against a stand-in for the bar. What it writes and when it writes it is
/// visible without a game. The entry's whole reason for existing is that it does not write when nothing changed,
/// which is exactly what a count of writes measures.
/// </summary>
public sealed class DtrEntryTests
{
    private sealed class FakeBarEntry : IDtrBarEntry
    {
        private SeString text = new();
        private SeString? tooltip;

        public int TextWrites { get; private set; }

        public int TooltipWrites { get; private set; }

        public bool Removed { get; private set; }

        public string Title => "fake";

        public bool HasClickAction => OnClick != null;

        public bool UserHidden => false;

        public (Vector2 Min, Vector2 Max) ScreenBounds => default;

        public bool Shown { get; set; } = true;

        public ushort MinimumWidth { get; set; }

        public Action<DtrInteractionEvent>? OnClick { get; set; }

        public SeString Text
        {
            get => text;
            set
            {
                text = value;
                TextWrites++;
            }
        }

        public SeString? Tooltip
        {
            get => tooltip;
            set
            {
                tooltip = value;
                TooltipWrites++;
            }
        }

        public void Remove() => Removed = true;

        public void Dispose() => Removed = true;
    }

    private static DtrInteractionEvent Click(MouseClickType type)
        => new() { ClickType = type, ModifierKeys = ClickModifierKeys.None };

    [Fact]
    public void AssigningTheSameText_NeverWritesToTheBarTwice()
    {
        var bar = new FakeBarEntry();
        var entry = new DtrEntry(bar, new DtrEntryOptions { Title = "probe", Text = "one" });

        var afterConstruction = bar.TextWrites;

        entry.Text = "one";
        entry.Text = "one";
        bar.TextWrites.Should().Be(afterConstruction, "the value never changed");

        entry.Text = "two";
        bar.TextWrites.Should().Be(afterConstruction + 1);
        bar.Text.TextValue.Should().Be("two");
    }

    [Fact]
    public void AClick_ReachesTheCallbackForItsOwnButton()
    {
        var left = 0;
        var right = 0;
        var bar = new FakeBarEntry();
        _ = new DtrEntry(bar, new DtrEntryOptions
        {
            Title = "probe",
            OnLeftClick = _ => left++,
            OnRightClick = _ => right++,
        });

        bar.OnClick!(Click(MouseClickType.Left));
        bar.OnClick!(Click(MouseClickType.Right));
        bar.OnClick!(Click(MouseClickType.Left));

        left.Should().Be(2);
        right.Should().Be(1);
    }

    [Fact]
    public void States_AdvanceOnTheChosenButtonAndWrapAround()
    {
        var landed = new List<string>();
        var bar = new FakeBarEntry();
        var entry = new DtrEntry(bar, new DtrEntryOptions
        {
            Title = "probe",
            States =
            [
                new DtrEntryState("on", "ON", "running"),
                new DtrEntryState("off", "OFF"),
            ],
            CycleOn = MouseClickType.Right,
            OnStateChanged = state => landed.Add(state.Name),
        });

        entry.State!.Value.Name.Should().Be("on", "an entry starts in its first state");
        bar.Text.TextValue.Should().Be("ON");
        bar.Tooltip!.TextValue.Should().Be("running");

        bar.OnClick!(Click(MouseClickType.Left));
        entry.State!.Value.Name.Should().Be("on", "the left button is not what cycles this entry");

        bar.OnClick!(Click(MouseClickType.Right));
        entry.State!.Value.Name.Should().Be("off");
        bar.Text.TextValue.Should().Be("OFF");

        bar.OnClick!(Click(MouseClickType.Right));
        entry.State!.Value.Name.Should().Be("on", "the cycle wraps at the end");

        landed.Should().Equal("off", "on");
    }

    [Fact]
    public void SetState_JumpsToANamedStateAndReportsAnUnknownOne()
    {
        var changed = 0;
        var bar = new FakeBarEntry();
        var entry = new DtrEntry(bar, new DtrEntryOptions
        {
            Title = "probe",
            States = [new DtrEntryState("a", "A"), new DtrEntryState("b", "B")],
            OnStateChanged = _ => changed++,
        });

        entry.SetState("b").Should().BeTrue();
        bar.Text.TextValue.Should().Be("B");
        changed.Should().Be(0, "the caller moved the entry itself and does not need telling");

        entry.SetState("nope").Should().BeFalse();
        entry.State!.Value.Name.Should().Be("b");
    }

    [Fact]
    public void Disposing_RemovesTheEntryOnceAndStopsWrites()
    {
        var bar = new FakeBarEntry();
        var entry = new DtrEntry(bar, new DtrEntryOptions { Title = "probe", Text = "one" });
        var writes = bar.TextWrites;

        entry.Dispose();
        entry.Dispose();

        bar.Removed.Should().BeTrue();
        entry.IsDisposed.Should().BeTrue();

        entry.Text = "two";
        bar.TextWrites.Should().Be(writes, "a removed entry writes nothing");
    }
}
