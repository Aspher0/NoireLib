using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A standalone button overlayed on the game screen, positioned through <see cref="UiPosition"/> and drawn every frame
/// independently from any window.
/// It registers itself on creation and is drawn until disposed, unless <see cref="NoireDrawable.AutoDraw"/> is turned off.
/// </summary>
[NoireFacadeFactory]
public class NoireOverlayButton : NoireDrawable
{
    private const ImGuiWindowFlags OverlayWindowFlags =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoBackground;

    /// <summary>The fault message reported when a consumer draw hook throws.</summary>
    private const string CallbackFault = "An overlay button hook threw.";

    private bool isDragging;
    private Vector2 dragGrabOffset;
    private bool visibleConditionFaultLogged;
    private bool persistPosition;
    private bool positionRestored;
    private OverlayDrawConditions drawConditions = OverlayDrawConditions.None;

    /// <summary>
    /// Initializes a new overlay button and registers it for drawing.
    /// </summary>
    /// <param name="id">An optional unique identifier. When <see langword="null"/>, a random one is generated.</param>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
    public NoireOverlayButton(string? id = null)
        : base(id, "OverlayButton")
    {
        // Opted in explicitly rather than left to the NoireUI.AutoDraw master default; null restores that default.
        AutoDraw = true;
        Register();
    }

    #region Position & Visibility

    /// <summary>
    /// Where the button is placed on screen. See <see cref="UiPosition"/>.
    /// </summary>
    public UiPosition Position { get; set; } = UiPosition.AtAnchor(UiAnchor.TopLeft, new Vector2(20f, 20f));

    /// <summary>
    /// Whether the button is currently shown. See also <see cref="VisibleCondition"/>.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// An optional condition evaluated on every draw, which must return <see langword="true"/> alongside
    /// <see cref="Visible"/> for the button to be drawn.
    /// </summary>
    public Func<bool>? VisibleCondition { get; set; } = null;

    /// <summary>
    /// Whether the button reacts to clicks and scrolls. When disabled, the button is dimmed (see <see cref="OverlayButtonStyle.DisabledAlpha"/>).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the button is kept in front of every other window, for clicks as well as for drawing.
    /// </summary>
    /// <remarks>Moves the button in both ImGui orders, draw and input, so it stays clickable as well as visible.</remarks>
    public bool AlwaysOnTop { get; set; } = false;

    /// <summary>
    /// In which normally-hidden game states this button keeps being drawn, defaulting to
    /// <see cref="OverlayDrawConditions.None"/>.
    /// The setting applies to this button alone; see <see cref="OverlayDrawConditions"/> for the one case where that
    /// does not hold.
    /// </summary>
    public OverlayDrawConditions DrawConditions
    {
        get => drawConditions;
        set
        {
            if (drawConditions == value)
                return;

            drawConditions = value;

            if (!IsDisposed)
                NoireUI.RefreshUiHideOverrides();
        }
    }

    /// <summary>
    /// The mouse cursor shown while the button is hovered, or <see langword="null"/> to leave the cursor unchanged.
    /// Requires <c>UiBuilder.OverrideGameCursor</c> for the cursor to be visible over the game.
    /// </summary>
    public ImGuiMouseCursor? HoverCursor { get; set; } = null;

    /// <summary>
    /// Whether the button can be repositioned by dragging it with the left mouse button, which replaces
    /// <see cref="Position"/> with an absolute position and invokes <see cref="OnDragEnd"/>.
    /// </summary>
    public bool Draggable { get; set; } = false;

    /// <summary>
    /// Whether the position the user dragged the button to is remembered across reloads through
    /// <see cref="NoireUiState"/>, restored on the next draw and saved again after every drag.
    /// Requires a stable <see cref="NoireDrawable.Id"/>: a button created without one is not persisted (logged once).
    /// </summary>
    public bool PersistPosition
    {
        get => persistPosition;
        set
        {
            if (persistPosition == value)
                return;

            persistPosition = value;

            // Turning it on re-arms the restore, whenever that happens relative to the first draw.
            if (value)
                positionRestored = false;
        }
    }

