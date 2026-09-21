using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Dims the screen, lights the widget the running tour points at, and draws its card.
/// </summary>
public sealed class NoireTourHost : NoireDrawable
{
    private const string CardWindowId = "###NoireTourCard";

    private const string DimWindowId = "###NoireTourDim";

    private const string TooltipWindowId = "###NoireTourTooltip";

    private const ImGuiWindowFlags TooltipFlags =
        ImGuiWindowFlags.NoDecoration |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoInputs |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.AlwaysAutoResize |
        UiWindowOrder.TopLayerFlag;

    private const ImGuiWindowFlags DimFlags =
        ImGuiWindowFlags.NoDecoration |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoInputs |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoBackground |
        UiWindowOrder.TopLayerFlag;

    private const int TargetGraceFrames = 30;

    private const ImGuiWindowFlags CardFlags =
        ImGuiWindowFlags.NoDecoration |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.AlwaysAutoResize |
        UiWindowOrder.TopLayerFlag;

    private static readonly object InstanceLock = new();

    private static readonly Vector4[] Bands = new Vector4[TourDim.MaxBands];

    private static readonly Vector4[] Holes = new Vector4[TourDim.MaxHoles];

    private static readonly BarStyle SettleStyle = new()
    {
        Height = 4f,
        Rounding = 2f,
    };

    private static readonly ButtonStyle StopStyle = new()
    {
        Tone = ButtonTone.Ghost,
        Icon = FontAwesomeIcon.Times,
    };

    private static NoireTourHost? instance;

    private NoireTourHost()
        : base("TourHost", "Tour")
    {
        // A started tour is expected on screen.
        AutoDraw = true;
        Register();
    }

    internal static NoireTourHost Instance
    {
        get
        {
            if (instance is { IsDisposed: false })
                return instance;

            lock (InstanceLock)
            {
                if (instance is { IsDisposed: false })
                    return instance;

                instance = new NoireTourHost();
                return instance;
            }
        }
    }

    /// <inheritdoc/>
    protected override void DrawCore()
    {
        var run = NoireTour.ActiveRun;

        if (run == null || !UiDraw.Available)
            return;

        if (run.AdvanceAtFrame != 0 && NoireUI.FrameCount >= run.AdvanceAtFrame)
        {
            run.AdvanceAtFrame = 0;
            NoireTour.Next();
            return;
        }

        var step = run.CurrentStep;

        if (step == null)
            return;

        if (!run.Entered)
        {
            run.Entered = true;
            run.EnteredAtFrame = NoireUI.FrameCount;
            step.OnEnter?.Invoke();
        }

        UpdateSettling(run, step);

        NoireTour.KeepTargetWindowInFront(step);

        var viewport = ImGui.GetMainViewport();
        var hasTarget = NoireTour.TryResolveTarget(step, out var target, out var clip);
        var spotlight = hasTarget ? TourLayout.Intersect(TourLayout.Padded(target, NoireUI.Scaled(step.Padding)), clip) : default;
        var scrolledAway = hasTarget && TourLayout.IsEmpty(spotlight);
        var direction = TourDirection.Down;

        if (scrolledAway)
        {
            direction = TourLayout.DirectionTo(target, clip);
            spotlight = TourLayout.EdgeOf(clip, direction, NoireUI.Scaled(30f));
        }

        var expectsTarget = step.Rect != null || step.Target.Length > 0;
        var waitingForTarget = !hasTarget && expectsTarget && NoireUI.FrameCount - run.EnteredAtFrame <= TargetGraceFrames;
        var card = waitingForTarget && run.CardRect.Z > run.CardRect.X
            ? TourLayout.MovedTo(run.CardRect, new Vector2(run.CardRect.X, run.CardRect.Y))
            : PlaceCard(run, step, viewport, spotlight, hasTarget);

        DrawDimLayer(run, viewport, step, spotlight, hasTarget, scrolledAway, direction);
        DrawCard(run, step, card, scrolledAway);
        DrawTooltip();
        Advance(run, step, target, hasTarget && !scrolledAway);
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        if (ReferenceEquals(instance, this))
            instance = null;
    }

