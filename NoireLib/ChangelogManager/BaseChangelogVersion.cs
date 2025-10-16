using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Changelog;

/// <summary>
/// Base class for changelog version files with helper methods
/// </summary>
public abstract class BaseChangelogVersion : IChangelogVersion
{
    protected static readonly Vector4 White = ColorHelper.HexToVector4("#FFFFFF");
    protected static readonly Vector4 Green = ColorHelper.HexToVector4("#1BCC18");
    protected static readonly Vector4 Orange = ColorHelper.HexToVector4("#FCC203");
    protected static readonly Vector4 Red = ColorHelper.HexToVector4("#E81313");
    protected static readonly Vector4 Blue = ColorHelper.HexToVector4("#4d8eff");
    protected static readonly Vector4 Grey = ColorHelper.HexToVector4("#B3B3B3");
    protected static readonly Vector4 DarkGrey = ColorHelper.HexToVector4("#262626");
    protected static readonly Vector4 Black = ColorHelper.HexToVector4("#000000");

    public abstract List<ChangelogVersion> GetVersions();

    /// <summary>
    /// Creates a header entry, which is a bold text with an optional icon.
    /// </summary>
    /// <param name="text">The text to display.</param>
    /// <param name="textColor">The color of the text.</param>
    /// <param name="icon">The optional icon to display next to the text, on the left.</param>
    /// <param name="iconColor">The optional color of the icon.</param>
    /// <returns>The built changelog entry.</returns>
    protected static ChangelogEntry Header(string text, Vector4? textColor = null, FontAwesomeIcon? icon = null, Vector4? iconColor = null)
        => new() { Text = text, IsHeader = true, TextColor = textColor, Icon = icon, IconColor = iconColor };

    /// <summary>
    /// Creates a separator entry, which is a horizontal line.
    /// </summary>
    /// <returns>The built changelog entry.</returns>
    protected static ChangelogEntry Separator() => new() { Text = string.Empty, IsSeparator = true };

    /// <summary>
    /// Creates a button entry, which is a button with optional text and icon.
    /// </summary>
    /// <param name="text">The text to display on the left of the button.</param>
    /// <param name="textColor">The color of the text left of the button.</param>
    /// <param name="buttonText">The text to display on the button.</param>
    /// <param name="buttonTextColor">The color of the text on the button.</param>
    /// <param name="buttonColor">The color of the button.</param>
    /// <param name="action">The action to perform when the button is clicked with the mouse button as parameter.</param>
    /// <param name="icon">The optional icon to display next to the text, on the left.</param>
    /// <param name="iconColor">The optional color of the icon.</param>
    /// <returns>The built changelog entry.</returns>
    protected static ChangelogEntry Button(string? text = null, Vector4? textColor = null, string? buttonText = null, Vector4? buttonTextColor = null, Vector4? buttonColor = null, Action<ImGuiMouseButton>? action = null, FontAwesomeIcon? icon = null, Vector4? iconColor = null)
        => new() { Text = text, TextColor = textColor, ButtonText = buttonText, ButtonColor = buttonColor, ButtonTextColor = buttonTextColor, ButtonAction = action, Icon = icon, IconColor = iconColor };

    /// <summary>
    /// Creates a standard entry, which is a normal text with optional icon and indentation.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="textColor"></param>
    /// <param name="indentLevel"></param>
    /// <param name="icon"></param>
    /// <param name="iconColor"></param>
    /// <returns></returns>
    protected static ChangelogEntry Entry(string text, Vector4? textColor = null, int indentLevel = 0, FontAwesomeIcon? icon = null, Vector4? iconColor = null)
        => new() { Text = text, TextColor = textColor, IndentLevel = indentLevel, Icon = icon, IconColor = iconColor };

    /// <summary>
    /// Creates a raw entry that executes custom ImGui code through a callback.
    /// </summary>
    /// <param name="action">The action containing custom code to execute.</param>
    /// <returns>The built changelog entry.</returns>
    protected static ChangelogEntry Raw(Action action)
        => new() { IsRaw = true, RawAction = action };
}
