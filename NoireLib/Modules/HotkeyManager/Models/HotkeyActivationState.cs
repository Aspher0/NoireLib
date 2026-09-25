namespace NoireLib.HotkeyManager;

// The phase of a hotkey's physical hold, tracked per entry by the activation state machine. The remaining runtime
// state (whether a release is armed, and the hold and repeat timers) lives alongside it in HotkeyActivationState.
internal enum HotkeyActivationPhase
{
    Idle,

    // A Held delay counting down, or a Repeat or HoldAndRepeat cadence advancing.
    Engaged,

    // A Held hotkey fired its single trigger for this hold and waits for the release.
    HoldFired,
}

// The per-entry runtime state of the hotkey activation state machine. Every field here is written only by
// EvaluateActivation and its trigger predicates, on the detection thread.
internal struct HotkeyActivationState
{
    public HotkeyActivationPhase Phase;

    // Detects the combination completing while the main key is already down.
    public bool CombinationWasActive;

    // A press armed a Released trigger: the next release fires it once.
    public bool Armed;

    // Start of the current hold, for the Held and HoldAndRepeat delay. Null when no hold is timed.
    public long? HoldStartMs;

    // Due time of the next Repeat or HoldAndRepeat trigger. Null before the first is scheduled.
    public long? NextRepeatMs;

    public readonly bool IsHeld => Phase != HotkeyActivationPhase.Idle;

    public void Reset() => this = default;
}
