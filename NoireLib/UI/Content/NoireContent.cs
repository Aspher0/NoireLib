using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A reusable block of rich inline content, built from segments (text, icons, images, keycaps, arbitrary widgets).<br/>
/// Segments flow on the same line, vertically centered against each other, until <see cref="AddNewLine"/> or
/// <see cref="AddSeparator"/> starts a new one.<br/>
/// It is not tied to any one surface: a custom tooltip (<see cref="NoireTooltip"/>) renders one, and so can any window,
/// label or cell of your own through the public <see cref="Draw"/>.<br/>
/// Example: <c>new NoireContent().AddText("Hold ").AddKeyCap("Ctrl").AddText(" and scroll")</c>.
/// </summary>
[NoireFacadeFactory]
public sealed class NoireContent
{
    private enum SegmentKind
    {
        Text,
        Icon,
        Image,
        KeyCap,
        Spacing,
        NewLine,
        Separator,
        Custom,
    }

    private sealed class Segment
    {
        public SegmentKind Kind;
        public string? Text;
        public Func<string>? TextProvider;
        public string? RuntimeText;
        public Vector4? Color;
        public FontAwesomeIcon Icon;
        public UiImageSource? Image;
        public Vector2? ImageSize;
        public float SpacingWidth;
        public Action? Custom;
    }

    private readonly List<Segment> segments = new();

    /// <summary>
    /// Gets whether this content has no segments.
    /// </summary>
    public bool IsEmpty => segments.Count == 0;

    /// <summary>
    /// Adds a text segment.
    /// </summary>
    /// <param name="text">The text to display.</param>
    /// <param name="color">An optional text color. When <see langword="null"/>, the current text color is used.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddText(string text, Vector4? color = null)
    {
        segments.Add(new Segment { Kind = SegmentKind.Text, Text = text ?? string.Empty, Color = color });
        return this;
    }

