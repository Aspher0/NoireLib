using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NoireLib.Helpers;

/// <summary>
/// A helper class for manipulating Windows' OS windows state.<br/>
/// For example, maximizing, minimizing, switching between fullscreen, borderless, windowed, changing resolution, etc.
/// </summary>
public static class WindowHelper
{
    private const int MonitorDefaultToNearest = 2;
    private const int GwlStyle = -16;
    private const long WsOverlappedWindow = 0x00CF0000L;
    private const long WsPopup = 0x80000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsThickFrame = 0x00040000L;

    private static readonly nint HwndTopMost = new(-1);
    private static readonly nint HwndNoTopMost = new(-2);
    private static readonly nint HwndTop = nint.Zero;

    /// <summary>
    /// Gets the handle for the current process window, falling back to the foreground window when unavailable.
    /// </summary>
    /// <returns>The window handle; <see cref="nint.Zero"/> if none is found.</returns>
    public static nint GetCurrentWindowHandle()
    {
        var handle = Process.GetCurrentProcess().MainWindowHandle;
        return handle != nint.Zero ? handle : GetForegroundWindow();
    }

    /// <summary>
    /// Gets the handle of the game window.
    /// </summary>
    /// <returns>The game window handle.</returns>
    public static nint GetGameWindowHandle() => NoireService.PluginInterface.UiBuilder.WindowHandlePtr;

    /// <summary>
    /// Maximizes the specified window.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <returns><see langword="true"/> if the operation succeeds; otherwise <see langword="false"/>.</returns>
    public static bool Maximize(nint hWnd) => ShowWindowSafe(hWnd, ShowWindowCommand.ShowMaximized);

    /// <summary>
    /// Minimizes the specified window.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <returns><see langword="true"/> if the operation succeeds; otherwise <see langword="false"/>.</returns>
    public static bool Minimize(nint hWnd) => ShowWindowSafe(hWnd, ShowWindowCommand.ShowMinimized);

    /// <summary>
    /// Restores the specified window from a minimized or maximized state.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <returns><see langword="true"/> if the operation succeeds; otherwise <see langword="false"/>.</returns>
    public static bool Restore(nint hWnd) => ShowWindowSafe(hWnd, ShowWindowCommand.Restore);

    /// <summary>
    /// Sets whether the specified window stays on top of others.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <param name="topMost">Whether to keep the window topmost.</param>
    /// <returns><see langword="true"/> if the operation succeeds; otherwise <see langword="false"/>.</returns>
    public static bool SetTopMost(nint hWnd, bool topMost)
    {
        if (!ValidateWindow(hWnd))
            return false;

        return SetWindowPos(hWnd, topMost ? HwndTopMost : HwndNoTopMost, 0, 0, 0, 0,
            SetWindowPosFlags.IgnoreMove | SetWindowPosFlags.IgnoreResize | SetWindowPosFlags.NoActivate);
    }

    /// <summary>
    /// Sets the size and position of the specified window.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <param name="x">The left position.</param>
    /// <param name="y">The top position.</param>
    /// <param name="width">The width of the window.</param>
    /// <param name="height">The height of the window.</param>
    /// <param name="repaint">Whether to repaint after moving.</param>
    /// <returns><see langword="true"/> if the operation succeeds; otherwise <see langword="false"/>.</returns>
    public static bool SetPosition(nint hWnd, int x, int y, int width, int height, bool repaint = true)
    {
        if (!ValidateWindow(hWnd))
            return false;

        var flags = SetWindowPosFlags.NoZOrder;
        if (!repaint)
            flags |= SetWindowPosFlags.NoRedraw;

        return SetWindowPos(hWnd, HwndTop, x, y, width, height, flags);
    }

    /// <summary>
    /// Makes the specified window borderless and resizes it to fill the current monitor.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <returns><see langword="true"/> if the operation succeeds; otherwise <see langword="false"/>.</returns>
    public static bool SetBorderless(nint hWnd)
    {
        if (!ValidateWindow(hWnd) || !TryGetMonitorRect(hWnd, out var rect))
            return false;

        var style = GetWindowLongPtr(hWnd, GwlStyle).ToInt64();
        style &= ~WsOverlappedWindow;
        style |= WsPopup | WsVisible;
        SetWindowLongPtr(hWnd, GwlStyle, new nint(style));

        return SetWindowPos(hWnd, HwndTop, rect.Left, rect.Top, rect.Width, rect.Height,
            SetWindowPosFlags.FrameChanged | SetWindowPosFlags.ShowWindow);
    }

