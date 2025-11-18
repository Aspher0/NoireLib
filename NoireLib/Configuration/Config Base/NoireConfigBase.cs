using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NoireLib.Helpers;

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
    /// Gets the configuration file name (without extension).
    /// Override this method to provide a custom file name for your configuration.
    /// </summary>
    /// <returns>The configuration file name.</returns>
    public abstract string GetConfigFileName();

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

            var loadedConfig = JsonSerializer.Deserialize(json, GetType(), JsonOptions);

            if (loadedConfig == null)
            {
                NoireLogger.LogWarning<NoireConfigBase>($"Failed to deserialize configuration from: {filePath}");
                return false;
            }

            CopyPropertiesFrom(loadedConfig);

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
