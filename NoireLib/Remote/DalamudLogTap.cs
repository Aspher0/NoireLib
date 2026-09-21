using NoireLib.Helpers;
using Serilog.Events;
using System;
using System.Reflection;

namespace NoireLib.Remote;

// Every line Dalamud logs, from every plugin, as its own log window receives it. The console's Dalamud Logs view.
// The sink is internal, reached through DalamudInternals. A handler left on it would call into an unloaded plugin.
internal static class DalamudLogTap
{
    private const string LoggerPrefix = "DalamudLogTap";

    // Most specific first.
    private static readonly string[] SourceProperties = ["Dalamud.PluginName", "Dalamud.ModuleName", "SourceContext"];

    private static readonly object Gate = new();
    private static readonly int ProcessId = Environment.ProcessId;

    private static object? sink;
    private static EventInfo? logLine;
    private static Delegate? handler;

    // A line written while publishing must not publish another forever.
    [ThreadStatic]
    private static bool publishing;

    internal static void Start()
    {
        lock (Gate)
        {
            if (handler != null)
                return;

            var type = DalamudInternals.Resolve(DalamudInternals.LogSinkType);
            var instance = type?.GetProperty("Instance", DalamudInternals.Everything)?.GetValue(null);
            var @event = type?.GetEvent("LogLine", DalamudInternals.Everything);
            var add = @event?.GetAddMethod(true);

            if (instance == null || @event == null || add == null)
            {
                NoireLogger.LogWarning("Dalamud's log sink is not where it was. The console shows no Dalamud log.", LoggerPrefix);
                return;
            }

            var created = new EventHandler<(string, LogEvent)>(OnLine);

            SafeExecutor.ExecuteSafely(() => add.Invoke(instance, [created]));

            sink = instance;
            logLine = @event;
            handler = created;
        }
    }

    internal static void Stop()
    {
        lock (Gate)
        {
            if (handler == null || sink == null || logLine == null)
                return;

            var remove = logLine.GetRemoveMethod(true);
            var removing = handler;
            var from = sink;

            if (remove != null)
                SafeExecutor.ExecuteSafely(() => remove.Invoke(from, [removing]));

            sink = null;
            logLine = null;
            handler = null;
        }
    }

    private static void OnLine(object? sender, (string Line, LogEvent Event) args)
    {
        if (publishing || !NoireRemoteLog.IsTailing)
            return;

        publishing = true;

        try
        {
            var logged = args.Event;

            NoireRemoteLog.Publish(new NoireRemoteLogLine
            {
                Level = LevelOf(logged.Level),
                Message = logged.RenderMessage(),
                AtUtc = logged.Timestamp.ToUniversalTime(),
                Exception = logged.Exception?.ToString(),
                Source = SourceOf(logged),
                ProcessId = ProcessId,
            });
        }
        catch (Exception)
        {
            // A line that cannot be published never costs Dalamud the line.
        }
        finally
        {
            publishing = false;
        }
    }

    private static string SourceOf(LogEvent logged)
    {
        foreach (var name in SourceProperties)
        {
            if (logged.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: string text } && text.Length > 0)
                return text;
        }

        return "Dalamud";
    }

    private static string LevelOf(LogEventLevel level)
        => level switch
        {
            LogEventLevel.Verbose => "verbose",
            LogEventLevel.Debug => "debug",
            LogEventLevel.Warning => "warning",
            LogEventLevel.Error => "error",
            LogEventLevel.Fatal => "fatal",
            _ => "information",
        };
}