    /// <summary>
    /// Sets the specified window to windowed mode with optional centering and resizing behavior.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <param name="width">The desired window width.</param>
    /// <param name="height">The desired window height.</param>
    /// <param name="centerOnMonitor">Whether to center the window on its monitor.</param>
    /// <param name="resizable">Whether the window should be resizable.</param>
    /// <returns><see langword="true"/> if the operation succeeds; otherwise <see langword="false"/>.</returns>
    public static bool SetWindowed(nint hWnd, int width, int height, bool centerOnMonitor = true, bool resizable = true)
    {
        if (!ValidateWindow(hWnd))
            return false;

        if (centerOnMonitor && !TryGetMonitorRect(hWnd, out var rect, true))
            return false;

        var style = GetWindowLongPtr(hWnd, GwlStyle).ToInt64();
        style &= ~WsPopup;
        style |= WsOverlappedWindow;

        if (!resizable)
            style &= ~WsThickFrame;

        SetWindowLongPtr(hWnd, GwlStyle, new nint(style));

        var position = centerOnMonitor && TryGetMonitorRect(hWnd, out var targetRect, true)
            ? CenterIn(targetRect, width, height)
            : (X: 0, Y: 0);

        return SetWindowPos(hWnd, HwndTop, position.X, position.Y, width, height,
            SetWindowPosFlags.FrameChanged | SetWindowPosFlags.ShowWindow);
    }

    /// <summary>
    /// Centers the specified window on its monitor with the given dimensions.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <param name="width">The desired window width.</param>
    /// <param name="height">The desired window height.</param>
    /// <param name="useWorkArea">Whether to use the work area instead of the full monitor area.</param>
    /// <returns><see langword="true"/> if the operation succeeds; otherwise <see langword="false"/>.</returns>
    public static bool CenterOnMonitor(nint hWnd, int width, int height, bool useWorkArea = true)
    {
        if (!ValidateWindow(hWnd) || !TryGetMonitorRect(hWnd, out var rect, useWorkArea))
            return false;

        var position = CenterIn(rect, width, height);
        return SetWindowPos(hWnd, HwndTop, position.X, position.Y, width, height,
            SetWindowPosFlags.NoZOrder | SetWindowPosFlags.ShowWindow);
    }

    /// <summary>
    /// Gets the size of the specified window.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <param name="width">The window width.</param>
    /// <param name="height">The window height.</param>
    /// <returns><see langword="true"/> if the operation succeeds; otherwise <see langword="false"/>.</returns>
    public static bool TryGetWindowSize(nint hWnd, out int width, out int height)
    {
        if (!ValidateWindow(hWnd) || !GetWindowRect(hWnd, out var rect))
        {
            width = 0;
            height = 0;
            return false;
        }

        width = rect.Width;
        height = rect.Height;
        return true;
    }

    /// <summary>
    /// Returns whether the game window is focused.
    /// </summary>
    /// <returns>True if the game window is focused, false otherwise.</returns>
    public static bool IsGameWindowFocused()
    {
        var handle = GetGameWindowHandle();
        return handle != IntPtr.Zero && GetForegroundWindow() == handle;
    }

    private static (int X, int Y) CenterIn(RECT rect, int width, int height)
    {
        var x = rect.Left + ((rect.Width - width) / 2);
        var y = rect.Top + ((rect.Height - height) / 2);
        return (x, y);
    }

    private static bool ShowWindowSafe(nint hWnd, ShowWindowCommand command) => ValidateWindow(hWnd) && ShowWindow(hWnd, command);

    private static bool ValidateWindow(nint hWnd) => hWnd != nint.Zero && IsWindow(hWnd);

    private static bool TryGetMonitorRect(nint hWnd, out RECT rect, bool workArea = false)
    {
        var monitor = MonitorFromWindow(hWnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };

        if (!GetMonitorInfo(monitor, ref info))
        {
            rect = default;
            return false;
        }

        rect = workArea ? info.rcWork : info.rcMonitor;
        return true;
    }

    private static nint GetWindowLongPtr(nint hWnd, int nIndex) => IntPtr.Size == 8
        ? GetWindowLongPtr64(hWnd, nIndex)
        : new nint(GetWindowLong32(hWnd, nIndex));

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong) => IntPtr.Size == 8
        ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
        : new nint(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(nint hWnd, ShowWindowCommand nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, SetWindowPosFlags uFlags);

    /// <summary>
    /// Gets the handle of the foreground window (the window currently receiving input).
    /// </summary>
    /// <returns>The handle of the foreground window.</returns>
    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW", CharSet = CharSet.Auto)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW", CharSet = CharSet.Auto)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongW", CharSet = CharSet.Auto)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    private enum ShowWindowCommand : int
    {
        Hide = 0,
        ShowNormal = 1,
        ShowMinimized = 2,
        ShowMaximized = 3,
        ShowNoActivate = 4,
        Show = 5,
        Minimize = 6,
        ShowMinNoActive = 7,
        ShowNa = 8,
        Restore = 9,
        ShowDefault = 10,
        ForceMinimize = 11
    }

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        NoSize = 0x0001,
        IgnoreResize = NoSize,
        NoMove = 0x0002,
        IgnoreMove = NoMove,
        NoZOrder = 0x0004,
        NoRedraw = 0x0008,
        NoActivate = 0x0010,
        FrameChanged = 0x0020,
        ShowWindow = 0x0040,
        NoCopyBits = 0x0100,
        NoOwnerZOrder = 0x0200,
        DontSendChanging = 0x0400,
        DeferErase = 0x2000,
        AsyncWindowPos = 0x4000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

}
