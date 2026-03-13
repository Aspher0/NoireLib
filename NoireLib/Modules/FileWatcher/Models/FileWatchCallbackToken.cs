using System;

namespace NoireLib.FileWatcher;

/// <summary>
/// Token representing a callback subscription for a specific file watch registration.
/// </summary>
public readonly record struct FileWatchCallbackToken(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
