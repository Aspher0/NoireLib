namespace NoireLib.UI;

/// <summary>Tells a single click from a double click: a row acts on a double click without firing its single click.</summary>
/// <typeparam name="T">What is clicked.</typeparam>
public sealed class NoireClicks<T> where T : class
{
    private T? pending;
    private float pendingAt;

    /// <summary>How long a second click still counts as a double click, in seconds.</summary>
    public float Window { get; init; } = 0.2f;

    /// <summary>Records a click.</summary>
    /// <param name="target">What was clicked.</param>
    /// <returns>True when the click completes a double click on the same target.</returns>
    public bool Click(T target)
    {
        var now = NoireUI.Time;

        if (ReferenceEquals(pending, target) && now - pendingAt <= Window)
        {
            pending = null;
            return true;
        }

        pending = target;
        pendingAt = now;
        return false;
    }

    /// <summary>Takes the target of a single click once the double click window has passed.</summary>
    /// <returns>The target, once, or <see langword="null"/>.</returns>
    public T? TakeSingle()
    {
        if (pending == null || NoireUI.Time - pendingAt <= Window)
            return null;

        var single = pending;
        pending = null;
        return single;
    }

    /// <summary>Forgets a pending single click.</summary>
    public void Cancel() => pending = null;
}
