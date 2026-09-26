using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A window drawn by the active skin: chrome, window menu, component tree and the behaviour every skin shares. A
/// native chrome hands the frame to Dalamud instead.
/// </summary>
public abstract class NoireSkinnedWindowBase : NoireWindow, IDisposable
{
    private static readonly List<NoireSkinnedWindowBase> AllWindows = [];

    private const string OptionSyncDisposeKey = "NoireLib.UI.NoireSkinnedWindowBase.OptionSync";

    private static bool optionSyncHooked;
    private static readonly WindowMenuStyle DefaultMenuStyle = new();

    private readonly List<TitleButton> titleButtons = [];
    private readonly List<NoireSkinnedWindowBase> attached = [];
    private readonly WindowResize resize = new();
    private readonly string menuId;
    private TitleButton[] visibleButtons = [];
    private WindowMenuSettings? options;
    private ComponentLayout? layout;
    private NoireSkin? layoutSkin;
    private NoireSkin? nativeButtonsFor;
    private bool collapsed;
    private bool resizePending;
    private bool sizeForced;
    private bool positionForced;
    private bool passClicks;
    private bool wasFocused;
    private bool menuOpen;
    private bool nativeMenuRequested;
    private float restoreHeight;
    private int groupFront;
    private int raisedFrame = -1;
    private int titleRevision = -1;
    private string fullName = string.Empty;
    private string? fittedFrom;
    private float fittedWidth = -1f;
    private float fittedFont = -1f;
    private NoireSkin? titleSkin;
    private UiVisibility appliedVisibility = UiVisibility.Default;
    private bool appliedAutoHide;
    private (int Colors, int StyleVars) pushedStyle;
    private Vector2 lastPos;
    private Vector2 lastSize;
    private Vector2 nativeMenuAnchor;

    /// <summary>Creates the window.</summary>
    /// <param name="id">A stable id: the ImGui id, and the key of its components, layout and options.</param>
    /// <param name="title">The title.</param>
    protected NoireSkinnedWindowBase(string id, NoireString title)
        : base(title.Text + "###" + id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        Title = title;
        OptionsKey = id;
        menuId = id + ".menu";
        Root = new WindowRoot(this);
        SizeCondition = ImGuiCond.FirstUseEver;
        DisableFadeInFadeOut = true;
        AllWindows.Add(this);
        HookOptionSync();
    }

    /// <summary>Every skinned window of the plugin that is not disposed.</summary>
    public static IReadOnlyList<NoireSkinnedWindowBase> All => AllWindows;

    /// <summary>The text size of the window being drawn, from its window menu; 1 outside a skinned window.</summary>
    public static float CurrentTextScale { get; private set; } = 1f;

    /// <summary>The skinned window being drawn, or <see langword="null"/> outside one.</summary>
    public static NoireSkinnedWindowBase? Current { get; private set; }

    /// <summary>The stable id.</summary>
    public string Id { get; }

    /// <summary>The title.</summary>
    public NoireString Title { get; }

    /// <summary>A second line of title, when the chrome shows one.</summary>
    public NoireString? Subtitle { get; set; }

    /// <summary>The size a first open uses, at 100%.</summary>
    public Vector2 DefaultSize { get; init; } = new(500f, 600f);

    /// <summary>The smallest size, at 100%.</summary>
    public Vector2 MinimumSize { get; init; } = new(300f, 200f);

    /// <summary>Whether the window keeps <see cref="DefaultSize"/>.</summary>
    public bool FixedSize { get; init; }

    /// <summary>Whether a drag on empty body space moves the window.</summary>
    public bool DragFromBody { get; init; } = true;

    /// <summary>Whether a drag on this window's header moves the window it is attached to instead of this one.</summary>
    public bool MovesOwner { get; init; } = true;

    /// <summary>Whether the window remembers a position and size of its own under each skin.</summary>
    public bool PlacementPerSkin { get; init; }

