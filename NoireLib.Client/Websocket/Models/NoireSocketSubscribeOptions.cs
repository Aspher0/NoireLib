namespace NoireLib.Websocket;

/// <summary>
/// What one subscription does beyond running its handler. Usable as-is. Every property has a working default.
/// </summary>
public sealed class NoireSocketSubscribeOptions
{
    /// <summary>
    /// Gets or sets a name for this subscription. Subscribing again under the same key replaces the previous
    /// handler, without adding a second one.
    /// </summary>
    public string? Key { get; set; } = null;

    /// <summary>
    /// Gets or sets whether the handler runs once and then removes itself.
    /// </summary>
    public bool Once { get; set; } = false;

    /// <summary>
    /// Gets or sets an object this subscription belongs to. A consumer can drop everything it registered in one
    /// call, without tracking tokens.
    /// </summary>
    public object? Owner { get; set; } = null;

    /// <summary>
    /// Copies these options.
    /// </summary>
    /// <returns>A copy that shares no state with this one.</returns>
    public NoireSocketSubscribeOptions Clone()
        => new() { Key = Key, Once = Once, Owner = Owner };
}
