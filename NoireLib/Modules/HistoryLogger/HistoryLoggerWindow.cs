using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using NoireLib.Core.Modules;
using System.Numerics;

namespace NoireLib.HistoryLogger;

/// <summary>
/// The window class that represents the user interface window for displaying and managing history log entries in <see cref="NoireHistoryLogger"/>.
/// </summary>
public class HistoryLoggerWindow : NoireModuleWindowBase<NoireHistoryLogger>
{
    private readonly HistoryLogDraw draw;

    /// <summary>Gets or sets the name of the display window.</summary>
    public override string DisplayWindowName { get; set; } = "History Logger";

    /// <summary>Initializes a new instance of the <see cref="HistoryLoggerWindow"/> class.</summary>
    /// <param name="noireHistoryLogger">The parent module instance.</param>
    public HistoryLoggerWindow(NoireHistoryLogger noireHistoryLogger)
        : base(noireHistoryLogger, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        draw = new HistoryLogDraw(noireHistoryLogger);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        UpdateTitleBarButtons();
    }

    /// <summary>Draws the content of the history logger window.</summary>
    public override void Draw() => draw.Draw();

    /// <inheritdoc />
    public override void Dispose() { }
}
