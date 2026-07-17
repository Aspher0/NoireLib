using FluentAssertions;
using NoireLib.CommandRouter;
using NoireLib.EventBus;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the NoireCommandRouter module: history snapshot isolation, history trimming and
/// <see cref="NoireCommandRouter.MaxHistorySize"/> validation, the contract that every dispatched command
/// reports exactly one outcome reflecting what the handler actually did, and the rule that an availability
/// condition gates everything inside the scope it is declared on.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireCommandRouterTests
{
    #region Helpers

    /// <summary>
    /// Creates a router that never reaches Dalamud. It is built inactive so that mapping a command does not try
    /// to register a handler; tests set IsActive directly afterwards, which opens the dispatch path without
    /// going through activation.
    /// </summary>
    private static NoireCommandRouter CreateRouter(NoireEventBus? eventBus = null, int maxHistorySize = 50)
        => new(
            moduleId: null,
            active: false,
            enableLogging: false,
            enableAutoHelp: false,
            maxHistorySize: maxHistorySize,
            eventBus: eventBus);

    private static NoireEventBus CreateEventBus() => new(null, true, enableLogging: false);

    /// <summary>
    /// Maps a single subcommand and opens the dispatch path.
    /// </summary>
    private static void MapRunCommand(NoireCommandRouter router, Action<SubCommandBuilder> configure)
    {
        router.Map("/test").AddSubCommand("run", configure);
        router.IsActive = true;
    }

    /// <summary>
    /// Waits for an async handler's continuation to report its outcome, which lands on a thread pool thread.
    /// </summary>
    private static async Task WaitUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        condition().Should().BeTrue(because);
    }

    #endregion

    #region History snapshot

    [Fact]
    public void GetHistory_ShouldReturnSnapshot_ThatSurvivesFurtherCommands()
    {
        var router = CreateRouter();
        MapRunCommand(router, sub => sub.Handle(() => { }));

        router.OnCommandDispatched("/test", "run");

        var history = router.GetHistory();

        var iterateWhileDispatching = () =>
        {
            foreach (var _ in history)
                router.OnCommandDispatched("/test", "run");
        };

        iterateWhileDispatching.Should().NotThrow(
            "GetHistory must hand back a copy, so a command executing mid-enumeration cannot invalidate it");

        history.Should().HaveCount(1, "a snapshot must not grow as later commands are recorded");
        router.GetHistory().Should().HaveCount(2, "the live history keeps recording behind the snapshot");
    }

    [Fact]
    public void ClearHistory_ShouldNotAffectAnAlreadyTakenSnapshot()
    {
        var router = CreateRouter();
        MapRunCommand(router, sub => sub.Handle(() => { }));

        router.OnCommandDispatched("/test", "run");
        var history = router.GetHistory();

        router.ClearHistory();

        history.Should().HaveCount(1);
        router.GetHistory().Should().BeEmpty();
    }

    #endregion

    #region MaxHistorySize

    [Fact]
    public void MaxHistorySize_ShouldThrow_WhenSetToNegativeValue()
    {
        var router = CreateRouter();

        var setNegative = () => router.MaxHistorySize = -1;

        setNegative.Should().Throw<ArgumentOutOfRangeException>(
            "a negative limit can never be satisfied and would otherwise throw later, from inside command dispatch");
        router.MaxHistorySize.Should().Be(50, "a rejected value must not be stored");
    }

    [Fact]
    public void SetMaxHistorySize_ShouldThrow_WhenGivenNegativeValue()
    {
        var router = CreateRouter();

        var setNegative = () => router.SetMaxHistorySize(-1);

        setNegative.Should().Throw<ArgumentOutOfRangeException>();
        router.MaxHistorySize.Should().Be(50);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenMaxHistorySizeIsNegative()
    {
        var construct = () => CreateRouter(maxHistorySize: -1);

        construct.Should().Throw<ArgumentOutOfRangeException>("the limit is rejected at the boundary, not on first use");
    }

    [Fact]
    public void Dispatch_ShouldRecordNothingAndNotThrow_WhenMaxHistorySizeIsZero()
    {
        var router = CreateRouter(maxHistorySize: 0);
        var handlerRuns = 0;
        MapRunCommand(router, sub => sub.Handle(() => handlerRuns++));

        var dispatch = () =>
        {
            router.OnCommandDispatched("/test", "run");
            router.OnCommandDispatched("/test", "run");
        };

        dispatch.Should().NotThrow();
        handlerRuns.Should().Be(2, "a zero limit disables history recording, not command execution");
        router.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public void History_ShouldDiscardOldestEntries_WhenExceedingMaxHistorySize()
    {
        var router = CreateRouter(maxHistorySize: 2);
        MapRunCommand(router, sub => sub.Handle(() => { }));

        router.OnCommandDispatched("/test", "run first");
        router.OnCommandDispatched("/test", "run second");
        router.OnCommandDispatched("/test", "run third");

        var history = router.GetHistory();

        history.Should().HaveCount(2);
        history[0].RawArgs.Should().Be("run second", "history is ordered oldest first and drops the oldest entry once full");
        history[1].RawArgs.Should().Be("run third");
    }

    #endregion

    #region Synchronous handlers

    [Fact]
    public void Dispatch_ShouldRecordSuccessAndPublishExecuted_ForSyncHandler()
    {
        var eventBus = CreateEventBus();
        var router = CreateRouter(eventBus);
        var executed = 0;
        var failed = 0;

        eventBus.Subscribe<CommandExecutedEvent>(_ => executed++);
        eventBus.Subscribe<CommandFailedEvent>(_ => failed++);

        MapRunCommand(router, sub => sub.Handle(() => { }));

        router.OnCommandDispatched("/test", "run");

        executed.Should().Be(1);
        failed.Should().Be(0);
        router.GetHistory().Should().ContainSingle().Which.WasSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Dispatch_ShouldRecordFailureAndPublishFailed_ForThrowingSyncHandler()
    {
        var eventBus = CreateEventBus();
        var router = CreateRouter(eventBus);
        var executed = 0;
        var failed = 0;

        eventBus.Subscribe<CommandExecutedEvent>(_ => executed++);
        eventBus.Subscribe<CommandFailedEvent>(_ => failed++);

        Action throwing = () => throw new InvalidOperationException("Handler failure.");
        MapRunCommand(router, sub => sub.Handle(throwing));

        router.OnCommandDispatched("/test", "run");

        executed.Should().Be(0, "a handler that threw did not execute successfully");
        failed.Should().Be(1);
        router.GetHistory().Should().ContainSingle().Which.WasSuccessful.Should().BeFalse();
    }

    #endregion

    #region Asynchronous handlers

    [Fact]
    public async Task Dispatch_ShouldDeferOutcome_UntilAsyncHandlerCompletes()
    {
        var eventBus = CreateEventBus();
        var router = CreateRouter(eventBus);
        var executed = 0;

        eventBus.Subscribe<CommandExecutedEvent>(_ => Interlocked.Increment(ref executed));

        var gate = new TaskCompletionSource();
        Func<Task> waitForGate = () => gate.Task;
        MapRunCommand(router, sub => sub.Handle(waitForGate));

        router.OnCommandDispatched("/test", "run");

        router.GetHistory().Should().BeEmpty("the outcome is unknown until the handler's task settles");
        Volatile.Read(ref executed).Should().Be(0, "CommandExecutedEvent must not fire before the handler has done anything");

        gate.SetResult();

        await WaitUntil(() => router.GetHistory().Count == 1, "the settled handler should report its outcome");

        router.GetHistory().Should().ContainSingle().Which.WasSuccessful.Should().BeTrue();
        Volatile.Read(ref executed).Should().Be(1, "exactly one outcome is reported per invocation");
    }

    [Fact]
    public async Task Dispatch_ShouldRecordFailureOnly_WhenAsyncHandlerFaults()
    {
        var eventBus = CreateEventBus();
        var router = CreateRouter(eventBus);
        var executed = 0;
        var failed = 0;

        eventBus.Subscribe<CommandExecutedEvent>(_ => Interlocked.Increment(ref executed));
        eventBus.Subscribe<CommandFailedEvent>(_ => Interlocked.Increment(ref failed));

        Func<Task> faulting = () => Task.FromException(new InvalidOperationException("Async handler failure."));
        MapRunCommand(router, sub => sub.Handle(faulting));

        router.OnCommandDispatched("/test", "run");

        await WaitUntil(() => Volatile.Read(ref failed) == 1, "a faulted handler should report a failure");

        Volatile.Read(ref executed).Should().Be(0, "a faulted invocation must not also be reported as executed");
        router.GetHistory().Should().ContainSingle().Which.WasSuccessful.Should().BeFalse(
            "history must record the real outcome of the async handler, not an assumed success");
    }

    [Fact]
    public async Task Dispatch_ShouldRecordSuccess_WhenAsyncHandlerCompletesAfterAwait()
    {
        var router = CreateRouter();
        var handlerFinished = false;

        Func<Task> handler = async () =>
        {
            await Task.Yield();
            handlerFinished = true;
        };

        MapRunCommand(router, sub => sub.Handle(handler));

        router.OnCommandDispatched("/test", "run");

        await WaitUntil(() => router.GetHistory().Count == 1, "the completed handler should report its outcome");

        handlerFinished.Should().BeTrue();
        router.GetHistory().Should().ContainSingle().Which.WasSuccessful.Should().BeTrue();
    }

    #endregion

    #region Availability conditions

    [Fact]
    public void Dispatch_ShouldNotRunAnyHandler_WhenTheRootConditionIsFalse()
    {
        var router = CreateRouter();
        var defaultHandlerRuns = 0;
        var subCommandRuns = 0;

        router.Map("/test")
            .WithCondition(() => false)
            .Handle(() => defaultHandlerRuns++)
            .AddSubCommand("run", sub => sub.Handle(() => subCommandRuns++));
        router.IsActive = true;

        router.OnCommandDispatched("/test", "");
        router.OnCommandDispatched("/test", "run");

        defaultHandlerRuns.Should().Be(0, "a false root condition blocks the command as a whole");
        subCommandRuns.Should().Be(0, "a subcommand is inside the scope the root condition gates");
        router.GetHistory().Should().HaveCount(2).And.OnlyContain(entry => !entry.WasSuccessful,
            "a blocked invocation is recorded as unsuccessful");
    }

    [Fact]
    public void Dispatch_ShouldRunTheHandler_WhenTheRootConditionIsTrue()
    {
        var router = CreateRouter();
        var handlerRuns = 0;

        router.Map("/test")
            .WithCondition(() => true)
            .AddSubCommand("run", sub => sub.Handle(() => handlerRuns++));
        router.IsActive = true;

        router.OnCommandDispatched("/test", "run");

        handlerRuns.Should().Be(1);
        router.GetHistory().Should().ContainSingle().Which.WasSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Dispatch_ShouldReevaluateTheRootCondition_OnEveryInvocation()
    {
        var router = CreateRouter();
        var available = false;
        var handlerRuns = 0;

        router.Map("/test")
            .WithCondition(() => available)
            .AddSubCommand("run", sub => sub.Handle(() => handlerRuns++));
        router.IsActive = true;

        router.OnCommandDispatched("/test", "run");
        available = true;
        router.OnCommandDispatched("/test", "run");

        handlerRuns.Should().Be(1, "the predicate is asked again rather than sampled once at mapping time");
    }

    [Fact]
    public void Dispatch_ShouldBlockTheRawHandler_WhenTheRootConditionIsFalse()
    {
        var router = CreateRouter();
        var rawHandlerRuns = 0;

        router.Map("/test")
            .WithCondition(() => false)
            .HandleRaw((_, _) => rawHandlerRuns++);
        router.IsActive = true;

        router.OnCommandDispatched("/test", "anything");

        rawHandlerRuns.Should().Be(0, "the raw handler bypasses subcommand dispatch, not the root condition");
    }

    [Fact]
    public void Dispatch_ShouldReportABlockedRootCommand_TheSameWayAsABlockedSubCommand()
    {
        var (rootHistory, rootExecuted, rootFailed) = DispatchBlockedCommand(gateTheRoot: true);
        var (subHistory, subExecuted, subFailed) = DispatchBlockedCommand(gateTheRoot: false);

        rootHistory.Should().Equal(subHistory,
            "a root blocked by its condition is recorded exactly as a blocked subcommand is");
        rootExecuted.Should().Be(subExecuted).And.Be(0, "a blocked command never executed");
        rootFailed.Should().Be(subFailed);
    }

    /// <summary>
    /// Dispatches "/test run" against a router where either the root or the subcommand carries a false condition,
    /// and reports what the router recorded and published for it.
    /// </summary>
    /// <param name="gateTheRoot">True to put the false condition on the root command; false to put it on the subcommand.</param>
    private static (bool[] History, int Executed, int Failed) DispatchBlockedCommand(bool gateTheRoot)
    {
        var eventBus = CreateEventBus();
        var router = CreateRouter(eventBus);
        var executed = 0;
        var failed = 0;

        eventBus.Subscribe<CommandExecutedEvent>(_ => executed++);
        eventBus.Subscribe<CommandFailedEvent>(_ => failed++);

        var root = router.Map("/test");

        if (gateTheRoot)
            root.WithCondition(() => false).AddSubCommand("run", sub => sub.Handle(() => { }));
        else
            root.AddSubCommand("run", sub => sub.WithCondition(() => false).Handle(() => { }));

        router.IsActive = true;
        router.OnCommandDispatched("/test", "run");

        return (router.GetHistory().Select(entry => entry.WasSuccessful).ToArray(), executed, failed);
    }

    #endregion

    #region Argument parsing

    [Fact]
    public void Dispatch_ShouldConvertUnorderedOptionalArguments_ToTheirDeclaredTypes()
    {
        var router = CreateRouter();
        int? capturedCount = null;
        var capturedFlag = false;

        router.Map("/test").AddSubCommand("run", sub => sub
            .WithUnorderedOptionalArguments()
            .AddArgument<int>("count", required: false, defaultValue: 0)
            .AddArgument<bool>("flag", required: false, defaultValue: false)
            .Handle(args =>
            {
                capturedCount = args.Get<int>("count");
                capturedFlag = args.Get<bool>("flag");
            }));
        router.IsActive = true;

        router.OnCommandDispatched("/test", "run true 7");

        capturedCount.Should().Be(7, "an optional argument is matched by the type it converts into, whatever its position");
        capturedFlag.Should().BeTrue();
    }

    #endregion

    #region The outcome is recorded before it is announced

    /// <summary>
    /// Every rejected invocation records what happened and then tells the user about it, in that order.
    /// <br/><br/>
    /// Announcing reaches the game's chat, which is not there in a test, so the announcement throws on each of the
    /// paths below. That is exactly what the ordering protects against: reporting an outcome is allowed to fail, and an
    /// outcome that is already known must survive that failure rather than be replaced by the rootless entry the
    /// dispatch-wide error boundary records for the reporting fault itself.
    /// </summary>
    [Fact]
    public void Dispatch_ShouldRecordTheBlockedSubCommand_EvenWhenAnnouncingItFails()
    {
        var router = CreateRouter();

        router.Map("/test").AddSubCommand("run", sub => sub.WithCondition(() => false).Handle(() => { }));
        router.IsActive = true;

        router.OnCommandDispatched("/test", "run");

        router.GetHistory().Should().Contain(entry => entry.SubCommandName == "run" && !entry.WasSuccessful,
            "a blocked subcommand is recorded against the path that was blocked");
    }

    [Fact]
    public void Dispatch_ShouldRecordTheUnknownSubCommand_EvenWhenAnnouncingItFails()
    {
        var router = CreateRouter();

        router.Map("/test").AddSubCommand("run", sub => sub.Handle(() => { }));
        router.IsActive = true;

        router.OnCommandDispatched("/test", "bogus");

        router.GetHistory().Should().Contain(entry => entry.SubCommandName == "bogus" && !entry.WasSuccessful,
            "an unknown subcommand is recorded against the token the user actually typed");
    }

    [Fact]
    public void Dispatch_ShouldRecordTheRejectedArguments_EvenWhenAnnouncingThemFails()
    {
        var router = CreateRouter();
        var handlerRuns = 0;

        router.Map("/test").AddSubCommand("run", sub => sub
            .AddArgument<int>("count")
            .Handle((Action<ParsedCommandArguments>)(_ => handlerRuns++)));
        router.IsActive = true;

        router.OnCommandDispatched("/test", "run notanumber");

        handlerRuns.Should().Be(0, "an invocation whose arguments do not convert never reaches its handler");
        router.GetHistory().Should().Contain(entry => entry.SubCommandName == "run" && !entry.WasSuccessful,
            "a rejected invocation is recorded against the subcommand it named");
    }

    [Fact]
    public void Dispatch_ShouldRecordTheHandlerlessSubCommand_EvenWhenAnnouncingItFails()
    {
        var router = CreateRouter();

        router.Map("/test").AddSubCommand("group", sub => sub.AddSubCommand("child", child => child.Handle(() => { })));
        router.IsActive = true;

        router.OnCommandDispatched("/test", "group");

        router.GetHistory().Should().Contain(entry => entry.SubCommandName == "group" && !entry.WasSuccessful,
            "a subcommand reached without an executable handler is recorded against its own path");
    }

    #endregion
}
