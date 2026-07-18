using FluentAssertions;
using NoireLib.Core.Modules;
using System;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Covers the module logging helpers on <see cref="NoireModuleBase{TModule}"/> and the
/// <see cref="NoireLogHandler"/> they use.<br/>
/// The property that matters is that a gated interpolated log call
/// (<c>LogInfo</c>/<c>LogDebug</c>/<c>LogVerbose</c>) neither evaluates its interpolation holes nor allocates a
/// string while the module is not logging, which is the whole reason the helpers take an interpolated string
/// handler rather than a plain <see cref="string"/>. The warning, error and fatal helpers are ungated and always
/// report.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireModuleLoggingTests
{
    /// <summary>
    /// A module that routes the protected logging helpers out through public methods so a test can call them, and
    /// counts how many times an interpolation hole was evaluated so a test can prove when the message was built.
    /// </summary>
    private sealed class LoggingModule : NoireModuleBase<LoggingModule>
    {
        public int HoleEvaluations { get; private set; }

        public LoggingModule() : base((string?)null, false, false) { }
        public LoggingModule(bool enableLogging) : base((string?)null, false, enableLogging) { }

        protected override void InitializeModule(params object?[] args) { /* no-op */ }
        protected override void OnActivated() { /* no-op */ }
        protected override void OnDeactivated() { /* no-op */ }
        protected override void DisposeInternal() { /* no-op */ }

        // A side-effecting value used inside an interpolation hole, so a test can observe whether the hole ran.
        private int NextValue() => ++HoleEvaluations;

        public void EmitDebugInterpolated() => LogDebug($"debug value {NextValue()}");
        public void EmitInfoInterpolated() => LogInfo($"info value {NextValue()}");
        public void EmitVerboseInterpolated() => LogVerbose($"verbose value {NextValue()}");

        public void EmitDebugConstant() => LogDebug("constant debug message");

        public void EmitWarning() => LogWarning("a warning");
        public void EmitError() => LogError("an error");
        public void EmitErrorWithException() => LogError(new InvalidOperationException("boom"), "an error with an exception");
        public void EmitFatal() => LogFatal("a fatal message");
        public void EmitFatalWithException() => LogFatal(new InvalidOperationException("boom"), "a fatal message with an exception");
    }

    [Fact]
    public void GatedInterpolatedLog_DoesNotEvaluateHoles_WhenLoggingDisabled()
    {
        var module = new LoggingModule(enableLogging: false);

        module.EmitDebugInterpolated();
        module.EmitInfoInterpolated();
        module.EmitVerboseInterpolated();

        module.HoleEvaluations.Should().Be(0, "an interpolated gated log must not run its holes while the module is not logging, which is the reason the helpers take a handler rather than a string");
    }

    [Fact]
    public void GatedInterpolatedLog_EvaluatesHoles_WhenLoggingEnabled()
    {
        var module = new LoggingModule(enableLogging: true);

        module.EmitDebugInterpolated();
        module.EmitInfoInterpolated();
        module.EmitVerboseInterpolated();

        module.HoleEvaluations.Should().Be(3, "with logging on, the message is built, so each hole runs exactly once");
    }

    [Fact]
    public void GatedInterpolatedLog_AllocatesNothing_WhenLoggingDisabled()
    {
        var module = new LoggingModule(enableLogging: false);

        module.EmitDebugInterpolated(); // warm-up (JIT)

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8; i++)
            module.EmitDebugInterpolated();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0, "a disabled-logging tick must build no string and allocate nothing, which is the gate for this helper");
        module.HoleEvaluations.Should().Be(0);
    }

    [Fact]
    public void NoireLogHandler_ReportsShouldAppend_MatchingEnableLogging()
    {
        var enabled = new LoggingModule(enableLogging: true);
        var disabled = new LoggingModule(enableLogging: false);

        var enabledHandler = new NoireLogHandler(0, 0, enabled, out var enabledShouldAppend);
        enabledShouldAppend.Should().BeTrue("the compiler must be told to append when the module is logging");
        enabledHandler.IsEnabled.Should().BeTrue();
        enabledHandler.ToStringAndClear(); // release the rented buffer

        var disabledHandler = new NoireLogHandler(0, 0, disabled, out var disabledShouldAppend);
        disabledShouldAppend.Should().BeFalse("the compiler must skip every append when the module is not logging");
        disabledHandler.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void NoireLogHandler_ComposesInterpolatedMessage_WhenEnabled()
    {
        var module = new LoggingModule(enableLogging: true);

        var handler = new NoireLogHandler(literalLength: 6, formattedCount: 1, module, out var shouldAppend);
        shouldAppend.Should().BeTrue();
        handler.AppendLiteral("value ");
        handler.AppendFormatted(42);

        handler.ToStringAndClear().Should().Be("value 42", "the handler forwards to the framework interpolation handler, so it must reproduce the interpolated message");
    }

    [Fact]
    public void UngatedLog_DoesNotThrow_RegardlessOfEnableLogging()
    {
        // Warnings, errors and fatals report regardless of EnableLogging. NoireLogger swallows the write while the
        // plugin log is not initialized (the test environment), so the observable contract here is that the helpers
        // are reachable and forward without throwing whether or not the module is logging.
        foreach (var logging in new[] { true, false })
        {
            var module = new LoggingModule(enableLogging: logging);

            var act = () =>
            {
                module.EmitWarning();
                module.EmitError();
                module.EmitErrorWithException();
                module.EmitFatal();
                module.EmitFatalWithException();
            };

            act.Should().NotThrow();
        }
    }

    [Fact]
    public void GatedConstantLog_DoesNotThrow_WhenLoggingDisabled()
    {
        var module = new LoggingModule(enableLogging: false);

        var act = () => module.EmitDebugConstant();

        act.Should().NotThrow("the plain-string gated overload is for constant messages and is a no-op write while logging is off");
    }
}
