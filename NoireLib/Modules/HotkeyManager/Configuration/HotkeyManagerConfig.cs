using NoireLib.Configuration;
using System;
using System.Collections.Generic;

namespace NoireLib.HotkeyManager;

/// <summary>
/// Configuration storage for hotkey bindings.
/// </summary>
[NoireConfig("HotkeyManagerConfig")]
public class HotkeyManagerConfigInstance : NoireConfigBase
{
    /// <inheritdoc />
    public override int Version { get; set; } = 1;

    /// <inheritdoc />
    public override string GetConfigFileName() => "HotkeyManagerConfig";

    /// <summary>
    /// Persisted hotkey bindings keyed by hotkey id.
    /// </summary>
    public Dictionary<string, HotkeyBinding> Keybinds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