    #endregion

    #region Content

    /// <summary>
    /// The text displayed on the button.
    /// </summary>
    public string? Text { get; set; } = null;

    /// <summary>
    /// The FontAwesome icon displayed on the button, before the text.
    /// </summary>
    public FontAwesomeIcon? Icon { get; set; } = null;

    /// <summary>
    /// The image displayed on the button, between the icon and the text. See <see cref="UiImageSource"/>.
    /// </summary>
    public UiImageSource? Image { get; set; } = null;

    /// <summary>
    /// The display size of <see cref="Image"/>. When <see langword="null"/>, the native texture size is used.
    /// </summary>
    public Vector2? ImageSize { get; set; } = null;

    /// <summary>
    /// A custom content renderer replacing the default icon, image and text, invoked with the cursor at the top left
    /// corner of the button.
    /// Not called while <see cref="OverlayButtonStyle.CustomDraw"/> is set, which replaces the whole painting.
    /// </summary>
    public Action<NoireOverlayButton>? CustomContent { get; set; } = null;

    /// <summary>
    /// An explicit button size at 100% scale, or <see langword="null"/> to compute it from the content and
    /// <see cref="OverlayButtonStyle.Padding"/>.
    /// </summary>
    public Vector2? Size { get; set; } = null;

    /// <summary>
    /// The visual style of the button. See <see cref="OverlayButtonStyle"/>.
    /// </summary>
    public OverlayButtonStyle Style { get; set; } = new();

    #endregion

    #region Interactions

    /// <summary>
    /// Invoked when the button is left clicked.
    /// </summary>
    public Action<NoireOverlayButton>? OnLeftClick { get; set; } = null;

    /// <summary>
    /// Invoked when the button is right clicked.
    /// </summary>
    public Action<NoireOverlayButton>? OnRightClick { get; set; } = null;

    /// <summary>
    /// Invoked when the button is middle clicked (mouse wheel click).
    /// </summary>
    public Action<NoireOverlayButton>? OnMiddleClick { get; set; } = null;

    /// <summary>
    /// Invoked when the mouse wheel is scrolled over the button. The parameter is the scroll delta (positive when scrolling up).
    /// </summary>
    public Action<NoireOverlayButton, float>? OnScroll { get; set; } = null;

    /// <summary>
    /// Invoked when a drag ends, after <see cref="Position"/> has been updated to the new absolute position. See <see cref="Draggable"/>.
    /// </summary>
    public Action<NoireOverlayButton>? OnDragEnd { get; set; } = null;

    /// <summary>
    /// A regular ImGui tooltip shown when the button is hovered.
    /// </summary>
    public string? Tooltip { get; set; } = null;

    /// <summary>
    /// A custom tooltip shown when the button is hovered, drawn with <see cref="NoireTooltip"/> and shown alongside
    /// <see cref="Tooltip"/> when both are set.
    /// </summary>
    public NoireContent? CustomTooltip { get; set; } = null;

    /// <summary>
    /// The style of <see cref="CustomTooltip"/>. When <see langword="null"/>, the default style is used.
    /// </summary>
    public TooltipStyle? CustomTooltipStyle { get; set; } = null;

    #endregion

    #region Show / Hide

    /// <summary>
    /// Shows the button.
    /// </summary>
    /// <returns>This <see cref="NoireOverlayButton"/> instance, for chaining.</returns>
    public NoireOverlayButton Show() => SetVisible(true);

    /// <summary>
    /// Hides the button.
    /// </summary>
    /// <returns>This <see cref="NoireOverlayButton"/> instance, for chaining.</returns>
    public NoireOverlayButton Hide() => SetVisible(false);

    /// <summary>
    /// Toggles the visibility of the button.
    /// </summary>
    /// <returns>This <see cref="NoireOverlayButton"/> instance, for chaining.</returns>
    public NoireOverlayButton Toggle() => SetVisible(null);

    /// <summary>
    /// Shows or hides the button.
    /// </summary>
    /// <param name="visible">Whether to show the button. Set to <see langword="null"/> to toggle it.</param>
    /// <returns>This <see cref="NoireOverlayButton"/> instance, for chaining.</returns>
    public NoireOverlayButton SetVisible(bool? visible)
    {
        Visible = visible ?? !Visible;
        return this;
    }

