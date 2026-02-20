using Dalamud.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
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

        if (fileName.IsNullOrWhitespace())
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
    /// Gets the path to a special system folder.
    /// </summary>
    /// <param name="folder">The special folder to get the path for.</param>
    /// <returns>The path to the special folder.</returns>
    public static string GetSpecialFolderPath(Environment.SpecialFolder folder)
    {
        return Environment.GetFolderPath(folder);
    }

    /// <summary>
    /// Gets the user's Documents folder path.
    /// </summary>
    public static string DocumentsPath => GetSpecialFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>
    /// Gets the user's AppData/Roaming folder path (Windows) or equivalent config folder (Linux).
    /// </summary>
    public static string AppDataPath => GetSpecialFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>
    /// Gets the user's Desktop folder path.
    /// </summary>
    public static string DesktopPath => GetSpecialFolderPath(Environment.SpecialFolder.Desktop);

    /// <summary>
    /// Ensures that a directory exists, creating it if necessary.
    /// </summary>
    /// <param name="directoryPath">The path to the directory.</param>
    /// <returns>True if the directory exists or was created successfully; otherwise, false.</returns>
    public static bool EnsureDirectoryExists(string directoryPath)
    {
        if (directoryPath.IsNullOrWhitespace())
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
        return !filePath.IsNullOrWhitespace() && File.Exists(filePath);
    }

    /// <summary>
    /// Checks if a directory exists at the specified path.
    /// </summary>
    /// <param name="directoryPath">The path to the directory.</param>
    /// <returns>True if the directory exists; otherwise, false.</returns>
    public static bool DirectoryExists(string? directoryPath)
    {
        return !directoryPath.IsNullOrWhitespace() && Directory.Exists(directoryPath);
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
        if (filePath.IsNullOrWhitespace())
            return false;

        try
        {
            var directory = Path.GetDirectoryName(filePath);

            if (!directory.IsNullOrWhitespace() && !EnsureDirectoryExists(directory))
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
        if (filePath.IsNullOrWhitespace())
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
        if (filePath.IsNullOrWhitespace())
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
        if (directoryPath.IsNullOrWhitespace())
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
        if (sourceFilePath.IsNullOrWhitespace() || destinationFilePath.IsNullOrWhitespace())
            return false;

        try
        {
            var directory = Path.GetDirectoryName(destinationFilePath);

            if (!directory.IsNullOrWhitespace() && !EnsureDirectoryExists(directory))
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
        if (sourceFilePath.IsNullOrWhitespace() || destinationFilePath.IsNullOrWhitespace())
            return false;

        try
        {
            var directory = Path.GetDirectoryName(destinationFilePath);

            if (!directory.IsNullOrWhitespace() && !EnsureDirectoryExists(directory))
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
        if (directoryPath.IsNullOrWhitespace())
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
        if (directoryPath.IsNullOrWhitespace())
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
        if (filePath.IsNullOrWhitespace())
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
        if (filePath1.IsNullOrWhitespace() || filePath2.IsNullOrWhitespace())
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
        if (filePath.IsNullOrWhitespace())
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
        if (filePath.IsNullOrWhitespace())
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
        if (filePath.IsNullOrWhitespace())
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
        if (filePath.IsNullOrWhitespace())
            return null;

        return Path.GetDirectoryName(filePath);
    }

    /// <summary>
    /// Serializes an object to JSON and writes it to a file.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="obj">The object to serialize.</param>
    /// <param name="settings">Optional JSON serializer settings.</param>
    /// <returns>True if the operation was successful; otherwise, false.</returns>
    public static bool WriteJsonToFile<T>(string filePath, T obj, JsonSerializerSettings? settings = null)
    {
        if (filePath.IsNullOrWhitespace() || obj == null)
            return false;

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!directory.IsNullOrWhitespace() && !EnsureDirectoryExists(directory))
                return false;

            var json = JsonConvert.SerializeObject(obj, settings);
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
    /// <param name="settings">Optional JSON serializer settings.</param>
    /// <returns>The deserialized object, or default(T) if the operation failed.</returns>
    public static T? ReadJsonFromFile<T>(string filePath, JsonSerializerSettings? settings = null)
    {
        if (filePath.IsNullOrWhitespace())
            return default;

        try
        {
            if (!File.Exists(filePath))
                return default;

            var json = File.ReadAllText(filePath);
            var obj = JsonConvert.DeserializeObject<T>(json, settings);
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
        if (filePath.IsNullOrWhitespace())
            return false;

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!directory.IsNullOrWhitespace() && !EnsureDirectoryExists(directory))
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
        if (filePath.IsNullOrWhitespace())
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
    /// <param name="settings">Optional JSON serializer settings.</param>
    /// <returns>True if the operation was successful; otherwise, false.</returns>
    public static async Task<bool> WriteJsonToFileAsync<T>(string filePath, T obj, JsonSerializerSettings? settings = null)
    {
        if (filePath.IsNullOrWhitespace() || obj == null)
            return false;

        try
        {
            var directory = Path.GetDirectoryName(filePath);

            if (!directory.IsNullOrWhitespace() && !EnsureDirectoryExists(directory))
                return false;

            var json = JsonConvert.SerializeObject(obj, settings);
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
    /// <param name="settings">Optional JSON serializer settings.</param>
    /// <returns>The deserialized object, or default(T) if the operation failed.</returns>
    public static async Task<T?> ReadJsonFromFileAsync<T>(string filePath, JsonSerializerSettings? settings = null)
    {
        if (filePath.IsNullOrWhitespace())
            return default;

        try
        {
            if (!File.Exists(filePath))
                return default;

            var json = await File.ReadAllTextAsync(filePath);
            var obj = JsonConvert.DeserializeObject<T>(json, settings);
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
        if (filePath.IsNullOrWhitespace() || !File.Exists(filePath))
            return null;

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"{fileName}_backup_{timestamp}{extension}";

            var targetDirectory = backupDirectory ?? Path.GetDirectoryName(filePath);
            if (targetDirectory.IsNullOrWhitespace())
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

    /// <summary>
    /// Retrieves all file paths from a directory that match a search pattern, with options to search recursively and return paths relative to the parent directory.
    /// </summary>
    /// <param name="parentDirectoryPath">The path to the parent directory.</param>
    /// <param name="searchPattern">The search pattern to match files.</param>
    /// <param name="recursive">Whether to search recursively in subdirectories.</param>
    /// <param name="returnRelativePaths">Whether to return paths relative to the parent directory.</param>
    /// <returns>A list of file paths.</returns>
    public static List<string> GetFilePathsInFolder(string parentDirectoryPath, string searchPattern, bool recursive, bool returnRelativePaths)
    {
        var filePaths = new List<string>();

        if (parentDirectoryPath.IsNullOrWhitespace() || !Directory.Exists(parentDirectoryPath))
            return filePaths;

        try
        {
            var allFiles = Directory.GetFiles(parentDirectoryPath, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            foreach (var file in allFiles)
            {
                if (returnRelativePaths)
                    filePaths.Add(Path.GetRelativePath(parentDirectoryPath, file));
                else
                    filePaths.Add(file);
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to get file paths {(recursive ? "recursively" : "non-recursively")} from: {parentDirectoryPath} with search pattern: {searchPattern}", "[FileHelper] ");
        }
        return filePaths;
    }

    /// <summary>
    /// Retrieves all file paths from a directory that match a search pattern, with an option to return paths relative to the parent directory.
    /// </summary>
    /// <param name="parentDirectoryPath">The path to the parent directory.</param>
    /// <param name="searchPattern">The search pattern to match files.</param>
    /// <param name="returnRelativePaths">Whether to return paths relative to the parent directory.</param>
    /// <returns>A list of file paths.</returns>
    public static List<string> GetFilePathsInFolder(string parentDirectoryPath, string searchPattern = "*", bool returnRelativePaths = false)
        => GetFilePathsInFolder(parentDirectoryPath, searchPattern, false, returnRelativePaths);

    /// <summary>
    /// Retrieves all file paths from a directory and its subdirectories that match a search pattern, with an option to return paths relative to the parent directory.
    /// </summary>
    /// <param name="parentDirectoryPath">The path to the parent directory.</param>
    /// <param name="searchPattern">The search pattern to match files.</param>
    /// <param name="returnRelativePaths">Whether to return paths relative to the parent directory.</param>
    /// <returns>A list of file paths.</returns>
    public static List<string> GetFilePathsInFolderRecursive(string parentDirectoryPath, string searchPattern = "*", bool returnRelativePaths = false)
        => GetFilePathsInFolder(parentDirectoryPath, searchPattern, true, returnRelativePaths);

    /// <summary>
    /// Retrieves all directory paths from a directory and its subdirectories, with an option to return paths relative to the parent directory.
    /// </summary>
    /// <param name="parentDirectoryPath">The path to the parent directory.</param>
    /// <param name="searchPattern">The search pattern to match directories.</param>
    /// <param name="recursive">Whether to search recursively in subdirectories.</param>
    /// <param name="returnRelativePaths">Whether to return paths relative to the parent directory.</param>
    /// <returns>A list of directory paths.</returns>
    public static List<string> GetDirectoryPathsInFolder(string parentDirectoryPath, string searchPattern, bool recursive, bool returnRelativePaths)
    {
        var directoryPaths = new List<string>();

        if (parentDirectoryPath.IsNullOrWhitespace() || !Directory.Exists(parentDirectoryPath))
            return directoryPaths;

        try
        {
            var allDirectories = Directory.GetDirectories(parentDirectoryPath, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            foreach (var dir in allDirectories)
            {
                if (returnRelativePaths)
                    directoryPaths.Add(Path.GetRelativePath(parentDirectoryPath, dir));
                else
                    directoryPaths.Add(dir);
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to get directory paths recursively from: {parentDirectoryPath}", "[FileHelper] ");
        }

        return directoryPaths;
    }

    /// <summary>
    /// Retrieves all directory paths from a directory that match a search pattern, with an option to return paths relative to the parent directory.
    /// </summary>
    /// <param name="parentDirectoryPath">The path to the parent directory.</param>
    /// <param name="searchPattern">The search pattern to match directories.</param>
    /// <param name="returnRelativePaths">Whether to return paths relative to the parent directory.</param>
    /// <returns>A list of directory paths.</returns>
    public static List<string> GetDirectoryPathsInFolder(string parentDirectoryPath, string searchPattern = "*", bool returnRelativePaths = false)
        => GetDirectoryPathsInFolder(parentDirectoryPath, searchPattern, false, returnRelativePaths);

    /// <summary>
    /// Retrieves all directory paths from a directory and its subdirectories that match a search pattern, with an option to return paths relative to the parent directory.
    /// </summary>
    /// <param name="parentDirectoryPath">The path to the parent directory.</param>
    /// <param name="searchPattern">The search pattern to match directories.</param>
    /// <param name="returnRelativePaths">Whether to return paths relative to the parent directory.</param>
    /// <returns>A list of directory paths.</returns>
    public static List<string> GetDirectoryPathsInFolderRecursive(string parentDirectoryPath, string searchPattern = "*", bool returnRelativePaths = false)
        => GetDirectoryPathsInFolder(parentDirectoryPath, searchPattern, true, returnRelativePaths);

    /// <summary>
    /// Creates a zip file containing the specified files.
    /// </summary>
    /// <param name="files">The list of files to be included in the zip file, each including its path and optional entry name.</param>
    /// <param name="destinationDirectory">The directory where the zip file will be created.</param>
    /// <param name="zipFileName">The name of the zip file. If not provided, a default name with a timestamp is used.</param>
    /// <returns>The path to the created zip file, or null if the operation fails.</returns>
    public static string? ZipFiles(List<(string FilePath, string? EntryName)> files, string destinationDirectory, string? zipFileName = null)
    {
        if (files == null || files.Count == 0)
            return null;

        try
        {
            if (zipFileName.IsNullOrWhitespace())
                zipFileName = $"Archive_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

            if (!zipFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                zipFileName += ".zip";

            if (destinationDirectory.IsNullOrWhitespace())
                return null;

            if (!EnsureDirectoryExists(destinationDirectory))
                return null;

            var zipFilePath = Path.Combine(destinationDirectory, zipFileName);

            using (var zipArchive = System.IO.Compression.ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    if (File.Exists(file.FilePath))
                    {
                        string entryName;

                        if (file.EntryName.IsNullOrWhitespace())
                            entryName = Path.GetFileName(file.FilePath);
                        else
                            entryName = file.EntryName;

                        zipArchive.CreateEntryFromFile(file.FilePath, entryName);
                    }
                }
            }

            return zipFilePath;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to zip files", "[FileHelper] ");
            return null;
        }
    }

    /// <summary>
    /// Creates a zip file containing the specified file. 
    /// </summary>
    /// <param name="file">The file to be included in the zip file, including its path and optional entry name.</param>
    /// <param name="destinationDirectory">The directory where the zip file will be created.</param>
    /// <param name="zipFileName">The name of the zip file. If not provided, a default name with a timestamp is used.</param>
    /// <returns>The path to the created zip file, or null if the operation fails.</returns>
    public static string? ZipFile((string FilePath, string? EntryName) file, string? destinationDirectory = null, string? zipFileName = null)
    {
        if (file.FilePath.IsNullOrWhitespace() || !File.Exists(file.FilePath))
            return null;

        if (destinationDirectory.IsNullOrWhitespace())
            destinationDirectory = Path.GetDirectoryName(file.FilePath);

        if (destinationDirectory.IsNullOrWhitespace()) // Just in case Path.GetDirectoryName() returns null
            return null;

        if (!EnsureDirectoryExists(destinationDirectory))
            return null;

        return ZipFiles(new List<(string, string?)> { file }, destinationDirectory, zipFileName);
    }

    /// <summary>
    /// Creates a zip file containing the contents of the specified directories.<br/>
    /// Each directory can have an optional entry name to specify how it should appear in the zip file.<br/>
    /// If no entry name is provided, the directory's name will be used as the root entry in the zip file.
    /// </summary>
    /// <param name="sourceDirectories">A list of tuples containing the directory path and an optional entry name for each directory.</param>
    /// <param name="destinationDirectory">The directory where the zip file will be created.</param>
    /// <param name="zipFileName">The name of the zip file. If not provided, a default name with a timestamp is used.</param>
    /// <returns>The path to the created zip file, or null if the operation fails.</returns>
    public static string? ZipFolders(List<(string DirectoryPath, string? EntryName)> sourceDirectories, string destinationDirectory, string? zipFileName = null)
    {
        if (sourceDirectories == null || sourceDirectories.Count == 0)
            return null;

        try
        {
            if (zipFileName.IsNullOrWhitespace())
                zipFileName = $"Archive_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

            if (!zipFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                zipFileName += ".zip";

            if (destinationDirectory.IsNullOrWhitespace())
                return null;

            if (!EnsureDirectoryExists(destinationDirectory))
                return null;

            var zipFilePath = Path.Combine(destinationDirectory, zipFileName);

            using (var zipArchive = System.IO.Compression.ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
            {
                foreach (var dir in sourceDirectories)
                {
                    if (Directory.Exists(dir.DirectoryPath))
                    {
                        var directoryInfo = new DirectoryInfo(dir.DirectoryPath);
                        var files = directoryInfo.GetFiles("*", SearchOption.AllDirectories);
                        string rootName = !dir.EntryName.IsNullOrWhitespace()
                            ? dir.EntryName!
                            : directoryInfo.Name;
                        foreach (var file in files)
                        {
                            var relativePath = Path.GetRelativePath(directoryInfo.FullName, file.FullName);
                            var entryName = Path.Combine(rootName, relativePath);
                            zipArchive.CreateEntryFromFile(file.FullName, entryName);
                        }
                    }
                }
            }

            return zipFilePath;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to zip folders", "[FileHelper] ");
            return null;
        }
    }

    /// <summary>
    /// Creates a zip file containing the contents of the specified directory.<br/>
    /// Each directory can have an optional entry name to specify how it should appear in the zip file.<br/>
    /// If no entry name is provided, the directory's name will be used as the root entry in the zip file.
    /// </summary>
    /// <param name="sourceDirectory">The directory to be zipped, along with an optional entry name.</param>
    /// <param name="destinationDirectory">The directory where the zip file will be created. If not provided, the source directory's parent will be used.</param>
    /// <param name="zipFileName">The name of the zip file. If not provided, a default name with a timestamp is used.</param>
    /// <returns>The path to the created zip file, or null if the operation fails.</returns>
    public static string? ZipFolder((string DirectoryPath, string? EntryName) sourceDirectory, string? destinationDirectory = null, string? zipFileName = null)
    {
        if (sourceDirectory.DirectoryPath.IsNullOrWhitespace() || !Directory.Exists(sourceDirectory.DirectoryPath))
            return null;

        if (destinationDirectory.IsNullOrWhitespace())
            destinationDirectory = Path.GetDirectoryName(sourceDirectory.DirectoryPath);

        if (destinationDirectory.IsNullOrWhitespace()) // Just in case Path.GetDirectoryName() returns null
            return null;

        if (!EnsureDirectoryExists(destinationDirectory))
            return null;

        return ZipFolders(new List<(string, string?)> { sourceDirectory }, destinationDirectory, zipFileName);
    }

    /// <summary>
    /// Extracts a zip file to the specified destination directory.
    /// </summary>
    /// <param name="zipFilePath">The path to the zip file.</param>
    /// <param name="destinationDirectory">The directory to extract to. If null, uses the zip file's directory.</param>
    /// <param name="overwrite">Whether to overwrite existing files.</param>
    /// <returns>True if extraction was successful; otherwise, false.</returns>
    public static bool UnzipFile(string zipFilePath, string? destinationDirectory = null, bool overwrite = false)
    {
        if (zipFilePath.IsNullOrWhitespace() || !zipFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(zipFilePath))
            return false;

        try
        {
            if (destinationDirectory.IsNullOrWhitespace())
                destinationDirectory = Path.GetDirectoryName(zipFilePath);

            if (destinationDirectory.IsNullOrWhitespace())
                return false;

            if (!EnsureDirectoryExists(destinationDirectory))
                return false;

            using (var archive = System.IO.Compression.ZipFile.OpenRead(zipFilePath))
            {
                foreach (var entry in archive.Entries)
                {
                    var destinationPath = Path.Combine(destinationDirectory, entry.FullName);
                    var destinationDir = Path.GetDirectoryName(destinationPath);

                    if (!destinationDir.IsNullOrWhitespace() && !Directory.Exists(destinationDir))
                        Directory.CreateDirectory(destinationDir);

                    if (entry.Name.IsNullOrEmpty()) // Directory entry
                        continue;

                    if (File.Exists(destinationPath) && !overwrite)
                        continue;

                    entry.ExtractToFile(destinationPath, overwrite);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to unzip file: {zipFilePath}", "[FileHelper] ");
            return false;
        }
    }
}
