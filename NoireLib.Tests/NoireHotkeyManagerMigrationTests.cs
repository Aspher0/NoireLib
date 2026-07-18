using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NoireLib.HotkeyManager;
using System;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Tests for the hotkey config version 1 to version 2 migration, which lifts a <c>Keybinds</c> map of id to binding
/// into a <c>Hotkeys</c> map of id to <see cref="PersistedHotkey"/>.<br/>
/// The file-based migration mechanics (backup before migrate, refusal to persist a degraded load, byte-identical file
/// on a failed migration) are the configuration framework's and are covered by <c>NoireConfigMigrationTests</c>; these
/// tests cover this migration's own transform: that a stored binding survives it exactly and that the new options come
/// up at their defaults. These use only local objects, so they touch none of the process-wide stored hotkeys.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireHotkeyManagerMigrationTests
{
    private static readonly JsonSerializerSettings DeserializeSettings = new() { TypeNameHandling = TypeNameHandling.None };

    private const string V1Json = """
        {
          "Version": 1,
          "Keybinds": {
            "My.Hotkey": { "VkCode": 65, "Ctrl": true, "Shift": false, "Alt": false, "GamepadButton": null },
            "Other.Hotkey": { "VkCode": 66, "Ctrl": false, "Shift": true, "Alt": false, "GamepadButton": null }
          }
        }
        """;

    [Fact]
    public void Migration_ReplacesKeybindsWithHotkeys_AtVersionTwo()
    {
        var migrated = JObject.Parse(new HotkeyConfigV1ToV2Migration().Migrate(JObject.Parse(V1Json)));

        migrated["Version"]!.Value<int>().Should().Be(2);
        migrated.ContainsKey("Keybinds").Should().BeFalse("the version 1 map is replaced, not kept alongside");
        migrated["Hotkeys"].Should().BeOfType<JObject>();
    }

    [Fact]
    public void Migration_LiftsEachStoredBindingIntoAHotkeyRecord_Unchanged()
    {
        var migrated = JObject.Parse(new HotkeyConfigV1ToV2Migration().Migrate(JObject.Parse(V1Json)));
        var hotkeys = (JObject)migrated["Hotkeys"]!;

        var first = (JObject)hotkeys["My.Hotkey"]!;
        var firstBinding = (JObject)first["Binding"]!;
        firstBinding["VkCode"]!.Value<int>().Should().Be(65);
        firstBinding["Ctrl"]!.Value<bool>().Should().BeTrue();

        var second = (JObject)hotkeys["Other.Hotkey"]!;
        var secondBinding = (JObject)second["Binding"]!;
        secondBinding["VkCode"]!.Value<int>().Should().Be(66);
        secondBinding["Shift"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public void MigratedConfig_Deserializes_WithBindingsPreservedAndOptionsAtTheirDefaults()
    {
        var migratedJson = new HotkeyConfigV1ToV2Migration().Migrate(JObject.Parse(V1Json));

        var config = JsonConvert.DeserializeObject<HotkeyManagerConfigInstance>(migratedJson, DeserializeSettings);

        config.Should().NotBeNull();
        config!.Version.Should().Be(2);
        config.Hotkeys.Should().ContainKey("My.Hotkey");

        var record = config.Hotkeys["My.Hotkey"];
        record.Binding.VkCode.Should().Be(65, "the binding survives the migration exactly");
        record.Binding.Ctrl.Should().BeTrue();

        // Everything the version 1 file did not carry comes up at the same default as the HotkeyEntry property.
        record.Enabled.Should().BeTrue();
        record.ActivationMode.Should().Be(HotkeyActivationMode.Pressed);
        record.HoldDelay.Should().Be(TimeSpan.FromMilliseconds(400));
        record.FixedRepeatDelay.Should().Be(TimeSpan.FromMilliseconds(80));
        record.UseRandomRepeatDelay.Should().BeFalse();
        record.BlockGameInput.Should().BeFalse();
        record.BlockWhenTextInputActive.Should().BeTrue();
        record.RequireGameFocus.Should().BeTrue();
    }

    [Fact]
    public void Migration_OfAFileWithNoKeybinds_YieldsAnEmptyHotkeysMap()
    {
        var migratedJson = new HotkeyConfigV1ToV2Migration().Migrate(JObject.Parse("""{ "Version": 1 }"""));

        var config = JsonConvert.DeserializeObject<HotkeyManagerConfigInstance>(migratedJson, DeserializeSettings);

        config!.Version.Should().Be(2);
        config.Hotkeys.Should().BeEmpty();
    }
}
