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
/// Game-free tests for the NoireUpdateTracker module.<br/>
/// They lock three invariants. First, the
/// <see cref="NoireUpdateTracker.ShouldStopNotifyingAfterFirstNotification"/> gate closes only when a detected update
/// actually reached a notification channel, so a tracker with every channel switched off keeps checking rather than
/// silencing itself over a detection nobody was told about. Second, the gate reopens when the thing it closed over stops
/// applying: what was shown was an update from one repository, so a different <see cref="NoireUpdateTracker.RepoUrl"/>
/// cannot be silenced by it. Third, the check timer is stopped exactly while there is nothing to fetch, and every start
/// of it restarts the countdown to the first check.<br/><br/>
/// A disposed module is the fourth: disposal does not deactivate the module, so nothing about its active state says it
/// is gone, and every path that would start a check or a timer has to recognize that for itself rather than resurrect a
/// module whose HTTP client and disposal token source are already torn down. Teardown itself is one of those paths, and
/// it runs more than once whenever a consumer disposes the module it owns and the library also tears its modules down,
/// so it has to run once and leave the latch closed however often it is called.<br/><br/>
/// The module is constructible without the game: neither initializing nor activating it needs an initialized NoireLib,
/// and a check declines and says so while there is not one. The delivery path is therefore driven directly through
/// <see cref="NoireUpdateTracker.ApplyUpdateDetected(Version, Version)"/>, with the event bus as the channel, since the
/// other two are Dalamud services and the event bus is the one a game-free test can observe.
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
                // Best effort cleanup.
            }
        }
    }

    /// <summary>
    /// Builds a silent tracker and registers it for disposal, so that no check timer outlives its test.
    /// </summary>
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

    /// <summary>
    /// The timer instance the module is currently scheduling checks on, or null while it is stopped.
    /// </summary>
    private static Timer? CheckTimerOf(NoireUpdateTracker tracker)
        => (Timer?)typeof(NoireUpdateTracker)
            .GetField("updateCheckTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(tracker);

    /// <summary>
    /// Reads the module source with line endings normalized, so that assertions spanning a line break do not depend on
    /// how the repository happens to be checked out.
    /// </summary>
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

    /// <summary>
    /// The module records how it should behave and starts a timer; neither needs a Dalamud service. Requiring one in
    /// order to exist would put every rule below out of reach of a test.
    /// </summary>
    [Fact]
    public void Constructor_WithoutAnInitializedNoireLib_Succeeds()
    {
        NoireService.IsInitialized().Should().BeFalse("these tests are the game-free case by construction");

        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");

        tracker.IsActive.Should().BeTrue();
        tracker.RepoUrl.Should().Be("https://example.invalid/repo.json");
    }

    /// <summary>
    /// A check is the part that genuinely needs NoireLib, and it declines rather than throwing, which is what keeps an
    /// uninitialized library from turning into an unobserved exception on the thread pool.
    /// </summary>
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

    /// <summary>
    /// The returned task is what a caller awaits to re-enable the control that started the check, so it must complete
    /// rather than fault whatever the check ran into.
    /// </summary>
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

    /// <summary>
    /// Disposal leaves <see cref="NoireUpdateTracker.IsActive"/> true, so the active state cannot stand in for a
    /// disposed one. A timer started from here would outlive the module that owns it: teardown has already disposed the
    /// timer it knew about, so nothing would ever dispose this one and it would go on waking every interval to run
    /// checks against a disposed HTTP client.
    /// </summary>
    [Fact]
    public void RepoUrl_AssignedOnADisposedModule_StartsNoTimer()
    {
        var tracker = MakeTracker(active: true);
        tracker.Dispose();

        tracker.IsActive.Should().BeTrue(
            "disposal does not deactivate the module, which is exactly why the timer paths need a disposed guard of their own");

        tracker.SetRepoUrl("https://example.invalid/repo.json");

        CheckTimerOf(tracker).Should().BeNull("a timer started after teardown would have nothing left to dispose it");
    }

    /// <summary>
    /// The interval setter restarts the timer for the same reason the URL setter does, so it resurrects a disposed
    /// module the same way.
    /// </summary>
    [Fact]
    public void SetCheckIntervalMinutes_OnADisposedModule_StartsNoTimer()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");
        tracker.Dispose();

        CheckTimerOf(tracker).Should().BeNull("teardown stops the timer it knew about");

        tracker.SetCheckIntervalMinutes(60);

        CheckTimerOf(tracker).Should().BeNull("a new interval on a dead module has nothing to schedule");
    }

    /// <summary>
    /// Reopening the gate touches nothing that teardown disposes, so it stays a plain no-op rather than throwing at a
    /// consumer that resets a module it has already torn down.
    /// </summary>
    [Fact]
    public void ResetUpdateNotification_OnADisposedModule_DoesNotThrow()
    {
        var tracker = MakeTracker(active: true, repoUrl: "https://example.invalid/repo.json");
        tracker.Dispose();

        var act = () => tracker.ResetUpdateNotification();

        act.Should().NotThrow();
    }

    #endregion

    #region The notification gate

    /// <summary>
    /// The regression gate, driven through the delivery path itself. A detection delivered nowhere must leave the gate
    /// open: latching here would stop every future check on the strength of a notification that was never shown.
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

    /// <summary>
    /// The latch is only ever set by a delivery. A later detection that reaches nobody must not reopen a gate an
    /// earlier delivery closed.
    /// </summary>
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

    /// <summary>
    /// A closed gate stops every further check. Without a way back it would be a one-shot per session, which is wrong
    /// the moment the tracker is pointed somewhere else.
    /// </summary>
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
    /// The core of the latch fix: what was shown was an update to the plugin as the previous repository described it,
    /// which says nothing about what a different one offers.
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

    /// <summary>
    /// Reset-then-check is the documented way to report a still-pending update again on demand, so the two must
    /// compose into one statement and the check must survive being started from a reopened gate.
    /// </summary>
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

    /// <summary>
    /// Every check would fetch nothing, so the timer must not wake on the interval to do nothing.
    /// </summary>
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

    /// <summary>
    /// Reconfiguring replaces the timer, which is what restarts the countdown to the first check. It is the whole
    /// mechanism behind both halves of the timing rule: a new repository is checked promptly instead of at the end of
    /// the interval that was already running, and a run of changes settles into one check instead of one request each.
    /// </summary>
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
        // Zero is the documented way to opt out of the settle window and check the moment the timer starts.
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
    /// The due time is the delay rather than the check interval, which is what stops a reconfiguration from waiting up
    /// to a full interval before it takes effect.<br/>
    /// Pinned at the source because a <see cref="Timer"/> does not expose the due time it was given, and a check has no
    /// game-free effect to observe it by: without an initialized NoireLib it declines and returns.
    /// </summary>
    [Fact]
    public void StartUpdateCheckTimer_ShouldScheduleTheFirstCheckAfterTheCheckStartDelay()
    {
        var source = ReadUpdateTrackerSource();

        source.Should().Contain(
            """
            updateCheckTimer = new Timer(async _ => await CheckForUpdateAsync(),
                        null,
                        TimeSpan.FromMilliseconds(CheckStartDelayMs),
                        TimeSpan.FromMinutes(CheckIntervalMinutes));
            """.Replace("\r\n", "\n"),
            "the first check must be due after the settle delay, and only the repeat must be due after the interval");
    }

    #endregion

    #region Framework thread marshalling

    /// <summary>
    /// The notification manager, the chat log and event bus handlers all expect the framework thread, while the HTTP
    /// call deliberately runs off it so a check cannot stall a frame. The consequences must therefore be marshalled
    /// back, as one hop rather than one per channel.
    /// </summary>
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

    /// <summary>
    /// The check is awaited from a TimerCallback, which makes the calling lambda async void: an exception escaping it
    /// has no caller to observe it and terminates the process rather than failing one check. The public manual check
    /// shares that body, so it inherits the same boundary and cannot fault the task it hands back.
    /// </summary>
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
    /// Reading the token of a disposed <see cref="CancellationTokenSource"/> throws, so a check on a disposed module
    /// has to decline before it gets there. The outer boundary would otherwise turn a call on a dead module into an
    /// error line blaming the network or the repository.<br/>
    /// Pinned at the source alongside the boundary above: the guard's effect is that a misleading error is not logged,
    /// and a log line is not something this suite observes. The guard must also sit inside the try like every other
    /// statement, since the check is awaited from a TimerCallback.
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
        // A repository response is exactly one document. Trailing content means the body is not what it claims to be.
        var act = () => NoireUpdateTracker.ParseRepositoryResponse("[] {}");

        act.Should().Throw<Exception>();
    }

    #endregion
}
