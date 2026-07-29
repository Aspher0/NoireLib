namespace NoireLib.HotkeyManager;

/// <summary>
/// The phase of a hotkey's physical hold, tracked per entry by the activation state machine. The remaining
/// runtime state (whether a release is armed, and the hold and repeat timers) lives alongside it in
/// <see cref="HotkeyActivationState"/>.
/// </summary>
internal enum HotkeyActivationPhase
{
    /// <summary>
    /// The binding's main key is not physically down; nothing is owed.
    /// </summary>
    Idle,

    /// <summary>
    /// The main key is physically down and the machine is evaluating triggers for it (a Held delay counting
    /// down, or a Repeat / HoldAndRepeat cadence advancing).
    /// </summary>
    Engaged,

    /// <summary>
    /// The main key is physically down and a Held-mode hotkey has already fired its single trigger for this hold,
    /// so it will not fire again until the key is released.
    /// </summary>
    HoldFired,
}

/// <summary>
/// The per-entry runtime state of the hotkey activation state machine. Every field here is written only by
/// <see cref="NoireHotkeyManager.EvaluateActivation"/> and its trigger predicates, on the detection thread.
/// </summary>
internal struct HotkeyActivationState
{
    /// <summary>
    /// The phase of the physical hold: <see cref="HotkeyActivationPhase.Idle"/> means the main key is not down,
    /// any other phase means it is.
    /// </summary>
    public HotkeyActivationPhase Phase;

    /// <summary>
    /// Whether the full binding combination was active on the previous evaluation, used to detect the edge where
    /// it completes while the main key is already physically down.
    /// </summary>
    public bool CombinationWasActive;

    /// <summary>
    /// Whether a press has armed a <see cref="HotkeyActivationMode.Released"/> trigger, so that the following
    /// release fires exactly once.
    /// </summary>
    public bool Armed;

    /// <summary>
    /// The timestamp in milliseconds at which the current hold began, used for the
    /// <see cref="HotkeyActivationMode.Held"/> and <see cref="HotkeyActivationMode.HoldAndRepeat"/> initial delay;
    /// null when no hold is being timed.
    /// </summary>
    public long? HoldStartMs;

    /// <summary>
    /// The timestamp in milliseconds at which the next <see cref="HotkeyActivationMode.Repeat"/> or
    /// <see cref="HotkeyActivationMode.HoldAndRepeat"/> trigger is due; null before the first repeat is scheduled.
    /// </summary>
    public long? NextRepeatMs;

    /// <summary>
    /// Whether the main key is currently considered physically held (any phase other than
    /// <see cref="HotkeyActivationPhase.Idle"/>).
    /// </summary>
    public readonly bool IsHeld => Phase != HotkeyActivationPhase.Idle;

    /// <summary>
    /// Resets the state to its idle default.
    /// </summary>
    public void Reset() => this = default;
}
