using NoireLib.Configuration;
using NoireLib.Configuration.Migrations;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace NoireLib.HotkeyManager;

/// <summary>
/// Configuration storage for hotkeys.<br/>
/// Each hotkey is persisted as a whole <see cref="PersistedHotkey"/> (its binding plus every option), keyed by id.
/// Version 1 stored only a binding per id; the <see cref="HotkeyConfigV1ToV2Migration"/> lifts such a file into this
/// shape on load.
/// </summary>
[NoireConfig("HotkeyManagerConfig")]
[ConfigMigration(typeof(HotkeyConfigV1ToV2Migration))]
public class HotkeyManagerConfigInstance : NoireConfigBase
{
    /// <inheritdoc />
    public override int Version { get; set; } = 2;

    /// <inheritdoc />
    public override string GetConfigFileName() => "HotkeyManagerConfig";

    /// <summary>
    /// Persisted hotkeys keyed by hotkey id, case insensitive, matching how hotkey ids are compared everywhere else.
    /// </summary>
    public Dictionary<string, PersistedHotkey> Hotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Restores the case insensitive comparer of <see cref="Hotkeys"/> after a load from disk, since
    /// deserialization always rebuilds the dictionary with the default ordinal comparer.
    /// </summary>
    /// <param name="context">The streaming context supplied by the serializer.</param>
    [OnDeserialized]
    internal void NormalizeHotkeysComparer(StreamingContext context)
    {
        // A file whose Hotkeys entry is null leaves the property null: deserialization replaces the
        // instance the property initializer created rather than populating it.
        if (Hotkeys == null)
        {
            Hotkeys = new Dictionary<string, PersistedHotkey>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        if (Hotkeys.Comparer != StringComparer.OrdinalIgnoreCase)
            Hotkeys = new Dictionary<string, PersistedHotkey>(Hotkeys, StringComparer.OrdinalIgnoreCase);
    }
}