    /// <summary>The ImGui window name, rebuilt when the language or the skin changes; its id part keeps the position and size.</summary>
    /// <param name="skin">The active skin.</param>
    /// <returns>The title, then <c>###</c> and <see cref="Id"/>, followed by the skin's id when <see cref="PlacementPerSkin"/> is set.</returns>
    protected virtual string WindowNameFor(NoireSkin skin) => PlacementPerSkin ? Title.Text + "###" + Id + "." + skin.Id : Title.Text + "###" + Id;

    /// <summary>The key the window menu's options are remembered under; windows giving the same key share one options object.</summary>
    public string OptionsKey { get; init; }

    /// <summary>The window menu's options: opacity, text size, locks, click through, always on top, stay visible, reduced motion.</summary>
    public WindowMenuSettings Options => options ??= NoireSkinsStore.LoadChrome(OptionsKey);

    /// <summary>The top-level components, in the order they were added.</summary>
    public IReadOnlyList<Component> Children => Root.Children;

    /// <summary>Whether the window is collapsed to its strip.</summary>
    public bool IsCollapsed => collapsed;

    /// <summary>The window this one is attached to, or <see langword="null"/>.</summary>
    public NoireSkinnedWindowBase? Owner { get; private set; }

    /// <summary>The windows attached to this one.</summary>
    public IReadOnlyList<NoireSkinnedWindowBase> Attached => attached;

    /// <summary>Confirmations asked over this window.</summary>
    public NoireAsk Ask { get; } = new();

    /// <summary>The window's top left corner this frame, in screen pixels.</summary>
    public Vector2 WindowMin { get; private set; }

    /// <summary>The window's bottom right corner this frame.</summary>
    public Vector2 WindowMax { get; private set; }

    /// <summary>The body's top left corner this frame, under the header.</summary>
    public Vector2 BodyMin { get; private set; }

    /// <summary>The body's bottom right corner this frame.</summary>
    public Vector2 BodyMax { get; private set; }

    /// <summary>Whether clicks on the body go through to the game this frame: click through is on and the mouse is off the header and the resize edges.</summary>
    public bool PassingClicks => passClicks;

    /// <summary>Whether the layout editor is open beside this window.</summary>
    public bool EditingLayout { get; set; }

    /// <summary>Whether the user can arrange anything: the window has components or title buttons.</summary>
    public bool HasLayout => Root.Children.Count > 0 || titleButtons.Count > 0;

    /// <summary>How the active skin shows this window.</summary>
    public Presentation Presentation => NoireSkins.Active.PresentationOf(GetType());

    /// <summary>Whether a confirmation or a modal window attached to this one blocks it.</summary>
    public bool Blocked
    {
        get
        {
            if (Ask.Open)
                return true;

            foreach (var window in attached)
            {
                if (window.IsOpen && window.Presentation == Presentation.Modal)
                    return true;
            }

            return false;
        }
    }

    internal WindowRoot Root { get; }

    internal IReadOnlyList<TitleButton> DeclaredButtons => titleButtons;

    /// <summary>Finds a component by its path below the window, such as <c>list/row[3012]/star</c>.</summary>
    /// <param name="path">The ids below the window joined by <c>/</c>.</param>
    /// <returns>The component, or <see langword="null"/>.</returns>
    public Component? Find(string path) => Root.Find(path);

    /// <summary>Opens the window and brings it to the front.</summary>
    public void Open()
    {
        IsOpen = true;
        BringToFront();
    }

    /// <summary>Collapses or expands the window.</summary>
    /// <param name="value">True to collapse.</param>
    public void SetCollapsed(bool value)
    {
        if (collapsed == value)
            return;

        if (value)
            restoreHeight = lastSize.Y > 0f ? lastSize.Y / NoireUI.Scale : DefaultSize.Y;

        collapsed = value;
        resizePending = true;
    }

