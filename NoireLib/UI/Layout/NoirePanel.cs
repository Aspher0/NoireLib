using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A container that paints chrome around whatever is drawn inside it: a bordered frame, a filled plate, an optional
/// header, and the room to hold them apart.
/// </summary>
/// <remarks>
/// <see cref="NoireShapes"/> paints a box between two points, which is only useful once something knows where those
/// points are. That is the whole job here: run the body, measure what it came to, and paint the box behind it. The
/// alternative every plugin writes by hand is to guess a height, or to draw the box from the height the same content
/// happened to have last frame, which lags by a frame the moment anything inside it animates.<br/>
/// Bodies are taken rather than begun and ended, per the library's scope rule, so there is nothing to close and a body
/// that throws still leaves the draw list balanced.
/// </remarks>
/// <example>
/// <code>
/// NoirePanel.Frame(() =>
/// {
///     NoireText.Draw("Selected index");
///     NoireText.Draw("14");
/// },
/// new FrameStyle { TickLength = 11f });
/// </code>
/// </example>
[NoireFacade]
public static class NoirePanel
{
    /// <summary>
    /// Draws a bordered frame around a body.
    /// </summary>
    /// <param name="body">The drawing to put inside.</param>
    /// <param name="style">The frame's look. When <see langword="null"/>, the theme's.</param>
    /// <param name="options">How the panel holds its body. When <see langword="null"/>, the defaults.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Frame(Action body, FrameStyle? style = null, PanelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Frame(body, static b => b(), style, options);
    }

    /// <summary>
    /// Draws a bordered frame around a body.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="state">Passed to <paramref name="body"/>, so the body can stay a static lambda.</param>
    /// <param name="body">The drawing to put inside.</param>
    /// <param name="style">The frame's look. When <see langword="null"/>, the theme's.</param>
    /// <param name="options">How the panel holds its body. When <see langword="null"/>, the defaults.</param>
    public static void Frame<TState>(TState state, Action<TState> body, FrameStyle? style = null, PanelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Draw(state, body, style, static (min, max, s) => NoireShapes.Frame(min, max, s), options);
    }

    /// <summary>
    /// Draws a filled plate under a body.
    /// </summary>
    /// <param name="body">The drawing to put inside.</param>
    /// <param name="style">The plate's look. When <see langword="null"/>, the theme's.</param>
    /// <param name="options">How the panel holds its body. When <see langword="null"/>, the defaults.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Plate(Action body, PlateStyle? style = null, PanelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Plate(body, static b => b(), style, options);
    }

    /// <summary>
    /// Draws a filled plate under a body.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="state">Passed to <paramref name="body"/>, so the body can stay a static lambda.</param>
    /// <param name="body">The drawing to put inside.</param>
    /// <param name="style">The plate's look. When <see langword="null"/>, the theme's.</param>
    /// <param name="options">How the panel holds its body. When <see langword="null"/>, the defaults.</param>
    public static void Plate<TState>(TState state, Action<TState> body, PlateStyle? style = null, PanelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Draw(state, body, style, static (min, max, s) => NoireShapes.Plate(min, max, s), options);
    }

    /// <summary>
    /// Runs a body inside a measured box and paints the chrome behind it.
    /// </summary>
    /// <remarks>
    /// The order on screen is chrome then body; the order in time is body then chrome, because the body is what says
    /// how tall the box is. Splitting the draw list is what lets those two disagree.
    /// </remarks>
    private static void Draw<TState, TStyle>(
        TState state,
        Action<TState> body,
        TStyle? style,
        Action<Vector2, Vector2, TStyle?> chrome,
        PanelOptions? options)
        where TStyle : class
    {
        NoireUI.EnsureFrameServices();

        var settings = options ?? DefaultOptions;
        var padding = NoireUI.Scaled(settings.Padding);
        var width = settings.Width > 0f ? NoireUI.Scaled(settings.Width) : NoireLayout.ContentWidth();
        var origin = ImGui.GetCursorScreenPos();
        var inner = MathF.Max(1f, width - (padding.X * 2f));

        var height = 0f;
        var split = BeginChrome();

        try
        {
            ImGui.SetCursorScreenPos(origin + padding);
            ImGui.BeginGroup();

            try
            {
                // The body is told the width it has, because nothing else can tell it: ImGui's content region always
                // reports the window's right edge however deeply anything is nested, so a panel that did not state its
                // own width would have every wrapping thing inside it run out to the window instead.
                NoireLayout.WrapText(inner, (state, body, settings, inner), static args =>
                {
                    DrawHeader(args.settings, args.inner);
                    NoireLayout.ItemWidth(args.inner, (args.state, args.body), static b => b.body(b.state));
                });
            }
            finally
            {
                ImGui.EndGroup();
            }

            // Rounded up to a whole pixel. The box is placed from this number and the next thing on the page is placed
            // after it, so a fractional height puts both on a sub-pixel boundary and any change in the body walks the
            // whole panel across a pixel while it animates.
            height = MathF.Ceiling(ImGui.GetItemRectSize().Y + (padding.Y * 2f));

            ToChrome();
            chrome(origin, origin + new Vector2(width, height), style);
            ToContent();
        }
        finally
        {
            EndChrome(split);
        }

        // The panel is one item as far as everything after it is concerned, whatever the body did with the cursor.
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>
    /// Draws the header and the hairline under it, when there is one.
    /// </summary>
    private static void DrawHeader(PanelOptions options, float inner)
    {
        if (string.IsNullOrEmpty(options.Header))
            return;

        var theme = NoireTheme.Current;
        var color = options.HeaderColor ?? theme.Resolve(ThemeColor.TextMuted);

        using (UiPush.Color(ImGuiCol.Text, color))
            NoireText.Tracked(options.Header, options.HeaderTracking, options.HeaderSize);

        if (options.HeaderRule)
        {
            var at = ImGui.GetCursorScreenPos();
            var y = MathF.Floor(at.Y + NoireUI.Scaled(options.HeaderGap * 0.5f));

            NoireShapes.Rect(
                new Vector2(at.X, y),
                new Vector2(at.X + inner, y + 1f),
                ColorHelper.ScaleAlpha(color, 0.35f));
        }

        ImGui.Dummy(new Vector2(1f, NoireUI.Scaled(options.HeaderGap)));
    }

    private static readonly PanelOptions DefaultOptions = new();

    /// <summary>
    /// The draw lists this call is nested inside, and whether each entry is the one that split its list.
    /// </summary>
    /// <remarks>
    /// A draw list can only be split once at a time, so a panel inside a panel must not split again. It does not need
    /// to: chrome from every depth shares one channel and content shares the other, so an inner panel's chrome lands
    /// on top of its parent's chrome and still behind all of the content, which is the order the nesting means.<br/>
    /// Tracked per draw list rather than as a plain depth count because a body may open a child window, and a child
    /// window draws to a list of its own that nothing has split yet.
    /// </remarks>
    private static readonly List<(nint List, bool Split)> ChromeStack = [];

    private static unsafe bool BeginChrome()
    {
        // The window's own list rather than a redirected one: channels are split on the list the panel's items land
        // on, and an item lands on the window's list whatever a shape redirect points at.
        using var draw = UiDraw.BeginWindow();

        var list = draw.List;

        // Pushed even when there is no list to split, because EndChrome pops unconditionally: returning early without
        // pushing would pop the entry belonging to the panel enclosing this one, and every panel after it would then
        // read the wrong list as its parent's.
        if (list.IsNull)
        {
            ChromeStack.Add((0, false));
            return false;
        }

        var handle = (nint)list.Handle;
        var split = ChromeStack.Count == 0 || ChromeStack[^1].List != handle;

        if (split)
        {
            list.ChannelsSplit(2);
            list.ChannelsSetCurrent(ContentChannel);
        }

        ChromeStack.Add((handle, split));
        return split;
    }

    private static void EndChrome(bool split)
    {
        if (ChromeStack.Count > 0)
            ChromeStack.RemoveAt(ChromeStack.Count - 1);

        if (!split)
            return;

        using var draw = UiDraw.BeginWindow();

        if (!draw.List.IsNull)
            draw.List.ChannelsMerge();
    }

    private static void ToChrome() => SetChannel(ChromeChannel);

    private static void ToContent() => SetChannel(ContentChannel);

    /// <summary>
    /// Points the window's list at one of the two channels <see cref="BeginChrome"/> split it into.
    /// </summary>
    private static void SetChannel(int channel)
    {
        using var draw = UiDraw.BeginWindow();

        if (!draw.List.IsNull)
            draw.List.ChannelsSetCurrent(channel);
    }

    private const int ChromeChannel = 0;
    private const int ContentChannel = 1;
}
