namespace NoireLib.UI;

/// <summary>
/// A skin's typefaces, for text a view draws itself. Push and pop are paired calls rather than a disposable. Faces
/// merge the glyphs of <see cref="NoireScriptFonts"/> by themselves.
/// </summary>
public interface IFontSkin
{
    /// <summary>Loads the faces; called when the skin becomes active.</summary>
    void Load();

    /// <summary>Releases the faces; called when another skin takes over.</summary>
    void Unload();

    bool IsReady => true;

    /// <summary>Makes a role's face the current ImGui font, at the drawing window's text size.</summary>
    /// <param name="role">What the text is.</param>
    void Push(TextRole role);

    /// <summary>Restores the font pushed before.</summary>
    void Pop();
}
