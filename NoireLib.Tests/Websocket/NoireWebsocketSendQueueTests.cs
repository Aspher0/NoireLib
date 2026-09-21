using FluentAssertions;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the outbound queue: it is bounded, it applies the policy the caller chose the moment a message would pass
/// the capacity, and it hands messages to the writer in the order they were queued.
/// </summary>
public sealed class NoireWebsocketSendQueueTests
{
    [Fact]
    public async Task DequeueAsync_ReturnsMessagesInTheOrderTheyWereQueued()
    {
        var queue = new SendQueue(8, NoireSocketSendOverflow.Fail);

        await queue.EnqueueAsync(Item("one"), CancellationToken.None);
        await queue.EnqueueAsync(Item("two"), CancellationToken.None);
        await queue.EnqueueAsync(Item("three"), CancellationToken.None);

        queue.Count.Should().Be(3);
        (await Read(queue)).Should().Be("one");
        (await Read(queue)).Should().Be("two");
        (await Read(queue)).Should().Be("three");
        queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task EnqueueAsync_UnderFailWhenFull_Throws()
    {
        var queue = new SendQueue(2, NoireSocketSendOverflow.Fail);

        await queue.EnqueueAsync(Item("one"), CancellationToken.None);
        await queue.EnqueueAsync(Item("two"), CancellationToken.None);

        var enqueue = async () => await queue.EnqueueAsync(Item("three"), CancellationToken.None);

        (await enqueue.Should().ThrowAsync<NoireSocketQueueFullException>()).Which.Capacity.Should().Be(2);
        queue.Count.Should().Be(2);
    }

    [Fact]
    public async Task EnqueueAsync_UnderDropOldestWhenFull_KeepsTheNewestAndSettlesTheOne()
    {
        var queue = new SendQueue(2, NoireSocketSendOverflow.DropOldest);
        var oldest = Item("one");

        await queue.EnqueueAsync(oldest, CancellationToken.None);
        await queue.EnqueueAsync(Item("two"), CancellationToken.None);
        await queue.EnqueueAsync(Item("three"), CancellationToken.None);

        queue.Count.Should().Be(2);
        oldest.Completion.Task.IsCompletedSuccessfully.Should().BeTrue();
        (await Read(queue)).Should().Be("two");
        (await Read(queue)).Should().Be("three");
    }

    [Fact]
    public async Task EnqueueAsync_UnderDropNewestWhenFull_KeepsWhatIsAlreadyQueued()
    {
        var queue = new SendQueue(2, NoireSocketSendOverflow.DropNewest);
        var newest = Item("three");

        await queue.EnqueueAsync(Item("one"), CancellationToken.None);
        await queue.EnqueueAsync(Item("two"), CancellationToken.None);
        await queue.EnqueueAsync(newest, CancellationToken.None);

        queue.Count.Should().Be(2);
        newest.Completion.Task.IsCompletedSuccessfully.Should().BeTrue();
        (await Read(queue)).Should().Be("one");
        (await Read(queue)).Should().Be("two");
    }

    [Fact]
    public async Task EnqueueAsync_UnderBlockWhenFull_WaitsForTheWriterToSettleOne()
    {
        var queue = new SendQueue(1, NoireSocketSendOverflow.Block);
        var first = Item("one");

        await queue.EnqueueAsync(first, CancellationToken.None);
        var blocked = queue.EnqueueAsync(Item("two"), CancellationToken.None);

        blocked.IsCompleted.Should().BeFalse("the queue is full and the policy waits for room");

        var written = await queue.DequeueAsync(CancellationToken.None);
        queue.Settle(written!, null);

        await blocked.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        queue.Count.Should().Be(1);
    }

    [Fact]
    public async Task Requeue_PutsAFailedWriteBackAheadOfWhatFollowedIt()
    {
        var queue = new SendQueue(8, NoireSocketSendOverflow.Fail);

        await queue.EnqueueAsync(Item("one"), CancellationToken.None);
        await queue.EnqueueAsync(Item("two"), CancellationToken.None);

        var taken = await queue.DequeueAsync(CancellationToken.None);
        queue.Requeue(taken!);

        (await Read(queue)).Should().Be("one");
        (await Read(queue)).Should().Be("two");
    }

    [Fact]
    public async Task Close_FaultsEveryMessageStillWaiting()
    {
        var queue = new SendQueue(8, NoireSocketSendOverflow.Fail);
        var waiting = Item("one");

        await queue.EnqueueAsync(waiting, CancellationToken.None);
        queue.Close(new NoireSocketClosedException("gone"));

        var observe = async () => await waiting.Completion.Task;

        await observe.Should().ThrowAsync<NoireSocketClosedException>();
        queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task EnqueueAsync_AfterClose_Throws()
    {
        var queue = new SendQueue(8, NoireSocketSendOverflow.Fail);
        queue.Close(new NoireSocketClosedException("gone"));

        var enqueue = async () => await queue.EnqueueAsync(Item("one"), CancellationToken.None);

        await enqueue.Should().ThrowAsync<NoireSocketClosedException>();
    }

    private static SendItem Item(string text)
        => new(Encoding.UTF8.GetBytes(text), NoireWebsocketMessageKind.Text);

    private static async Task<string> Read(SendQueue queue)
    {
        var item = await queue.DequeueAsync(CancellationToken.None);
        return Encoding.UTF8.GetString(item!.Payload);
    }
}
