namespace NoireLib.Configuration;

/// <summary>
/// Interface for NoireLib configuration classes that can be saved and loaded from JSON files.
/// </summary>
public interface INoireConfig
{
    /// <summary>
    /// Saves the current configuration to a JSON file.
    /// </summary>
    /// <returns>True if the save operation was successful; otherwise, false.</returns>
    bool Save();

    /// <summary>
    /// Loads the configuration from a JSON file and populates the current instance.
    /// </summary>
    /// <returns>True if the load operation was successful; otherwise, false.</returns>
    bool Load();

    /// <summary>
    /// Gets the configuration file name (without extension).
    /// </summary>
    /// <returns>The configuration file name.</returns>
    string GetConfigFileName();
}
