using NoireLib.Localizer;
using NoireLib.UI;
using System.Numerics;

namespace NoireLib.HistoryLogger;

/// <summary>
/// The history log as a skinned window, shown by <see cref="NoireHistoryLogger"/> in place of <see cref="HistoryLoggerWindow"/>
/// once the plugin registers skins. A skin restyles it with a view of this type; Stock draws the built-in window's content.
/// </summary>
public sealed class NoireHistoryLogWindow : NoireSkinnedWindow
{
    private readonly HistoryLogDraw draw;

    internal NoireHistoryLogWindow(NoireHistoryLogger logger)
        : base("noire-logs", NoireStrings.LogsTitle)
    {
        Logger = logger;
        draw = new HistoryLogDraw(logger);
        DefaultSize = new Vector2(980f, 640f);
        MinimumSize = new Vector2(800f, 420f);

        Add("filters", new Part(draw, filters: true) { Name = NoireStrings.LogsFilters, CanHide = true });
        Add("entries", new Part(draw, filters: false) { Name = NoireStrings.LogsEntries, Grows = true, CanMove = false });
    }

    /// <summary>The logger.</summary>
    public NoireHistoryLogger Logger { get; }

    /// <summary>The window's search, filters, sort and page over the logger's entries.</summary>
    public HistoryLogView Log => draw.View;

    private sealed class Part(HistoryLogDraw draw, bool filters) : Component
    {
        public HistoryLogDraw Draw { get; } = draw;

        public bool Filters { get; } = filters;

        protected internal override NoireView? DefaultView() => new PartView();
    }

    private sealed class PartView : NoireView<Part>
    {
        protected internal override void Draw(Part target)
        {
            if (target.Filters)
                target.Draw.Header();
            else
                target.Draw.Entries();
        }
    }
}
