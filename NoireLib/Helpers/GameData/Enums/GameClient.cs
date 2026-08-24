namespace NoireLib.Helpers;

/// <summary>
/// The region of the running client.
/// </summary>
public enum GameClient
{
    /// <summary>The global client: Japanese, European, Oceanian and American data centers.</summary>
    Global,

    /// <summary>The Korean game client.</summary>
    Korean,

    /// <summary>The Chinese game client.</summary>
    Chinese,

    /// <summary>A client whose data files match no known service.</summary>
    Unknown,
}