    #endregion

    #region Drawing

    /// <inheritdoc/>
    protected override void DrawCore()
    {
        RestorePersistedPosition();

        if (!Visible || ShouldHideForGameState() || !EvaluateVisibleCondition())
        {
            isDragging = false;
            return;
        }

        var size = Vector2.Max(ResolveSize(), NoireUI.Scaled(new Vector2(4f, 4f)));
        var viewport = ImGui.GetMainViewport();

        Vector2 windowPos;
        if (isDragging)
        {
            windowPos = ImGui.GetMousePos() - dragGrabOffset;
            if (Position.ClampToViewport)
            {
                var max = viewport.Pos + viewport.Size - size;
                windowPos = new Vector2(
                    MathF.Max(viewport.Pos.X, MathF.Min(windowPos.X, max.X)),
                    MathF.Max(viewport.Pos.Y, MathF.Min(windowPos.Y, max.Y)));
            }
        }
        else if (!Position.TryResolve(size, viewport.Pos, viewport.Size, out windowPos))
        {
            // The button is pinned to a game window that is not on screen, so there is nowhere to put it.
            return;
        }

        ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);

        var alpha = Math.Clamp(Style.Alpha * (Enabled ? 1f : Style.DisabledAlpha), 0f, 1f);
        using var alphaStyle = UiPush.Style(ImGuiStyleVar.Alpha, alpha);
        using var paddingStyle = UiPush.Style(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var borderStyle = UiPush.Style(ImGuiStyleVar.WindowBorderSize, 0f);
        using var minSizeStyle = UiPush.Style(ImGuiStyleVar.WindowMinSize, Vector2.One);

        var flags = OverlayWindowFlags;
        if (AlwaysOnTop)
            flags |= UiWindowOrder.TopLayerFlag;

        if (ImGui.Begin(ImGuiId, flags))
        {
            if (AlwaysOnTop)
                UiWindowOrder.KeepInFront();

            if (Style.FontScale != 1f)
                ImGui.SetWindowFontScale(Style.FontScale);

            DrawButton(size);

            if (Style.FontScale != 1f)
                ImGui.SetWindowFontScale(1f);
        }

        ImGui.End();
    }

