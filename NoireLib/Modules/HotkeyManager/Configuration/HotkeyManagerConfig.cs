using NoireLib.Configuration;
using System;
using System.Collections.Generic;

namespace NoireLib.HotkeyManager;

/// <summary>
/// Configuration storage for hotkey bindings.
/// </summary>
public class HotkeyManagerConfig : NoireConfigBase<HotkeyManagerConfig>
{
    /// <inheritdoc />
    public override int Version { get; set; } = 1;

    /// <summary>
    /// Persisted hotkey bindings keyed by hotkey id.
    /// </summary>
    public Dictionary<string, HotkeyBinding> Keybinds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string GetConfigFileName() => "NoireHotkeyManager";
}
