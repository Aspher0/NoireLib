using NoireLib.Enums;
using System;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// A class containing methods to execute actions safely, with automatic logging and various features.
/// </summary>
public static class SafeExecutor
{
    /// <summary>
    /// Executes the provided action safely, catching and logging any exceptions that occur.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    public static void ExecuteSafely(Action action)
    {
        ExecuteSafely(action, ExceptionBehavior.LogAndContinue);
    }

    /// <summary>
    /// Executes the provided action safely with configurable exception behavior.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="behavior">The behavior to apply when an exception occurs.</param>
    public static void ExecuteSafely(Action action, ExceptionBehavior behavior)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            HandleException(ex, behavior, "An error occurred while executing a safe action.");
        }
    }

    /// <summary>
    /// Executes the provided function safely, catching and logging any exceptions that occur.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <param name="defaultValue">The default value to return if an exception occurs.</param>
    /// <returns>The result of the function, or the default value if an exception occurs.</returns>
    public static T? ExecuteSafely<T>(Func<T> func, T? defaultValue = default)
    {
        return ExecuteSafely(func, defaultValue, ExceptionBehavior.LogAndContinue);
    }

    /// <summary>
    /// Executes the provided function safely with configurable exception behavior.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <param name="defaultValue">The default value to return if an exception occurs.</param>
    /// <param name="behavior">The behavior to apply when an exception occurs.</param>
    /// <returns>The result of the function, or the default value if an exception occurs.</returns>
    public static T? ExecuteSafely<T>(Func<T> func, T? defaultValue, ExceptionBehavior behavior)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            HandleException(ex, behavior, "An error occurred while executing a safe function.");
            return defaultValue;
        }
    }

    /// <summary>
    /// Executes the provided asynchronous action safely, catching and logging any exceptions that occur.
    /// </summary>
    /// <param name="asyncAction">The asynchronous action to execute.</param>
    public static async Task ExecuteSafelyAsync(Func<Task> asyncAction)
    {
        await ExecuteSafelyAsync(asyncAction, ExceptionBehavior.LogAndContinue);
    }

    /// <summary>
    /// Executes the provided asynchronous action safely with configurable exception behavior.
    /// </summary>
    /// <param name="asyncAction">The asynchronous action to execute.</param>
    /// <param name="behavior">The behavior to apply when an exception occurs.</param>
    public static async Task ExecuteSafelyAsync(Func<Task> asyncAction, ExceptionBehavior behavior)
    {
        try
        {
            await asyncAction();
        }
        catch (Exception ex)
        {
            HandleException(ex, behavior, "An error occurred while executing a safe async action.");
        }
    }

    /// <summary>
    /// Executes the provided asynchronous function safely, catching and logging any exceptions that occur.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="asyncFunc">The asynchronous function to execute.</param>
    /// <param name="defaultValue">The default value to return if an exception occurs.</param>
    /// <returns>The result of the function, or the default value if an exception occurs.</returns>
    public static async Task<T?> ExecuteSafelyAsync<T>(Func<Task<T>> asyncFunc, T? defaultValue = default)
    {
        return await ExecuteSafelyAsync(asyncFunc, defaultValue, ExceptionBehavior.LogAndContinue);
    }

    /// <summary>
    /// Executes the provided asynchronous function safely with configurable exception behavior.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="asyncFunc">The asynchronous function to execute.</param>
    /// <param name="defaultValue">The default value to return if an exception occurs.</param>
    /// <param name="behavior">The behavior to apply when an exception occurs.</param>
    /// <returns>The result of the function, or the default value if an exception occurs.</returns>
    public static async Task<T?> ExecuteSafelyAsync<T>(Func<Task<T>> asyncFunc, T? defaultValue, ExceptionBehavior behavior)
    {
        try
        {
            return await asyncFunc();
        }
        catch (Exception ex)
        {
            HandleException(ex, behavior, "An error occurred while executing a safe async function.");
            return defaultValue;
        }
    }

    /// <summary>
    /// Executes the provided action safely and returns whether it succeeded.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <returns>True if the action succeeded, false if an exception occurred.</returns>
    public static bool TryExecute(Action action)
    {
        return TryExecute(action, ExceptionBehavior.LogAndContinue);
    }

    /// <summary>
    /// Executes the provided action safely and returns whether it succeeded, with configurable exception behavior.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="behavior">The behavior to apply when an exception occurs.</param>
    /// <returns>True if the action succeeded, false if an exception occurred.</returns>
    public static bool TryExecute(Action action, ExceptionBehavior behavior)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            HandleException(ex, behavior, "An error occurred while trying to execute an action.");
            return false;
        }
    }

    /// <summary>
    /// Executes the provided function safely and returns whether it succeeded, along with the result.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <param name="result">The result of the function if successful, otherwise default.</param>
    /// <returns>True if the function succeeded, false if an exception occurred.</returns>
    public static bool TryExecute<T>(Func<T> func, out T? result)
    {
        return TryExecute(func, out result, ExceptionBehavior.LogAndContinue);
    }

    /// <summary>
    /// Executes the provided function safely and returns whether it succeeded, along with the result, with configurable exception behavior.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <param name="result">The result of the function if successful, otherwise default.</param>
    /// <param name="behavior">The behavior to apply when an exception occurs.</param>
    /// <returns>True if the function succeeded, false if an exception occurred.</returns>
    public static bool TryExecute<T>(Func<T> func, out T? result, ExceptionBehavior behavior)
    {
        try
        {
            result = func();
            return true;
        }
        catch (Exception ex)
        {
            HandleException(ex, behavior, "An error occurred while trying to execute a function.");
            result = default;
            return false;
        }
    }

    /// <summary>
    /// Handles an exception according to the specified behavior.
    /// </summary>
    /// <param name="ex">The exception to handle.</param>
    /// <param name="behavior">The behavior to apply.</param>
    /// <param name="message">The message to log if logging is enabled.</param>
    private static void HandleException(Exception ex, ExceptionBehavior behavior, string message)
    {
        switch (behavior)
        {
            case ExceptionBehavior.LogAndContinue:
                NoireLogger.LogError(ex, message, typeof(SafeExecutor).Name);
                break;

            case ExceptionBehavior.LogAndThrow:
                NoireLogger.LogError(ex, message, typeof(SafeExecutor).Name);
                throw ex;

            case ExceptionBehavior.Suppress:
                // Do nothing
                break;

            case ExceptionBehavior.Throw:
                throw ex;

            default:
                NoireLogger.LogError(ex, message, typeof(SafeExecutor).Name);
                break;
        }
    }
}
