using FluentAssertions;
using NoireLib.Changelog;
using NoireLib.Configuration;
using NoireLib.Helpers;
using NoireLib.HistoryLogger;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the built-in log and changelog drawing at zero allocation once warm, the changelog with an entry drawn
/// through a moving gradient and a motion.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class LogAndChangelogDrawAllocationTests : IClassFixture<UiHarness>, IDisposable
{
    private const int Warm = 3;

    private static readonly Version Shown = new(1, 2, 0, 0);
    private static readonly NoireMotion Wobble = NoireMotion.Create().Scaling().Rocking().Glitching(every: 0f);

    private readonly UiHarness harness;
    private readonly NoireHistoryLogger logger;
    private readonly HistoryLogDraw logDraw;
    private readonly NoireChangelogManager changelog;

    public LogAndChangelogDrawAllocationTests(UiHarness harness)
    {
        this.harness = harness;
        WindowedModuleCollection.EnsureWindowSystem();

        // Without a plugin folder the settings cannot load. Cached here as a loaded plugin configuration is.
        NoireConfigManager.AddConfigToCache(typeof(HistoryLoggerConfigInstance), new HistoryLoggerConfigInstance());

        logger = new NoireHistoryLogger(active: false, enableLogging: false);

        for (var i = 0; i < 30; i++)
            logger.AddEntry("Message " + i + "\nSecond line", i % 2 == 0 ? "Swap" : "Chat", (HistoryLogLevel)(i % 6), "Source");

        logDraw = new HistoryLogDraw(logger);

        changelog = new NoireChangelogManager(active: false, enableLogging: false, versions:
        [
            new ChangelogVersion
            {
                Version = Shown,
                Date = "24-09-2026",
                Title = "Title",
                Description = "A description.",
                Entries =
                [
                    new ChangelogEntry { Text = "Header", IsHeader = true, HasBullet = true, TextColor = new Vector4(1f, 0.5f, 0f, 1f) },
                    new ChangelogEntry { Text = "A plain line.", HasBullet = true, IndentLevel = 1 },
                    new ChangelogEntry { Text = "A line drawn with a gradient and a motion.", HasBullet = true, IndentLevel = 1, Gradient = NoireGradient.Rainbow, Motion = Wobble },
                ],
            },
        ]);

        changelog.SelectVersion(Shown);
    }

    public void Dispose()
    {
        logger.Dispose();
        changelog.Dispose();
        NoireConfigManager.UnloadConfig<HistoryLoggerConfigInstance>();
    }

    [Fact]
    public void The_log_window_draws_without_allocating()
    {
        var result = harness.Draw(() => logDraw.Draw(), warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void The_log_window_draws_whole_entries_without_allocating()
    {
        HistoryLoggerConfig.Instance.SelectLinesSeparately = false;

        var result = harness.Draw(() => logDraw.Draw(), warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void A_changelog_with_an_effect_entry_draws_without_allocating()
    {
        var result = harness.Draw(() => ChangelogDraw.Content(changelog, 300f), warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Theory]
    [InlineData(0f, 1f, 0f, 0f)]
    [InlineData(1f / 3f, 0f, 1f, 0f)]
    [InlineData(2f / 3f, 0f, 0f, 1f)]
    [InlineData(1f, 1f, 0f, 0f)]
    [InlineData(-2f / 3f, 0f, 1f, 0f)]
    public void FromHsv_places_the_primary_hues_and_wraps(float hue, float r, float g, float b)
    {
        var color = ColorHelper.FromHsv(hue, 1f, 1f);

        color.X.Should().BeApproximately(r, 0.001f);
        color.Y.Should().BeApproximately(g, 0.001f);
        color.Z.Should().BeApproximately(b, 0.001f);
        color.W.Should().Be(1f);
    }
}
