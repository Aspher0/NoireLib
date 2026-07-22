using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

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

    /// <summary>
    /// What one measurement depended on, so a segment measured under the same conditions reuses its answer.
    /// </summary>
    /// <remarks>
    /// The same facts <see cref="UiTextMeasureCache"/> keys on, read once per draw instead of once per segment: the
    /// shared cache still answers correctly, but answering costs the reads and a string hash per ask, and a tooltip
    /// asks for every segment on every frame it is open. Holding the answer on the segment brings a warm draw down to
    /// four compares.
    /// </remarks>
    private readonly record struct MeasureStamp(nint Font, float SizePx, float Scale, int Generation);

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

        /// <summary>
        /// The text encoded once, for the segments whose text never changes. ImGui takes UTF-8, so a managed string
        /// handed over per frame is re-encoded per frame; this pays that cost when the segment is added instead.
        /// </summary>
        public byte[]? Utf8;

        /// <summary>The conditions <see cref="MeasuredSize"/> was taken under.</summary>
        public MeasureStamp Stamp;

        /// <summary>The measured size of the text or glyph, valid while <see cref="Stamp"/> matches the frame's.</summary>
        public Vector2 MeasuredSize;
    }

    private readonly List<Segment> segments = new();

    /// <summary>
    /// The runs of same-line segments, one entry per line, derived from where the break segments sit.
    /// </summary>
    /// <remarks>
    /// Content is add-only, so the grouping is a property of the segment list rather than of any frame; it is rebuilt
    /// only when a segment has been added. Grouping per draw instead cost a pooled buffer rented and cleared on every
    /// frame a tooltip was open.
    /// </remarks>
    private readonly List<(int Start, int Count, bool SeparatorAfter)> lines = new();

    /// <summary>How many segments <see cref="lines"/> was built from, which is a version because content is add-only.</summary>
    private int linesBuiltFrom = -1;

    /// <summary>The longest line, so the height pass can borrow one buffer sized for any of them.</summary>
    private int longestLine;

    /// <summary>Whether any text segment resolves through a provider, so a draw without one skips the resolve pass.</summary>
    private bool hasProviders;

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
        text ??= string.Empty;

        segments.Add(new Segment
        {
            Kind = SegmentKind.Text,
            Text = text,
            RuntimeText = text,
            Utf8 = Encoding.UTF8.GetBytes(text),
            Color = color,
        });

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
        key ??= string.Empty;

        segments.Add(new Segment
        {
            Kind = SegmentKind.KeyCap,
            Text = key,
            RuntimeText = key,
            Utf8 = Encoding.UTF8.GetBytes(key),
        });

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
        if (segments.Count == 0)
            return;

        if (linesBuiltFrom != segments.Count)
            RebuildLines();

        // Dynamic text is resolved once per draw, so the provider runs a single time and the measure and draw passes
        // agree. Static text carries its own copy from the moment it was added, so with no provider there is nothing
        // to resolve.
        if (hasProviders)
        {
            foreach (var segment in segments)
            {
                if (segment.TextProvider != null)
                    segment.RuntimeText = segment.TextProvider() ?? string.Empty;
            }
        }

        // Everything a measurement depends on, read once for the whole draw rather than once per segment ask.
        var stamp = new MeasureStamp(
            UiTextMeasureCache.CurrentFont(),
            ImGui.GetFontSize(),
            NoireUI.Scale,
            UiFontCache.Generation);

        var keyCapPadding = NoireUI.Scaled(new Vector2(5f, 2f));
        var lineHeight = ImGui.GetTextLineHeight();

        // The heights of one line's segments, measured once and read back when each segment is centered. Borrowed
        // rather than allocated because this runs on every frame a tooltip is open; a buffer of floats is returned to
        // the pool uncleared, so the round trip is cheap.
        using var heights = PooledBuffer<float>.Rent(longestLine);
        var lineHeights = heights.Span;

        var firstLine = true;

        foreach (var (start, count, separatorAfter) in lines)
        {
            SpaceBeforeLine(firstLine);
            DrawLine(start, count, firstLine, stamp, keyCapPadding, lineHeight, lineHeights);
            firstLine = false;

            if (separatorAfter)
                ImGui.Separator();
        }
    }

    /// <summary>
    /// Rebuilds the line runs from the break segments. Runs when a segment has been added, never per frame.
    /// </summary>
    /// <remarks>
    /// Every break closes the run before it, including an empty one: two consecutive breaks are a blank line, which
    /// <see cref="DrawLine"/> gives its height. The final run is closed by the end of the list rather than by a break.
    /// </remarks>
    private void RebuildLines()
    {
        lines.Clear();
        longestLine = 0;
        hasProviders = false;

        var start = 0;

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];

            if (segment.TextProvider != null)
                hasProviders = true;

            if (segment.Kind is not (SegmentKind.NewLine or SegmentKind.Separator))
                continue;

            lines.Add((start, index - start, segment.Kind == SegmentKind.Separator));
            longestLine = Math.Max(longestLine, index - start);
            start = index + 1;
        }

        lines.Add((start, segments.Count - start, false));
        longestLine = Math.Max(longestLine, segments.Count - start);
        linesBuiltFrom = segments.Count;
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

    /// <summary>
    /// Draws one run of same-line segments, vertically centered against the tallest of them.
    /// </summary>
    /// <remarks>
    /// The heights are taken in one pass and read back in the second, rather than measured again for each segment's
    /// centering. A segment as tall as the line is drawn without touching the cursor's vertical position at all, which
    /// makes the single-segment line, the shape almost every tooltip is, cost no centering work.
    /// </remarks>
    private void DrawLine(int start, int count, bool isFirstLine, in MeasureStamp stamp, Vector2 keyCapPadding, float lineHeight, Span<float> lineHeights)
    {
        if (count == 0)
        {
            // An empty line (e.g. two consecutive new lines) still takes vertical space, except at the very end of the content.
            if (!isFirstLine)
                ImGui.Dummy(new Vector2(0f, lineHeight));
            return;
        }

        var maxHeight = 0f;

        for (var index = 0; index < count; index++)
        {
            var height = MeasureHeight(segments[start + index], stamp, keyCapPadding, lineHeight);
            lineHeights[index] = height;
            maxHeight = MathF.Max(maxHeight, height);
        }

        var startY = ImGui.GetCursorPosY();

        for (var index = 0; index < count; index++)
        {
            if (index > 0)
                ImGui.SameLine(0f, 0f);

            // The tallest segment sits where the cursor already is; only the shorter ones are moved down to centre.
            // After a SameLine the cursor is back at the previous segment's offset, so a segment following a moved
            // one has to be placed even when its own offset is zero.
            var offset = (maxHeight - lineHeights[index]) / 2f;

            if (offset > 0f || index > 0)
                ImGui.SetCursorPosY(startY + offset);

            DrawSegment(segments[start + index], keyCapPadding);
        }

        // Realign the cursor under the tallest segment of the line so the next line starts below it. The gap between
        // lines is added in front of the next one by SpaceBeforeLine, never here.
        ImGui.SetCursorPosY(startY + maxHeight);
    }

    /// <summary>
    /// The height a segment takes on its line, from its cached measurement when the conditions have not moved.
    /// </summary>
    /// <remarks>
    /// Measured against the font in hand rather than through <c>CalcSize</c>, which would resolve and push one of its
    /// own: a segment is drawn in whatever the caller pushed, and a height taken in a different font puts the line's
    /// baseline in the wrong place. The answer is held on the segment against everything it depended on, so a warm
    /// frame compares a stamp instead of hashing text into the shared measure cache.
    /// </remarks>
    private static float MeasureHeight(Segment segment, in MeasureStamp stamp, Vector2 keyCapPadding, float lineHeight)
    {
        switch (segment.Kind)
        {
            case SegmentKind.Text:
                return MeasureText(segment, stamp).Y;

            case SegmentKind.Icon:
                if (segment.Stamp != stamp)
                {
                    using (UiPush.Font(UiBuilder.IconFont))
                        segment.MeasuredSize = NoireText.CalcSizeInCurrentFont(UiValueText.Icon(segment.Icon));

                    segment.Stamp = stamp;
                }

                return segment.MeasuredSize.Y;

            case SegmentKind.Image:
                return ResolveImageSize(segment, lineHeight).Y;

            case SegmentKind.KeyCap:
                return MeasureText(segment, stamp).Y + (keyCapPadding.Y * 2f);

            default:
                return lineHeight;
        }
    }

    /// <summary>
    /// The measured size of a text-bearing segment, refreshed when the stamp moved or a provider changed the text.
    /// </summary>
    private static Vector2 MeasureText(Segment segment, in MeasureStamp stamp)
    {
        // Provider text can change without anything in the stamp moving, so it goes through the shared cache, which
        // is keyed on the text itself. Static text short-circuits on the stamp alone.
        if (segment.TextProvider != null)
            return NoireText.CalcSizeInCurrentFont(segment.RuntimeText ?? string.Empty);

        if (segment.Stamp != stamp)
        {
            segment.MeasuredSize = NoireText.CalcSizeInCurrentFont(segment.RuntimeText ?? string.Empty);
            segment.Stamp = stamp;
        }

        return segment.MeasuredSize;
    }

    private static Vector2 ResolveImageSize(Segment segment, float lineHeight)
    {
        if (segment.ImageSize.HasValue)
            return segment.ImageSize.Value;

        var nativeSize = segment.Image?.GetNativeSize();
        if (nativeSize.HasValue)
            return nativeSize.Value;

        return new Vector2(lineHeight, lineHeight);
    }

    private static void DrawSegment(Segment segment, Vector2 keyCapPadding)
    {
        switch (segment.Kind)
        {
            case SegmentKind.Text:
                using (UiPush.Color(ImGuiCol.Text, segment.Color ?? Vector4.One, segment.Color.HasValue))
                {
                    // The pre-encoded bytes when the text is static, so ImGui is not handed a managed string to
                    // re-encode on every frame the segment is drawn.
                    if (segment.Utf8 != null && segment.TextProvider == null)
                        ImGui.TextUnformatted(segment.Utf8.AsSpan());
                    else
                        ImGui.TextUnformatted(segment.RuntimeText ?? string.Empty);
                }

                break;

            case SegmentKind.Icon:
                using (UiPush.Color(ImGuiCol.Text, segment.Color ?? Vector4.One, segment.Color.HasValue))
                using (UiPush.Font(UiBuilder.IconFont))
                    ImGui.TextUnformatted(UiValueText.Icon(segment.Icon));
                break;

            case SegmentKind.Image:
                var size = ResolveImageSize(segment, ImGui.GetTextLineHeight());
                var wrap = segment.Image?.GetWrap();
                if (wrap != null)
                    ImGui.Image(wrap.Handle, size);
                else
                    ImGui.Dummy(size);
                break;

            case SegmentKind.KeyCap:
                DrawKeyCap(segment, keyCapPadding);
                break;

            case SegmentKind.Spacing:
                ImGui.Dummy(new Vector2(NoireUI.Scaled(segment.SpacingWidth), 0f));
                break;

            case SegmentKind.Custom:
                segment.Custom?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Draws one keycap tile around its label, using the size the height pass already measured.
    /// </summary>
    private static void DrawKeyCap(Segment segment, Vector2 padding)
    {
        var position = ImGui.GetCursorScreenPos();
        var textSize = segment.MeasuredSize;
        var tileSize = new Vector2(textSize.X + (padding.X * 2f), textSize.Y + (padding.Y * 2f));
        var rounding = NoireUI.Scaled(3f);

        using var draw = UiDraw.BeginMethod();

        var drawList = draw.List;

        if (!drawList.IsNull)
        {
            drawList.AddRectFilled(position, position + tileSize, ImGui.GetColorU32(ImGuiCol.FrameBg), rounding);
            drawList.AddRect(position, position + tileSize, ImGui.GetColorU32(ImGuiCol.Border), rounding);
            drawList.AddText(position + padding, ImGui.GetColorU32(ImGuiCol.Text), segment.Utf8.AsSpan());
        }

        // Reserved whether or not anything was painted, so a caller's layout does not move depending on whether there
        // was a list to paint into.
        ImGui.Dummy(tileSize);
    }
}
