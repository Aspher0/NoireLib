using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// A helper class for system-related operations.
/// </summary>
public static class SystemHelper
{
    /// <summary>
    /// Opens the specified URL in the default web browser.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    public static void OpenUrl(string url)
    {
        try
        {
            if (IsWindows)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            else if (IsLinux)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = url,
                    UseShellExecute = false
                });
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to open URL {url} in the default browser.", "[SystemHelper] ");
        }
    }

    /// <summary>
    /// Opens the specified folder in the file manager.
    /// </summary>
    /// <param name="folderPath">The path to the folder to open.</param>
    public static void OpenFolder(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
            {
                NoireLogger.LogWarning($"Folder does not exist: {folderPath}", "[SystemHelper] ");
                return;
            }

            if (IsWindows)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else if (IsLinux)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = false
                });
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to open folder {folderPath}.", "[SystemHelper] ");
        }
    }

    /// <summary>
    /// Opens the specified file with its default associated application.
    /// </summary>
    /// <param name="filePath">The path to the file to open.</param>
    public static void OpenFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                NoireLogger.LogWarning($"File does not exist: {filePath}", "[SystemHelper] ");
                return;
            }

            if (IsWindows)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            else if (IsLinux)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false
                });
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to open file {filePath}.", "[SystemHelper] ");
        }
    }

    /// <summary>
    /// Opens the file manager and selects the specified file.
    /// </summary>
    /// <param name="filePath">The path to the file to select.</param>
    public static void OpenFileLocation(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                NoireLogger.LogWarning($"File does not exist: {filePath}", "[SystemHelper] ");
                return;
            }

            if (IsWindows)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            else if (IsLinux)
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    OpenFolder(directory);
                }
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to open file location for {filePath}.", "[SystemHelper] ");
        }
    }

    /// <summary>
    /// Checks if the current OS is Windows.
    /// </summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Checks if the current OS is Linux.
    /// </summary>
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>
    /// Checks if the current OS is macOS.
    /// </summary>
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Gets the OS description.
    /// </summary>
    public static string OSDescription => RuntimeInformation.OSDescription;

    /// <summary>
    /// Runs a command in the system shell and returns the output.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="arguments">The command arguments.</param>
    /// <param name="workingDirectory">Optional working directory.</param>
    /// <returns>The command output, or null if failed.</returns>
    public static string? RunCommand(string command, string arguments = "", string? workingDirectory = null)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDirectory))
                processInfo.WorkingDirectory = workingDirectory;

            using var process = Process.Start(processInfo);
            if (process == null)
                return null;

            var output = new StringBuilder();
            output.Append(process.StandardOutput.ReadToEnd());
            output.Append(process.StandardError.ReadToEnd());

            process.WaitForExit();
            return output.ToString();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to run command: {command} {arguments}", "[SystemHelper] ");
            return null;
        }
    }

    /// <summary>
    /// Runs a command in the system shell asynchronously and returns the output.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="arguments">The command arguments.</param>
    /// <param name="workingDirectory">Optional working directory.</param>
    /// <returns>The command output, or null if failed.</returns>
    public static async Task<string?> RunCommandAsync(string command, string arguments = "", string? workingDirectory = null)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDirectory))
                processInfo.WorkingDirectory = workingDirectory;

            using var process = Process.Start(processInfo);
            if (process == null)
                return null;

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = new StringBuilder();
            output.Append(await standardOutputTask);
            output.Append(await standardErrorTask);

            return output.ToString();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to run command asynchronously: {command} {arguments}", "[SystemHelper] ");
            return null;
        }
    }

    /// <summary>
    /// Starts a process without waiting for it to exit.
    /// </summary>
    /// <param name="command">The process to start.</param>
    /// <param name="arguments">The process arguments.</param>
    /// <param name="workingDirectory">Optional working directory.</param>
    /// <returns><see langword="true"/> if the process was started; otherwise, <see langword="false"/>.</returns>
    public static bool StartProcess(string command, string arguments = "", string? workingDirectory = null)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = IsWindows,
                CreateNoWindow = !IsWindows
            };

            if (!string.IsNullOrEmpty(workingDirectory))
                processInfo.WorkingDirectory = workingDirectory;

            using var process = Process.Start(processInfo);
            return process != null;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to start process: {command} {arguments}", "[SystemHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Gets the current process memory usage in bytes.
    /// </summary>
    /// <returns>The memory usage in bytes.</returns>
    public static long GetProcessMemoryUsage()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            return currentProcess.WorkingSet64;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Failed to get process memory usage.", "[SystemHelper] ");
            return -1;
        }
    }

    /// <summary>
    /// Gets the current process CPU time.
    /// </summary>
    /// <returns>The total processor time.</returns>
    public static TimeSpan GetProcessCpuTime()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            return currentProcess.TotalProcessorTime;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Failed to get process CPU time.", "[SystemHelper] ");
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Gets the process uptime (time since the process started).
    /// </summary>
    /// <returns>The uptime as a TimeSpan.</returns>
    public static TimeSpan GetProcessUptime()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            return DateTime.Now - currentProcess.StartTime;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Failed to get process uptime.", "[SystemHelper] ");
            return TimeSpan.Zero;
        }
    }
}
