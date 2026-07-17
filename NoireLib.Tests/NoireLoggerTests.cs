using Dalamud.Game.Text;
using FluentAssertions;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for <see cref="NoireLogger"/>.<br/><br/>
/// They lock two invariants. First, nothing on the logger throws at a caller: a log write already swallowed its own
/// failures, and chat output now holds the same contract, so plugin code that logs or prints is not made to guard every
/// call against the library it is logging through. Second, the chat log is only ever handed a message from the framework
/// thread, which is what makes <see cref="NoireLogger.PrintToChat(string, string?, string?, Vector3?, Vector3?)"/> and
/// its overloads callable from a background task, a timer callback or an HTTP continuation.<br/><br/>
/// The honest limit: without the game, <see cref="NoireService.IsInitialized"/> is false, so a print returns at its
/// first guard and the marshalling below it is never reached. These tests therefore cannot observe the hop, and none of
/// them claims to. What they cover behaviorally is the uninitialized path, on the calling thread and on a background
/// thread, for every public print overload. The hop itself is pinned at the source instead, in the same way the check
/// timer's due time is: a thread it takes no game-free path to reach has no game-free effect to assert on.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireLoggerTests
{
    #region Helpers

    /// <summary>
    /// Reads the logger source with line endings normalized, so that assertions spanning a line break do not depend on
    /// how the repository happens to be checked out.
    /// </summary>
    private static string ReadLoggerSource()
        => File.ReadAllText(FindLoggerSourceFile()).Replace("\r\n", "\n");

    private static string FindLoggerSourceFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "NoireLib", "NoireLogger.cs");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate NoireLogger.cs from the test output path.");
    }

    private static int CountOccurrences(string source, string value)
        => Regex.Matches(source, Regex.Escape(value)).Count;

    private static readonly Vector3 MessageColor = new(1f, 0.5f, 0f);

    #endregion

    #region Uninitialized chat output

    /// <summary>
    /// The reachable half of the safety contract. A print made while there is no chat log to print to resolves to
    /// nothing rather than to the null service behind it, which is what lets module and plugin code print without
    /// knowing whether a game is attached.
    /// </summary>
    [Fact]
    public void PrintToChat_WhileNoireLibIsNotInitialized_DoesNotThrow()
    {
        NoireService.IsInitialized().Should().BeFalse("these tests are the game-free case by construction");

        var act = () => NoireLogger.PrintToChat("A message with nowhere to go.");

        act.Should().NotThrow();
    }

    /// <summary>
    /// The call a consumer makes from a Task.Run, a timer callback or an HTTP continuation. Game-free this only reaches
    /// the uninitialized guard, so it pins that the guard is reached from any thread rather than that the message is
    /// marshalled; the marshalling is pinned at the source below.
    /// </summary>
    [Fact]
    public async Task PrintToChat_FromABackgroundThread_DoesNotThrow()
    {
        var act = async () => await Task.Run(() => NoireLogger.PrintToChat("A message from off the framework thread."));

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Every public print overload funnels into the same guard, so every one of them has to survive the uninitialized
    /// case. Asserted overload by overload rather than on the funnel, so that an overload added later without routing
    /// through it is caught here.
    /// </summary>
    [Fact]
    public void PrintToChat_EveryPublicOverload_WhileNoireLibIsNotInitialized_DoesNotThrow()
    {
        var builder = NoireLogger.CreateChatMessageBuilder().AddText("built");

        var act = () =>
        {
            NoireLogger.PrintToChat(builder);
            NoireLogger.PrintToChat(this, builder);
            NoireLogger.PrintToChat("plain");
            NoireLogger.PrintToChat(this, "plain with instance");
            NoireLogger.PrintToChatTagged("<color=#FF0000>tagged</color>");
            NoireLogger.PrintToChatTagged(this, "<color=#FF0000>tagged with instance</color>");
            NoireLogger.PrintToChat(XivChatType.Debug, "typed");
            NoireLogger.PrintToChat(this, XivChatType.Debug, "typed with instance");
            NoireLogger.PrintToChat(XivChatType.Debug, builder);
            NoireLogger.PrintToChat(this, XivChatType.Debug, builder);
            NoireLogger.PrintToChatTagged(XivChatType.Debug, "<glow=#00FF00>typed tagged</glow>");
            NoireLogger.PrintToChatTagged(this, XivChatType.Debug, "<glow=#00FF00>typed tagged with instance</glow>");
            NoireLogger.PrintToChat(XivChatType.Debug, "colored", MessageColor);
            NoireLogger.PrintToChat(this, XivChatType.Debug, "colored with instance", MessageColor);
        };

        act.Should().NotThrow();
    }

    /// <summary>
    /// An empty message is substituted for a blank payload before the entry is built, so the substitution has to stay
    /// on the near side of the guard rather than become a second way for a print to fail.
    /// </summary>
    [Fact]
    public void PrintToChat_WithAnEmptyMessage_DoesNotThrow()
    {
        var act = () => NoireLogger.PrintToChat(string.Empty);

        act.Should().NotThrow();
    }

    #endregion

    #region Log writes

    /// <summary>
    /// The plugin log is Serilog-backed and takes a write from any thread, so the log levels need no marshalling. What
    /// they do need is to stay callable without a plugin log at all, which is the property module code leans on when it
    /// is exercised without the game.
    /// </summary>
    [Fact]
    public async Task LogWrites_FromABackgroundThread_DoNotThrow()
    {
        var act = async () => await Task.Run(() =>
        {
            NoireLogger.LogVerbose("verbose");
            NoireLogger.LogDebug("debug");
            NoireLogger.LogInfo("info");
            NoireLogger.LogWarning("warning");
            NoireLogger.LogError("error");
            NoireLogger.LogError(new InvalidOperationException("boom"), "error with exception");
            NoireLogger.LogFatal("fatal");
            NoireLogger.LogFatal(new InvalidOperationException("boom"), "fatal with exception");
        });

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Framework thread marshalling

    /// <summary>
    /// The chat log is filled by <c>IChatGui.Print</c> and drained on the framework thread through a queue that is not
    /// synchronized, so a message handed over from any other thread races that drain. Every print must therefore reach
    /// the chat log through the one funnel that decides the thread, and no path may reach it directly.
    /// </summary>
    [Fact]
    public void PrintToChat_ShouldReachTheChatLogThroughASingleFunnel()
    {
        var source = ReadLoggerSource();

        CountOccurrences(source, "ChatGui.Print").Should().Be(1,
            "the chat log must have exactly one call site, so that the thread decision cannot be bypassed");

        source.Should().Contain("PrintChatEntry(entry);",
            "the built entry must be handed to the funnel rather than to the chat log");
    }

    /// <summary>
    /// A call that is already on the framework thread must hand its message over inline. Marshalling it anyway would
    /// change every existing correct caller, since a command handler or a UI callback prints from that thread already,
    /// and a multi-line report printed from it depends on its lines being queued in the order they were written.
    /// </summary>
    [Fact]
    public void PrintChatEntry_ShouldHandOverInlineWhenAlreadyOnTheFrameworkThread()
    {
        var source = ReadLoggerSource();

        source.Should().Contain(
            """
            if (NoireService.Framework.IsInFrameworkUpdateThread)
                        {
                            PrintChatEntryToChatLog(entry);
                            return;
                        }
            """.Replace("\r\n", "\n"),
            "a caller already on the framework thread must print without a hop, keeping the call synchronous");
    }

    /// <summary>
    /// The hop goes through the library helper, which is the one place the framework-thread switch is implemented.
    /// </summary>
    [Fact]
    public void PrintChatEntry_ShouldMarshalThroughAsyncHelper()
    {
        var source = ReadLoggerSource();

        source.Should().Contain(
            "_ = AsyncHelper.RunOnFrameworkThreadAsync(() => PrintChatEntryToChatLog(entry));",
            "a print from off the framework thread must be marshalled onto it through AsyncHelper");

        source.Should().NotContain("Framework.RunOnFrameworkThread(",
            "framework thread hops go through AsyncHelper rather than being hand-rolled");
    }

    /// <summary>
    /// The marshalled action is fire-and-forget, so an exception escaping it would fault a task nobody holds rather than
    /// reach a caller. It has to be swallowed on the inside of the hop, where the log is the only thing left to report
    /// to.
    /// </summary>
    [Fact]
    public void PrintChatEntryToChatLog_ShouldSwallowItsOwnFailures()
    {
        var source = ReadLoggerSource();

        var body = source[source.IndexOf("private static void PrintChatEntryToChatLog(", StringComparison.Ordinal)..];

        // The keyword on its own line, rather than the substring the method name itself ends with.
        var openingTry = body.IndexOf("\n        try\n", StringComparison.Ordinal);
        var printCall = body.IndexOf("NoireService.ChatGui.Print(entry);", StringComparison.Ordinal);
        var catchAll = body.IndexOf("catch (Exception ex)", StringComparison.Ordinal);

        openingTry.Should().BeGreaterThan(0, "the chat log call must sit inside an error boundary");
        printCall.Should().BeGreaterThan(openingTry, "the chat log call must be inside the try rather than before it");
        catchAll.Should().BeGreaterThan(printCall, "every failure of the chat log call must be caught");
    }

    /// <summary>
    /// Before initialization there is no chat log and the service behind it is null, so the guard has to come first: a
    /// print made then resolves to nothing rather than to a null dereference at the caller.
    /// </summary>
    [Fact]
    public void PrintChatEntry_ShouldGuardOnInitializationBeforeTouchingAnyService()
    {
        var source = ReadLoggerSource();

        var body = source[source.IndexOf("private static void PrintChatEntry(XivChatEntry entry)", StringComparison.Ordinal)..];
        var guard = body.IndexOf("if (!NoireService.IsInitialized())", StringComparison.Ordinal);
        var frameworkTouch = body.IndexOf("NoireService.Framework", StringComparison.Ordinal);

        guard.Should().BeGreaterThan(0, "the funnel must decline while there is no chat log to print to");
        frameworkTouch.Should().BeGreaterThan(guard,
            "the framework service is only safe to read once NoireLib is initialized, so the guard must precede it");
    }

    #endregion
}
