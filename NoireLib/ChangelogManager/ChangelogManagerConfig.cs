using System;
using NoireLib.Configuration;

namespace NoireLib.Changelog;

public class ChangelogManagerConfig : NoireConfigBase
{
    public override string GetConfigFileName() => "ChangelogManagerConfig";

    /// <summary>
    /// The last seen changelog version by the user.
    /// </summary>
    public Version? LastSeenChangelogVersion { get; set; }

    public bool UpdateLastSeenVersion(Version? version)
    {
        LastSeenChangelogVersion = version;
        return Save();
    }

    public bool ClearLastSeenVersion()
    {
        LastSeenChangelogVersion = null;
        return Save();
    }
}
