using System;

namespace NoireLib.UpdateTracker;

/// <summary>
/// Event triggered when a new version of the plugin is detected.
/// </summary>
/// <param name="CurrentVersion">The current version.</param>
/// <param name="NewVersion">The newly detected version.</param>
public record NewPluginVersionDetectedEvent(Version CurrentVersion, Version NewVersion);
