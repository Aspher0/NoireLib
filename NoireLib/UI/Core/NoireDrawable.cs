using NoireLib.Helpers;
using System;

namespace NoireLib.UI;

/// <summary>
/// The base of everything the NoireUI hub can draw on your behalf: a screen-anchored element that exists on its own
/// rather than inside one of your windows.
/// </summary>
public abstract class NoireDrawable : IDisposable
{
    private readonly string disposeKey;

    private bool registered;
    private bool persistRefusalLogged;
    private int lastDrawnFrame = -1;

    /// <summary>
    /// Initializes the identity of a drawable.
    /// </summary>
    /// <param name="id">An optional unique identifier; when <see langword="null"/> or blank, a random one is generated.</param>
    /// <param name="kind">The short type name used in the emitted ImGui id and in log messages.</param>
    protected NoireDrawable(string? id, string kind)
    {
        Kind = kind;
        HasGeneratedId = string.IsNullOrWhiteSpace(id);
        Id = HasGeneratedId ? RandomGenerator.GenerateGuidString() : id!;
        disposeKey = $"NoireLib.UI.{kind}.{Id}";
    }

    /// <summary>
    /// The unique identifier of this drawable.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The short type name of this drawable, used in the emitted ImGui id and in log messages.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Whether <see cref="Id"/> was generated rather than supplied.
    /// </summary>
    /// <remarks>A generated id is different on every session, so nothing keyed on it may be persisted.</remarks>
    public bool HasGeneratedId { get; }

    /// <summary>
    /// Whether this drawable has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Whether NoireLib draws this object automatically every frame, <see langword="null"/> to follow the
    /// <see cref="NoireUI.AutoDraw"/> master default.
    /// </summary>
    public bool? AutoDraw { get; set; }

    /// <summary>
    /// Whether this object actually draws itself, resolved as <c>AutoDraw ?? NoireUI.AutoDraw</c>.
    /// </summary>
    public bool EffectiveAutoDraw => AutoDraw ?? NoireUI.AutoDraw;

    /// <summary>
    /// The ImGui id this drawable emits, namespaced so two NoireLib elements can never collide.
    /// </summary>
    protected string ImGuiId => UiIds.For("###Noire", Kind, Id);

    // Drives the fault ladder in UiDiagnostics.FaultTolerance, and resets as soon as a draw succeeds.
    internal int ConsecutiveDrawFaults { get; set; }

    /// <summary>
    /// Draws this object for the current frame.
    /// </summary>
    public void Draw()
    {
        if (IsDisposed)
            return;

        lastDrawnFrame = NoireUI.FrameCount;
        DrawCore();
    }

    /// <summary>
    /// Draws the object, never called directly since <see cref="Draw"/> is the entry point.
    /// </summary>
    protected abstract void DrawCore();

    /// <summary>
    /// Builds the <see cref="NoireUiState"/> key this drawable stores a piece of remembered state under, and refuses to
    /// build one when the id was generated rather than given.
    /// </summary>
    /// <param name="subKey">What is being remembered.</param>
    /// <param name="key">The state key, or an empty string when persisting is refused.</param>
    /// <returns>True when the state may be persisted.</returns>
    protected bool TryGetPersistKey(string subKey, out string key)
        => UiPersistKey.TryBuild(Kind, Id, HasGeneratedId, subKey, ref persistRefusalLogged, out key);

    /// <summary>
    /// Registers this drawable with the hub and for automatic disposal.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
    protected void Register()
    {
        if (registered)
            return;

        registered = true;
        NoireUI.RegisterDrawable(this);
        NoireLibMain.RegisterOnDispose(disposeKey, Dispose);
    }

    // Skips anything already drawn manually this frame.
    internal bool TryAutoDraw()
    {
        if (IsDisposed || !EffectiveAutoDraw || lastDrawnFrame == NoireUI.FrameCount)
            return false;

        Draw();
        return true;
    }

    /// <summary>
    /// Unregisters the object so it stops being drawn.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        DisposeCore();

        if (registered)
        {
            NoireUI.UnregisterDrawable(this);
            NoireLibMain.UnregisterOnDispose(disposeKey);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases what the derived drawable owns, before it is unregistered from the hub.
    /// </summary>
    protected virtual void DisposeCore()
    {
    }
}