    /// <summary>Stores <see cref="Options"/> after the plugin changed them in code.</summary>
    public void SaveOptions() => NoireSkinsStore.SaveChrome(OptionsKey, Options);

    /// <summary>Restores the window's default arrangement for the active skin.</summary>
    public void ResetLayout()
    {
        LayoutFor(NoireSkins.Active).Clear();
        SaveLayout();
    }

    /// <summary>Disposes the component tree and the views, and forgets the window.</summary>
    public void Dispose()
    {
        Root.Dispose();
        DropViews();
        AllWindows.Remove(this);

        if (appliedAutoHide)
            ApplyAutoHide();

        GC.SuppressFinalize(this);
    }

    /// <summary>Registers a top-level component under an id and attaches it.</summary>
    /// <typeparam name="T">The component's type.</typeparam>
    /// <param name="id">An id unique among the window's top-level components.</param>
    /// <param name="child">The component.</param>
    /// <returns>The component.</returns>
    protected T Add<T>(string id, T child) where T : Component
    {
        Root.AddChild(id, child);
        return child;
    }

    /// <summary>Detaches a top-level component and disposes it.</summary>
    /// <param name="child">The component.</param>
    protected void Remove(Component child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!ReferenceEquals(child.Parent, Root))
            throw new ArgumentException($"'{child.Id}' is not a top-level component of '{Id}'.", nameof(child));

