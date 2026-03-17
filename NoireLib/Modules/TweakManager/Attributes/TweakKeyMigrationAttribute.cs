using System;

namespace NoireLib.TweakManager;

/// <summary>
/// Declares a previous <see cref="TweakBase.InternalKey"/> for automatic config migration.<br/>
/// When the <see cref="NoireTweakManager"/> registers a tweak, it checks for this attribute
/// and automatically moves any persisted config from the old key to the current key.<br/>
/// Apply multiple times if a tweak has been renamed more than once.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TweakKeyMigrationAttribute : Attribute
{
    /// <summary>
    /// The previous internal key that this tweak was registered under.
    /// </summary>
    public string OldKey { get; }

    /// <summary>
    /// Declares a previous internal key for automatic config migration.
    /// </summary>
    /// <param name="oldKey">The old internal key this tweak was previously registered under.</param>
    public TweakKeyMigrationAttribute(string oldKey)
    {
        OldKey = oldKey;
    }
}
