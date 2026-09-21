namespace NoireLib.Remote;

/// <summary>
/// What the listener announces on the reserved topic <c>_instance.surface</c> when what it publishes changes.<br/>
/// Carries the new revision. A reader fetches the manifest only when the number moved.
/// </summary>
public sealed class NoireRemoteSurfaceChange
{
    /// <summary>
    /// Gets or sets the manifest revision after the change.
    /// </summary>
    public int Revision { get; set; }

    /// <summary>
    /// Gets or sets how many APIs the listener now publishes.
    /// </summary>
    public int Endpoints { get; set; }

    /// <summary>
    /// Gets or sets how many sockets the listener now publishes.
    /// </summary>
    public int Sockets { get; set; }
}
