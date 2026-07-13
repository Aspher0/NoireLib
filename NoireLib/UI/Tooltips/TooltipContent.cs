using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The content of a custom tooltip, built from inline segments (text, icons, images...).<br/>
/// Segments flow on the same line, vertically centered against each other, until <see cref="AddNewLine"/> or <see cref="AddSeparator"/> is called.<br/>
/// Example: <c>new TooltipContent().AddText("CTRL + ").AddIcon(FontAwesomeIcon.Mouse)</c>.
/// </summary>
public sealed class TooltipContent
{
    private enum SegmentKind
    {
        Text,
        Icon,
        Image,
        Spacing,
        NewLine,
        Separator,
        Custom,
    }

    private sealed class Segment
    {
        public SegmentKind Kind;
        public string? Text;
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
    /// <param name="color">An optional text color. When <see langword="null"/>, the tooltip text color is used.</param>
    /// <returns>This <see cref="TooltipContent"/> instance, for chaining.</returns>
    public TooltipContent AddText(string text, Vector4? color = null)
    {
        segments.Add(new Segment { Kind = SegmentKind.Text, Text = text ?? string.Empty, Color = color });
        return this;
    }

    /// <summary>
    /// Adds a FontAwesome icon segment.
    /// </summary>
    /// <param name="icon">The icon to display.</param>
    /// <param name="color">An optional icon color. When <see langword="null"/>, the tooltip text color is used.</param>
    /// <returns>This <see cref="TooltipContent"/> instance, for chaining.</returns>
    public TooltipContent AddIcon(FontAwesomeIcon icon, Vector4? color = null)
    {
        segments.Add(new Segment { Kind = SegmentKind.Icon, Icon = icon, Color = color });
        return this;
    }

    /// <summary>
    /// Adds an image segment.
    /// </summary>
    /// <param name="image">The image source to display.</param>
    /// <param name="size">The display size in pixels. When <see langword="null"/>, the native size of the texture is used, falling back to a text-line-sized square while loading.</param>
    /// <returns>This <see cref="TooltipContent"/> instance, for chaining.</returns>
    public TooltipContent AddImage(UiImageSource image, Vector2? size = null)
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
    /// <returns>This <see cref="TooltipContent"/> instance, for chaining.</returns>
    public TooltipContent AddImage(string filePath, Vector2? size = null)
        => AddImage(UiImageSource.FromFile(filePath), size);

    /// <summary>
    /// Adds an image segment from a game icon id.
    /// </summary>
    /// <param name="gameIconId">The id of the game icon.</param>
    /// <param name="size">The display size in pixels. When <see langword="null"/>, the native size of the texture is used.</param>
    /// <returns>This <see cref="TooltipContent"/> instance, for chaining.</returns>
    public TooltipContent AddImage(uint gameIconId, Vector2? size = null)
        => AddImage(UiImageSource.FromGameIcon(gameIconId), size);

    /// <summary>
    /// Adds an image segment from an existing texture wrap. The wrap stays owned by the caller.
    /// </summary>
    /// <param name="textureWrap">The texture wrap to display.</param>
    /// <param name="size">The display size in pixels. When <see langword="null"/>, the native size of the texture is used.</param>
    /// <returns>This <see cref="TooltipContent"/> instance, for chaining.</returns>
    public TooltipContent AddImage(IDalamudTextureWrap textureWrap, Vector2? size = null)
        => AddImage(UiImageSource.FromWrap(textureWrap), size);

    /// <summary>
    /// Adds a horizontal spacing segment on the current line.
    /// </summary>
    /// <param name="width">The width of the spacing in pixels.</param>
    /// <returns>This <see cref="TooltipContent"/> instance, for chaining.</returns>
    public TooltipContent AddSpacing(float width)
    {
        segments.Add(new Segment { Kind = SegmentKind.Spacing, SpacingWidth = width });
        return this;
    }

    /// <summary>
    /// Ends the current line: the next segments will be placed on a new line.
    /// </summary>
    /// <returns>This <see cref="TooltipContent"/> instance, for chaining.</returns>
    public TooltipContent AddNewLine()
    {
        segments.Add(new Segment { Kind = SegmentKind.NewLine });
        return this;
    }

    /// <summary>
    /// Adds a horizontal separator line. Also ends the current line.
    /// </summary>
    /// <returns>This <see cref="TooltipContent"/> instance, for chaining.</returns>
    public TooltipContent AddSeparator()
    {
        segments.Add(new Segment { Kind = SegmentKind.Separator });
        return this;
    }

    /// <summary>
    /// Adds a custom segment: the action is invoked in place and can draw anything using ImGui.<br/>
    /// Custom segments are drawn in the natural flow of the line, without vertical centering.
    /// </summary>
    /// <param name="draw">The action drawing the segment.</param>
    /// <returns>This <see cref="TooltipContent"/> instance, for chaining.</returns>
    public TooltipContent AddCustom(Action draw)
    {
        if (draw == null)
            throw new ArgumentNullException(nameof(draw), "Draw action cannot be null.");

        segments.Add(new Segment { Kind = SegmentKind.Custom, Custom = draw });
        return this;
    }

    /// <summary>
    /// Creates a <see cref="TooltipContent"/> containing a single text segment.
    /// </summary>
    /// <param name="text">The text of the tooltip.</param>
    public static implicit operator TooltipContent(string text)
        => new TooltipContent().AddText(text);

    /// <summary>
    /// Draws the content inside the current ImGui window, line by line with vertical centering.
    /// </summary>
    internal void Draw()
    {
        var line = new List<Segment>();
        var firstLine = true;

        foreach (var segment in segments)
        {
            if (segment.Kind == SegmentKind.NewLine || segment.Kind == SegmentKind.Separator)
            {
                FlushLine(line, firstLine);
                firstLine = false;
                line.Clear();

                if (segment.Kind == SegmentKind.Separator)
                    ImGui.Separator();

                continue;
            }

            line.Add(segment);
        }

        FlushLine(line, firstLine);
    }

    private static void FlushLine(List<Segment> line, bool isFirstLine)
    {
        if (line.Count == 0)
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

        // Realign the cursor under the tallest segment of the line so the next line starts below it.
        ImGui.SetCursorPosY(startY + maxHeight + ImGui.GetStyle().ItemSpacing.Y);
    }

    private static float MeasureHeight(Segment segment)
    {
        switch (segment.Kind)
        {
            case SegmentKind.Text:
                return ImGui.CalcTextSize(segment.Text ?? string.Empty).Y;

            case SegmentKind.Icon:
                using (ImRaii.PushFont(UiBuilder.IconFont))
                    return ImGui.CalcTextSize(segment.Icon.ToIconString()).Y;

            case SegmentKind.Image:
                return ResolveImageSize(segment).Y;

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

    private static void DrawSegment(Segment segment)
    {
        switch (segment.Kind)
        {
            case SegmentKind.Text:
                using (ImRaii.PushColor(ImGuiCol.Text, segment.Color ?? Vector4.One, segment.Color.HasValue))
                    ImGui.TextUnformatted(segment.Text ?? string.Empty);
                break;

            case SegmentKind.Icon:
                using (ImRaii.PushColor(ImGuiCol.Text, segment.Color ?? Vector4.One, segment.Color.HasValue))
                using (ImRaii.PushFont(UiBuilder.IconFont))
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

            case SegmentKind.Spacing:
                ImGui.Dummy(new Vector2(segment.SpacingWidth, 0f));
                break;

            case SegmentKind.Custom:
                segment.Custom?.Invoke();
                break;
        }
    }
}