    private void DrawButton(Vector2 size)
    {
        ImGui.SetCursorPos(Vector2.Zero);
        var pressed = ImGui.InvisibleButton("##NoireOverlayButtonHitbox", size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();

        var dragJustEnded = HandleDragging(active);

        if (Style.CustomDraw is { } customDraw)
        {
            InvokeCustomDraw(customDraw, rectMin, rectMax, hovered, active);
        }
        else
        {
            DrawBackground(rectMin, rectMax, hovered, active);

            if (CustomContent != null)
            {
                ImGui.SetCursorPos(Vector2.Zero);
                InvokeSafely(CustomContent, "custom content");
            }
            else
            {
                DrawDefaultContent(size);
            }
        }

        if (Enabled && !isDragging && !dragJustEnded)
        {
            if (pressed)
                InvokeSafely(OnLeftClick, "left click");

            if (hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                InvokeSafely(OnRightClick, "right click");

            if (hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
                InvokeSafely(OnMiddleClick, "middle click");

            if (hovered && OnScroll != null)
            {
                var wheel = ImGui.GetIO().MouseWheel;
                if (wheel != 0f)
                {
                    try
                    {
                        OnScroll(this, wheel);
                    }
                    catch (Exception ex)
                    {
                        NoireLogger.LogError(this, ex, $"The scroll callback of overlay button '{Id}' threw an exception.");
                    }
                }
            }
        }

        if (hovered)
        {
            if (HoverCursor.HasValue)
                ImGui.SetMouseCursor(HoverCursor.Value);

            if (!isDragging)
            {
                if (!string.IsNullOrEmpty(Tooltip))
                    ImGui.SetTooltip(Tooltip);

                if (CustomTooltip != null && !CustomTooltip.IsEmpty)
                    NoireTooltip.Show(CustomTooltip, CustomTooltipStyle, UiIds.For("OverlayButton_", Id));
            }
        }
    }

    private void DrawBackground(Vector2 rectMin, Vector2 rectMax, bool hovered, bool active)
    {
        using var draw = UiDraw.Begin();
        var drawList = draw.List;

        if (drawList.IsNull)
            return;

        var rounding = Style.ResolveRounding();
        drawList.AddRectFilled(rectMin, rectMax, ColorHelper.Vector4ToUint(ResolveBackground(hovered, active)), rounding);

        var borderSize = Style.ScaledBorderSize;
        if (borderSize > 0f)
            drawList.AddRect(rectMin, rectMax, ColorHelper.Vector4ToUint(ResolveBorderColor()), rounding, ImDrawFlags.None, borderSize);
    }

    /// <summary>
    /// The fill for the current state, routed through <see cref="ImGui.GetColorU32(Vector4)"/> and unpacked so the
    /// pushed style alpha is already folded in.
    /// </summary>
    private Vector4 ResolveBackground(bool hovered, bool active)
    {
        var interactive = Enabled;

        if (Style.Background.HasValue)
        {
            // Unset hovered and active variants fall back to a brightened or darkened custom background.
            var color = active && interactive
                ? Style.BackgroundActive ?? Style.Background.Value * new Vector4(0.9f, 0.9f, 0.9f, 1f)
                : hovered && interactive
                    ? Style.BackgroundHovered ?? Style.Background.Value * new Vector4(1.1f, 1.1f, 1.1f, 1f)
                    : Style.Background.Value;

            return ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(color));
        }

        var colorIndex = active && interactive
            ? ImGuiCol.ButtonActive
            : hovered && interactive
                ? ImGuiCol.ButtonHovered
                : ImGuiCol.Button;

        return ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(colorIndex));
    }

    /// <summary>The border colour, resolved the same way as <see cref="ResolveBackground"/>.</summary>
    private Vector4 ResolveBorderColor()
        => ImGui.ColorConvertU32ToFloat4(Style.BorderColor.HasValue
            ? ImGui.GetColorU32(Style.BorderColor.Value)
            : ImGui.GetColorU32(ImGuiCol.Border));

    /// <summary>
    /// Hands the painting to a custom-draw hook, with the state and every colour resolved.
    /// </summary>
    /// <param name="customDraw">The hook that paints the button.</param>
    /// <param name="rectMin">The top left corner of the hitbox.</param>
    /// <param name="rectMax">The bottom right corner of the hitbox.</param>
    /// <param name="hovered">Whether the hitbox is hovered.</param>
    /// <param name="active">Whether the hitbox is held.</param>
    private void InvokeCustomDraw(Action<UiOverlayButtonDraw> customDraw, Vector2 rectMin, Vector2 rectMax, bool hovered, bool active)
    {
        using var draw = UiDraw.Begin();

        if (draw.List.IsNull)
            return;

        var args = new UiOverlayButtonDraw(
            draw.List,
            this,
            rectMin,
            rectMax,
            hovered,
            active,
            Enabled,
            isDragging,
            ResolveBackground(hovered, active),
            ResolveBorderColor(),
            Style.ScaledBorderSize,
            Style.ResolveRounding());

        UiHook.Invoke(customDraw, args, nameof(NoireOverlayButton), CallbackFault);
    }

    /// <summary>
    /// Draws the icon, image and text parts centred inside the button.
    /// </summary>
    /// <param name="size">The size the button is drawn at.</param>
    internal void DrawDefaultContent(Vector2 size)
    {
        var (iconSize, imageSize, textSize) = MeasureContentParts();
        var partCount = (iconSize.HasValue ? 1 : 0) + (imageSize.HasValue ? 1 : 0) + (textSize.HasValue ? 1 : 0);
        if (partCount == 0)
            return;

        var totalWidth = (iconSize?.X ?? 0f) + (imageSize?.X ?? 0f) + (textSize?.X ?? 0f) + (Style.ScaledContentSpacing * (partCount - 1));
        var cursorX = (size.X - totalWidth) / 2f;

        if (iconSize.HasValue)
        {
            ImGui.SetCursorPos(new Vector2(cursorX, (size.Y - iconSize.Value.Y) / 2f));
            using (UiPush.Color(ImGuiCol.Text, Style.IconColor ?? Style.TextColor ?? Vector4.One, (Style.IconColor ?? Style.TextColor).HasValue))
            using (UiPush.Font(UiBuilder.IconFont))
                ImGui.TextUnformatted(UiValueText.Icon(Icon!.Value));

            cursorX += iconSize.Value.X + Style.ScaledContentSpacing;
        }

        if (imageSize.HasValue)
        {
            var wrap = Image?.GetWrap();
            ImGui.SetCursorPos(new Vector2(cursorX, (size.Y - imageSize.Value.Y) / 2f));
            if (wrap != null)
                ImGui.Image(wrap.Handle, imageSize.Value, Vector2.Zero, Vector2.One, Style.ImageTint);
            else
                ImGui.Dummy(imageSize.Value);

            cursorX += imageSize.Value.X + Style.ScaledContentSpacing;
        }

        if (textSize.HasValue)
        {
            ImGui.SetCursorPos(new Vector2(cursorX, (size.Y - textSize.Value.Y) / 2f));
            using (UiPush.Color(ImGuiCol.Text, Style.TextColor ?? Vector4.One, Style.TextColor.HasValue))
                ImGui.TextUnformatted(Text!);
        }
    }

    /// <summary>
    /// The size the button is drawn at: the explicit <see cref="Size"/> scaled, or one measured from the content.
    /// </summary>
    /// <remarks>The measured size needs no scaling, being built from text metrics and a resolved padding that are already real pixels.</remarks>
    private Vector2 ResolveSize()
        => Size.HasValue ? NoireUI.Scaled(Size.Value) : MeasureAutoSize();

    /// <summary>
    /// Measures the icon, image and text parts of the default content.
    /// </summary>
    /// <param name="externalFontScale">The font scale to apply, which must stay 1 inside the button window because <c>ImGui.CalcTextSize</c> already accounts for the window font scale there.</param>
    /// <returns>The measured size of each present part.</returns>
    private (Vector2? IconSize, Vector2? ImageSize, Vector2? TextSize) MeasureContentParts(float externalFontScale = 1f)
    {
        Vector2? iconSize = null;
        if (Icon.HasValue)
        {
            using (UiPush.Font(UiBuilder.IconFont))
                iconSize = NoireText.CalcSizeInCurrentFont(UiValueText.Icon(Icon.Value)) * externalFontScale;
        }

        Vector2? imageSize = null;
        if (Image != null)
        {
            var lineHeight = ImGui.GetTextLineHeight() * externalFontScale;
            imageSize = ImageSize ?? Image.GetNativeSize() ?? new Vector2(lineHeight, lineHeight);
        }

        Vector2? textSize = null;
        if (!string.IsNullOrEmpty(Text))
            textSize = NoireText.CalcSizeInCurrentFont(Text) * externalFontScale;

        return (iconSize, imageSize, textSize);
    }

    private Vector2 MeasureAutoSize()
    {
        var (iconSize, imageSize, textSize) = MeasureContentParts(Style.FontScale);
        var partCount = (iconSize.HasValue ? 1 : 0) + (imageSize.HasValue ? 1 : 0) + (textSize.HasValue ? 1 : 0);

        if (partCount == 0)
        {
            var fallback = ImGui.GetFrameHeight();
            return new Vector2(fallback, fallback);
        }

        var contentSize = new Vector2(
            (iconSize?.X ?? 0f) + (imageSize?.X ?? 0f) + (textSize?.X ?? 0f) + (Style.ScaledContentSpacing * (partCount - 1)),
            MathF.Max(iconSize?.Y ?? 0f, MathF.Max(imageSize?.Y ?? 0f, textSize?.Y ?? 0f)));

        var padding = Style.ResolvePadding();
        return contentSize + (padding * 2f);
    }

    /// <summary>
    /// Handles the drag-to-reposition behavior.
    /// </summary>
    /// <param name="active">Whether the button hitbox is currently held.</param>
    /// <returns>True when a drag ended this frame, in which case click callbacks are suppressed.</returns>
    private bool HandleDragging(bool active)
    {
        if (!Draggable)
        {
            isDragging = false;
            return false;
        }

        if (isDragging)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                return false;

            isDragging = false;

            var viewport = ImGui.GetMainViewport();
            Position = UiPosition.AtAbsolute(ImGui.GetWindowPos() - viewport.Pos)
                .WithClampToViewport(Position.ClampToViewport);

            SavePersistedPosition();
            InvokeSafely(OnDragEnd, "drag end");
            return true;
        }

        if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4f))
        {
            isDragging = true;
            dragGrabOffset = ImGui.GetMousePos() - ImGui.GetWindowPos();
        }

        return false;
    }

    /// <summary>
    /// Reads the live game state and decides whether the button must be hidden this frame.
    /// </summary>
    /// <returns>True when the button must be hidden this frame.</returns>
    private bool ShouldHideForGameState()
    {
        if (!NoireService.IsInitialized())
            return false;

        return ShouldHideForGameState(
            DrawConditions,
            NoireService.PluginInterface.UiBuilder.CutsceneActive,
            NoireService.ClientState.IsGPosing,
            NoireService.GameGui.GameUiHidden);
    }

    /// <summary>
    /// Decides whether a button with the given <paramref name="conditions"/> is hidden in the given game state.
    /// </summary>
    /// <param name="conditions">The button draw conditions.</param>
    /// <param name="cutsceneActive">Whether a cutscene is playing.</param>
    /// <param name="gposing">Whether group pose is active.</param>
    /// <param name="gameUiHidden">Whether the game UI is hidden.</param>
    /// <returns>True when the button must be hidden.</returns>
    internal static bool ShouldHideForGameState(OverlayDrawConditions conditions, bool cutsceneActive, bool gposing, bool gameUiHidden)
    {
        if (cutsceneActive && (conditions & OverlayDrawConditions.DrawInCutscenes) == 0)
            return true;

        if (gposing && (conditions & OverlayDrawConditions.DrawInGpose) == 0)
            return true;

        if (gameUiHidden && (conditions & OverlayDrawConditions.DrawWhenGameUiHidden) == 0)
            return true;

        return false;
    }

    /// <summary>
    /// Applies the saved dragged position once, on the first draw after <see cref="PersistPosition"/> is turned on,
    /// leaving the position set in code standing when nothing is saved.
    /// </summary>
    private void RestorePersistedPosition()
    {
        if (!persistPosition || positionRestored)
            return;

        positionRestored = true;

        if (!TryGetPersistKey("position", out var key))
            return;

        if (NoireUiState.TryGet<Vector2>(key, out var saved))
            Position = UiPosition.AtAbsolute(saved).WithClampToViewport(Position.ClampToViewport);
    }

    /// <summary>
    /// Stores the absolute position the button was dragged to, which a drag is the only thing to produce.
    /// </summary>
    private void SavePersistedPosition()
    {
        if (!persistPosition || !TryGetPersistKey("position", out var key))
            return;

        NoireUiState.Set(key, Position.AbsolutePosition);
    }

    private bool EvaluateVisibleCondition()
    {
        if (VisibleCondition == null)
            return true;

        try
        {
            var visible = VisibleCondition();
            visibleConditionFaultLogged = false;
            return visible;
        }
        catch (Exception ex)
        {
            if (!visibleConditionFaultLogged)
            {
                visibleConditionFaultLogged = true;
                NoireLogger.LogError(this, ex, $"The visibility condition of overlay button '{Id}' threw an exception. The button is hidden until the condition stops throwing.");
            }

            return false;
        }
    }

    private void InvokeSafely(Action<NoireOverlayButton>? callback, string callbackName)
    {
        if (callback == null)
            return;

        try
        {
            callback(this);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"The {callbackName} callback of overlay button '{Id}' threw an exception.");
        }
    }

    #endregion
}
