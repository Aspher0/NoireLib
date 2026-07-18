using NoireLib.Configuration.Migrations;
using Newtonsoft.Json.Linq;

namespace NoireLib.HotkeyManager;

/// <summary>
/// Migrates <see cref="HotkeyManagerConfigInstance"/> from version 1 (a <c>Keybinds</c> map of id to binding) to
/// version 2 (a <c>Hotkeys</c> map of id to <see cref="PersistedHotkey"/>), so that a config that stored only
/// bindings comes up storing the whole option set.<br/>
/// Each stored binding is lifted into a persisted-hotkey object that carries that exact binding; every other option
/// comes up at its default when the record is read, because a <see cref="PersistedHotkey"/> deserialized from a
/// document missing a field keeps that field's default. The binding's JSON is moved rather than reserialized, so the
/// migration cannot alter a value it does not understand.
/// </summary>
public sealed class HotkeyConfigV1ToV2Migration : ConfigMigrationBase
{
    /// <inheritdoc/>
    public override int FromVersion => 1;

    /// <inheritdoc/>
    public override int ToVersion => 2;

    /// <inheritdoc/>
    public override string Migrate(JObject jsonObject)
        => MigrationBuilder.Create()
            .WithCustomOperation(root =>
            {
                if (root["Keybinds"] is not JObject keybinds)
                    return;

                var hotkeys = new JObject();

                foreach (var binding in keybinds.Properties())
                    hotkeys[binding.Name] = new JObject { ["Binding"] = binding.Value };

                root["Hotkeys"] = hotkeys;
                root.Remove("Keybinds");
            })
            .Migrate(jsonObject, ToVersion);
}
