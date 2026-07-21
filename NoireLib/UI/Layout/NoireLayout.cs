using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Containers that take their body instead of handing you a scope to close.<br/>
/// There is no <c>using</c>, no <c>Dispose</c> and no <c>End</c> anywhere here: nesting is the scope, the layout the
/// container implies comes with it, and a body whose begin failed is simply never called, so there is no
/// <c>if (child.Success)</c> to get wrong.
/// </summary>
/// <remarks>
/// Every body runs through the same guard, so raw ImGui stays fully available inside one: a push left unpopped is
/// unwound at the container boundary and logged once naming the container, instead of quietly recolouring everything
/// drawn after it.<br/>
/// Each container has a state overload taking the value the body needs, so the body can stay a <see langword="static"/>
/// lambda. The simpler overload allocates one delegate per call, which is a few dozen bytes a frame and invisible in
/// most UIs; the state overload is there for the cases where it is not.
/// </remarks>
[NoireFacade]
public static partial class NoireLayout
{
    /// <summary>
    /// Groups the body so it measures and hit-tests as a single item.
    /// </summary>
    /// <param name="body">The drawing to group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Group(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Group(body, static b => b());
    }

    /// <summary>
    /// Groups the body so it measures and hit-tests as a single item.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to group.</param>
    public static void Group<TState>(TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ImGui.BeginGroup();

        try
        {
            UiScope.Run(nameof(Group), state, body);
        }
        finally
        {
            ImGui.EndGroup();
        }
    }

    /// <summary>
    /// Indents the body by <paramref name="amount"/> pixels, and puts the cursor back where it was afterwards.
    /// </summary>
    /// <remarks>
    /// An amount of zero or less indents by nothing at all, which is deliberately unlike ImGui's own <c>Indent</c>:
    /// that one reads zero as "use the default step", so an animated indent easing down to zero would jump a whole
    /// step outwards on its last frame instead of arriving where it was heading. Ask for the default step by name with
    /// <see cref="DefaultIndent"/>.
    /// </remarks>
    /// <param name="amount">The indent in pixels. Zero or less does not indent.</param>
    /// <param name="body">The drawing to indent.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Indent(float amount, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Indent(amount, body, static b => b());
    }

    /// <summary>
    /// Indents the body by <paramref name="amount"/> pixels, and puts the cursor back where it was afterwards.
    /// </summary>
    /// <remarks>
    /// An amount of zero or less indents by nothing at all, which is deliberately unlike ImGui's own <c>Indent</c>:
    /// that one reads zero as "use the default step", so an animated indent easing down to zero would jump a whole
    /// step outwards on its last frame instead of arriving where it was heading. Ask for the default step by name with
    /// <see cref="DefaultIndent"/>.
    /// </remarks>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="amount">The indent in pixels. Zero or less does not indent.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to indent.</param>
    public static void Indent<TState>(float amount, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (amount <= 0f)
        {
            UiScope.Run(nameof(Indent), state, body);
            return;
        }

        ImGui.Indent(amount);

        try
        {
            UiScope.Run(nameof(Indent), state, body);
        }
        finally
        {
            ImGui.Unindent(amount);
        }
    }

    /// <summary>
    /// The current ImGui indent step in pixels, for passing to <see cref="Indent(float, Action)"/> when you want the
    /// standard amount rather than a measured one.
    /// </summary>
    public static float DefaultIndent => NoireService.IsInitialized() ? ImGui.GetStyle().IndentSpacing : NoireUI.Scaled(21f);

    /// <summary>
    /// Puts the body in its own id namespace, so two copies of the same widget code can coexist without colliding.
    /// </summary>
    /// <param name="id">The id to push.</param>
    /// <param name="body">The drawing to namespace.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Id(string id, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Id(id, body, static b => b());
    }

    /// <summary>
    /// Puts the body in its own id namespace, so two copies of the same widget code can coexist without colliding.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="id">The id to push.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to namespace.</param>
    public static void Id<TState>(string id, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ImGui.PushID(id);

        try
        {
            UiScope.Run(nameof(Id), state, body);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>
    /// Draws the body greyed out and unclickable when <paramref name="disabled"/> is true, and normally when it is not,
    /// so the two cases are one call rather than two branches.
    /// </summary>
    /// <param name="disabled">Whether to disable the body.</param>
    /// <param name="body">The drawing to gate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Disabled(bool disabled, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Disabled(disabled, body, static b => b());
    }

    /// <summary>
    /// Draws the body greyed out and unclickable when <paramref name="disabled"/> is true, and normally when it is not.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="disabled">Whether to disable the body.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to gate.</param>
    public static void Disabled<TState>(bool disabled, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ImGui.BeginDisabled(disabled);

        try
        {
            UiScope.Run(nameof(Disabled), state, body);
        }
        finally
        {
            ImGui.EndDisabled();
        }
    }

    /// <summary>
    /// Sizes every widget in the body to <paramref name="width"/>, instead of setting it before each one.
    /// </summary>
    /// <param name="width">The item width in pixels. A negative value is measured back from the right edge.</param>
    /// <param name="body">The drawing to size.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void ItemWidth(float width, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ItemWidth(width, body, static b => b());
    }

    /// <summary>
    /// Sizes every widget in the body to <paramref name="width"/>, instead of setting it before each one.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="width">The item width in pixels. A negative value is measured back from the right edge.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to size.</param>
    public static void ItemWidth<TState>(float width, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ImGui.PushItemWidth(width);

        try
        {
            UiScope.Run(nameof(ItemWidth), state, body);
        }
        finally
        {
            ImGui.PopItemWidth();
        }
    }

    /// <summary>
    /// Wraps the text in the body at <paramref name="width"/> pixels from the current cursor.
    /// </summary>
    /// <remarks>
    /// ImGui's own <c>PushTextWrapPos</c> takes a window-local x coordinate, not a screen one. Passing a screen
    /// coordinate (the natural mistake, since laying a panel out uses screen coordinates) puts the wrap point far off to
    /// the right, where it silently does nothing and the text simply never wraps. This takes a width and does the
    /// conversion, so the mistake is not reachable.
    /// </remarks>
    /// <param name="width">The wrap width in pixels, measured from the cursor.</param>
    /// <param name="body">The drawing to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void WrapText(float width, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        WrapText(width, body, static b => b());
    }

    /// <summary>
    /// Wraps the text in the body at <paramref name="width"/> pixels from the current cursor.
    /// </summary>
    /// <remarks>
    /// ImGui's own <c>PushTextWrapPos</c> takes a window-local x coordinate, not a screen one, so passing a screen
    /// coordinate puts the wrap point far off to the right where it silently does nothing. This takes a width and does
    /// the conversion, so the mistake is not reachable.
    /// </remarks>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="width">The wrap width in pixels, measured from the cursor.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to wrap.</param>
    public static void WrapText<TState>(float width, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);

        try
        {
            UiScope.Run(nameof(WrapText), state, body);
        }
        finally
        {
            ImGui.PopTextWrapPos();
        }
    }

    /// <summary>
    /// Draws the body inside a scrolling, clipped child region.<br/>
    /// The body is not called when the region is entirely clipped away, which is the case a hand-written
    /// <c>BeginChild</c> has to remember to check.
    /// </summary>
    /// <param name="id">A unique id for the region.</param>
    /// <param name="size">The region size. A zero component fills the available space; a negative one leaves that many pixels.</param>
    /// <param name="body">The drawing to put inside.</param>
    /// <param name="border">Whether to outline the region.</param>
    /// <param name="flags">Extra window flags for the region.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Child(string id, Vector2 size, Action body, bool border = false, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        ArgumentNullException.ThrowIfNull(body);
        Child(id, size, body, static b => b(), border, flags);
    }

    /// <summary>
    /// Draws the body inside a scrolling, clipped child region.<br/>
    /// The body is not called when the region is entirely clipped away, which is the case a hand-written
    /// <c>BeginChild</c> has to remember to check.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="id">A unique id for the region.</param>
    /// <param name="size">The region size.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to put inside.</param>
    /// <param name="border">Whether to outline the region.</param>
    /// <param name="flags">Extra window flags for the region.</param>
    public static void Child<TState>(string id, Vector2 size, TState state, Action<TState> body, bool border = false, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        ArgumentNullException.ThrowIfNull(body);

        // BeginChild pairs with EndChild whatever it returns, unlike the popup and tooltip calls below.
        var visible = ImGui.BeginChild(id, size, border, flags);

        try
        {
            if (visible)
                UiScope.Run(nameof(Child), state, body);
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    /// <summary>
    /// Draws the body inside a tooltip.
    /// </summary>
    /// <param name="body">The tooltip contents.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Tooltip(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Tooltip(body, static b => b());
    }

    /// <summary>
    /// Draws the body inside a tooltip.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The tooltip contents.</param>
    public static void Tooltip<TState>(TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ImGui.BeginTooltip();

        try
        {
            UiScope.Run(nameof(Tooltip), state, body);
        }
        finally
        {
            ImGui.EndTooltip();
        }
    }

    /// <summary>
    /// Draws the body as a tooltip only while the item drawn just before is hovered, so the hover test and the tooltip
    /// are one call.
    /// </summary>
    /// <param name="body">The tooltip contents.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void TooltipOnItemHover(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (ImGui.IsItemHovered())
            Tooltip(body);
    }

    /// <summary>
    /// A labelled block: a heading, an optional wrapping description, and the body indented under them.<br/>
    /// This is the plain visual grouping; a collapsible section that remembers its state ships with the wider layout
    /// widgets.
    /// </summary>
    /// <param name="label">The heading.</param>
    /// <param name="body">The drawing to put under the heading.</param>
    /// <param name="description">Optional prose under the heading, wrapped to the available width.</param>
    /// <param name="indent">How far to indent the body, in pixels. Zero uses <see cref="DefaultIndent"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Section(string label, Action body, string? description = null, float indent = 0f)
    {
        ArgumentNullException.ThrowIfNull(body);
        Section(label, body, static b => b(), description, indent);
    }

    /// <summary>
    /// A labelled block: a heading, an optional wrapping description, and the body indented under them.<br/>
    /// This is the plain visual grouping; a collapsible section that remembers its state ships with the wider layout
    /// widgets.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="label">The heading.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to put under the heading.</param>
    /// <param name="description">Optional prose under the heading.</param>
    /// <param name="indent">How far to indent the body, in pixels. Zero uses <see cref="DefaultIndent"/>.</param>
    public static void Section<TState>(string label, TState state, Action<TState> body, string? description = null, float indent = 0f)
    {
        ArgumentNullException.ThrowIfNull(body);

        ImGui.TextUnformatted(label);
        ImGui.Separator();

        if (!string.IsNullOrEmpty(description))
        {
            ImGui.TextWrapped(description);
            ImGui.Spacing();
        }

        Indent(indent > 0f ? indent : DefaultIndent, state, body);
    }
}
