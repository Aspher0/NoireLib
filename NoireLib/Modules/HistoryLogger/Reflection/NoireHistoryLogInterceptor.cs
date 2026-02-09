using Castle.DynamicProxy;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.HistoryLogger;

internal sealed class NoireHistoryLogInterceptor : IInterceptor
{
    private readonly NoireHistoryLogger logger;
    private readonly bool logAllMethods;
    private readonly string? defaultCategory;
    private readonly ConcurrentDictionary<MethodInfo, NoireLogAttribute?> attributeCache = new();

    public NoireHistoryLogInterceptor(NoireHistoryLogger logger, bool logAllMethods, string? defaultCategory)
    {
        this.logger = logger;
        this.logAllMethods = logAllMethods;
        this.defaultCategory = defaultCategory;
    }

    public void Intercept(IInvocation invocation)
    {
        try
        {
            invocation.Proceed();
            LogInvocation(invocation, null);
        }
        catch (Exception ex)
        {
            LogInvocation(invocation, ex);
            throw;
        }
    }

    private void LogInvocation(IInvocation invocation, Exception? exception)
    {
        var method = invocation.MethodInvocationTarget ?? invocation.Method;
        var attribute = GetAttribute(method);

        if (!logAllMethods && attribute == null)
            return;

        var category = attribute?.Category ?? defaultCategory ?? method.DeclaringType?.Name ?? "General";
        var level = attribute?.Level ?? (exception == null ? HistoryLogLevel.Info : HistoryLogLevel.Error);

        var message = attribute?.Message;
        if (string.IsNullOrWhiteSpace(message))
            message = BuildDefaultMessage(method, exception != null);

        if (attribute?.IncludeArguments == true)
            message = AppendArguments(message, invocation.Arguments);

        if (exception != null)
            message = $"{message}: {exception.GetType().Name} - {exception.Message}";

        var entry = new HistoryLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Category = category,
            Level = level,
            Message = message,
            Source = method.DeclaringType?.FullName
        };

        logger.AddEntry(entry);
    }

    private NoireLogAttribute? GetAttribute(MethodInfo method)
    {
        return attributeCache.GetOrAdd(method, static info =>
        {
            var attribute = info.GetCustomAttribute<NoireLogAttribute>(true);
            if (attribute != null)
                return attribute;

            return info.DeclaringType?.GetCustomAttribute<NoireLogAttribute>(true);
        });
    }

    private static string BuildDefaultMessage(MethodInfo method, bool isException)
    {
        var typeName = method.DeclaringType?.Name ?? "Unknown";
        var name = method.IsSpecialName ? method.Name.Replace("get_", string.Empty).Replace("set_", string.Empty) : method.Name;
        var action = isException ? "failed" : "invoked";
        return $"{typeName}.{name} {action}";
    }

    private static string AppendArguments(string message, IReadOnlyList<object?> arguments)
    {
        if (arguments.Count == 0)
            return message;

        var formatted = string.Join(", ", arguments.Select(arg => arg == null ? "null" : arg.ToString()));
        return $"{message} ({formatted})";
    }
}
