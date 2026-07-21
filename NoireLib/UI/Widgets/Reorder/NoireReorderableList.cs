using Dalamud.Game.ClientState.Keys;
using NoireLib.Helpers;
using NoireLib.HotkeyManager;
using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// A list whose rows can be dragged into a different order, with a grip to take hold of, a gap showing where a row
/// will land, per-row actions, keyboard reordering and an empty state.
/// </summary>
/// <remarks>
/// Flat lists only. Trees are a different widget with different rules and are deliberately out of scope: everything
/// that makes reordering pleasant here (one insertion point, one gap, one index) stops being true the moment a row can
/// be dropped *into* another one.<br/>
/// The list is yours. The widget reorders it in place and tells you it did; it never holds a copy.
/// </remarks>
/// <example>
/// <code>
/// var list = new NoireReorderableList&lt;string&gt;("steps", config.Steps)
/// {
///     Label = step =&gt; step,
///     AllowDelete = true,
/// };
///
/// if (list.Draw())
///     config.Save();
/// </code>
/// </example>
/// <typeparam name="T">The row type.</typeparam>
[NoireFacadeFactory]
public sealed partial class NoireReorderableList<T>
{
    private IList<T> items = new List<T>();

    /// <summary>
    /// Creates a reorderable list.
    /// </summary>
    /// <param name="id">A stable id for the widget. When <see langword="null"/>, a random one is generated.</param>
    /// <param name="items">The list to reorder, in place.</param>
    public NoireReorderableList(string? id = null, IList<T>? items = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? RandomGenerator.GenerateGuidString() : id;

        if (items != null)
            Items = items;
    }

    /// <summary>The unique identifier of this widget, used for the ImGui ids.</summary>
    public string Id { get; }

    /// <summary>
    /// The list being reordered. Held rather than copied, so a reorder is a reorder of yours.
    /// </summary>
    public IList<T> Items
    {
        get => items;
        set => items = value ?? new List<T>();
    }

    /// <summary>What each row is called. When <see langword="null"/>, the row's own <c>ToString</c>.</summary>
    public Func<T, string>? Label { get; set; }

    /// <summary>
    /// Paints a row instead of its label: an icon, a colour, a pair of controls.
    /// </summary>
    /// <remarks>
    /// The list keeps the grip, the drag, the gap, the row's size and its actions. The hook only paints, inside the
    /// space left between the grip and the buttons.
    /// </remarks>
    public Action<UiReorderRowDraw<T>>? Renderer { get; set; }

    #region Behaviour

    /// <summary>Whether each row carries a button that removes it. Off by default.</summary>
    public bool AllowDelete { get; set; }

    /// <summary>Whether each row carries a button that copies it in below itself. Off by default.</summary>
    public bool AllowDuplicate { get; set; }

    /// <summary>
    /// Copies a row for <see cref="AllowDuplicate"/>. When <see langword="null"/>, the row itself is added again.
    /// </summary>
    /// <remarks>
    /// Fine for a string or a record; for anything mutable, give this, or the duplicate and the original are one
    /// object and editing either edits both.
    /// </remarks>
    public Func<T, T>? Duplicate { get; set; }

    /// <summary>
    /// Whether the focused row moves with the arrow keys while a modifier is held. On by default.
    /// </summary>
    /// <remarks>
    /// Dragging is not available to everyone and is awkward in a long list even for those it is. The keyboard path
    /// costs one branch and is the difference between a reorderable list and a reorderable list somebody can use.
    /// </remarks>
    public bool AllowKeyboard { get; set; } = true;

