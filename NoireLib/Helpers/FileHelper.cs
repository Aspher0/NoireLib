using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// A utility class providing file and directory operations for Dalamud plugins.
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Gets the configuration directory path for the current plugin.
    /// </summary>
    /// <returns>The full path to the plugin's configuration directory, or null if NoireLib is not initialized.</returns>
    public static string? GetPluginConfigDirectory()
    {
        if (!NoireService.IsInitialized())
        {
            NoireLogger.LogError("Cannot get config directory path: NoireLib is not initialized.", "[FileHelper] ");
            return null;
        }

        return NoireService.PluginInterface.GetPluginConfigDirectory();
    }

    /// <summary>
    /// Builds a file path within the plugin's configuration directory.
    /// </summary>
    /// <param name="fileName">The file name (including extension).</param>
    /// <returns>The full path to the file in the config directory, or null if NoireLib is not initialized.</returns>
    public static string? GetPluginConfigFilePath(string fileName)
    {
        if (!NoireService.IsInitialized())
        {
            NoireLogger.LogError("Cannot get config directory path: NoireLib is not initialized.", "[FileHelper] ");
            return null;
        }

        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var configDirectory = GetPluginConfigDirectory();
        if (configDirectory == null)
            return null;

        try
        {
            if (!EnsureDirectoryExists(configDirectory))
                return null;

            return Path.Combine(configDirectory, fileName);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to get plugin config file path for: {fileName}", "[FileHelper] ");
            return null;
        }
    }

    /// <summary>
    /// Ensures that a directory exists, creating it if necessary.
    /// </summary>
    /// <param name="directoryPath">The path to the directory.</param>
    /// <returns>True if the directory exists or was created successfully; otherwise, false.</returns>
    public static bool EnsureDirectoryExists(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return false;

        try
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to ensure directory exists: {directoryPath}", "[FileHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Checks if a file exists at the specified path.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <returns>True if the file exists; otherwise, false.</returns>
    public static bool FileExists(string? filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);
    }

    /// <summary>
    /// Checks if a directory exists at the specified path.
    /// </summary>
    /// <param name="directoryPath">The path to the directory.</param>
    /// <returns>True if the directory exists; otherwise, false.</returns>
    public static bool DirectoryExists(string? directoryPath)
    {
        return !string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath);
    }

    /// <summary>
    /// Writes text to a file, creating the directory structure if necessary.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="content">The content to write.</param>
    /// <param name="encoding">Optional encoding to use. Defaults to UTF-8.</param>
    /// <returns>True if the write operation was successful; otherwise, false.</returns>
    public static bool WriteTextToFile(string filePath, string content, Encoding? encoding = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var directory = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(directory) && !EnsureDirectoryExists(directory))
                return false;

            encoding ??= Encoding.UTF8;
            File.WriteAllText(filePath, content, encoding);
            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to write to file: {filePath}", "[FileHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Reads text from a file.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="encoding">Optional encoding to use. Defaults to UTF-8.</param>
    /// <returns>The content of the file, or null if the read operation failed.</returns>
    public static string? ReadTextFromFile(string filePath, Encoding? encoding = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            if (!File.Exists(filePath))
                return null;

            encoding ??= Encoding.UTF8;
            var content = File.ReadAllText(filePath, encoding);
            return content;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to read from file: {filePath}", "[FileHelper] ");
            return null;
        }
    }

    /// <summary>
    /// Deletes a file if it exists.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <returns>True if the file was deleted or doesn't exist; otherwise, false.</returns>
    public static bool DeleteFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to delete file: {filePath}", "[FileHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Deletes a directory if it exists.
    /// </summary>
    /// <param name="directoryPath">The path to the directory.</param>
    /// <param name="recursive">Whether to delete subdirectories and files.</param>
    /// <returns>True if the directory was deleted or doesn't exist; otherwise, false.</returns>
    public static bool DeleteDirectory(string directoryPath, bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return false;

        try
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive);

            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to delete directory: {directoryPath}", "[FileHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Copies a file to a new location.
    /// </summary>
    /// <param name="sourceFilePath">The source file path.</param>
    /// <param name="destinationFilePath">The destination file path.</param>
    /// <param name="overwrite">Whether to overwrite the destination file if it exists.</param>
    /// <returns>True if the copy operation was successful; otherwise, false.</returns>
    public static bool CopyFile(string sourceFilePath, string destinationFilePath, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || string.IsNullOrWhiteSpace(destinationFilePath))
            return false;

        try
        {
            var directory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(directory) && !EnsureDirectoryExists(directory))
                return false;

            File.Copy(sourceFilePath, destinationFilePath, overwrite);
            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to copy file from {sourceFilePath} to {destinationFilePath}", "[FileHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Moves a file to a new location.
    /// </summary>
    /// <param name="sourceFilePath">The source file path.</param>
    /// <param name="destinationFilePath">The destination file path.</param>
    /// <param name="overwrite">Whether to overwrite the destination file if it exists.</param>
    /// <returns>True if the move operation was successful; otherwise, false.</returns>
    public static bool MoveFile(string sourceFilePath, string destinationFilePath, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || string.IsNullOrWhiteSpace(destinationFilePath))
            return false;

        try
        {
            var directory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(directory) && !EnsureDirectoryExists(directory))
                return false;

            if (overwrite && File.Exists(destinationFilePath))
                File.Delete(destinationFilePath);

            File.Move(sourceFilePath, destinationFilePath);
            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to move file from {sourceFilePath} to {destinationFilePath}", "[FileHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Gets all files in a directory matching a search pattern.
    /// </summary>
    /// <param name="directoryPath">The directory to search.</param>
    /// <param name="searchPattern">The search pattern (e.g., "*.json").</param>
    /// <param name="recursive">Whether to search subdirectories.</param>
    /// <returns>An array of file paths, or an empty array if the operation failed.</returns>
    public static string[] GetFiles(string directoryPath, string searchPattern = "*", bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return [];

        try
        {
            if (!Directory.Exists(directoryPath))
                return [];

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.GetFiles(directoryPath, searchPattern, searchOption);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to get files from directory: {directoryPath}", "[FileHelper] ");
            return [];
        }
    }

    /// <summary>
    /// Gets all subdirectories in a directory.
    /// </summary>
    /// <param name="directoryPath">The directory to search.</param>
    /// <param name="recursive">Whether to search subdirectories recursively.</param>
    /// <returns>An array of directory paths, or an empty array if the operation failed.</returns>
    public static string[] GetDirectories(string directoryPath, bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return [];

        try
        {
            if (!Directory.Exists(directoryPath))
                return [];

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.GetDirectories(directoryPath, "*", searchOption);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to get directories from: {directoryPath}", "[FileHelper] ");
            return [];
        }
    }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <returns>The file size in bytes, or -1 if the operation failed.</returns>
    public static long GetFileSize(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return -1;

        try
        {
            var fileInfo = new FileInfo(filePath);
            return fileInfo.Exists ? fileInfo.Length : -1;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to get file size: {filePath}", "[FileHelper] ");
            return -1;
        }
    }

    /// <summary>
    /// Gets a human-readable file size string (e.g., "1.5 MB", "500 KB").
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>A formatted string representing the file size.</returns>
    public static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Checks whether two files are equal by comparing their contents.
    /// </summary>
    /// <param name="filePath1">The path to the first file.</param>
    /// <param name="filePath2">The path to the second file.</param>
    /// <returns>True if the files are equal; otherwise, false.</returns>
    public static bool AreFilesEqual(string filePath1, string filePath2)
    {
        if (string.IsNullOrWhiteSpace(filePath1) || string.IsNullOrWhiteSpace(filePath2))
            return false;

        try
        {
            if (!File.Exists(filePath1) || !File.Exists(filePath2))
                return false;

            var fileInfo1 = new FileInfo(filePath1);
            var fileInfo2 = new FileInfo(filePath2);

            if (fileInfo1.Length != fileInfo2.Length)
                return false;

            using var fs1 = File.OpenRead(filePath1);
            using var fs2 = File.OpenRead(filePath2);

            int byte1, byte2;
            do
            {
                byte1 = fs1.ReadByte();
                byte2 = fs2.ReadByte();
                if (byte1 != byte2)
                    return false;
            } while (byte1 != -1);

            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to compare files: {filePath1} and {filePath2}", "[FileHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Combines multiple path segments into a single path.
    /// </summary>
    /// <param name="paths">The path segments to combine.</param>
    /// <returns>The combined path.</returns>
    public static string CombinePaths(params string[] paths)
    {
        return Path.Combine(paths);
    }

    /// <summary>
    /// Gets the file name from a file path.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The file name with extension.</returns>
    public static string? GetFileName(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        return Path.GetFileName(filePath);
    }

    /// <summary>
    /// Gets the file name without extension from a file path.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The file name without extension.</returns>
    public static string? GetFileNameWithoutExtension(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        return Path.GetFileNameWithoutExtension(filePath);
    }

    /// <summary>
    /// Gets the file extension from a file path.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The file extension including the dot (e.g., ".json").</returns>
    public static string? GetFileExtension(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        return Path.GetExtension(filePath);
    }

    /// <summary>
    /// Gets the directory name from a file path.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The directory name.</returns>
    public static string? GetDirectoryName(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        return Path.GetDirectoryName(filePath);
    }

    /// <summary>
    /// Serializes an object to JSON and writes it to a file.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="obj">The object to serialize.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>True if the operation was successful; otherwise, false.</returns>
    public static bool WriteJsonToFile<T>(string filePath, T obj, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || obj == null)
            return false;

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !EnsureDirectoryExists(directory))
                return false;

            var json = JsonSerializer.Serialize(obj, obj.GetType(), options);
            File.WriteAllText(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to write JSON to file: {filePath}", "[FileHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Reads JSON from a file and deserializes it to an object.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>The deserialized object, or default(T) if the operation failed.</returns>
    public static T? ReadJsonFromFile<T>(string filePath, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return default;

        try
        {
            if (!File.Exists(filePath))
                return default;

            var json = File.ReadAllText(filePath);
            var obj = JsonSerializer.Deserialize<T>(json, options);
            return obj;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to read JSON from file: {filePath}", "[FileHelper] ");
            return default;
        }
    }

    /// <summary>
    /// Asynchronously writes text to a file, creating the directory structure if necessary.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="content">The content to write.</param>
    /// <param name="encoding">Optional encoding to use. Defaults to UTF-8.</param>
    /// <returns>True if the write operation was successful; otherwise, false.</returns>
    public static async Task<bool> WriteTextToFileAsync(string filePath, string content, Encoding? encoding = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !EnsureDirectoryExists(directory))
                return false;

            encoding ??= Encoding.UTF8;
            await File.WriteAllTextAsync(filePath, content, encoding);
            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to write to file asynchronously: {filePath}", "[FileHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Asynchronously reads text from a file.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="encoding">Optional encoding to use. Defaults to UTF-8.</param>
    /// <returns>The content of the file, or null if the read operation failed.</returns>
    public static async Task<string?> ReadTextFromFileAsync(string filePath, Encoding? encoding = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            if (!File.Exists(filePath))
                return null;

            encoding ??= Encoding.UTF8;
            var content = await File.ReadAllTextAsync(filePath, encoding);
            return content;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to read from file asynchronously: {filePath}", "[FileHelper] ");
            return null;
        }
    }

    /// <summary>
    /// Asynchronously serializes an object to JSON and writes it to a file.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="obj">The object to serialize.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>True if the operation was successful; otherwise, false.</returns>
    public static async Task<bool> WriteJsonToFileAsync<T>(string filePath, T obj, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || obj == null)
            return false;

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !EnsureDirectoryExists(directory))
                return false;

            var json = JsonSerializer.Serialize(obj, options);
            await File.WriteAllTextAsync(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to write JSON to file asynchronously: {filePath}", "[FileHelper] ");
            return false;
        }
    }

    /// <summary>
    /// Asynchronously reads JSON from a file and deserializes it to an object.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>The deserialized object, or default(T) if the operation failed.</returns>
    public static async Task<T?> ReadJsonFromFileAsync<T>(string filePath, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return default;

        try
        {
            if (!File.Exists(filePath))
                return default;

            var json = await File.ReadAllTextAsync(filePath);
            var obj = JsonSerializer.Deserialize<T>(json, options);
            return obj;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to read JSON from file asynchronously: {filePath}", "[FileHelper] ");
            return default;
        }
    }

    /// <summary>
    /// Creates a backup of a file by copying it with a timestamp suffix.
    /// </summary>
    /// <param name="filePath">The path to the file to backup.</param>
    /// <param name="backupDirectory">Optional backup directory. If null, the backup is created in the same directory as the original file.</param>
    /// <returns>The path to the backup file, or null if the operation failed.</returns>
    public static string? BackupFile(string filePath, string? backupDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"{fileName}_backup_{timestamp}{extension}";

            var targetDirectory = backupDirectory ?? Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(targetDirectory))
                return null;

            if (!EnsureDirectoryExists(targetDirectory))
                return null;

            var backupPath = Path.Combine(targetDirectory, backupFileName);
            File.Copy(filePath, backupPath);
            return backupPath;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to create backup of file: {filePath}", "[FileHelper] ");
            return null;
        }
    }
}
