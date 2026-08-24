using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace NoireLib.UI;

// Without Dalamud the glyph is drawn in whatever font is current, since UiBuilder.IconFont blocks
// rather than returning an empty font.
internal static class UiIconFont
{
    // A null pointer when there is no Dalamud to ask.
    internal static ImFontPtr Current => NoireService.IsInitialized() ? UiBuilder.IconFont : ImFontPtr.Null;
}
