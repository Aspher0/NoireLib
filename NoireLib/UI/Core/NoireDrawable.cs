using NoireLib.Helpers;
using System;

namespace NoireLib.UI;

/// <summary>
/// The base of everything the NoireUI hub can draw on your behalf: a screen-anchored element that exists on its own
/// rather than inside one of your windows.<br/>
/// A drawable registers with <see cref="NoireUI"/> when it is created and is disposed automatically with NoireLib, so
/// nothing has to be tracked in parallel. Whether it draws itself is decided by <see cref="AutoDraw"/> against the
/// <see cref="NoireUI.AutoDraw"/> master default; <see cref="Draw"/> always works regardless.
/// </summary>
public abstract class NoireDrawable : IDisposable
{
    private readonly string disposeKey;

    private bool registered;
    private bool persistRefusalLogged;
    private int lastDrawnFrame = -1;

    /// <summary>
    /// Initializes the identity of a drawable. The derived constructor calls <see cref="Register"/> once it is ready to
    /// be drawn.
    /// </summary>
    /// <param name="id">An optional unique identifier. When <see langword="null"/> or blank, a random one is generated
    /// and <see cref="HasGeneratedId"/> becomes true.</param>
    /// <param name="kind">The short type name used in the emitted ImGui id and in log messages, for example "OverlayButton".</param>
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
    /// Whether <see cref="Id"/> was generated rather than supplied.<br/>
    /// A generated id is different on every session, so nothing keyed on it may be persisted: a settings file keyed that
    /// way grows forever and restores nothing.
    /// </summary>
    public bool HasGeneratedId { get; }

    /// <summary>
    /// Whether this drawable has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Whether NoireLib draws this object automatically every frame.<br/>
    /// <see langword="null"/> (the usual default) follows the <see cref="NoireUI.AutoDraw"/> master default; setting it
    /// explicitly wins in either direction, including over a master that is off. See <see cref="EffectiveAutoDraw"/>.
    /// </summary>
    public bool? AutoDraw { get; set; }

    /// <summary>
    /// Whether this object actually draws itself, resolved as <c>AutoDraw ?? NoireUI.AutoDraw</c>.
    /// </summary>
    public bool EffectiveAutoDraw => AutoDraw ?? NoireUI.AutoDraw;

    /// <summary>
    /// The ImGui id this drawable emits, namespaced so two NoireLib elements can never collide.
    /// </summary>
    protected string ImGuiId => $"###Noire{Kind}_{Id}";

    /// <summary>
    /// How many frames in a row this drawable has thrown while the hub drew it. Drives the fault ladder in
    /// <see cref="UiDiagnostics.FaultTolerance"/>, and resets as soon as a draw succeeds.
    /// </summary>
    internal int ConsecutiveDrawFaults { get; set; }

    /// <summary>
    /// Draws this object for the current frame.<br/>
    /// Always available, whether or not the object also draws itself: call it from your own ImGui code to place it
    /// exactly where you want it in your draw order. The hub skips anything already drawn manually on the same frame.
    /// </summary>
    public void Draw()
    {
        if (IsDisposed)
            return;

        lastDrawnFrame = NoireUI.FrameCount;
        DrawCore();
    }

    /// <summary>
    /// Draws the object. Implemented by each drawable; never called directly, <see cref="Draw"/> is the entry point.
    /// </summary>
    protected abstract void DrawCore();

    /// <summary>
    /// Builds the <see cref="NoireUiState"/> key this drawable stores a piece of remembered state under, and refuses to
    /// build one when the id was generated rather than given.
    /// </summary>
    /// <remarks>
    /// A generated id is a fresh GUID every session. Persisting against one would write an entry that can never be read
    /// back, so the state file would grow forever and restore nothing, and the symptom (a setting that silently never
    /// sticks) points nowhere near the cause. Refusing here, once, with a message naming the fix, is the whole point.
    /// </remarks>
    /// <param name="subKey">What is being remembered, for example "position".</param>
    /// <param name="key">The state key, or an empty string when persisting is refused.</param>
    /// <returns>True when the state may be persisted.</returns>
    protected bool TryGetPersistKey(string subKey, out string key)
    {
        if (!HasGeneratedId)
        {
            key = $"{Kind}.{Id}.{subKey}";
            return true;
        }

        key = string.Empty;

        if (!persistRefusalLogged)
        {
            persistRefusalLogged = true;
            NoireLogger.LogWarning(
                $"This {Kind} was created without an id, so its id is a new GUID every session and nothing keyed on it can be restored. " +
                "Its persisted state is being skipped. Give it a stable id in the constructor to persist it.",
                nameof(NoireDrawable));
        }

        return false;
    }

    /// <summary>
    /// Registers this drawable with the hub and for automatic disposal. Called by the derived constructor once the
    /// object is ready to be drawn.
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

    /// <summary>
    /// Draws this object from the hub's per-frame pass, unless it has already been drawn manually this frame.
    /// </summary>
    /// <returns>True when the object was drawn.</returns>
    internal bool TryAutoDraw()
    {
        if (IsDisposed || !EffectiveAutoDraw || lastDrawnFrame == NoireUI.FrameCount)
            return false;

        Draw();
        return true;
    }

    /// <summary>
    /// Unregisters the object so it stops being drawn. Safe to call multiple times.<br/>
    /// Called automatically when NoireLib is disposed; call it earlier to remove the object yourself.
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
    /// Releases what the derived drawable owns. Runs before it is unregistered from the hub.
    /// </summary>
    protected virtual void DisposeCore()
    {
    }
}
