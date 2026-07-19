using FluentAssertions;
using NoireLib.UI;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the modal contracts whose failure is a hang rather than an error: every pending dialog completes when the
/// library goes away, a cancelled dialog resolves rather than being dropped, dialogs queue in order, and a remembered
/// answer skips the dialog entirely.
/// </summary>
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

        NoireModal.PendingCount.Should().Be(1, "because nothing was remembered, so the dialog has to appear again");
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
            .Should().BeFalse("because a cancelled dialog was not answered, so there is nothing to remember");
    }

    [Fact]
    public void Choice_RefusesAnEmptyOptionList()
    {
        var act = () => NoireModal.ChoiceAsync("Pick", "Which?", Array.Empty<string>());

        act.Should().ThrowAsync<ArgumentException>();
    }
}
