using FluentAssertions;
using NoireLib.Helpers;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Ported from BypassEmote's emote catalog. The stamp is what stops derived data outliving whatever it was
/// derived from, and all three parts of it have to agree: a game patch rewrites the source files, a plugin
/// update can change how they are read, and the schema number covers the case where neither moved but the
/// author changed their mind about the shape.
/// </summary>
public class VersionedJsonCacheTests
{
    private const string Game = "2026.07.01.0000.0000";
    private const string Plugin = "1.7.4.0";
    private const int Schema = 1;

    private static bool Current(string game, string plugin, int schema)
        => VersionedJsonCache<object>.IsStampCurrent(Game, Plugin, Schema, game, plugin, schema);

    [Fact]
    public void AllThreeMatch_IsCurrent()
        => Current(Game, Plugin, Schema).Should().BeTrue();

    [Fact]
    public void GameVersionMoved_IsStale()
        => Current("2026.08.01.0000.0000", Plugin, Schema).Should().BeFalse();

    [Fact]
    public void PluginVersionMoved_IsStale()
        => Current(Game, "1.7.5.0", Schema).Should().BeFalse();

    [Fact]
    public void SchemaVersionMoved_IsStale()
        => Current(Game, Plugin, 2).Should().BeFalse();

    [Fact]
    public void AnUnstampedCache_IsStale()
        => VersionedJsonCache<object>.IsStampCurrent(null, null, -1, Game, Plugin, Schema)
            .Should().BeFalse("a file that carries no stamp cannot be shown to match");

    [Fact]
    public void AnEmptyStamp_IsStale()
        => VersionedJsonCache<object>.IsStampCurrent(string.Empty, string.Empty, Schema, Game, Plugin, Schema)
            .Should().BeFalse();
}
