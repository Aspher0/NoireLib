namespace NoireLib.Helpers;

/// <summary>How a searched name is compared with a sheet name. Both compare case insensitively.</summary>
public enum NameMatch
{
    /// <summary>The whole name matches.</summary>
    Exact,

    /// <summary>The searched text appears anywhere in the name.</summary>
    Contains,
}
