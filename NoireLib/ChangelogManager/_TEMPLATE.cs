using Dalamud.Bindings.ImGui;
using NoireLib.Changelog;
using NoireLib.Helpers;
using System.Collections.Generic;

namespace NoireLib.Examples.Changelog;

/// <summary>
/// Template for creating new changelog versions.
/// </summary>
public class ChangelogTemplate : BaseChangelogVersion
{
    public override List<ChangelogVersion> GetVersions() => new()
    {
        V0_0_0_1(),
        //V0_0_0_2(),
        //V0_0_0_3(),
        // ...
    };

    private static ChangelogVersion V0_0_0_1() => new()
    {
        Version = new(0, 0, 0, 1),
        Date = "2025-01-01",
        Title = "Initial Release",
        TitleColor = Blue,
        Description = "Sample short description.",
        Entries = new List<ChangelogEntry>
        {
            Header("New Features", Green),
            Entry("Feature 1: Amazing functionality"),
            Entry("Feature 2: Cool new tool"),
                Entry("Feature 2.1: ...", null, 1),
                Entry("Feature 2.2: ...", null, 1),

            Separator(),

            Header("Known Issues", Orange),
            Entry("Minor bug with settings UI"),

            Button("Check out the GitHub Repo", null, "Click me!", White, Blue, (e) => { CommonHelper.OpenUrl("https://github.com/Aspher0/NoireLib"); }),

            Raw(() => { ImGui.TextColored(Blue, "This is some raw code!"); }),
        }
    };
}
