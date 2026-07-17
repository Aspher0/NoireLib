using NoireLib.Configuration;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

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
    /// Persisted hotkey bindings keyed by hotkey id.<br/>
    /// Lookups are case insensitive, matching how hotkey ids are compared everywhere else.
    /// </summary>
    public Dictionary<string, HotkeyBinding> Keybinds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Restores the case insensitive comparer of <see cref="Keybinds"/> once a load from disk has finished.<br/>
    /// Deserialization always builds a fresh dictionary with the default ordinal comparer, so a config read
    /// back from disk would otherwise resolve bindings case sensitively while every other lookup of the same
    /// id ignores case. This is the only boundary that can introduce the wrong comparer, so normalizing it
    /// here keeps read paths free of repair work and of the write that repairing would need.
    /// </summary>
    /// <param name="context">The streaming context supplied by the serializer.</param>
    [OnDeserialized]
    internal void NormalizeKeybindsComparer(StreamingContext context)
    {
        // A file whose Keybinds entry is null leaves the property null, since deserialization replaces
        // rather than populates the instance the property initializer created.
        if (Keybinds == null)
        {
            Keybinds = new Dictionary<string, HotkeyBinding>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        if (Keybinds.Comparer != StringComparer.OrdinalIgnoreCase)
            Keybinds = new Dictionary<string, HotkeyBinding>(Keybinds, StringComparer.OrdinalIgnoreCase);
    }
}