    /// <summary>
    /// Adds a text segment whose text is evaluated on every draw, for a value that changes over time.<br/>
    /// The provider runs once per draw. Keep it cheap, and note that a formatted string allocates each frame.
    /// </summary>
    /// <param name="textProvider">Produces the text to display, called on every draw.</param>
    /// <param name="color">An optional text color. When <see langword="null"/>, the current text color is used.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="textProvider"/> is <see langword="null"/>.</exception>
    public NoireContent AddText(Func<string> textProvider, Vector4? color = null)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        segments.Add(new Segment { Kind = SegmentKind.Text, TextProvider = textProvider, Color = color });
        return this;
    }

    /// <summary>
    /// Adds a FontAwesome icon segment.
    /// </summary>
    /// <param name="icon">The icon to display.</param>
    /// <param name="color">An optional icon color. When <see langword="null"/>, the current text color is used.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddIcon(FontAwesomeIcon icon, Vector4? color = null)
    {
        segments.Add(new Segment { Kind = SegmentKind.Icon, Icon = icon, Color = color });
        return this;
    }

    /// <summary>
    /// Adds a keycap segment: the label drawn in a small bordered tile, for spelling out a shortcut inline.<br/>
    /// The tile borrows the current theme's frame and border colors, so it sits naturally in whatever surface renders it.
    /// </summary>
    /// <param name="key">The key label, for example "Ctrl" or "F1".</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddKeyCap(string key)
    {
        segments.Add(new Segment { Kind = SegmentKind.KeyCap, Text = key ?? string.Empty });
        return this;
    }

    /// <summary>
    /// Adds an image segment.
    /// </summary>
    /// <param name="image">The image source to display.</param>
    /// <param name="size">The display size in real pixels, not scaled: it shares a space with the native texture size it
    /// falls back to. When <see langword="null"/>, the native size of the texture is used, falling back to a
    /// text-line-sized square while loading. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddImage(UiImageSource image, Vector2? size = null)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image), "Image source cannot be null.");

        segments.Add(new Segment { Kind = SegmentKind.Image, Image = image, ImageSize = size });
        return this;
    }

    /// <summary>
    /// Adds an image segment from an image file on disk.
    /// </summary>
    /// <param name="filePath">The path of the image file.</param>
    /// <param name="size">The display size in pixels. When <see langword="null"/>, the native size of the texture is used.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddImage(string filePath, Vector2? size = null)
        => AddImage(UiImageSource.FromFile(filePath), size);

    /// <summary>
    /// Adds an image segment from a game icon id.
    /// </summary>
    /// <param name="gameIconId">The id of the game icon.</param>
    /// <param name="size">The display size in pixels. When <see langword="null"/>, the native size of the texture is used.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddImage(uint gameIconId, Vector2? size = null)
        => AddImage(UiImageSource.FromGameIcon(gameIconId), size);

    /// <summary>
    /// Adds an image segment from an existing texture wrap. The wrap stays owned by the caller.
    /// </summary>
    /// <param name="textureWrap">The texture wrap to display.</param>
    /// <param name="size">The display size in pixels. When <see langword="null"/>, the native size of the texture is used.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddImage(IDalamudTextureWrap textureWrap, Vector2? size = null)
        => AddImage(UiImageSource.FromWrap(textureWrap), size);

    /// <summary>
    /// Adds a horizontal spacing segment on the current line.
    /// </summary>
    /// <param name="width">The width of the spacing, at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddSpacing(float width)
    {
        segments.Add(new Segment { Kind = SegmentKind.Spacing, SpacingWidth = width });
        return this;
    }

    /// <summary>
    /// Ends the current line: the next segments will be placed on a new line.
    /// </summary>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddNewLine()
    {
        segments.Add(new Segment { Kind = SegmentKind.NewLine });
        return this;
    }

    /// <summary>
    /// Adds a horizontal separator line. Also ends the current line.
    /// </summary>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddSeparator()
    {
        segments.Add(new Segment { Kind = SegmentKind.Separator });
        return this;
    }

    /// <summary>
    /// Adds a custom segment: the action is invoked in place and can draw anything using ImGui.<br/>
    /// Custom segments are drawn in the natural flow of the line, without vertical centering.
    /// </summary>
    /// <param name="draw">The action drawing the segment.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddCustom(Action draw)
    {
        if (draw == null)
            throw new ArgumentNullException(nameof(draw), "Draw action cannot be null.");

        segments.Add(new Segment { Kind = SegmentKind.Custom, Custom = draw });
        return this;
    }

    /// <summary>
    /// Creates a <see cref="NoireContent"/> containing a single text segment.
    /// </summary>
    /// <param name="text">The text of the content.</param>
    public static implicit operator NoireContent(string text)
        => new NoireContent().AddText(text);

    /// <summary>
    /// Draws the content at the current cursor, line by line with vertical centering.<br/>
    /// Call this from your own ImGui code to render the content anywhere; a custom tooltip renders it for you.
    /// </summary>
    public void Draw()
    {
        // Dynamic text is resolved once per draw, so the provider runs a single time and the measure and draw passes agree.
        foreach (var segment in segments)
        {
            if (segment.Kind == SegmentKind.Text)
                segment.RuntimeText = segment.TextProvider != null ? segment.TextProvider() ?? string.Empty : segment.Text ?? string.Empty;
            else if (segment.Kind == SegmentKind.KeyCap)
                segment.RuntimeText = segment.Text ?? string.Empty;
        }

        // Borrowed rather than allocated: content is the body of every custom tooltip, so this runs on every frame one
        // is open, and a fresh list per frame is garbage on the draw thread. A line cannot hold more segments than the
        // content has, which is what makes the length known before the loop starts.
        using var buffer = PooledBuffer<Segment>.Rent(segments.Count);

        var line = buffer.Span;
        var count = 0;
        var firstLine = true;

        foreach (var segment in segments)
        {
            if (segment.Kind == SegmentKind.NewLine || segment.Kind == SegmentKind.Separator)
            {
                SpaceBeforeLine(firstLine);
                FlushLine(line[..count], firstLine);
                firstLine = false;
                count = 0;

                if (segment.Kind == SegmentKind.Separator)
                    ImGui.Separator();

                continue;
            }

            line[count++] = segment;
        }

        SpaceBeforeLine(firstLine);
        FlushLine(line[..count], firstLine);
    }

    /// <summary>
    /// Puts the gap between two lines in front of the second one rather than after the first.
    /// </summary>
    /// <remarks>
    /// Trailing spacing after the last line is not free here: the line advance is a <c>SetCursorPosY</c> rather than a
    /// real item, and ImGui grows a window's content height to any cursor position set inside it without the
    /// compensation it applies to items. Spacing after the final line therefore became a permanent extra strip of
    /// padding along the bottom of every tooltip, leaving them visibly heavier underneath than on top.
    /// </remarks>
    /// <param name="isFirstLine">Whether the line about to be drawn is the first, which needs no gap in front of it.</param>
    private static void SpaceBeforeLine(bool isFirstLine)
    {
        if (!isFirstLine)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().ItemSpacing.Y);
    }

    private static void FlushLine(ReadOnlySpan<Segment> line, bool isFirstLine)
    {
        if (line.Length == 0)
        {
            // An empty line (e.g. two consecutive new lines) still takes vertical space, except at the very end of the content.
            if (!isFirstLine)
                ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight()));
            return;
        }

        var maxHeight = 0f;
        foreach (var segment in line)
            maxHeight = MathF.Max(maxHeight, MeasureHeight(segment));

        var startY = ImGui.GetCursorPosY();
        var first = true;

        foreach (var segment in line)
        {
            if (!first)
                ImGui.SameLine(0f, 0f);

            ImGui.SetCursorPosY(startY + ((maxHeight - MeasureHeight(segment)) / 2f));
            DrawSegment(segment);
            first = false;
        }

        // Realign the cursor under the tallest segment of the line so the next line starts below it. The gap between
        // lines is added in front of the next one by SpaceBeforeLine, never here.
        ImGui.SetCursorPosY(startY + maxHeight);
    }

    private static float MeasureHeight(Segment segment)
    {
        switch (segment.Kind)
        {
            // Measured against the font in hand rather than through CalcSize, which would resolve and push one of its
            // own: a segment is drawn in whatever the caller pushed, and a height taken in a different font puts the
            // line's baseline in the wrong place. The cache identifies the font, so the icon pass below cannot answer
            // for the text pass even when the two report the same size.
            case SegmentKind.Text:
                return NoireText.CalcSizeInCurrentFont(segment.RuntimeText ?? string.Empty).Y;

            case SegmentKind.Icon:
                using (UiPush.Font(UiBuilder.IconFont))
                    return NoireText.CalcSizeInCurrentFont(segment.Icon.ToIconString()).Y;

            case SegmentKind.Image:
                return ResolveImageSize(segment).Y;

            case SegmentKind.KeyCap:
                return NoireText.CalcSizeInCurrentFont(segment.RuntimeText ?? string.Empty).Y + KeyCapPadding.Y * 2f;

            default:
                return ImGui.GetTextLineHeight();
        }
    }

    private static Vector2 ResolveImageSize(Segment segment)
    {
        if (segment.ImageSize.HasValue)
            return segment.ImageSize.Value;

        var nativeSize = segment.Image?.GetNativeSize();
        if (nativeSize.HasValue)
            return nativeSize.Value;

        var lineHeight = ImGui.GetTextLineHeight();
        return new Vector2(lineHeight, lineHeight);
    }

    /// <summary>The inner padding of a keycap tile, at 100%.</summary>
    private static Vector2 KeyCapPadding => NoireUI.Scaled(new Vector2(5f, 2f));

    private static void DrawSegment(Segment segment)
    {
        switch (segment.Kind)
        {
            case SegmentKind.Text:
                using (UiPush.Color(ImGuiCol.Text, segment.Color ?? Vector4.One, segment.Color.HasValue))
                    ImGui.TextUnformatted(segment.RuntimeText ?? string.Empty);
                break;

            case SegmentKind.Icon:
                using (UiPush.Color(ImGuiCol.Text, segment.Color ?? Vector4.One, segment.Color.HasValue))
                using (UiPush.Font(UiBuilder.IconFont))
                    ImGui.TextUnformatted(segment.Icon.ToIconString());
                break;

            case SegmentKind.Image:
                var size = ResolveImageSize(segment);
                var wrap = segment.Image?.GetWrap();
                if (wrap != null)
                    ImGui.Image(wrap.Handle, size);
                else
                    ImGui.Dummy(size);
                break;

            case SegmentKind.KeyCap:
                DrawKeyCap(segment.RuntimeText ?? string.Empty);
                break;

            case SegmentKind.Spacing:
                ImGui.Dummy(new Vector2(NoireUI.Scaled(segment.SpacingWidth), 0f));
                break;

            case SegmentKind.Custom:
                segment.Custom?.Invoke();
                break;
        }
    }

    private static void DrawKeyCap(string label)
    {
        var padding = KeyCapPadding;
        var position = ImGui.GetCursorScreenPos();
        var textSize = NoireText.CalcSizeInCurrentFont(label);
        var tileSize = new Vector2(textSize.X + padding.X * 2f, textSize.Y + padding.Y * 2f);
        var rounding = NoireUI.Scaled(3f);

        using var draw = UiDraw.BeginMethod();

        var drawList = draw.List;

        if (!drawList.IsNull)
        {
            drawList.AddRectFilled(position, position + tileSize, ImGui.GetColorU32(ImGuiCol.FrameBg), rounding);
            drawList.AddRect(position, position + tileSize, ImGui.GetColorU32(ImGuiCol.Border), rounding);
            drawList.AddText(position + padding, ImGui.GetColorU32(ImGuiCol.Text), label);
        }

        // Reserved whether or not anything was painted, so a caller's layout does not move depending on whether there
        // was a list to paint into.
        ImGui.Dummy(tileSize);
    }
}
