using System;
using System.IO;
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
        IncludeFields = false,
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
        if (NoireService.PluginInstance == null || NoireService.PluginInterface == null)
        {
            NoireLogger.LogError<NoireConfigBase>("Cannot get config file path: NoireLib is not initialized.");
            return null;
        }

        try
        {
            var configDirectory = NoireService.PluginInterface.ConfigDirectory;
            var pluginConfigDirectory = configDirectory.FullName;

            if (!Directory.Exists(pluginConfigDirectory))
            {
                Directory.CreateDirectory(pluginConfigDirectory);
                NoireLogger.LogDebug<NoireConfigBase>($"Created configuration directory: {pluginConfigDirectory}");
            }

            var fileName = $"{GetConfigFileName()}.json";
            return Path.Combine(pluginConfigDirectory, fileName);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, "Failed to get config file path.");
            return null;
        }
    }

    /// <summary>
    /// Saves the current configuration to a JSON file.
    /// </summary>
    /// <returns>True if the save operation was successful; otherwise, false.</returns>
    public virtual bool Save()
    {
        if (NoireService.PluginInstance == null || NoireService.PluginInterface == null)
        {
            NoireLogger.LogWarning<NoireConfigBase>("Cannot save configuration: NoireLib is not initialized.");
            return false;
        }

        var filePath = GetConfigFilePath();
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            var json = JsonSerializer.Serialize(this, GetType(), JsonOptions);
            File.WriteAllText(filePath, json);
            NoireLogger.LogDebug<NoireConfigBase>($"Configuration saved successfully to: {filePath}");
            return true;
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
        if (NoireService.PluginInstance == null || NoireService.PluginInterface == null)
        {
            NoireLogger.LogWarning<NoireConfigBase>("Cannot load configuration: NoireLib is not initialized.");
            return false;
        }

        var filePath = GetConfigFilePath();
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            if (!File.Exists(filePath))
            {
                NoireLogger.LogDebug<NoireConfigBase>($"Configuration file not found: {filePath}. Using default values.");
                return false;
            }

            var json = File.ReadAllText(filePath);
            var loadedConfig = JsonSerializer.Deserialize(json, GetType(), JsonOptions);

            if (loadedConfig == null)
            {
                NoireLogger.LogWarning<NoireConfigBase>($"Failed to deserialize configuration from: {filePath}");
                return false;
            }

            CopyPropertiesFrom(loadedConfig);

            NoireLogger.LogDebug<NoireConfigBase>($"Configuration loaded successfully from: {filePath}");
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
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                NoireLogger.LogDebug<NoireConfigBase>($"Configuration file deleted: {filePath}");
                return true;
            }

            return false;
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
        return !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
    }
}
