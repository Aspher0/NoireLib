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
/// A reusable block of rich inline content built from segments (text, icons, images, keycaps, arbitrary widgets).
/// Segments flow on the same line, vertically centered against each other, until <see cref="AddNewLine"/> or
/// <see cref="AddSeparator"/> starts a new one. It is tied to no surface: <see cref="Draw"/> renders it anywhere.
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
    /// The same facts <see cref="UiTextMeasureCache"/> keys on, read once per draw rather than hashed per segment ask.
    /// </remarks>
    /// <param name="Font">Handle of the font in hand.</param>
    /// <param name="SizePx">Font size in pixels.</param>
    /// <param name="Scale">UI scale the measurement was taken at.</param>
    /// <param name="Generation">Font cache generation.</param>
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
        /// The text encoded once at add time, for a segment whose text never changes, so no per-frame re-encode.
        /// </summary>
        public byte[]? Utf8;

        /// <summary>The conditions <see cref="MeasuredSize"/> was taken under.</summary>
        public MeasureStamp Stamp;

        /// <summary>The measured size of the text or glyph, valid while <see cref="Stamp"/> matches the frame's.</summary>
        public Vector2 MeasuredSize;
    }

    private readonly List<Segment> segments = new();

    /// <summary>
    /// The runs of same-line segments, one entry per line, derived from where the break segments sit. Content is
    /// add-only, so this is rebuilt when a segment has been added rather than per draw.
    /// </summary>
    private readonly List<(int Start, int Count, bool SeparatorAfter)> lines = new();

    /// <summary>How many segments <see cref="lines"/> was built from, which serves as a version because content is add-only.</summary>
    private int linesBuiltFrom = -1;

    /// <summary>The longest line, so the height pass can borrow one buffer sized for any of them.</summary>
    private int longestLine;

    /// <summary>Whether any text segment resolves through a provider, so a draw without one skips the resolve pass.</summary>
    private bool hasProviders;

    /// <summary>Whether this content has no segments.</summary>
    public bool IsEmpty => segments.Count == 0;

    /// <summary>Adds a text segment.</summary>
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
    /// Adds a text segment whose text is resolved once per draw, for a value that changes over time.
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

    /// <summary>Adds a FontAwesome icon segment.</summary>
    /// <param name="icon">The icon to display.</param>
    /// <param name="color">An optional icon color. When <see langword="null"/>, the current text color is used.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddIcon(FontAwesomeIcon icon, Vector4? color = null)
    {
        segments.Add(new Segment { Kind = SegmentKind.Icon, Icon = icon, Color = color });
        return this;
    }

    /// <summary>
    /// Adds a keycap segment: the label drawn in a small bordered tile, in the current theme's frame and border colors.
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

    /// <summary>Adds an image segment.</summary>
    /// <param name="image">The image source to display.</param>
    /// <param name="size">Display size in real, unscaled pixels; when <see langword="null"/>, the texture's native
    /// size, falling back to a text-line-sized square while loading. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddImage(UiImageSource image, Vector2? size = null)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image), "Image source cannot be null.");

        segments.Add(new Segment { Kind = SegmentKind.Image, Image = image, ImageSize = size });
        return this;
    }

    /// <summary>Adds an image segment from an image file on disk.</summary>
    /// <param name="filePath">The path of the image file.</param>
    /// <param name="size">Display size in pixels, or <see langword="null"/> for the texture's native size.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddImage(string filePath, Vector2? size = null)
        => AddImage(UiImageSource.FromFile(filePath), size);

    /// <summary>Adds an image segment from a game icon id.</summary>
    /// <param name="gameIconId">The id of the game icon.</param>
    /// <param name="size">Display size in pixels, or <see langword="null"/> for the texture's native size.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddImage(uint gameIconId, Vector2? size = null)
        => AddImage(UiImageSource.FromGameIcon(gameIconId), size);

    /// <summary>Adds an image segment from an existing texture wrap, which stays owned by the caller.</summary>
    /// <param name="textureWrap">The texture wrap to display.</param>
    /// <param name="size">Display size in pixels, or <see langword="null"/> for the texture's native size.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddImage(IDalamudTextureWrap textureWrap, Vector2? size = null)
        => AddImage(UiImageSource.FromWrap(textureWrap), size);

    /// <summary>Adds a horizontal spacing segment on the current line.</summary>
    /// <param name="width">Spacing width at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddSpacing(float width)
    {
        segments.Add(new Segment { Kind = SegmentKind.Spacing, SpacingWidth = width });
        return this;
    }

    /// <summary>Ends the current line, so the next segments start a new one.</summary>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddNewLine()
    {
        segments.Add(new Segment { Kind = SegmentKind.NewLine });
        return this;
    }

    /// <summary>Adds a horizontal separator line, ending the current line.</summary>
    /// <returns>This <see cref="NoireContent"/> instance, for chaining.</returns>
    public NoireContent AddSeparator()
    {
        segments.Add(new Segment { Kind = SegmentKind.Separator });
        return this;
    }

    /// <summary>
    /// Adds a custom segment, drawn in the natural flow of the line without vertical centering. Its drawn bounds are
    /// measured, so it may be arbitrarily tall and whatever follows the line starts below it.
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

    /// <summary>Creates a <see cref="NoireContent"/> containing a single text segment.</summary>
    /// <param name="text">The text of the content.</param>
    /// <returns>The new content.</returns>
    public static implicit operator NoireContent(string text)
        => new NoireContent().AddText(text);

    /// <summary>Draws the content at the current cursor, line by line with vertical centering.</summary>
    public void Draw()
    {
        if (segments.Count == 0)
            return;

        if (linesBuiltFrom != segments.Count)
            RebuildLines();

        // Resolved once per draw so the measure and draw passes see the same text.
        if (hasProviders)
        {
            foreach (var segment in segments)
            {
                if (segment.TextProvider != null)
                    segment.RuntimeText = segment.TextProvider() ?? string.Empty;
            }
        }

        var stamp = new MeasureStamp(
            UiTextMeasureCache.CurrentFont(),
            ImGui.GetFontSize(),
            NoireUI.Scale,
            UiFontCache.Generation);

        var keyCapPadding = NoireUI.Scaled(new Vector2(5f, 2f));
        var lineHeight = ImGui.GetTextLineHeight();

        // One line's segment heights, measured once and read back while centering. Pooled because this runs on every
        // frame a surface holding the content is visible.
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

    /// <summary>Rebuilds the line runs from the break segments.</summary>
    /// <remarks>
    /// Every break closes the run before it, including an empty one, so two consecutive breaks are a blank line. The
    /// final run is closed by the end of the list rather than by a break.
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

    /// <summary>Puts the gap between two lines in front of the second one rather than after the first.</summary>
    /// <remarks>
    /// The line advance is a <c>SetCursorPosY</c> rather than a real item, and ImGui grows a window's content height
    /// to any cursor position set inside it, so spacing after the final line would be permanent bottom padding.
    /// </remarks>
    /// <param name="isFirstLine">Whether the line about to be drawn is the first.</param>
    private static void SpaceBeforeLine(bool isFirstLine)
    {
        if (!isFirstLine)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().ItemSpacing.Y);
    }

    /// <summary>Draws one run of same-line segments, vertically centered against the tallest of them.</summary>
    /// <param name="start">Index of the run's first segment.</param>
    /// <param name="count">Segments in the run.</param>
    /// <param name="isFirstLine">Whether this is the content's first line.</param>
    /// <param name="stamp">The conditions measurements are taken under this draw.</param>
    /// <param name="keyCapPadding">Scaled padding inside a keycap tile.</param>
    /// <param name="lineHeight">Height of one text line.</param>
    /// <param name="lineHeights">Scratch buffer holding each segment's measured height.</param>
    private void DrawLine(int start, int count, bool isFirstLine, in MeasureStamp stamp, Vector2 keyCapPadding, float lineHeight, Span<float> lineHeights)
    {
        if (count == 0)
        {
            // An empty line still takes vertical space, except at the very end of the content.
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
        var drawnMaxHeight = maxHeight;

        for (var index = 0; index < count; index++)
        {
            if (index > 0)
                ImGui.SameLine(0f, 0f);

            // After a SameLine the cursor sits at the previous segment's offset, so a segment following a moved one
            // has to be placed even when its own offset is zero.
            var offset = (maxHeight - lineHeights[index]) / 2f;

            if (offset > 0f || index > 0)
                ImGui.SetCursorPosY(startY + offset);

            var segment = segments[start + index];
            DrawSegment(segment, keyCapPadding);

            // A custom segment's real size is known only once it ran, so folding it in here keeps the next line
            // below what was actually drawn even on the first frame, when the measure pass had no cached size.
            if (segment.Kind == SegmentKind.Custom)
                drawnMaxHeight = MathF.Max(drawnMaxHeight, segment.MeasuredSize.Y);
        }

        // Under the tallest segment of the line; the gap to the next line is added by SpaceBeforeLine, never here.
        ImGui.SetCursorPosY(startY + drawnMaxHeight);
    }

    /// <summary>The height a segment takes on its line, cached against the conditions it was measured under.</summary>
    /// <remarks>
    /// Measured against the font in hand rather than through <c>CalcSize</c>, which resolves and pushes one of its
    /// own: a segment draws in whatever the caller pushed, and a height taken in another font misplaces the baseline.
    /// </remarks>
    /// <param name="segment">The segment to measure.</param>
    /// <param name="stamp">The conditions measurements are taken under this draw.</param>
    /// <param name="keyCapPadding">Scaled padding inside a keycap tile.</param>
    /// <param name="lineHeight">Height of one text line.</param>
    /// <returns>The segment's height in pixels.</returns>
    private static float MeasureHeight(Segment segment, in MeasureStamp stamp, Vector2 keyCapPadding, float lineHeight)
    {
        switch (segment.Kind)
        {
            case SegmentKind.Text:
                return MeasureTextHeight(segment, stamp);

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

            case SegmentKind.Custom:
                // Knowable only after the action ran, so the previous draw's size stands in and DrawLine's
                // realignment covers the first frame with the real drawn size.
                return segment.MeasuredSize.Y > 0f ? segment.MeasuredSize.Y : lineHeight;

            default:
                return lineHeight;
        }
    }

    /// <summary>
    /// The measured size of a text-bearing segment, refreshed when the stamp moved or a provider changed the text.
    /// </summary>
    /// <param name="segment">The segment to measure.</param>
    /// <param name="stamp">The conditions measurements are taken under this draw.</param>
    /// <returns>The measured text size.</returns>
    private static Vector2 MeasureText(Segment segment, in MeasureStamp stamp)
    {
        // Provider text can change without anything in the stamp moving, so it goes through the shared cache, which
        // is keyed on the text itself.
        if (segment.TextProvider != null)
            return NoireText.CalcSizeInCurrentFont(segment.RuntimeText ?? string.Empty);

        if (segment.Stamp != stamp)
        {
            segment.MeasuredSize = NoireText.CalcSizeInCurrentFont(segment.RuntimeText ?? string.Empty);
            segment.Stamp = stamp;
        }

        return segment.MeasuredSize;
    }

    /// <summary>
    /// The height a text segment takes on its line, accounting for an ambient text wrap position such as the one
    /// <see cref="NoireLayout.WrapText(float, Action)"/> pushes around a whole <see cref="Draw"/> call.
    /// </summary>
    /// <remarks>
    /// Under an active wrap, <c>TextUnformatted</c> reflows the segment onto several lines and advances the cursor
    /// past all of them, so reserving the cached single-line height would let the next item overlap them. The
    /// remeasure runs only when the natural width actually exceeds the wrap position.
    /// </remarks>
    /// <param name="segment">The text segment to measure.</param>
    /// <param name="stamp">The conditions measurements are taken under this draw.</param>
    /// <returns>The height the segment occupies in pixels.</returns>
    private static float MeasureTextHeight(Segment segment, in MeasureStamp stamp)
    {
        var natural = MeasureText(segment, stamp);
        var wrapWidth = NoireLayout.ActiveWrapWidth();

        if (wrapWidth is not { } width || natural.X <= width)
            return natural.Y;

        return ImGui.CalcTextSize(segment.RuntimeText ?? string.Empty, false, width).Y;
    }

    /// <summary>The display size of an image segment, falling back to the native texture size then a text line.</summary>
    /// <param name="segment">The image segment.</param>
    /// <param name="lineHeight">Height of one text line.</param>
    /// <returns>The size to draw the image at.</returns>
    private static Vector2 ResolveImageSize(Segment segment, float lineHeight)
    {
        if (segment.ImageSize.HasValue)
            return segment.ImageSize.Value;

        var nativeSize = segment.Image?.GetNativeSize();
        if (nativeSize.HasValue)
            return nativeSize.Value;

        return new Vector2(lineHeight, lineHeight);
    }

    /// <summary>Draws one segment at the current cursor.</summary>
    /// <param name="segment">The segment to draw.</param>
    /// <param name="keyCapPadding">Scaled padding inside a keycap tile.</param>
    private static void DrawSegment(Segment segment, Vector2 keyCapPadding)
    {
        switch (segment.Kind)
        {
            case SegmentKind.Text:
                using (UiPush.Color(ImGuiCol.Text, segment.Color ?? Vector4.One, segment.Color.HasValue))
                {
                    // Pre-encoded bytes when the text is static, so ImGui re-encodes nothing per frame.
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
                // Grouped so the drawn bounds are known: the size feeds this line's realignment and the next frame's
                // height measurement.
                ImGui.BeginGroup();
                segment.Custom?.Invoke();
                ImGui.EndGroup();
                segment.MeasuredSize = ImGui.GetItemRectSize();
                break;
        }
    }

    /// <summary>Draws one keycap tile around its label, using the size the height pass already measured.</summary>
    /// <param name="segment">The keycap segment.</param>
    /// <param name="padding">Scaled padding between the label and the tile edge.</param>
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

        // Reserved whether or not anything was painted, so layout does not shift when there is nothing to paint into.
        ImGui.Dummy(tileSize);
    }
}
