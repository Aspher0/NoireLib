using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace NoireLib.UI;

// Per-frame values read straight from Dalamud's ImGui context, not across native code each time. The address is taken
// once and checked against the native answers.
internal static unsafe class UiContext
{
    private static ImGuiContext* context;
    private static bool resolved;

    // Null until NoireLib is initialized, and for good if the binding's layout ever disagreed with the native calls.
    private static ImGuiContext* Context
    {
        get
        {
            if (resolved)
                return context;

            if (!NoireService.IsInitialized())
                return null;

            // Asked again until ImGui exists: a plugin can load before Dalamud's interface is up.
            var live = ImGui.GetCurrentContext().Handle;

            if (live == null)
                return null;

            resolved = true;

            if (live->FrameCount == ImGui.GetFrameCount() && &live->IO == ImGui.GetIO().Handle)
                context = live;

            return context;
        }
    }

    // What ImGui.GetCurrentContext answers, without the native call once the context has been checked.
    internal static ImGuiContextPtr Current
    {
        get
        {
            var live = Context;
            return live != null ? live : ImGui.GetCurrentContext();
        }
    }

    internal static int FrameCount
    {
        get
        {
            var live = Context;
            return live != null ? live->FrameCount : ImGui.GetFrameCount();
        }
    }

    internal static float Time
    {
        get
        {
            var live = Context;
            return live != null ? (float)live->Time : (float)ImGui.GetTime();
        }
    }

    internal static float DeltaTime
    {
        get
        {
            var live = Context;
            return live != null ? live->IO.DeltaTime : ImGui.GetIO().DeltaTime;
        }
    }

    // Dalamud's global scale, which reads the configured scale until ImGui exists.
    internal static float GlobalScale
    {
        get
        {
            var live = Context;
            return live != null ? live->IO.FontGlobalScale : ImGuiHelpers.GlobalScale;
        }
    }

    // What ImGui.GetWindowDrawList answers, including marking the window as written to, which ImGui's hover and move
    // logic reads. Null outside a window, where the native call would dereference nothing.
    internal static ImDrawListPtr WindowDrawList
    {
        get
        {
            var live = Context;

            if (live == null)
                return ImGui.GetWindowDrawList();

            var window = live->CurrentWindow;

            if (window == null)
                return ImDrawListPtr.Null;

            window->WriteAccessed = 1;
            return window->DrawList;
        }
    }
}
