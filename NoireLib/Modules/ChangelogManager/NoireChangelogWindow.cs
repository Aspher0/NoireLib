using NoireLib.Localizer;
using NoireLib.UI;
using System.Numerics;

namespace NoireLib.Changelog;

/// <summary>
/// The changelog as a skinned window, shown by <see cref="NoireChangelogManager"/> in place of <see cref="ChangelogWindow"/>
/// once the plugin registers skins. A skin restyles it with a view of this type; Stock draws the built-in window's content.
/// </summary>
public sealed class NoireChangelogWindow : NoireSkinnedWindow
{
    internal NoireChangelogWindow(NoireChangelogManager manager)
        : base("noire-changelog", NoireStrings.ChangelogTitle)
    {
        Manager = manager;
        DefaultSize = new Vector2(750f, 500f);
        MinimumSize = new Vector2(520f, 320f);

        Add("versions", new Part(manager, ChangelogPart.Versions) { Name = NoireStrings.ChangelogVersions });
        Add("entries", new Part(manager, ChangelogPart.Entries) { Name = NoireStrings.ChangelogEntries, Grows = true, CanMove = false });
        Add("footer", new Part(manager, ChangelogPart.Footer) { Name = NoireStrings.ChangelogFooter, CanHide = true });
    }

    /// <summary>The manager: its versions, the selected one and the last seen one.</summary>
    public NoireChangelogManager Manager { get; }

    private enum ChangelogPart
    {
        Versions,
        Entries,
        Footer,
    }

    private sealed class Part(NoireChangelogManager manager, ChangelogPart part) : Component
    {
        public NoireChangelogManager Manager { get; } = manager;

        public ChangelogPart Kind { get; } = part;

        protected internal override NoireView? DefaultView() => new PartView();
    }

    private sealed class PartView : NoireView<Part>
    {
        protected internal override void Draw(Part target)
        {
            switch (target.Kind)
            {
                case ChangelogPart.Versions:
                    ChangelogDraw.VersionSelector(target.Manager);
                    break;
                case ChangelogPart.Entries:
                    ChangelogDraw.Content(target.Manager, Area.Size.Y);
                    break;
                case ChangelogPart.Footer:
                    ChangelogDraw.Footer(target.Manager);
                    break;
            }
        }
    }
}
