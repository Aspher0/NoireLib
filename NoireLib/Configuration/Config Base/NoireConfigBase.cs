using NoireLib.Configuration.Migrations;
using NoireLib.Helpers;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoireLib.Configuration;

/// <summary>
/// Base class for NoireLib configuration classes that provides automatic JSON serialization and file management.
/// </summary>
public abstract class NoireConfigBase : INoireConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// The version of the configuration schema, for potential migrations.
    /// </summary>
    public abstract int Version { get; set; }

    /// <summary>
    /// Gets the configuration file name (without extension).
    /// Override this method to provide a custom file name for your configuration.
    /// </summary>
    /// <returns>The configuration file name.</returns>
    public abstract string GetConfigFileName();

    /// <summary>
    /// Gets the default version value defined in the derived class.
    /// </summary>
    /// <returns>The default version value.</returns>
    protected virtual int GetDefaultVersion()
    {
        try
        {
            var tempInstance = Activator.CreateInstance(GetType());
            if (tempInstance is NoireConfigBase configInstance)
            {
                return configInstance.Version;
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, "Failed to get default version, using current version.");
        }

        return Version;
    }

    /// <summary>
    /// Gets the full path to the configuration file.
    /// </summary>
    /// <returns>The full path to the configuration JSON file, or null if NoireLib is not initialized.</returns>
    protected string? GetConfigFilePath()
    {
        var fileName = $"{GetConfigFileName()}.json";
        return FileHelper.GetPluginConfigFilePath(fileName);
    }

    /// <summary>
    /// Saves the current configuration to a JSON file.
    /// </summary>
    /// <returns>True if the save operation was successful; otherwise, false.</returns>
    public virtual bool Save()
    {
        if (!NoireService.IsInitialized())
        {
            NoireLogger.LogWarning<NoireConfigBase>("Cannot save configuration: NoireLib is not initialized.");
            return false;
        }

        var filePath = GetConfigFilePath();
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            // Force save with default version
            var defaultVersion = GetDefaultVersion();
            Version = defaultVersion;

            var currentJson = JsonSerializer.Serialize(this, GetType(), JsonOptions);

            if (FileHelper.FileExists(filePath))
            {
                var existingJson = FileHelper.ReadTextFromFile(filePath);
                if (existingJson != null && existingJson.Equals(currentJson, StringComparison.Ordinal))
                {
                    NoireLogger.LogVerbose<NoireConfigBase>($"Configuration unchanged, skipping save: {filePath}");
                    return true;
                }
            }

            var success = FileHelper.WriteJsonToFile(filePath, this, JsonOptions);
            if (success)
                NoireLogger.LogVerbose<NoireConfigBase>($"Configuration saved successfully to: {filePath}");

            return success;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to save configuration to: {filePath}");
            return false;
        }
    }

    /// <summary>
    /// Loads the configuration from a JSON file and populates the current instance.
    /// Automatically executes migrations if the file version is older than the current version.
    /// </summary>
    /// <returns>True if the load operation was successful; otherwise, false.</returns>
    public virtual bool Load()
    {
        if (!NoireService.IsInitialized())
        {
            NoireLogger.LogWarning<NoireConfigBase>("Cannot load configuration: NoireLib is not initialized.");
            return false;
        }

        var filePath = GetConfigFilePath();
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            if (!FileHelper.FileExists(filePath))
            {
                NoireLogger.LogDebug<NoireConfigBase>($"Configuration file not found: {filePath}. Using default values.");
                return false;
            }

            var json = FileHelper.ReadTextFromFile(filePath);
            if (json == null)
            {
                NoireLogger.LogWarning<NoireConfigBase>($"Failed to read configuration from: {filePath}");
                return false;
            }

            var fileVersion = GetVersionFromJson(json);
            var targetVersion = Version;
            bool migrationSuccess = false;

            if (fileVersion < targetVersion)
            {
                NoireLogger.LogInfo<NoireConfigBase>($"Configuration version mismatch: file={fileVersion}, target={targetVersion}. Attempting migration.");

                var migratedJson = MigrationExecutor.ExecuteMigrations(GetType(), json, fileVersion, targetVersion);

                if (migratedJson != null)
                {
                    migrationSuccess = true;
                    json = migratedJson;
                    NoireLogger.LogInfo<NoireConfigBase>($"Successfully migrated configuration from version {fileVersion} to {targetVersion}");
                }
                else
                {
                    NoireLogger.LogError<NoireConfigBase>($"Failed to migrate configuration {GetType().Name} from version {fileVersion} to {targetVersion}. Using default values.");
                }
            }

            var loadedConfig = JsonSerializer.Deserialize(json, GetType(), JsonOptions);

            if (loadedConfig == null)
            {
                NoireLogger.LogWarning<NoireConfigBase>($"Failed to deserialize configuration from: {filePath}");
                return false;
            }

            CopyPropertiesFrom(loadedConfig);

            if (fileVersion < targetVersion && migrationSuccess)
            {
                NoireLogger.LogDebug<NoireConfigBase>("Saving migrated configuration to disk...");
                Save();
            }

            NoireLogger.LogVerbose<NoireConfigBase>($"Configuration loaded successfully from: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to load configuration from: {filePath}");
            return false;
        }
    }

    /// <summary>
    /// Extracts the version number from a JSON string.
    /// </summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The version number, or 0 if not found.</returns>
    private static int GetVersionFromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("Version", out var versionElement))
            {
                return versionElement.GetInt32();
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return 0;
    }

    /// <summary>
    /// Copies all properties from another instance to this instance.
    /// </summary>
    /// <param name="source">The source configuration to copy from.</param>
    protected virtual void CopyPropertiesFrom(object source)
    {
        if (source == null || source.GetType() != GetType())
            return;

        var properties = GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (property.CanWrite && property.CanRead)
            {
                try
                {
                    var value = property.GetValue(source);
                    property.SetValue(this, value);
                }
                catch (Exception ex)
                {
                    NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to copy property: {property.Name}");
                }
            }
        }
    }

    /// <summary>
    /// Deletes the configuration file.
    /// </summary>
    /// <returns>True if the delete operation was successful; otherwise, false.</returns>
    public virtual bool Delete()
    {
        var filePath = GetConfigFilePath();
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            var success = FileHelper.DeleteFile(filePath);
            if (success)
            {
                NoireLogger.LogDebug<NoireConfigBase>($"Configuration file deleted: {filePath}");
            }
            return success;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to delete configuration file: {filePath}");
            return false;
        }
    }

    /// <summary>
    /// Checks if the configuration file exists.
    /// </summary>
    /// <returns>True if the file exists; otherwise, false.</returns>
    public virtual bool Exists()
    {
        var filePath = GetConfigFilePath();
        return FileHelper.FileExists(filePath);
    }
}
