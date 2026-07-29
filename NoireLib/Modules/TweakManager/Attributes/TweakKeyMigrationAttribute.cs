using System;

namespace NoireLib.TweakManager;

/// <summary>
/// Declares a previous <see cref="TweakBase.InternalKey"/> for automatic migration of persisted data. When the
/// <see cref="NoireTweakManager"/> registers a tweak, it moves everything the old key holds (enabled state,
/// serialized config, favorite) to the current key, unless the current key already holds data of its own. Apply
/// multiple times if a tweak has been renamed more than once.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TweakKeyMigrationAttribute : Attribute
{
    /// <summary>
    /// The previous internal key that this tweak was registered under.
    /// </summary>
    public string OldKey { get; }

    /// <summary>
    /// Declares a previous internal key for automatic migration of persisted data.
    /// </summary>
    /// <param name="oldKey">The old internal key this tweak was previously registered under.</param>
    public TweakKeyMigrationAttribute(string oldKey)
    {
        OldKey = oldKey;
    }
}