    internal static void UpdateSettling(TourRun run, TourStep step)
    {
        if (step.Advance != TourAdvance.WhenReady || step.SettleSeconds <= 0f)
        {
            run.SettleFraction = 0f;
            return;
        }

        if (step.IsReady?.Invoke() != true)
        {
            run.Settling = false;
            run.SettleFraction = 0f;
            return;
        }

        var stamp = step.SettleStamp?.Invoke() ?? 0;

        if (!run.Settling || stamp != run.SettleStamp)
        {
            run.Settling = true;
            run.SettleSince = NoireUI.Time;
            run.SettleStamp = stamp;
        }

        run.SettleFraction = Math.Clamp((NoireUI.Time - run.SettleSince) / step.SettleSeconds, 0f, 1f);
    }

    private static Vector4 PlaceCard(TourRun run, TourStep step, ImGuiViewportPtr viewport, Vector4 spotlight, bool hasTarget)
    {
        var size = run.CardSize;

        if (size.X <= 0f)
            size = new Vector2(NoireUI.Scaled(run.Options.CardWidth), NoireUI.Scaled(140f));

        return TourLayout.Place(
            viewport.WorkPos,
            viewport.WorkPos + viewport.WorkSize,
            spotlight,
            hasTarget,
            size,
            step.Placement,
            NoireUI.Scaled(run.Options.CardGap));
    }

