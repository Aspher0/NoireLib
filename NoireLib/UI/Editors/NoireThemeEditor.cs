using Dalamud.Bindings.ImGui;
using NoireLib.Localizer;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The colour editor of the active skin: a picker per colour the skin lets the user change, applied live, restorable
/// one by one or all at once, and shareable as a code. Drawn in plain ImGui.
/// </summary>
public sealed class NoireThemeEditor : NoireWindow
{
    private const string WindowId = "###noire-colors";

    private static NoireThemeEditor? instance;

    private string status = string.Empty;
    private int titleRevision = -1;

    internal NoireThemeEditor()
        : base(NoireStrings.ColorsTitle.Text + WindowId)
    {
        Size = new Vector2(380f, 460f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>Opens the editor on the active skin, creating it on first use.</summary>
    public static void Open()
    {
        if (instance == null)
        {
            if (NoireService.NoireWindowSystem is not { } windows)
                return;

            instance = new NoireThemeEditor();
            windows.AddWindow(instance);
            NoireLibMain.RegisterOnDispose("NoireThemeEditor", static () => instance = null);
        }

        instance.IsOpen = true;
        instance.BringToFront();
    }

    /// <summary>Follows the language in the title.</summary>
    public override void PreDraw()
    {
        if (titleRevision == NoireLanguages.Revision)
            return;

        titleRevision = NoireLanguages.Revision;
        WindowName = NoireStrings.ColorsTitle.Text + WindowId;
    }

    /// <summary>Draws the colours of the active skin, the restore buttons and the share code buttons.</summary>
    public override void Draw()
    {
        if (!UiDraw.Available)
            return;

        var skin = NoireSkins.Active;
        var theme = NoireSkins.Theme;
        var roles = skin.EditableColors;

        ImGui.PushStyleColor(ImGuiCol.Text, NoireTheme.Current.Resolve(ThemeColor.TextMuted));
        ImGui.TextUnformatted(skin.Name.Text);
        ImGui.PopStyleColor();
        ImGui.Separator();

        for (var i = 0; i < roles.Count; i++)
        {
            var role = roles[i];
            var color = Enum.TryParse<ThemeColor>(role.Key, out var token) ? theme.Resolve(token) : theme.Resolve(role.Key, Vector4.One);

            ImGui.PushID(role.Key);

            if (ImGui.ColorEdit4("##color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
                NoireSkins.EditColor(skin, role, color);

            ImGui.SameLine();
            ImGui.TextUnformatted(role.Label.Text);

            if (NoireSkins.EditedColor(skin, role) != null)
            {
                ImGui.SameLine();

                if (ImGui.SmallButton(NoireStrings.Restore.Text))
                    NoireSkins.EditColor(skin, role, null);
            }

            ImGui.PopID();
        }

        ImGui.Spacing();

        if (ImGui.Button(NoireStrings.ResetColors.Text))
            NoireSkins.ResetColors(skin);

        ImGui.SameLine();

        if (ImGui.Button(NoireStrings.CopyColors.Text))
        {
            ImGui.SetClipboardText(NoireSkins.ExportColors(skin));
            status = NoireStrings.Copied.Text;
        }

        ImGui.SameLine();

        if (ImGui.Button(NoireStrings.ImportColors.Text))
            status = NoireSkins.ImportColors(skin, ImGui.GetClipboardText()).Success ? NoireStrings.Applied.Text : NoireStrings.ImportFailed.Text;

        if (status.Length == 0)
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, NoireTheme.Current.Resolve(ThemeColor.TextMuted));
        ImGui.TextUnformatted(status);
        ImGui.PopStyleColor();
    }
}
