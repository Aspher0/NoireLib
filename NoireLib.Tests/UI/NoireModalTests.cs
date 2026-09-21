using FluentAssertions;
using NoireLib.UI;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks the modal contracts whose failure is a hang.</summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireModalTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"NoireModalTests_{Guid.NewGuid():N}.json");

    public NoireModalTests()
    {
        NoireModal.CancelAll();
        NoireUiState.FilePath = path;
        NoireUiState.Clear();
    }

    public void Dispose()
    {
        NoireModal.CancelAll();
        NoireUiState.Clear();
        NoireUiState.FilePath = null;
        NoireUiState.Reload();

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CountdownSeconds_IsZeroWhenTheButtonDoesNotWait()
        => NoireModalHost.CountdownSeconds(0f, 10f, 0f).Should().Be(0);

    [Theory]
    [InlineData(0f, 5, "because the whole wait is still ahead")]
    [InlineData(0.2f, 5, "because a part second still reads as the second it is in")]
    [InlineData(1f, 4, "because a second has gone")]
    [InlineData(4.9f, 1, "because the last second still counts")]
    public void CountdownSeconds_CountsWholeSecondsDown(float elapsed, int expected, string because)
        => NoireModalHost.CountdownSeconds(100f, 100f + elapsed, 5f).Should().Be(expected, because);

    [Fact]
    public void CountdownSeconds_IsZeroOnceTheWaitHasElapsed()
        => NoireModalHost.CountdownSeconds(100f, 105f, 5f).Should().Be(0);

    [Fact]
    public void CountdownSeconds_NeverReportsMoreThanTheWaitAskedFor()
        => NoireModalHost.CountdownSeconds(100f, 50f, 5f).Should()
            .Be(5, "because a clock reading behind the one the dialog opened at must not extend the wait");

    [Fact]
    public void WidestLabel_IsTheLabelItselfWithoutACountdown()
        => NoireModalHost.WidestLabel("Switch", 0f).Should().Be("Switch");

    [Fact]
    public void WidestLabel_CarriesTheHighestDigitTheCountdownShows()
        => NoireModalHost.WidestLabel("Switch", 5f).Should()
            .Be("Switch (5)", "because measuring a narrower form makes the button jitter as the digit changes");

    [Fact]
    public async Task CancelAll_CompletesEveryPendingDialog()
    {
        var first = NoireModal.ConfirmAsync("One", "Really?");
        var second = NoireModal.ConfirmAsync("Two", "Really?");

        NoireModal.PendingCount.Should().Be(2);

        NoireModal.CancelAll();

        (await first).Should().BeFalse();
        (await second).Should().BeFalse();
        NoireModal.PendingCount.Should().Be(0, "because a dialog left uncompleted suspends its await forever");
    }

    [Fact]
    public async Task CancelAll_CompletesAPromptAsNull()
    {
        var prompt = NoireModal.PromptAsync("Rename", "What should it be called?", "old");

        NoireModal.CancelAll();

        (await prompt).Should().BeNull();
    }

    [Fact]
    public async Task CancelAll_CompletesAChoiceAsMinusOne()
    {
        var choice = NoireModal.ChoiceAsync("Pick", "Which one?", new[] { "A", "B" });

        NoireModal.CancelAll();

        (await choice).Should().Be(-1);
    }

    [Fact]
    public void Dialogs_QueueInTheOrderTheyWereRaised()
    {
        _ = NoireModal.ConfirmAsync("First", "Really?");
        _ = NoireModal.ConfirmAsync("Second", "Really?");

        NoireModal.Current!.Title.Should().Be("First");

        NoireModal.Complete(NoireModal.Current!, 1);

        NoireModal.Current!.Title.Should().Be("Second");
    }

    [Fact]
    public async Task Complete_ResolvesTheAwaiter()
    {
        var confirm = NoireModal.ConfirmAsync("Delete", "Sure?");

        NoireModal.Complete(NoireModal.Current!, 1);

        (await confirm).Should().BeTrue();
    }

    [Fact]
    public async Task Choice_ReturnsTheChosenIndex()
    {
        var choice = NoireModal.ChoiceAsync("Pick", "Which?", new[] { "A", "B", "C" });

        NoireModal.Complete(NoireModal.Current!, 2);

        (await choice).Should().Be(2);
    }

    [Fact]
    public async Task Prompt_ReturnsTheEditedValue()
    {
        var prompt = NoireModal.PromptAsync("Rename", "New name?", "old");

        var request = NoireModal.Current!;
        request.Value = "new";
        NoireModal.Complete(request, 1);

        (await prompt).Should().Be("new");
    }

    [Fact]
    public async Task RememberedAnswer_SkipsTheDialogEntirely()
    {
        var options = new ModalOptions { RememberKey = "close-to-tray" };

        var first = NoireModal.ConfirmAsync("Close", "Minimise instead?", options);
        var request = NoireModal.Current!;
        request.Remember = true;
        NoireModal.Complete(request, 1);

        (await first).Should().BeTrue();

        var second = NoireModal.ConfirmAsync("Close", "Minimise instead?", options);

        second.IsCompleted.Should().BeTrue("because a remembered answer must not queue a dialog at all");
        (await second).Should().BeTrue();
        NoireModal.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task AnswerIsOnlyRemembered_WhenTheUserTickedTheBox()
    {
        var options = new ModalOptions { RememberKey = "ask-again" };

        var first = NoireModal.ConfirmAsync("Close", "Minimise instead?", options);
        NoireModal.Complete(NoireModal.Current!, 1);
        await first;

        _ = NoireModal.ConfirmAsync("Close", "Minimise instead?", options);

        NoireModal.PendingCount.Should().Be(1, "because nothing was remembered");
    }

    [Fact]
    public async Task Forget_ClearsARememberedAnswer()
    {
        var options = new ModalOptions { RememberKey = "forget-me" };

        var first = NoireModal.ConfirmAsync("Close", "Minimise instead?", options);
        var request = NoireModal.Current!;
        request.Remember = true;
        NoireModal.Complete(request, 1);
        await first;

        NoireModal.Forget("forget-me").Should().BeTrue();

        _ = NoireModal.ConfirmAsync("Close", "Minimise instead?", options);
        NoireModal.PendingCount.Should().Be(1);
    }

    [Fact]
    public void ADeclinedAnswerIsRemembered_Separately()
    {
        var options = new ModalOptions { RememberKey = "declined" };

        _ = NoireModal.ConfirmAsync("Close", "Minimise instead?", options);
        var request = NoireModal.Current!;
        request.Remember = true;
        NoireModal.Complete(request, NoireModal.CancelledResult);

        NoireUiState.TryGet<bool>(NoireModal.StateKeyFor("declined"), out _)
            .Should().BeFalse("because a cancelled dialog was not answered");
    }

    [Fact]
    public void Choice_RefusesAnEmptyOptionList()
    {
        var act = () => NoireModal.ChoiceAsync("Pick", "Which?", Array.Empty<string>());

        act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(10, ModalRequest.NoFrame, 11, true, "because the caller gets two frames to draw a new dialog")]
    [InlineData(10, ModalRequest.NoFrame, 13, false, "because a dialog nobody drew falls back to the built-in popup")]
    [InlineData(10, 40, 42, true, "because a dialog presented last frame is still the caller's")]
    [InlineData(10, 40, 43, false, "because a caller that stopped drawing hands the dialog back")]
    public void DefersToCaller_FallsBackWhenTheCallerStopsDrawing(int firstSeen, int presented, int frame, bool expected, string because)
        => NoireModalHost.DefersToCaller(firstSeen, presented, frame).Should().Be(expected, because);

    [Fact]
    public async Task Active_ConfirmsThroughTheView()
    {
        var confirm = NoireModal.ConfirmAsync("Switch", "Really?", new ModalOptions { CustomDraw = true, ConfirmLabel = "Switch" });

        var view = NoireModal.Active!;
        view.Title.Should().Be("Switch");
        view.ConfirmLabel.Should().Be("Switch");
        view.Confirm().Should().BeTrue();

        (await confirm).Should().BeTrue();
        NoireModal.Active.Should().BeNull();
    }

    [Fact]
    public async Task Active_CountsDownFromTheFirstPresentedFrame()
    {
        var time = 100f;
        NoireUI.TimeOverride = () => time;
        NoireUI.FrameOverride = () => 1;

        try
        {
            var confirm = NoireModal.ConfirmAsync("Unsafe", "Really?", new ModalOptions { CustomDraw = true, ConfirmLabel = "Enable", EnableAfterSeconds = 5f });
            var view = NoireModal.Active!;

            view.ConfirmLabel.Should().Be("Enable (5)", "because the wait has not started before the dialog is presented");

            view.MarkPresented();
            time = 102.5f;

            view.SecondsUntilEnabled.Should().Be(3);
            view.ConfirmLabel.Should().Be("Enable (3)");
            view.Confirm().Should().BeFalse("because the confirming button is still disabled");

            time = 105f;

            view.ConfirmLabel.Should().Be("Enable");
            view.Confirm().Should().BeTrue();
            (await confirm).Should().BeTrue();
        }
        finally
        {
            NoireUI.TimeOverride = null;
            NoireUI.FrameOverride = null;
        }
    }

    [Fact]
    public async Task Active_CancelDeclines()
    {
        var confirm = NoireModal.ConfirmAsync("Switch", "Really?", new ModalOptions { CustomDraw = true });

        NoireModal.Active!.Cancel();

        (await confirm).Should().BeFalse();
    }
}
