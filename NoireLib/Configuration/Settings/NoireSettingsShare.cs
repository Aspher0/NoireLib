using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;

namespace NoireLib.Configuration;

/// <summary>Settings as a share code: the modified ones only, by property name, readable by newer plugin versions.</summary>
public static class NoireSettingsShare
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        Converters = [new StringEnumConverter()],
    };

    private static readonly JsonSerializer ValueSerializer = JsonSerializer.Create(JsonSettings);

    /// <summary>Writes the modified settings as a share code.</summary>
    /// <param name="settings">The settings to consider.</param>
    /// <returns>The share code.</returns>
    public static string Export(IEnumerable<INoireSetting> settings)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var setting in settings)
        {
            if (setting.IsModified)
                values[setting.Name] = setting.Boxed;
        }

        return ShareCodeHelper.Encode(Kind, values, JsonSettings);
    }

    /// <summary>Applies a share code to the settings; unknown names and unreadable values are skipped.</summary>
    /// <param name="code">The pasted code.</param>
    /// <param name="settings">The settings the code may set.</param>
    /// <returns>How many settings were applied, or why the code could not be read.</returns>
    public static ShareCodeResult<int> Import(string? code, IEnumerable<INoireSetting> settings)
    {
        var decoded = ShareCodeHelper.Decode<Dictionary<string, JToken>>(code, Kind);

        if (!decoded.Success)
            return ShareCodeResult<int>.Fail(decoded.Error, decoded.Message, decoded.Kind);

        var values = decoded.Value ?? [];
        var applied = 0;

        foreach (var setting in settings)
        {
            if (!values.TryGetValue(setting.Name, out var token))
                continue;

            try
            {
                var value = token.ToObject(setting.ValueType, ValueSerializer);

                if (value == null && setting.ValueType.IsValueType && Nullable.GetUnderlyingType(setting.ValueType) == null)
                    continue;

                setting.Boxed = value;
                applied++;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidCastException or FormatException)
            {
                NoireLogger.LogDebug<INoireSetting>($"Skipped the shared value of {setting.Name}: {ex.Message}");
            }
        }

        return ShareCodeResult<int>.Ok(applied, decoded.Kind);
    }

    // Tagged with the plugin so another plugin's settings code is refused rather than half-applied.
    private static string Kind => (NoireService.IsInitialized() ? NoireService.PluginInterface.InternalName : "noire") + ".settings";
}
