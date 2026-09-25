using NoireLib.Localizer;
using System;

namespace NoireLib.UI;

internal sealed record SettingsPageEntry(string Id, NoireString Label, Action<NoireSettingsPage> Draw, NoireIcon? Icon);