    /// <summary>
    /// The binding that moves the focused row up. The up arrow by default.
    /// </summary>
    /// <remarks>
    /// A <see cref="HotkeyBinding"/> matched with the same rules as a <see cref="NoireHotkeyManager"/> hotkey, read
    /// through <see cref="KeybindsHelper.IsBindingHeld"/>. A plain <see cref="VirtualKey"/> converts implicitly
    /// (<c>list.MoveUpBinding = VirtualKey.PRIOR;</c>) and the full binding surface is there for a key with
    /// modifiers.<br/>
    /// Modifiers are matched exactly, so the default fires on the bare arrow and not on ctrl with it.<br/>
    /// Ignored while a hotkey is attached through <see cref="BindReorderHotkeys"/>; read
    /// <see cref="ResolvedMoveUpBinding"/> for the one actually in force.
    /// </remarks>
    public HotkeyBinding MoveUpBinding { get; set; } = VirtualKey.UP;

    /// <summary>
    /// The binding that moves the focused row down. The down arrow by default.
    /// </summary>
    /// <remarks>
    /// A <see cref="HotkeyBinding"/> matched with the same rules as a <see cref="NoireHotkeyManager"/> hotkey, read
    /// through <see cref="KeybindsHelper.IsBindingHeld"/>. A plain <see cref="VirtualKey"/> converts implicitly
    /// (<c>list.MoveDownBinding = VirtualKey.NEXT;</c>) and the full binding surface is there for a key with
    /// modifiers.<br/>
    /// Modifiers are matched exactly, so the default fires on the bare arrow and not on ctrl with it.<br/>
    /// Ignored while a hotkey is attached through <see cref="BindReorderHotkeys"/>; read
    /// <see cref="ResolvedMoveDownBinding"/> for the one actually in force.
    /// </remarks>
    public HotkeyBinding MoveDownBinding { get; set; } = VirtualKey.DOWN;

    /// <summary>The binding actually moving a row up: the attached hotkey's when there is one.</summary>
    public HotkeyBinding ResolvedMoveUpBinding => Resolve(upHotkeyId, MoveUpBinding);

    /// <summary>The binding actually moving a row down: the attached hotkey's when there is one.</summary>
    public HotkeyBinding ResolvedMoveDownBinding => Resolve(downHotkeyId, MoveDownBinding);