    private static void DrawDimLayer(TourRun run, ImGuiViewportPtr viewport, TourStep step, Vector4 spotlight, bool hasTarget, bool scrolledAway, TourDirection direction)
    {
        ImGui.SetNextWindowPos(viewport.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(viewport.Size, ImGuiCond.Always);

        if (!ImGui.Begin(DimWindowId, DimFlags))
        {
            ImGui.End();
            return;
        }

        try
        {
            UiWindowOrder.KeepInFront();

            using var draw = UiDraw.BeginWindow();
            var list = draw.List;

            if (list.IsNull)
                return;

            list.PushClipRectFullScreen();

            DrawBands(run, viewport, spotlight, hasTarget, list);

            if (hasTarget && !scrolledAway)
                DrawSpotlight(run, step, spotlight, list);

            if (scrolledAway)
                DrawScrollArrows(run, spotlight, direction, list);

            list.PopClipRect();
        }
        finally
        {
            ImGui.End();
        }
    }

    private static void DrawBands(TourRun run, ImGuiViewportPtr viewport, Vector4 spotlight, bool hasTarget, ImDrawListPtr list)
    {
        var theme = NoireTheme.Current;
        var color = run.Options.DimColor ?? theme.Resolve(ThemeColor.Shadow);
        color.W = Math.Clamp(run.Options.DimAlpha, 0f, 1f);

        var holeCount = 0;

        if (hasTarget)
            Holes[holeCount++] = spotlight;

        var count = TourDim.Bands(Bands, viewport.Pos, viewport.Pos + viewport.Size, Holes, holeCount);

        NoireShapes.On(list, (count, color), static state =>
        {
            for (var index = 0; index < state.count; index++)
            {
                var band = Bands[index];
                NoireShapes.Rect(new Vector2(band.X, band.Y), new Vector2(band.Z, band.W), state.color);
            }
        });
    }

    private static void DrawSpotlight(TourRun run, TourStep step, Vector4 spotlight, ImDrawListPtr list)
    {
        if (step.Spotlight == TourSpotlight.None)
            return;

        var theme = NoireTheme.Current;
        var color = run.Options.SpotlightColor ?? theme.Resolve(ThemeColor.Accent);
        var thickness = NoireUI.Scaled(run.Options.SpotlightThickness);

        if (run.Options.Pulse && !NoireUI.ReducedMotion)
        {
            var wave = 0.75f + (0.25f * MathF.Sin(NoireUI.Time * 3f));
            color = ColorHelper.ScaleAlpha(color, wave);
            thickness *= wave;
        }

        var min = new Vector2(spotlight.X, spotlight.Y);
        var max = new Vector2(spotlight.Z, spotlight.W);
        var rounding = NoireUI.Scaled(run.Options.SpotlightRounding);

        NoireShapes.On(list, (step.Spotlight, min, max, color, thickness, rounding), static state =>
        {
            var (shape, min, max, color, thickness, rounding) = state;

            if (shape == TourSpotlight.Circle)
            {
                var centre = (min + max) * 0.5f;
                NoireShapes.Ring(centre, Vector2.Distance(centre, max), color, thickness);
                return;
            }

            NoireShapes.RectOutline(
                min,
                max,
                color,
                thickness,
                shape == TourSpotlight.Rounded ? CornerShape.Rounded : CornerShape.Square,
                rounding);
        });
    }

    private static void DrawScrollArrows(TourRun run, Vector4 edge, TourDirection direction, ImDrawListPtr list)
    {
        var color = ColorHelper.Vector4ToUint(run.Options.SpotlightColor ?? NoireTheme.Current.Resolve(ThemeColor.Accent));
        var centre = new Vector2((edge.X + edge.Z) * 0.5f, (edge.Y + edge.W) * 0.5f);
        var size = NoireUI.Scaled(9f);
        var travel = NoireUI.ReducedMotion ? 0f : MathF.Sin(NoireUI.Time * 4f) * NoireUI.Scaled(4f);
        var gap = NoireUI.Scaled(12f);

        for (var index = 0; index < 3; index++)
        {
            var slide = travel + ((index - 1) * gap);

            var tip = direction switch
            {
                TourDirection.Up => centre + new Vector2(0f, -slide),
                TourDirection.Down => centre + new Vector2(0f, slide),
                TourDirection.Left => centre + new Vector2(-slide, 0f),
                _ => centre + new Vector2(slide, 0f),
            };

            var (left, right) = direction switch
            {
                TourDirection.Up => (tip + new Vector2(-size, size), tip + new Vector2(size, size)),
                TourDirection.Down => (tip + new Vector2(size, -size), tip + new Vector2(-size, -size)),
                TourDirection.Left => (tip + new Vector2(size, size), tip + new Vector2(size, -size)),
                _ => (tip + new Vector2(-size, -size), tip + new Vector2(-size, size)),
            };

            list.AddTriangleFilled(tip, left, right, color);
        }
    }

    private static void DrawTooltip()
    {
        if (!NoireTour.TryTakeTooltip(out var text))
            return;

        ImGui.SetNextWindowPos(ImGui.GetIO().MousePos + new Vector2(NoireUI.Scaled(16f), NoireUI.Scaled(8f)), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(1f);

        using var background = UiPush.Color(ImGuiCol.PopupBg, NoireTheme.Current.Resolve(ThemeColor.SurfaceRaised));

        if (!ImGui.Begin(TooltipWindowId, TooltipFlags))
        {
            ImGui.End();
            return;
        }

        try
        {
            UiWindowOrder.KeepInFront();
            NoireText.Draw(text);
        }
        finally
        {
            ImGui.End();
        }
    }

    private static void DrawCard(TourRun run, TourStep step, Vector4 card, bool scrolledAway)
    {
        var width = NoireUI.Scaled(run.Options.CardWidth);

        ImGui.SetNextWindowPos(new Vector2(card.X, card.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(width, float.MaxValue));
        ImGui.SetNextWindowBgAlpha(1f);

        using var background = UiPush.Color(ImGuiCol.PopupBg, NoireTheme.Current.Resolve(ThemeColor.SurfaceRaised));

        if (!ImGui.Begin(CardWindowId, CardFlags))
        {
            ImGui.End();
            return;
        }

        try
        {
            UiWindowOrder.KeepInFront();
            UiScope.Run(nameof(NoireTourHost), (run, step, scrolledAway), static state => DrawCardContents(state.run, state.step, state.scrolledAway));

            var position = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            run.CardSize = size;
            run.CardRect = new Vector4(position.X, position.Y, position.X + size.X, position.Y + size.Y);
        }
        finally
        {
            ImGui.End();
        }
    }

    private static void DrawCardContents(TourRun run, TourStep step, bool scrolledAway)
    {
        var theme = NoireTheme.Current;
        using var textColor = UiPush.Color(ImGuiCol.Text, theme.Resolve(ThemeColor.Text));

        DrawTitleRow(run, step);

        if (!string.IsNullOrEmpty(step.Body))
            NoireText.Wrapped(ImGui.GetContentRegionAvail().X, step.Body);

        if (scrolledAway)
        {
            ImGui.Spacing();
            NoireText.Colored(
                theme.Resolve(ThemeColor.Warning),
                NoireUI.Localize("NoireUI.Tour.Scroll", run.Options.ScrollHint ?? "Scroll the panel until it is in view."),
                TextSize.Caption);
        }

        if (run.SettleFraction > 0f)
        {
            ImGui.Spacing();
            SettleStyle.Color = theme.Resolve(ThemeColor.Accent);
            NoireGauges.Bar(run.SettleFraction, SettleStyle);
        }

        ImGui.Spacing();
        DrawControls(run, step);
    }

    private static void DrawTitleRow(TourRun run, TourStep step)
    {
        var stopWidth = ImGui.GetFrameHeight();
        var available = ImGui.GetContentRegionAvail().X;

        if (!string.IsNullOrEmpty(step.Title))
        {
            ImGui.AlignTextToFramePadding();
            NoireText.Draw(step.Title, TextSize.Heading);
            ImGui.SameLine();
        }

        if (!run.Options.ShowStop)
            return;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, available - stopWidth - ImGui.GetCursorPosX() + ImGui.GetStyle().WindowPadding.X));

        if (NoireButtons.Button(UiIds.For("###NoireTour", "stop"), StopStyle, new Vector2(stopWidth, stopWidth)))
            NoireTour.Stop();

        if (ImGui.IsItemHovered())
            NoireTour.Tooltip(NoireUI.Localize("NoireUI.Tour.Stop", run.Options.StopLabel ?? "End the tour"));
    }

    private static void DrawControls(TourRun run, TourStep step)
    {
        var options = run.Options;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var available = ImGui.GetContentRegionAvail().X;
        var start = ImGui.GetCursorPosX();

        if (options.ShowCounter)
        {
            ImGui.AlignTextToFramePadding();
            NoireText.Muted(run.Counter, TextSize.Caption);
            ImGui.SameLine();
        }

        var waits = step.Advance != TourAdvance.Manual;

        var nextLabel = run.IsLastStep
            ? NoireUI.Localize("NoireUI.Tour.Finish", options.FinishLabel ?? "Done")
            : NoireUI.Localize("NoireUI.Tour.Next", options.NextLabel ?? "Next");

        var backLabel = NoireUI.Localize("NoireUI.Tour.Back", options.BackLabel ?? "Back");
        var skipLabel = NoireUI.Localize("NoireUI.Tour.Skip", options.SkipLabel ?? "Skip this step");

        var hint = waits
            ? NoireUI.Localize(
                step.Advance == TourAdvance.OnTargetClick ? "NoireUI.Tour.ClickHint" : "NoireUI.Tour.ActionHint",
                step.ActionHint ?? (step.Advance == TourAdvance.OnTargetClick ? "Click it to continue" : "Do it to continue"))
            : string.Empty;

        var showBack = options.ShowBack && run.Index > 0;
        var showSkip = options.ShowSkip && waits;

        var width = waits ? NoireText.CalcSize(hint, TextSize.Caption).X : ButtonWidth(nextLabel);

        if (showBack)
            width += ButtonWidth(backLabel) + spacing;

        if (showSkip)
            width += ButtonWidth(skipLabel) + spacing;

        ImGui.SameLine(0f, 0f);
        ImGui.SetCursorPosX(Math.Max(start, (start + available) - width));

        if (showBack && NoireButtons.Button(UiIds.Labelled(backLabel, "###NoireTour", "back"), ButtonTone.Neutral))
        {
            NoireTour.Previous();
            return;
        }

        if (showBack)
            ImGui.SameLine();

        if (showSkip && NoireButtons.Button(UiIds.Labelled(skipLabel, "###NoireTour", "skip"), ButtonTone.Ghost))
        {
            NoireTour.Next();
            return;
        }

        if (showSkip)
            ImGui.SameLine();

        if (waits)
        {
            ImGui.AlignTextToFramePadding();
            NoireText.Colored(NoireTheme.Current.Resolve(ThemeColor.Accent), hint, TextSize.Caption);
            return;
        }

        if (NoireButtons.Button(UiIds.Labelled(nextLabel, "###NoireTour", "next"), ButtonTone.Accent))
            NoireTour.Next();
    }

    private static float ButtonWidth(string label)
        => NoireText.CalcSize(label).X + (ImGui.GetStyle().FramePadding.X * 2f);

    private static void Advance(TourRun run, TourStep step, Vector4 target, bool canClick)
    {
        if (!ReferenceEquals(run, NoireTour.ActiveRun))
            return;

        switch (step.Advance)
        {
            case TourAdvance.OnTargetClick:
                if (canClick && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && TourLayout.Contains(target, ImGui.GetIO().MousePos))
                    run.AdvanceAtFrame = NoireUI.FrameCount + 1;

                break;

            case TourAdvance.WhenReady:
                if (step.SettleSeconds > 0f)
                {
                    if (run.SettleFraction >= 1f)
                        NoireTour.Next();

                    break;
                }

                if (step.IsReady?.Invoke() == true)
                    NoireTour.Next();

                break;
        }
    }
}
