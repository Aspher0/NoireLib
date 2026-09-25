using System;

namespace NoireLib.UI;

/// <summary>
/// A confirmation waiting for an answer, scoped to one window. Holds no drawing: the window's drawing reads
/// <see cref="State"/> and reports the answer.
/// </summary>
public sealed class NoireAsk
{
    private Action<bool>? answered;
    private float openedAt;

    /// <summary>The confirmation being asked, or <see langword="null"/>.</summary>
    public NoireConfirm? Current { get; private set; }

    /// <summary>Whether a confirmation is being asked.</summary>
    public bool Open => Current != null;

    /// <summary>Asks a confirmation, replacing a pending one, which is answered as cancelled.</summary>
    /// <param name="confirm">What to ask.</param>
    /// <param name="onAnswer">Receives true when confirmed.</param>
    public void Ask(NoireConfirm confirm, Action<bool> onAnswer)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(onAnswer);

        var replaced = answered;

        Current = confirm;
        answered = onAnswer;
        openedAt = NoireUI.Time;

        replaced?.Invoke(false);
    }

    /// <summary>What drawing the pending confirmation needs this frame.</summary>
    /// <exception cref="InvalidOperationException">Thrown when nothing is being asked.</exception>
    public ConfirmState State
    {
        get
        {
            var current = Current ?? throw new InvalidOperationException("No confirmation is being asked.");
            var age = NoireUI.Time - openedAt;
            return new ConfirmState(current, Math.Max(0, current.CountdownSeconds - (int)age), age);
        }
    }

    /// <summary>Applies an answer. A confirmation while the countdown runs is ignored.</summary>
    /// <param name="answer">The answer.</param>
    /// <returns>True when the confirmation closed.</returns>
    public bool Answer(ConfirmAnswer answer)
    {
        if (answer == ConfirmAnswer.Pending || Current == null)
            return false;

        if (answer == ConfirmAnswer.Confirmed && State.SecondsLeft > 0)
            return false;

        var callback = answered;
        Current = null;
        answered = null;
        callback?.Invoke(answer == ConfirmAnswer.Confirmed);
        return true;
    }
}
