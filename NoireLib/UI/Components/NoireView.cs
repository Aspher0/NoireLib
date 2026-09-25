using System;

namespace NoireLib.UI;

/// <summary>
/// How one skin draws one type of component or window. Built the first time the skin draws it, disposed when another
/// skin takes over or the component is disposed. Derive from <see cref="NoireView{T}"/>.
/// </summary>
public abstract class NoireView : IDisposable
{
    private protected NoireView()
    {
    }

    /// <summary>The skin this view belongs to.</summary>
    public NoireSkin Skin { get; internal set; } = null!;

    /// <summary>The skinned window being drawn.</summary>
    public NoireSkinnedWindowBase Window { get; internal set; } = null!;

    /// <summary>Where the view draws this frame; the ImGui cursor starts at its top left corner.</summary>
    public UiRect Area { get; internal set; }

    /// <summary>The skin's controls.</summary>
    protected IControlSkin Controls => Skin.Controls;

    /// <summary>Releases what the view holds.</summary>
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    internal abstract void DrawTarget(object target);

    internal abstract void OverlayTarget(object target);
}