        Root.Detach(child);
        child.Dispose();
    }

    /// <summary>Adds a title bar button; a native chrome shows it as a Dalamud title bar button.</summary>
    /// <param name="id">A stable id, also the key the user hides it under.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="tooltip">The tooltip.</param>
    /// <param name="click">What it does.</param>
    /// <param name="visible">Whether it shows, read each frame, or <see langword="null"/> for always.</param>
    protected void HeaderButton(string id, NoireIcon icon, NoireString tooltip, Action click, Func<bool>? visible = null)
        => titleButtons.Add(new TitleButton(id, icon, tooltip, click) { Visible = visible });

    /// <summary>
    /// Attaches a window: raised together, hidden while this one is closed, dragged by its header. Add it to the window
    /// system after this one.
    /// </summary>
    /// <param name="window">The window.</param>
    protected void Attach(NoireSkinnedWindowBase window)
    {
        ArgumentNullException.ThrowIfNull(window);

        attached.Add(window);
        window.Owner = this;
    }

    /// <summary>The window's content, between the chrome and the overlays.</summary>
    protected abstract void DrawBody();

    /// <summary>What is drawn above the body and the chrome: menus, cards, pills.</summary>
    protected virtual void DrawOverlay()
    {
    }

    /// <summary>Whether the window draws this frame: not while hidden by the skin, or while its owner is closed or collapsed.</summary>
    /// <returns>True when it draws.</returns>
    public override bool DrawConditions()
    {
        HookOptionSync();
        SyncOptions();

        if (Presentation == Presentation.Hidden || !NoireSkins.Active.Fonts.IsReady)
            return false;

        if (Owner is { } owner && (!owner.IsOpen || owner.IsCollapsed))
            return false;

        return base.DrawConditions();
    }

    /// <summary>Applies the window's options and the active chrome before ImGui begins the window.</summary>
    public override void PreDraw()
    {
        var chrome = NoireSkins.Active.ChromeFor(this);
        var scale = NoireUI.Scale;

        RefreshTitle();

        SyncOptions();

        RespectCloseHotkey = !menuOpen && !Ask.Open;
        CollectButtons();
        pushedStyle = default;

        if (chrome.Native)
        {
            PreDrawNative();
            FitNativeTitle();
            return;
        }

        DisableFadeInFadeOut = true;

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;

        if (collapsed)
            flags |= ImGuiWindowFlags.NoSavedSettings;

        passClicks = Options.ClickThrough && !IsMouseOverChrome(chrome.Metrics, scale);

        if (passClicks)
            flags |= ImGuiWindowFlags.NoMouseInputs;

        Flags = flags;
        BgAlpha = null;
        AllowPinning = false;
        AllowClickthrough = false;
        TitleBarButtons.Clear();
        nativeButtonsFor = null;
        ApplyConstraints(chrome.Metrics);

        if (Presentation == Presentation.Modal && Owner != null)
        {
            Position = ((Owner.WindowMin + Owner.WindowMax) * 0.5f) - (DefaultSize * scale * 0.5f);
            PositionCondition = ImGuiCond.Always;
            positionForced = true;
        }

        if (resizePending)
        {
            var width = lastSize.X > 0f ? lastSize.X / scale : DefaultSize.X;
            Size = new Vector2(width, collapsed ? chrome.Metrics.CollapsedHeight : restoreHeight);
            SizeCondition = ImGuiCond.Always;
            sizeForced = true;
            resizePending = false;
        }

        var depth = UiStyleDepth.Capture();
        chrome.PushWindowStyle(scale);
        pushedStyle = depth.Since();
    }

    /// <summary>Pops the chrome's window style.</summary>
    public override void PostDraw()
    {
        UiStyleDepth.Pop(pushedStyle);
        pushedStyle = default;
        // Only what this window forced: condition 0 is Always, and would pin a position set by anyone else.
        if (sizeForced)
        {
            SizeCondition = ImGuiCond.FirstUseEver;
            sizeForced = false;
        }

        if (positionForced)
        {
            Position = null;
            PositionCondition = ImGuiCond.FirstUseEver;
            positionForced = false;
        }
    }

    /// <summary>Draws the chrome, the body, the overlays, the confirmation and the window menu.</summary>
    public sealed override void Draw()
    {
        var skin = NoireSkins.Active;
        var chrome = skin.ChromeFor(this);
        var steps = (chrome.MenuStyle ?? DefaultMenuStyle).TextSteps;

        CurrentTextScale = steps.Length > 0 ? steps[Math.Clamp(Options.TextStep, 0, steps.Length - 1)] : 1f;
        var outer = Current;
        Current = this;
        NoireUI.EnterWindowMotion(Options.ReducedMotion);
        NoireSkins.EnterTheme();

        try
        {
            var inFront = Options.AlwaysOnTop || Presentation == Presentation.Modal;

            if (inFront)
                NoireWindowChrome.KeepInFront();

            var raised = TickGroupFocus();

            if (chrome.Native)
                DrawNative(skin, chrome);
            else
                DrawCustom(skin, chrome);

            // Popups and combo lists this window opened come forward with it.
            if (inFront || raised)
                UiWindowOrder.KeepPopupsInFront(topLayer: inFront);

            menuOpen = NoireWindowMenu.IsOpen(menuId);
        }
        finally
        {
            NoireSkins.LeaveTheme();
            NoireUI.LeaveWindowMotion();
            CurrentTextScale = 1f;
            Current = outer;
        }
    }

    /// <summary>Closes the attached windows, cancels a pending confirmation and closes the window menu.</summary>
    public override void OnClose()
    {
        foreach (var window in attached)
            window.IsOpen = false;

        Ask.Answer(ConfirmAnswer.Cancelled);
        NoireWindowMenu.Close(menuId);
        EditingLayout = false;
        base.OnClose();
    }

    internal static void SkinChanged()
    {
        foreach (var window in AllWindows)
        {
            window.DropViews();
            window.EditingLayout = false;
            window.nativeButtonsFor = null;
        }
    }

    internal ComponentLayout LayoutFor(NoireSkin skin)
    {
        if (layout == null || !ReferenceEquals(layoutSkin, skin))
        {
            layout = NoireSkinsStore.LoadLayout(skin.Id, Id);
            layoutSkin = skin;
        }

        return layout;
    }

    internal void SaveLayout()
    {
        if (layout != null && layoutSkin != null)
            NoireSkinsStore.SaveLayout(layoutSkin.Id, Id, layout);
    }

    internal virtual void DropViews() => Root.DropViews();

    // A module shows the next window down in place of a skinned window the active skin hides.
    internal static Window? UnlessHidden(Window? window)
        => window is NoireSkinnedWindowBase { Presentation: Presentation.Hidden } ? null : window;

    private void DrawCustom(NoireSkin skin, IChromeSkin chrome)
    {
        var metrics = chrome.Metrics;
        var scale = NoireUI.Scale;

        if (!Options.LockPosition)
            NoireWindowChrome.ContinueDrag(ImGuiMouseCursor.Arrow);

        var resizable = Resizable;

        if (resizable)
            resize.Tick(metrics, scale, MinimumSize, Options.LockWidth, Options.LockHeight, Options.LockPosition);

        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        WindowMin = lastPos = min;
        WindowMax = max;
        lastSize = max - min;

        var frame = new ChromeFrame(min, max, scale, Math.Clamp(Options.Opacity, 0.2f, 1f), collapsed, wasFocused, Options.AlwaysOnTop,
            Options.ClickThrough, resizable, Id, (Owner ?? this).Id, NoireWindowMenu.IsOpen(menuId));

        chrome.Background(frame);

        var title = new ChromeTitle(Title.Text, Subtitle?.Text);
        var headerMax = new Vector2(max.X, min.Y + ((collapsed ? metrics.CollapsedHeight : metrics.HeaderHeight) * scale));
        Vector2 menuMin;
        Vector2 menuMax;
        var control = collapsed
            ? chrome.Collapsed(frame, title, visibleButtons, out menuMin, out menuMax)
            : chrome.Header(frame, title, visibleButtons, out menuMin, out menuMax);

        HandleControl(control);

        if (!collapsed)
        {
            BodyMin = new Vector2(min.X, headerMax.Y);
            BodyMax = max;

            ImGui.PushClipRect(BodyMin, BodyMax, true);

            try
            {
                DrawContent(skin);
            }
            finally
            {
                ImGui.PopClipRect();
            }

        }

        DrawOverlays(skin, chrome);

        if (!collapsed && resizable && !Options.LockWidth && !Options.LockHeight)
            chrome.ResizeGrip(frame);

        if (Options.ClickThrough)
            chrome.ClickThroughOutline(frame);

        DrawWindowMenu(chrome, menuMin, menuMax);
        HandleFrameInput(min, headerMax);
    }

    private void DrawNative(NoireSkin skin, IChromeSkin chrome)
    {
        WindowMin = lastPos = ImGui.GetWindowPos();
        WindowMax = WindowMin + ImGui.GetWindowSize();
        BodyMin = ImGui.GetCursorScreenPos();
        BodyMax = BodyMin + ImGui.GetContentRegionAvail();
        lastSize = WindowMax - WindowMin;

        if (nativeMenuRequested)
        {
            nativeMenuRequested = false;
            nativeMenuAnchor = ImGui.GetMousePos();
            NoireWindowMenu.Toggle(menuId);
        }

        DrawContent(skin);
        DrawOverlays(skin, chrome);
        DrawWindowMenu(chrome, nativeMenuAnchor, nativeMenuAnchor);
    }

    private void DrawContent(NoireSkin skin)
    {
        var fonts = skin.Fonts;

        ImGui.SetCursorScreenPos(BodyMin);
        ImGui.BeginDisabled(Blocked);
        fonts.Push(TextRole.Body);

        var effects = NoireEffects.Depth;

        try
        {
            DrawBody();
        }
        finally
        {
            // An effect scope the body left open is closed here, while its draw list is still this window's.
            NoireEffects.CloseAbove(effects);
            fonts.Pop();
            ImGui.EndDisabled();
        }
    }

    private void DrawOverlays(NoireSkin skin, IChromeSkin chrome)
    {
        if (collapsed)
            return;

        UiHover.ReleaseDisabled();
        DrawOverlay();

        if (Blocked)
            chrome.BlockedVeil(BodyMin, BodyMax, NoireUI.Scale);

        if (Ask.Open)
            Ask.Answer(skin.Overlays.Confirm(Ask.State, BodyMin, BodyMax));

        if (EditingLayout)
            LayoutEditor.Draw(this);
    }

    // An attached window's header moves its owner.
    private void HandleFrameInput(Vector2 min, Vector2 headerMax)
    {
        if (resize.Resizing || Presentation == Presentation.Modal)
            return;

        var hasHeader = headerMax.Y > min.Y && headerMax.X > min.X;

        if (hasHeader && NoireWindowChrome.DoubleClickFrom(min, headerMax))
            SetCollapsed(!collapsed);

        if (Options.LockPosition)
            return;

        if (Owner is { } owner && MovesOwner)
        {
            if (!hasHeader)
                return;

            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton("##owner-drag", headerMax - min);

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f) && !owner.Options.LockPosition)
                ImGui.SetWindowPos(owner.WindowName, owner.WindowMin + ImGui.GetIO().MouseDelta);

            return;
        }

        if (hasHeader)
            NoireWindowChrome.DragFrom(min, headerMax, ImGuiMouseCursor.Arrow);

        if (DragFromBody && !Options.ClickThrough && !collapsed)
            NoireWindowChrome.DragFromBody(ImGuiMouseCursor.Arrow);
    }

    private void HandleControl(ChromeControl control)
    {
        switch (control)
        {
            case ChromeControl.Menu:
                NoireWindowMenu.Toggle(menuId);
                break;
            case ChromeControl.Collapse:
                SetCollapsed(!collapsed);
                break;
            case ChromeControl.Close:
                IsOpen = false;
                break;
        }
    }

    // Every frame, open or not: the menu tracks a press on its button while closed.
    private void DrawWindowMenu(IChromeSkin chrome, Vector2 anchorMin, Vector2 anchorMax)
    {
        var result = NoireWindowMenu.Draw(menuId, anchorMin, anchorMax, Options, chrome.MenuStyle);

        if (result.Changed)
            NoireSkinsStore.SaveChrome(OptionsKey, Options);
    }

    private void PreDrawNative()
    {
        SetUpNativeWindow();

        if (Presentation == Presentation.Modal && Owner != null)
        {
            Flags |= ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove;
            Position = ((Owner.WindowMin + Owner.WindowMax) * 0.5f) - (DefaultSize * NoireUI.Scale * 0.5f);
            PositionCondition = ImGuiCond.Always;
            positionForced = true;
        }

        if (resizePending)
        {
            Collapsed = collapsed;
            CollapsedCondition = ImGuiCond.Always;
            resizePending = false;
        }
        else if (Collapsed != null)
        {
            // Applied once: left set, condition 0 would force it every frame.
            Collapsed = null;
        }
    }

    /// <summary>Sets the Dalamud window up for a native chrome, every frame before ImGui begins it. Override to do it yourself.</summary>
    protected virtual void SetUpNativeWindow()
    {
        Flags = FixedSize ? ImGuiWindowFlags.NoResize : ImGuiWindowFlags.None;

        if (Options.LockPosition)
            Flags |= ImGuiWindowFlags.NoMove;

        BgAlpha = Math.Clamp(Options.Opacity, 0.2f, 1f);
        DisableFadeInFadeOut = true;
        AllowPinning = true;
        AllowClickthrough = true;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = FixedSize ? DefaultSize : MinimumSize,
            MaximumSize = FixedSize ? DefaultSize : new Vector2(float.MaxValue, float.MaxValue),
        };
        Size ??= DefaultSize;

        SyncNativeButtons();
    }

    // Rebuilt on a skin or visibility change, never per frame.
    private void SyncNativeButtons()
    {
        if (ReferenceEquals(nativeButtonsFor, NoireSkins.Active))
            return;

        nativeButtonsFor = NoireSkins.Active;
        TitleBarButtons.Clear();

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Bars,
            Click = _ => nativeMenuRequested = true,
            ShowTooltip = static () => PlainTooltip(NoireStrings.WindowOptions.Text),
        });

        foreach (var button in visibleButtons)
        {
            TitleBarButtons.Add(new TitleBarButton
            {
                Icon = NoireIcons.Glyph(button.Icon) ?? FontAwesomeIcon.ExternalLinkAlt,
                Click = _ => button.Click(),
                ShowTooltip = () => PlainTooltip(button.Tooltip.Text),
            });
        }
    }

    // The array only changes when the set of shown buttons does.
    private void CollectButtons()
    {
        var hidden = LayoutFor(NoireSkins.Active).HiddenButtons;
        var count = 0;
        var same = true;

        foreach (var button in titleButtons)
        {
            if (hidden.Contains(button.Id) || !(button.Visible?.Invoke() ?? true))
                continue;

            same &= count < visibleButtons.Length && ReferenceEquals(visibleButtons[count], button);
            count++;
        }

        if (same && count == visibleButtons.Length)
            return;

        visibleButtons = new TitleButton[count];
        count = 0;

        foreach (var button in titleButtons)
        {
            if (!hidden.Contains(button.Id) && (button.Visible?.Invoke() ?? true))
                visibleButtons[count++] = button;
        }

        nativeButtonsFor = null;
    }

    // Attached windows share focus: the owner comes forward and they follow above it. True when this window came forward.
    private bool TickGroupFocus()
    {
        var focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        var root = Owner ?? this;
        var frame = ImGui.GetFrameCount();

        if (focused && !wasFocused)
            root.groupFront = 1;

        wasFocused = focused;

        if (ReferenceEquals(root, this) && groupFront > 0)
        {
            groupFront--;
            raisedFrame = frame;
        }

        if (root.raisedFrame != frame || Options.AlwaysOnTop || !NoireService.IsInitialized())
            return false;

        ImGuiP.BringWindowToDisplayFront(ImGuiP.GetCurrentWindow());
        return true;
    }

    private bool IsMouseOverChrome(in ChromeMetrics metrics, float scale)
    {
        if (lastSize.X <= 0f || !UiDraw.Available)
            return false;

        var mouse = ImGui.GetMousePos();
        var headerMax = lastPos + new Vector2(lastSize.X, metrics.HeaderHeight * scale);

        if (mouse.X >= lastPos.X && mouse.Y >= lastPos.Y && mouse.X < headerMax.X && mouse.Y < headerMax.Y)
            return true;

        return Resizable && WindowResize.IsOver(mouse, lastPos, lastPos + lastSize, metrics, scale);
    }

    private bool Resizable => !collapsed && !FixedSize && Presentation != Presentation.Modal && !(Options.LockWidth && Options.LockHeight);

    private void ApplyConstraints(in ChromeMetrics metrics)
    {
        var minimum = FixedSize ? DefaultSize : MinimumSize;
        var maxWidth = FixedSize ? DefaultSize.X : float.MaxValue;

        SizeConstraints = collapsed
            ? new WindowSizeConstraints { MinimumSize = new Vector2(minimum.X, metrics.CollapsedHeight), MaximumSize = new Vector2(maxWidth, metrics.CollapsedHeight) }
            : new WindowSizeConstraints { MinimumSize = minimum, MaximumSize = FixedSize ? DefaultSize : new Vector2(float.MaxValue, float.MaxValue) };

        Size ??= DefaultSize;
    }

    // Rebuilt only when the language or skin changed.
    private void RefreshTitle()
    {
        var revision = NoireLanguages.Revision;
        var skin = NoireSkins.Active;

        if (revision == titleRevision && ReferenceEquals(skin, titleSkin))
            return;

        titleRevision = revision;
        titleSkin = skin;
        WindowName = fullName = WindowNameFor(skin);
    }

    // Dalamud draws its title bar buttons over the title: the title part ends in an ellipsis before them.
    private void FitNativeTitle()
    {
        if (!UiDraw.Available)
            return;

        var width = lastSize.X;
        var font = ImGui.GetFontSize();

        if (ReferenceEquals(fittedFrom, fullName) && width == fittedWidth && font == fittedFont)
            return;

        fittedFrom = fullName;
        fittedWidth = width;
        fittedFont = font;

        var split = fullName.IndexOf("###", StringComparison.Ordinal);
        var title = split < 0 ? fullName : fullName[..split];
        var id = split < 0 ? string.Empty : fullName[split..];

        if (width <= 0f || title.Length == 0)
        {
            WindowName = fullName;
            return;
        }

        var style = ImGui.GetStyle();
        var slot = font + style.ItemInnerSpacing.X;
        var collapsible = (Flags & ImGuiWindowFlags.NoCollapse) == 0;
        var collapseRight = collapsible && style.WindowMenuButtonPosition == ImGuiDir.Right;
        var native = (ShowCloseButton ? 1 : 0) + (collapseRight ? 1 : 0);

        // Dalamud adds a button for pinning and click through when either is allowed and its option is on.
        var buttons = TitleBarButtons.Count + (AllowPinning || AllowClickthrough ? 1 : 0);
        var left = style.FramePadding.X + (collapsible && !collapseRight ? slot : 0f);
        var right = (native == 0 ? style.FramePadding.X : 0f) + ((native + buttons) * slot);
        var room = width - left - right - style.ItemInnerSpacing.X;

        WindowName = Fit(title, room) + id;
    }

    private static string Fit(string title, float room)
    {
        const string Ellipsis = "...";

        if (ImGui.CalcTextSize(title).X <= room)
            return title;

        var low = 0;
        var high = title.Length;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;

            if (ImGui.CalcTextSize(title[..mid] + Ellipsis).X <= room)
                low = mid;
            else
                high = mid - 1;
        }

        // A surrogate pair is never cut in half.
        if (low > 0 && char.IsHighSurrogate(title[low - 1]))
            low--;

        return title[..low].TrimEnd() + Ellipsis;
    }

    // Dalamud's automatic hiding is one switch per plugin: off while any window asks to stay.
    private void SyncOptions()
    {
        if (Options.Visibility != appliedVisibility)
            Visibility = appliedVisibility = Options.Visibility;

        if (Options.StayAutoHide != appliedAutoHide)
        {
            appliedAutoHide = Options.StayAutoHide;
            ApplyAutoHide();
        }
    }

    private static void HookOptionSync()
    {
        if (optionSyncHooked || !NoireService.IsInitialized())
            return;

        optionSyncHooked = true;
        NoireService.Framework.Update += SyncAllOptions;

        if (!NoireLibMain.IsRegisteredOnDispose(OptionSyncDisposeKey))
            NoireLibMain.RegisterOnDispose(OptionSyncDisposeKey, UnhookOptionSync);
    }

    private static void UnhookOptionSync()
    {
        if (!optionSyncHooked)
            return;

        optionSyncHooked = false;

        if (NoireService.IsInitialized())
            NoireService.Framework.Update -= SyncAllOptions;
    }

    private static void SyncAllOptions(IFramework framework)
    {
        for (var index = AllWindows.Count - 1; index >= 0; index--)
            AllWindows[index].SyncOptions();
    }

    private static void ApplyAutoHide()
    {
        if (!NoireService.IsInitialized())
            return;

        var any = false;

        foreach (var window in AllWindows)
            any |= window.appliedAutoHide;

        NoireService.PluginInterface.UiBuilder.DisableAutomaticUiHide = any;
    }

    private static void PlainTooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
    }
}
