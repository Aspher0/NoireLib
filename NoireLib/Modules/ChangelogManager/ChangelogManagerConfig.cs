using System;
using NoireLib.Configuration;

namespace NoireLib.Changelog;

[Serializable]
public class ChangelogManagerConfig : NoireConfigBase<ChangelogManagerConfig>
{
    public override string GetConfigFileName() => "ChangelogManagerConfig";

    /// <summary>
    /// The last seen changelog version by the user.
    /// </summary>
    [AutoSave]
    public virtual Version? LastSeenChangelogVersion { get; set; }

    public void UpdateLastSeenVersion(Version? version) => LastSeenChangelogVersion = version;

    public void ClearLastSeenVersion() => LastSeenChangelogVersion = null;
}
