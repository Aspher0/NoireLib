using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace NoireLib.UI;

/// <summary>
/// The icon font, resolved in a way that survives having no Dalamud behind the library.<br/>
/// Without Dalamud the glyph is drawn in whatever font is current, since <see cref="UiBuilder.IconFont"/> blocks
/// rather than returning an empty font.
/// </summary>
internal static class UiIconFont
{
    /// <summary>
    /// The icon font to push, or a null pointer when there is no Dalamud to ask.
    /// </summary>
    internal static ImFontPtr Current => NoireService.IsInitialized() ? UiBuilder.IconFont : ImFontPtr.Null;
}
