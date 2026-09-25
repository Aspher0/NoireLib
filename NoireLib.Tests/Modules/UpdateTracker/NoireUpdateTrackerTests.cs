using FluentAssertions;
using NoireLib.EventBus;
using NoireLib.UpdateTracker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the notification gate, the check timer and disposal: the gate closes only on a delivery and reopens for a
/// new repository, the timer runs only with something to fetch, and teardown runs once and stays latched.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireUpdateTrackerTests : IDisposable
{
    #region Helpers

    private readonly List<NoireUpdateTracker> trackersToClean = new();

    public void Dispose()
    {
        foreach (var tracker in trackersToClean)
        {
            try
            {
                tracker.Dispose();
            }
            catch
            {
            }
        }
    }

    // Registered for disposal: no check timer outlives its test.
    private NoireUpdateTracker MakeTracker(bool active = false, string? repoUrl = null)
    {
        var tracker = new NoireUpdateTracker(
            active: active,
            enableLogging: false,
            repoUrl: repoUrl,
            shouldPrintMessageInChatOnUpdate: false,
            shouldShowNotificationOnUpdate: false);

        trackersToClean.Add(tracker);
        return tracker;
    }

    /// <summary>
    /// Builds a tracker whose only channel is an event bus, which is the one a game-free test can observe: the
    /// notification manager and the chat log are Dalamud services.
    /// </summary>
    private (NoireUpdateTracker Tracker, List<NewPluginVersionDetectedEvent> Detections) MakeEventBusTracker()
    {
        var eventBus = new NoireEventBus(null, true, enableLogging: false);
        var tracker = MakeTracker();
        tracker.EventBus = eventBus;

        var detections = new List<NewPluginVersionDetectedEvent>();
        eventBus.Subscribe<NewPluginVersionDetectedEvent>(detections.Add);

        return (tracker, detections);
    }

    private static readonly Version CurrentVersion = new(1, 0, 0, 0);
    private static readonly Version RemoteVersion = new(2, 0, 0, 0);

    /// <summary>The timer instance the module is currently scheduling checks on, or null while it is stopped.</summary>
    private static Timer? CheckTimerOf(NoireUpdateTracker tracker)
        => (Timer?)typeof(NoireUpdateTracker)
            .GetField("updateCheckTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(tracker);

    // Line endings normalized: assertions spanning a line break do not depend on the checkout.
    private static string ReadUpdateTrackerSource()
        => File.ReadAllText(FindUpdateTrackerSourceFile()).Replace("\r\n", "\n");

    private static string FindUpdateTrackerSourceFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "NoireLib", "Modules", "UpdateTracker", "NoireUpdateTracker.cs");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate NoireUpdateTracker.cs from the test output path.");
    }

    #endregion

    #region Game-free construction

    /// <summary>Neither recording settings nor starting a timer needs a Dalamud service.</summary>
    [Fact]
    public void Constructor_WithoutAnInitializedNoireLib_Succeeds()
    {
        NoireService.IsInitialized().Should().BeFalse("these tests are the game-free case by construction");

        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");

        tracker.IsActive.Should().BeTrue();
        tracker.RepoUrl.Should().Be("https://example.invalid/repo.json");
    }

    /// <summary>A check declines without NoireLib instead of faulting on the thread pool.</summary>
    [Fact]
    public async Task CheckForUpdatesNowAsync_WithoutAnInitializedNoireLib_CompletesWithoutThrowing()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");

        var act = async () => await tracker.CheckForUpdatesNowAsync();

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Manual checks

    [Fact]
    public async Task CheckForUpdatesNowAsync_WhileInactive_CompletesWithoutThrowing()
    {
        var tracker = MakeTracker(active: false, repoUrl: "https://example.invalid/repo.json");

        var act = async () => await tracker.CheckForUpdatesNowAsync();

        await act.Should().NotThrowAsync("a manual check on an inactive module is a no-op, not an error");
    }

    [Fact]
    public async Task CheckForUpdatesNowAsync_WithNoRepoUrl_CompletesWithoutThrowing()
    {
        var tracker = MakeTracker(active: true);

        var act = async () => await tracker.CheckForUpdatesNowAsync();

        await act.Should().NotThrowAsync("there is nothing to fetch, which is not a failure");
    }

    /// <summary>A caller awaits this task to re-enable its control: it must complete, never fault.</summary>
    [Fact]
    public async Task CheckForUpdatesNowAsync_ReturnsATaskThatCompletes()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");

        var check = tracker.CheckForUpdatesNowAsync();
        await check;

        check.IsCompletedSuccessfully.Should().BeTrue(
            "a check reports its own failures through the log, so discarding this task is as safe as awaiting it");
    }

    [Fact]
    public async Task CheckForUpdatesNowAsync_DoesNotDisturbTheCheckTimer()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");
        var scheduled = CheckTimerOf(tracker);

        await tracker.CheckForUpdatesNowAsync();

        CheckTimerOf(tracker).Should().BeSameAs(scheduled, "a manual check must not delay, advance or restart the schedule");
    }

    #endregion

    #region A disposed module

    /// <summary>
    /// A call on a dead module is a no-op, not a failure of the check: the task still completes, which is the contract
    /// a caller awaiting it to re-enable its own control depends on.
    /// </summary>
    [Fact]
    public async Task CheckForUpdatesNowAsync_OnADisposedModule_ReturnsATaskThatCompletes()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");
        tracker.Dispose();

        var check = tracker.CheckForUpdatesNowAsync();
        await check;

        check.IsCompletedSuccessfully.Should().BeTrue(
            "a check on a disposed module declines rather than reaching the torn-down token source and faulting");
    }

    /// <summary>IsActive stays true after disposal. A timer started here would outlive the module.</summary>
    [Fact]
    public void RepoUrl_AssignedOnADisposedModule_StartsNoTimer()
    {
        var tracker = MakeTracker(active: true);
        tracker.Dispose();

        tracker.SetRepoUrl("https://example.invalid/repo.json");

        CheckTimerOf(tracker).Should().BeNull("a timer started after teardown would have nothing left to dispose it");
    }

    [Fact]
    public void SetCheckIntervalMinutes_OnADisposedModule_StartsNoTimer()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");
        tracker.Dispose();

        CheckTimerOf(tracker).Should().BeNull("teardown stops the timer it knew about");

        tracker.SetCheckIntervalMinutes(60);

        CheckTimerOf(tracker).Should().BeNull("a new interval on a dead module has nothing to schedule");
    }

    [Fact]
    public void ResetUpdateNotification_OnADisposedModule_DoesNotThrow()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");
        tracker.Dispose();

        var act = () => tracker.ResetUpdateNotification();

        act.Should().NotThrow();
    }

    /// <summary>A module is disposed by its owner and by the library: the second pass must find nothing to do.</summary>
    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");

        var act = () =>
        {
            tracker.Dispose();
            tracker.Dispose();
        };

        act.Should().NotThrow("teardown is reachable both from a consumer and from the library, and neither can tell that the other already ran");
    }

    [Fact]
    public void Dispose_CalledRepeatedly_LeavesTheModuleDisposed()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");

        tracker.Dispose();
        tracker.Dispose();
        tracker.Dispose();

        CheckTimerOf(tracker).Should().BeNull("teardown stops the timer it knew about, and running again must not schedule a new one");

        tracker.SetRepoUrl("https://another.invalid/repo.json");
        CheckTimerOf(tracker).Should().BeNull("a repeated teardown must leave the latch closed, or the timer paths would start treating a disposed module as a working one again");

        tracker.SetCheckIntervalMinutes(60);
        CheckTimerOf(tracker).Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdatesNowAsync_AfterARepeatedDispose_ReturnsATaskThatCompletes()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");
        tracker.Dispose();
        tracker.Dispose();

        var check = tracker.CheckForUpdatesNowAsync();
        await check;

        check.IsCompletedSuccessfully.Should().BeTrue(
            "a check on a disposed module declines whether teardown ran once or twice");
    }

    /// <summary>
    /// The latch and the cancellation land before the objects they protect are torn down. Pinned at the source: the
    /// order only shows as a race.
    /// </summary>
    [Fact]
    public void DisposeInternal_ShouldGuardOnTheLatchWithoutReorderingTeardown()
    {
        var source = ReadUpdateTrackerSource();

        var body = source[source.IndexOf("protected override void DisposeInternal()", StringComparison.Ordinal)..];
        var guard = body.IndexOf("if (disposed)", StringComparison.Ordinal);
        var latch = body.IndexOf("disposed = true;", StringComparison.Ordinal);
        var cancel = body.IndexOf("disposalTokenSource.Cancel();", StringComparison.Ordinal);
        var stopTimer = body.IndexOf("StopUpdateCheckTimer();", StringComparison.Ordinal);
        var disposeClient = body.IndexOf("httpClient.Dispose();", StringComparison.Ordinal);
        var disposeSource = body.IndexOf("disposalTokenSource.Dispose();", StringComparison.Ordinal);

        guard.Should().BeGreaterThan(0, "a second teardown must return rather than cancel a token source it already disposed")
            .And.BeLessThan(latch, "the guard reads the latch, so it can only work ahead of the statement that sets it");

        latch.Should().BeLessThan(cancel,
            "the latch closes the paths that would start a check or a timer, including one racing this teardown");

        cancel.Should().BeLessThan(stopTimer, "a callback the timer already started is not waited for by disposing the timer")
            .And.BeLessThan(disposeClient, "a check suspended on the HTTP call must be called off before the client it would resume against is disposed")
            .And.BeLessThan(disposeSource, "cancelling a disposed token source throws");
    }

    #endregion

    #region The notification gate

    /// <summary>
    /// A detection delivered nowhere must leave the gate open: latching here would stop every future check on the
    /// strength of a notification that was never shown.
    /// </summary>
    [Fact]
    public void ApplyUpdateDetected_WithEveryChannelDisabled_LeavesTheGateOpen()
    {
        var tracker = MakeTracker();

        tracker.ApplyUpdateDetected(CurrentVersion, RemoteVersion);

        tracker.HasShownUpdateNotification.Should().BeFalse(
            "no notification, chat message or event carried the detection, so the gate has nothing to close on");
    }

    [Fact]
    public void ApplyUpdateDetected_WithAnEventBus_DeliversTheDetectionAndClosesTheGate()
    {
        var (tracker, detections) = MakeEventBusTracker();

        tracker.ApplyUpdateDetected(CurrentVersion, RemoteVersion);

        detections.Should().ContainSingle();
        detections[0].CurrentVersion.Should().Be(CurrentVersion);
        detections[0].NewVersion.Should().Be(RemoteVersion);

        tracker.HasShownUpdateNotification.Should().BeTrue(
            "a subscriber received the detection and decides what to present, which is a delivery like any other");
    }

    [Fact]
    public void ApplyUpdateDetected_ThatReachesNobodyAfterOneThatDid_KeepsTheGateClosed()
    {
        var (tracker, _) = MakeEventBusTracker();
        tracker.ApplyUpdateDetected(CurrentVersion, RemoteVersion);
        tracker.HasShownUpdateNotification.Should().BeTrue();

        tracker.EventBus = null;
        tracker.ApplyUpdateDetected(CurrentVersion, RemoteVersion);

        tracker.HasShownUpdateNotification.Should().BeTrue();
    }

    [Fact]
    public void DetectionReachesAChannel_WithEveryChannelDisabled_IsFalse()
    {
        NoireUpdateTracker.DetectionReachesAChannel(hasEventBus: false, showsNotification: false, printsInChat: false)
            .Should().BeFalse();
    }

    [Fact]
    public void DetectionReachesAChannel_WithEventBusOnly_IsTrue()
    {
        NoireUpdateTracker.DetectionReachesAChannel(hasEventBus: true, showsNotification: false, printsInChat: false)
            .Should().BeTrue(
                "a subscriber receives the detection and decides what to present, which is a delivery like any other");
    }

    [Fact]
    public void DetectionReachesAChannel_WithNotificationOnly_IsTrue()
    {
        NoireUpdateTracker.DetectionReachesAChannel(hasEventBus: false, showsNotification: true, printsInChat: false)
            .Should().BeTrue();
    }

    [Fact]
    public void DetectionReachesAChannel_WithChatOnly_IsTrue()
    {
        NoireUpdateTracker.DetectionReachesAChannel(hasEventBus: false, showsNotification: false, printsInChat: true)
            .Should().BeTrue();
    }

    [Fact]
    public void DetectionReachesAChannel_WithEveryChannelEnabled_IsTrue()
    {
        NoireUpdateTracker.DetectionReachesAChannel(hasEventBus: true, showsNotification: true, printsInChat: true)
            .Should().BeTrue();
    }

    #endregion

    #region Reopening the notification gate

    [Fact]
    public void ResetUpdateNotification_ReopensTheGate()
    {
        var (tracker, _) = MakeEventBusTracker();
        tracker.ApplyUpdateDetected(CurrentVersion, RemoteVersion);
        tracker.HasShownUpdateNotification.Should().BeTrue();

        tracker.ResetUpdateNotification();

        tracker.HasShownUpdateNotification.Should().BeFalse();
    }

    [Fact]
    public void ResetUpdateNotification_ReturnsTheModuleForChaining()
    {
        var tracker = MakeTracker();

        tracker.ResetUpdateNotification().Should().BeSameAs(tracker);
    }

    /// <summary>
    /// What was shown was an update to the plugin as the previous repository described it, which says nothing
    /// about what a different one offers.
    /// </summary>
    [Fact]
    public void RepoUrl_ChangedToADifferentRepository_ReopensTheGate()
    {
        var (tracker, _) = MakeEventBusTracker();
        tracker.SetRepoUrl("https://example.invalid/repo.json");
        tracker.ApplyUpdateDetected(CurrentVersion, RemoteVersion);
        tracker.HasShownUpdateNotification.Should().BeTrue();

        tracker.SetRepoUrl("https://another.invalid/repo.json");

        tracker.HasShownUpdateNotification.Should().BeFalse(
            "a notification shown for one repository cannot be the reason to stop checking another");
    }

    /// <summary>
    /// A consumer writing its configured URL every frame must not reopen the gate every frame, which would turn
    /// ShouldStopNotifyingAfterFirstNotification into a notification per check.
    /// </summary>
    [Fact]
    public void RepoUrl_AssignedTheValueItAlreadyHolds_LeavesTheGateClosed()
    {
        var (tracker, _) = MakeEventBusTracker();
        tracker.SetRepoUrl("https://example.invalid/repo.json");
        tracker.ApplyUpdateDetected(CurrentVersion, RemoteVersion);
        tracker.HasShownUpdateNotification.Should().BeTrue();

        tracker.SetRepoUrl("https://example.invalid/repo.json");

        tracker.HasShownUpdateNotification.Should().BeTrue("the repository did not change, so nothing was invalidated");
    }

    [Fact]
    public async Task ResetUpdateNotification_ChainedIntoAManualCheck_Completes()
    {
        var (tracker, _) = MakeEventBusTracker();
        tracker.SetRepoUrl("https://example.invalid/repo.json");
        tracker.SetActive(true);
        tracker.ApplyUpdateDetected(CurrentVersion, RemoteVersion);

        tracker.ShouldStopNotifyingAfterFirstNotification.Should().BeTrue("this is the default the gate is about");
        tracker.HasShownUpdateNotification.Should().BeTrue();

        var act = async () => await tracker.ResetUpdateNotification().CheckForUpdatesNowAsync();

        await act.Should().NotThrowAsync();
        tracker.HasShownUpdateNotification.Should().BeFalse(
            "the check found nothing to deliver, so it must not have closed the gate the reset just opened");
    }

    #endregion

    #region The check timer

    [Fact]
    public void CheckTimer_WithNoRepoUrl_StaysStopped()
    {
        var tracker = MakeTracker(active: true);

        CheckTimerOf(tracker).Should().BeNull();
    }

    [Fact]
    public void CheckTimer_WhenARepoUrlIsAssignedOnAnActiveModule_Starts()
    {
        var tracker = MakeTracker(active: true);

        tracker.SetRepoUrl("https://example.invalid/repo.json");

        CheckTimerOf(tracker).Should().NotBeNull();
    }

    [Fact]
    public void CheckTimer_WhenTheRepoUrlIsCleared_StopsAgain()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");
        CheckTimerOf(tracker).Should().NotBeNull();

        tracker.SetRepoUrl(string.Empty);

        CheckTimerOf(tracker).Should().BeNull();
    }

    [Fact]
    public void CheckTimer_WhileInactive_StaysStopped()
    {
        var tracker = MakeTracker(active: false, repoUrl: "https://example.invalid/repo.json");

        CheckTimerOf(tracker).Should().BeNull();

        tracker.SetActive(true);
        CheckTimerOf(tracker).Should().NotBeNull();

        tracker.SetActive(false);
        CheckTimerOf(tracker).Should().BeNull();
    }

    [Fact]
    public void CheckTimer_OnEveryReconfiguration_IsRestarted()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");

        var onActivation = CheckTimerOf(tracker);
        onActivation.Should().NotBeNull();

        tracker.SetRepoUrl("https://another.invalid/repo.json");
        var afterUrlChange = CheckTimerOf(tracker);
        afterUrlChange.Should().NotBeSameAs(onActivation, "a new repository must not wait out the running interval");

        tracker.SetCheckIntervalMinutes(60);
        var afterIntervalChange = CheckTimerOf(tracker);
        afterIntervalChange.Should().NotBeSameAs(afterUrlChange, "a new interval applies from the next check, not the current one");
    }

    /// <summary>
    /// An assignment that changes nothing must not restart the countdown, or a consumer writing its configured URL
    /// every frame would push the first check back forever.
    /// </summary>
    [Fact]
    public void CheckTimer_WhenTheRepoUrlIsAssignedTheValueItAlreadyHolds_IsNotRestarted()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");
        var before = CheckTimerOf(tracker);

        tracker.SetRepoUrl("https://example.invalid/repo.json");

        CheckTimerOf(tracker).Should().BeSameAs(before);
    }

    /// <summary>Start and stop swap the timer under one lock: a racing pair leaves at most one timer.</summary>
    [Fact]
    public void CheckTimer_StartedConcurrently_EndsWithOneTimerThatAStopRemoves()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");

        Parallel.For(0, 400, i => tracker.SetCheckIntervalMinutes(30 + (i % 5)));

        CheckTimerOf(tracker).Should().NotBeNull();

        tracker.Dispose();
        CheckTimerOf(tracker).Should().BeNull("disposal must stop the timer the last start installed");
    }

    /// <summary>
    /// Pinned at the source because a timer a racing start leaked is unreferenced and has no game-free effect to
    /// observe: both paths swap the field under the same lock, and a replaced timer's running callback is waited out.
    /// </summary>
    [Fact]
    public void StartAndStopUpdateCheckTimer_ShouldSwapTheTimerUnderOneLockAndWaitOutTheCallback()
    {
        var source = ReadUpdateTrackerSource();

        var start = source[source.IndexOf("private void StartUpdateCheckTimer()", StringComparison.Ordinal)..];
        start = start[..start.IndexOf("private void RunScheduledCheck()", StringComparison.Ordinal)];
        var stop = source[source.IndexOf("private void StopUpdateCheckTimer()", StringComparison.Ordinal)..];
        stop = stop[..stop.IndexOf("private static void DisposeTimerAndWait", StringComparison.Ordinal)];

        start.Should().Contain("lock (timerLock)").And.Contain("DisposeTimerAndWait(replaced);");
        stop.Should().Contain("lock (timerLock)").And.Contain("DisposeTimerAndWait(timer);");
        source.Should().Contain("if (timer.Dispose(timerDrained))",
            "Timer.Dispose() returns while a callback may still be running; only the wait-handle overload waits it out");
        source.Should().NotContain("async _ =>", "an async timer callback is async void, and a fault in it terminates the process");
    }

    /// <summary>
    /// The timer callback is synchronous and starts the check; its safety must not depend on the check happening to
    /// catch everything.
    /// </summary>
    [Fact]
    public async Task RunScheduledCheck_WithAFaultingCheck_NeverFaults()
    {
        var tracker = MakeTracker();

        var faultedTask = tracker.RunScheduledCheck(() => Task.FromException(new InvalidOperationException("check fault")));
        await faultedTask;
        faultedTask.IsCompletedSuccessfully.Should().BeTrue();

        Task? thrownTask = null;
        Action act = () => thrownTask = tracker.RunScheduledCheck(() => throw new InvalidOperationException("synchronous fault"));

        act.Should().NotThrow("an exception escaping a timer callback is unhandled on the thread pool and terminates the game");
        await thrownTask!;
        thrownTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    #endregion

    #region The check start delay

    [Fact]
    public void CheckStartDelayMs_Default_IsTwoSeconds()
    {
        MakeTracker().CheckStartDelayMs.Should().Be(2000);
    }

    [Fact]
    public void CheckStartDelayMs_SetToZero_IsAccepted()
    {
        var tracker = MakeTracker();

        tracker.SetCheckStartDelayMs(0).CheckStartDelayMs.Should().Be(0);
    }

    [Fact]
    public void CheckStartDelayMs_SetToANegativeValue_Throws()
    {
        var tracker = MakeTracker();

        var act = () => tracker.SetCheckStartDelayMs(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// The first check waits the start delay, not the interval. Pinned at the source: a Timer does not expose its due time.
    /// </summary>
    [Fact]
    public void StartUpdateCheckTimer_ShouldScheduleTheFirstCheckAfterTheCheckStartDelay()
    {
        var source = ReadUpdateTrackerSource();

        source.Should().Contain(
            """
            updateCheckTimer = new Timer(static state => ((NoireUpdateTracker)state!).RunScheduledCheck(),
                            this,
                            TimeSpan.FromMilliseconds(CheckStartDelayMs),
                            TimeSpan.FromMinutes(CheckIntervalMinutes));
            """.Replace("\r\n", "\n"),
            "the first check must be due after the settle delay, and only the repeat must be due after the interval");
    }

    #endregion

    #region Framework thread marshalling

    /// <summary>Consequences reach the framework thread in one hop. The HTTP call stays off it.</summary>
    [Fact]
    public void CheckForUpdate_ShouldMarshalItsConsequencesOntoTheFrameworkThread()
    {
        var source = ReadUpdateTrackerSource();

        source.Should().Contain(
            "await AsyncHelper.RunOnFrameworkThreadAsync(() => ApplyUpdateDetected(currentVersion, remoteVersion))",
            "the whole consequence block must reach the framework thread through AsyncHelper in a single hop");

        source.Should().Contain("httpClient.SendAsync(req, disposalToken)",
            "the request must observe the disposal token so teardown does not leave a check in flight");

        source.Should().NotContain("Framework.RunOnFrameworkThread",
            "framework thread hops go through AsyncHelper rather than being hand-rolled");
    }

    /// <summary>The check runs from a TimerCallback: an escaping exception would terminate the process.</summary>
    [Fact]
    public void CheckForUpdate_ShouldKeepItsWholeBodyInsideTheErrorBoundary()
    {
        var source = ReadUpdateTrackerSource();

        var body = source[source.IndexOf("private async Task CheckForUpdateAsync()", StringComparison.Ordinal)..];
        var firstGuard = body.IndexOf("if (!IsActive", StringComparison.Ordinal);
        var openingTry = body.IndexOf("\n        try\n", StringComparison.Ordinal);

        firstGuard.Should().BeGreaterThan(0, "the check must still guard on the module being active");
        openingTry.Should().BeGreaterThan(0).And.BeLessThan(firstGuard,
            "every statement of the check, including the guards and the disposal token read, must sit inside the try");

        body.Should().Contain("catch (OperationCanceledException)",
            "a check cancelled by disposal must exit silently rather than log an error");
    }

    /// <summary>
    /// A disposed module declines before reading its disposed token source, inside the try like every statement.
    /// </summary>
    [Fact]
    public void CheckForUpdate_ShouldDeclineOnADisposedModuleBeforeReadingTheDisposalToken()
    {
        var source = ReadUpdateTrackerSource();

        var body = source[source.IndexOf("private async Task CheckForUpdateAsync()", StringComparison.Ordinal)..];
        var openingTry = body.IndexOf("\n        try\n", StringComparison.Ordinal);
        var disposedGuard = body.IndexOf("if (disposed)", StringComparison.Ordinal);
        var tokenRead = body.IndexOf("var disposalToken = disposalTokenSource.Token;", StringComparison.Ordinal);

        tokenRead.Should().BeGreaterThan(0, "the check must still read the disposal token before its first await");
        disposedGuard.Should().BeGreaterThan(openingTry, "the guard must sit inside the error boundary")
            .And.BeLessThan(tokenRead, "reading a disposed source's token throws, which is what the guard exists to avoid");

        body.Should().Contain("catch (ObjectDisposedException)",
            "teardown landing between the guard and the token read is the same benign case, not a failed check");
    }

    #endregion

    #region Repository response parsing

    [Fact]
    public void ParseRepositoryResponse_WithWellFormedArray_ReadsEntries()
    {
        var json = """
        [
            { "InternalName": "SomePlugin", "AssemblyVersion": "1.2.3.4" },
            { "InternalName": "OtherPlugin", "AssemblyVersion": "0.1.0.0" }
        ]
        """;

        var entries = NoireUpdateTracker.ParseRepositoryResponse(json);

        entries.Should().NotBeNull();
        entries!.Should().HaveCount(2);
        entries![0].InternalName.Should().Be("SomePlugin");
        entries![0].AssemblyVersion.Should().Be("1.2.3.4");
    }

    [Fact]
    public void ParseRepositoryResponse_WithEmptyArray_ReadsNoEntries()
    {
        var entries = NoireUpdateTracker.ParseRepositoryResponse("[]");

        entries.Should().NotBeNull();
        entries!.Should().BeEmpty();
    }

    [Fact]
    public void ParseRepositoryResponse_WithTrailingContent_Throws()
    {
        var act = () => NoireUpdateTracker.ParseRepositoryResponse("[] {}");

        act.Should().Throw<Exception>();
    }

    #endregion
}
