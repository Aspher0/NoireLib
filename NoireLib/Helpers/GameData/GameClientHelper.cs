namespace NoireLib.Helpers;

/// <summary>
/// Tells which region the running client belongs to, by probing language variants of a
/// universal data file. The answer is probed once and cached for the session.<br/>
/// An installation that cannot be probed reads as <see cref="GameClient.Global"/>.
/// </summary>
public static class GameClientHelper
{
    private const string SchemaProbePath = "exd/addon.exh";
    private const string GlobalDataPath = "exd/addon_0_en.exd";
    private const string KoreanDataPath = "exd/addon_0_ko.exd";
    private static readonly string[] ChineseDataPaths =
        ["exd/addon_0_chs.exd", "exd/addon_0_cht.exd", "exd/addon_0_tc.exd"];

    private static GameClient? _detected;

    /// <summary>
    /// An override taking precedence over detection, for testing another region's behavior on this
    /// installation. <see langword="null"/> restores detection.
    /// </summary>
    public static GameClient? Forced { get; set; }

    /// <summary>The client to act as: <see cref="Forced"/> when set, the detected client otherwise.</summary>
    public static GameClient Current() => Forced ?? Detected();

    /// <summary>
    /// The detected client, probed on first call and cached. Before <c>NoireService</c> is initialized
    /// the answer is <see cref="GameClient.Global"/> and nothing is cached.
    /// </summary>
    public static GameClient Detected()
    {
        if (_detected is { } cached)
            return cached;

        if (!NoireService.IsInitialized())
            return GameClient.Global;

        _detected = SafeExecutor.ExecuteSafely(Probe, GameClient.Global);

        return _detected.Value;
    }

    private static GameClient Probe()
    {
        var data = NoireService.DataManager.GameData;

        if (!data.FileExists(SchemaProbePath))
            return GameClient.Global;

        if (data.FileExists(KoreanDataPath))
            return GameClient.Korean;

        foreach (var path in ChineseDataPaths)
        {
            if (data.FileExists(path))
                return GameClient.Chinese;
        }

        return data.FileExists(GlobalDataPath) ? GameClient.Global : GameClient.Unknown;
    }

    /// <summary>A human-readable name for the client.</summary>
    public static string Name(GameClient client) => client switch
    {
        GameClient.Korean => "Korean",
        GameClient.Chinese => "Chinese",
        GameClient.Unknown => "unidentified",
        _ => "Global",
    };

    /// <summary>
    /// Reads a user-supplied region name, accepting the common short forms. Anything unrecognized,
    /// including <see langword="null"/>, reads as <see cref="GameClient.Global"/>.
    /// </summary>
    public static GameClient Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "kr" or "korea" or "korean" => GameClient.Korean,
        "cn" or "china" or "chinese" => GameClient.Chinese,
        "other" or "unknown" => GameClient.Unknown,
        _ => GameClient.Global,
    };
}