    /// <summary>
    /// Drives the reorder keys from a <see cref="NoireHotkeyManager"/>, so the user can rebind them.
    /// </summary>
    /// <remarks>
    /// The same two ways of being bound that <see cref="NoireComboBox{T}"/>'s wheel cycle offers: a local binding for
    /// a plugin that does not want a rebindable hotkey, and a hotkey id for one that does. A rebinding applies
    /// immediately, with no bookkeeping on your side.
    /// </remarks>
    /// <param name="hotkeyManager">The module holding the hotkeys.</param>
    /// <param name="moveUpHotkeyId">The id of the hotkey that moves a row up.</param>
    /// <param name="moveDownHotkeyId">The id of the hotkey that moves a row down.</param>
    /// <returns>This instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="hotkeyManager"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when either id is null, empty or blank.</exception>
    public NoireReorderableList<T> BindReorderHotkeys(NoireHotkeyManager hotkeyManager, string moveUpHotkeyId, string moveDownHotkeyId)
    {
        ArgumentNullException.ThrowIfNull(hotkeyManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(moveUpHotkeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moveDownHotkeyId);

        // Given back before the ids change, since a block is released against whichever hotkeys are attached at the
        // time: rebinding a list that happened to be holding the keys would otherwise hand back the new hotkeys' keys
        // and leave the old ones held.
        ReleaseInputBlocking();

        hotkeys = hotkeyManager;
        upHotkeyId = moveUpHotkeyId;
        downHotkeyId = moveDownHotkeyId;
        return this;
    }

    /// <summary>
    /// Detaches the hotkeys, so the keys fall back to <see cref="MoveUpBinding"/> and <see cref="MoveDownBinding"/>.
    /// Safe to call when none are attached.
    /// </summary>
    /// <returns>This instance, for chaining.</returns>
    public NoireReorderableList<T> UnbindReorderHotkeys()
    {
        // Given back before the hotkeys are let go of, or a list detached while it happened to be holding the arrow
        // keys would leave them held with nothing left that knows how to release them.
        ReleaseInputBlocking();

        hotkeys = null;
        upHotkeyId = null;
        downHotkeyId = null;
        return this;
    }

    /// <summary>
    /// Whether an attached hotkey swallows the key from the game while the shortcut is actually live.
    /// </summary>
    /// <remarks>
    /// On by default, and only while live: a row focused, the window focused, and the keys otherwise doing nothing.
    /// A hotkey left blocking permanently takes the arrow keys away from the game for as long as the plugin is
    /// loaded, which is not a trade a reorderable list is entitled to make on anyone's behalf.<br/>
    /// Held through <see cref="HotkeyEntry.SuppressGameInput"/> rather than by writing
    /// <see cref="HotkeyEntry.BlockGameInput"/>. That option is a persisted setting belonging to whoever registered
    /// the hotkey, and a widget writing it would both override an answer that is not its to give and store its own
    /// momentary state as the hotkey's standing one. A suppression is runtime only, so the worst this can cost is the
    /// rest of the session.<br/>
    /// Only applies with hotkeys attached through <see cref="BindReorderHotkeys"/>. A local binding has no entry to
    /// block with.
    /// </remarks>
    public bool BlockGameInputWhileActive { get; set; } = true;

    private NoireHotkeyManager? hotkeys;
    private string? upHotkeyId;
    private string? downHotkeyId;
    private bool blockRaised;
    private int blockRenewedOnFrame = -1;
    private bool watchdogAttached;

    /// <summary>
    /// Raises or restores the game-input blocking on the attached hotkeys.
    /// </summary>
    /// <remarks>
    /// A raised block is renewed for one frame at a time and expires on its own. Blocking works by clearing the key
    /// out of the game's own key state on every framework tick, so a block left raised swallows that key for as long
    /// as the plugin is loaded: it cannot be left to a call that only happens while the list is being drawn, because
    /// the list not being drawn is precisely the case that has to release it. Closing the window or moving to another
    /// tab while a row is focused is not an unusual thing to do, and it should not cost the arrow keys.
    /// </remarks>
    /// <param name="live">Whether the shortcut can currently do anything.</param>
    internal void ApplyInputBlocking(bool live)
    {
        if (live && BlockGameInputWhileActive && hotkeys != null)
        {
            RenewInputBlocking();
            return;
        }

        // Released rather than returned from. Turning the option off, or detaching the hotkeys, while the block is up
        // has to give the key back; leaving early would strand it raised with nothing left that would lower it.
        ReleaseInputBlocking();
    }

    /// <summary>
    /// Raises the block if it is not already up, and marks it as wanted for this frame.
    /// </summary>
    private void RenewInputBlocking()
    {
        blockRenewedOnFrame = NoireUI.FrameCount;

        if (blockRaised)
            return;

        blockRaised = true;
        ApplyBlockingTo(upHotkeyId, true);
        ApplyBlockingTo(downHotkeyId, true);
        AttachWatchdog();
    }

    /// <summary>
    /// Puts the keys back, leaving the hotkeys' own settings exactly as they were found.
    /// </summary>
    private void ReleaseInputBlocking()
    {
        DetachWatchdog();

        if (!blockRaised)
            return;

        blockRaised = false;
        ApplyBlockingTo(upHotkeyId, false);
        ApplyBlockingTo(downHotkeyId, false);
    }

    /// <summary>
    /// Watches for the list going quiet while it still holds the keys, and hands them back when it does.
    /// </summary>
    /// <remarks>
    /// Attached only while a block is up, and it removes itself as soon as the block comes down, so a list nobody is
    /// using costs nothing. A frame of slack is allowed before releasing, because the tick and the drawing are on
    /// separate clocks: a tick that lands after the next frame has been begun but before the list has drawn into it
    /// would otherwise take the keys back from a list that is still being worked in, and hand the game an arrow the
    /// user meant for the row. Waiting a frame longer to release costs nothing.
    /// </remarks>
    private void OnBlockWatchdog(Dalamud.Plugin.Services.IFramework framework)
    {
        if (blockRaised && NoireUI.FrameCount - blockRenewedOnFrame > 1)
        {
            // The row keeps no focus through this either, so a list that comes back into view comes back neutral
            // rather than quietly holding the keys again the moment it is drawn.
            focusedIndex = -1;
            ReleaseInputBlocking();
        }
    }

    private void AttachWatchdog()
    {
        if (watchdogAttached || !NoireService.IsInitialized())
            return;

        watchdogAttached = true;
        NoireService.Framework.Update += OnBlockWatchdog;
    }

    private void DetachWatchdog()
    {
        if (!watchdogAttached)
            return;

        watchdogAttached = false;

        if (NoireService.IsInitialized())
            NoireService.Framework.Update -= OnBlockWatchdog;
    }

    /// <summary>
    /// Takes one hotkey's key from the game while the shortcut is live, and gives it back otherwise.
    /// </summary>
    /// <remarks>
    /// Paired one for one with <see cref="blockRaised"/>, which is what keeps the suppression balanced: it is taken
    /// only on the transition into a raised block and given back only on the transition out of one.
    /// </remarks>
    private void ApplyBlockingTo(string? hotkeyId, bool live)
    {
        if (hotkeyId == null || hotkeys == null || !hotkeys.TryGetHotkey(hotkeyId, out var entry))
            return;

        if (live)
            entry.SuppressGameInput();
        else
            entry.ReleaseGameInputSuppression();
    }

    /// <summary>
    /// The binding in force for one of the two directions, preferring an attached hotkey over the local one.
    /// </summary>
    private HotkeyBinding Resolve(string? hotkeyId, HotkeyBinding local)
    {
        if (hotkeys == null || hotkeyId == null)
            return local;

        // A hotkey that has been disabled means the shortcut is off, not that it falls back to a binding the user
        // never sees in the rebinding UI.
        return hotkeys.TryGetHotkey(hotkeyId, out var entry) && entry.Enabled ? entry.Binding : default;
    }

    /// <summary>
    /// Whether a drag can start anywhere on a row rather than on its grip alone. Off by default.
    /// </summary>
    /// <remarks>
    /// Off is the safer default, because a row that carries its own controls would otherwise start moving every time
    /// one of them was used. Turn it on for rows that are only a label: the grip is then a picture of what to do
    /// rather than the only place to do it.
    /// </remarks>
    public bool DragAnywhere { get; set; }

    /// <summary>The height of a row in real pixels. Zero measures it from the text.</summary>
    public float RowHeight { get; set; }

    /// <summary>What is shown when the list is empty.</summary>
    public string EmptyText { get; set; } = "Nothing here yet.";

    /// <summary>Invoked after any change, with the list in its new order.</summary>
    public Action<IList<T>>? OnChanged { get; set; }

    #endregion

    #region Reordering, as logic

    /// <summary>
    /// Moves a row to another position, shifting everything between them along.
    /// </summary>
    /// <remarks>
    /// This is the whole of reordering, separated out because it is the part worth being sure about and the part a
    /// drag cannot demonstrate: every off-by-one in a drag-to-reorder lives here, in what "dropped at index 4" means
    /// when the row being dropped came from above rather than below it.
    /// </remarks>
    /// <param name="list">The list to reorder in place.</param>
    /// <param name="from">Where the row is now.</param>
    /// <param name="to">Where it should end up, as a position in the list after the move.</param>
    /// <returns>True when anything moved.</returns>
    internal static bool MoveItem(IList<T> list, int from, int to)
    {
        if (list == null || from == to)
            return false;

        if (from < 0 || from >= list.Count)
            return false;

        // Clamped rather than refused: a drag that ends past the last row means "put it last", which is the one thing
        // the user was unambiguously asking for.
        var target = Math.Clamp(to, 0, list.Count - 1);

        if (target == from)
            return false;

        var moving = list[from];
        list.RemoveAt(from);
        list.Insert(target, moving);

        return true;
    }

    /// <summary>
    /// Which row a pointer position falls on.
    /// </summary>
    /// <remarks>
    /// Worked out from the pointer rather than from which row reports itself hovered, and that is the whole reason
    /// this exists: while a drag is running the dragged row is ImGui's active item and no other item is given the
    /// hover, so a hover-driven target only ever resolves in whichever direction happens to keep the pointer inside
    /// the row it started on. Dragging then works one way and not the other.<br/>
    /// A pointer above or below the list clamps to its ends, so a drag that leaves the widget still means something.
    /// </remarks>
    /// <param name="pointerY">Where the pointer is.</param>
    /// <param name="listTop">Where the first row starts.</param>
    /// <param name="rowStep">How far apart two rows start, height and spacing together.</param>
    /// <param name="count">How many rows there are.</param>
    /// <returns>The row the pointer is over.</returns>
    internal static int ResolveSlot(float pointerY, float listTop, float rowStep, int count)
    {
        if (count <= 0)
            return -1;

        if (rowStep <= 0f)
            return 0;

        var slot = (int)MathF.Floor((pointerY - listTop) / rowStep);
        return Math.Clamp(slot, 0, count - 1);
    }

    /// <summary>
    /// Moves a row up or down by one, which is what the keyboard path does.
    /// </summary>
    /// <param name="list">The list to reorder in place.</param>
    /// <param name="index">The row to move.</param>
    /// <param name="offset">How far, usually -1 or 1.</param>
    /// <returns>The row's new index, or the old one when it did not move.</returns>
    internal static int Nudge(IList<T> list, int index, int offset)
    {
        if (list == null || index < 0 || index >= list.Count)
            return index;

        var target = index + offset;

        if (target < 0 || target >= list.Count)
            return index;

        return MoveItem(list, index, target) ? target : index;
    }

    #endregion

    #region Editing

    /// <summary>
    /// Moves a row to another position.
    /// </summary>
    /// <param name="from">Where the row is now.</param>
    /// <param name="to">Where it should end up.</param>
    /// <returns>True when anything moved.</returns>
    public bool Move(int from, int to)
    {
        if (!MoveItem(items, from, to))
            return false;

        Notify();
        return true;
    }

    /// <summary>Moves a row up one place.</summary>
    /// <param name="index">The row to move.</param>
    /// <returns>True when it moved.</returns>
    public bool MoveUp(int index) => Move(index, index - 1);

    /// <summary>Moves a row down one place.</summary>
    /// <param name="index">The row to move.</param>
    /// <returns>True when it moved.</returns>
    public bool MoveDown(int index) => Move(index, index + 1);

    /// <summary>Removes a row.</summary>
    /// <param name="index">The row to remove.</param>
    /// <returns>True when there was a row there.</returns>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= items.Count)
            return false;

        items.RemoveAt(index);
        Notify();
        return true;
    }

    /// <summary>
    /// Copies a row in below itself.
    /// </summary>
    /// <param name="index">The row to copy.</param>
    /// <returns>True when there was a row there.</returns>
    public bool DuplicateAt(int index)
    {
        if (index < 0 || index >= items.Count)
            return false;

        var source = items[index];
        var copy = source;

        if (Duplicate != null)
        {
            try
            {
                copy = Duplicate(source);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"The duplicate callback of list '{Id}' threw an exception.", nameof(NoireReorderableList<T>));
                return false;
            }
        }

        items.Insert(index + 1, copy);
        Notify();
        return true;
    }

    #endregion

    /// <summary>
    /// The text a row shows, falling back to its own <c>ToString</c>.
    /// </summary>
    private string LabelOf(T item)
    {
        if (Label == null)
            return item?.ToString() ?? string.Empty;

        try
        {
            return Label(item) ?? string.Empty;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"The label callback of list '{Id}' threw an exception.", nameof(NoireReorderableList<T>));
            return string.Empty;
        }
    }

    private void Notify()
    {
        changedThisFrame = true;

        try
        {
            OnChanged?.Invoke(items);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"The change callback of list '{Id}' threw an exception.", nameof(NoireReorderableList<T>));
        }
    }
}
