using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Applies a style around a block of drawing.<br/>
/// There is nothing to release: the scope takes its body, and everything pushed is popped when the body returns,
/// including when it throws. Raw ImGui inside the body is fine, and anything it leaves pushed is unwound at the boundary
/// with a single log line naming this scope.
/// </summary>
/// <example>
/// <code>
/// NoireStyle.With(new UiStyle { TextColor = theme.Danger }, () => ImGui.TextUnformatted("Careful"));
/// NoireStyle.WithAlpha(0.5f, () => DrawPreview());
/// </code>
/// </example>
[NoireFacade]
public static class NoireStyle
{
    /// <summary>
    /// Runs <paramref name="body"/> with <paramref name="style"/> applied.
    /// </summary>
    /// <param name="style">The style to apply. A <see langword="null"/> or empty style changes nothing.</param>
    /// <param name="body">The drawing to do inside the style.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void With(UiStyle? style, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        With(style, body, static b => b());
    }

    /// <summary>
    /// Runs <paramref name="body"/> with <paramref name="style"/> applied, passing <paramref name="state"/> through.<br/>
    /// This overload exists so the body can stay a <see langword="static"/> lambda and allocate nothing per frame; the
    /// simpler overload allocates one delegate per call.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="style">The style to apply. A <see langword="null"/> or empty style changes nothing.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to do inside the style.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void With<TState>(UiStyle? style, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (style == null || style.IsEmpty)
        {
            UiScope.Run(nameof(NoireStyle), state, body);
            return;
        }

        var pushed = style.Push();

        try
        {
            UiScope.Run(nameof(NoireStyle), state, body);
        }
        finally
        {
            UiStyle.Pop(pushed);
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> with a single ImGui colour overridden.
    /// </summary>
    /// <param name="color">The colour slot to override.</param>
    /// <param name="value">The colour to use.</param>
    /// <param name="body">The drawing to do inside the style.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void WithColor(ImGuiCol color, Vector4 value, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ImGui.PushStyleColor(color, value);

        try
        {
            UiScope.Run(nameof(NoireStyle), body, static b => b());
        }
        finally
        {
            ImGui.PopStyleColor();
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> at a reduced opacity, for a preview or a section that is not currently in effect.
    /// </summary>
    /// <param name="alpha">The opacity multiplier, from 0 to 1.</param>
    /// <param name="body">The drawing to do inside the style.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void WithAlpha(float alpha, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Clamp(alpha, 0f, 1f));

        try
        {
            UiScope.Run(nameof(NoireStyle), body, static b => b());
        }
        finally
        {
            ImGui.PopStyleVar();
        }
    }
}
