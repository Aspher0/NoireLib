using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>What changing a setting on a running listener does. No setting may restart the listener or move its port.</summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteSettingsTests : NoireRemoteTestBase
{
    [NoireRemoteClass("Settings", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class SettingsProbe
    {
        [NoireRemote]
        public static int One() => 1;
    }

    [Fact]
    public void TogglingASetting_LeavesThePortAlone()
    {
        NoireRemote.PublishType(typeof(SettingsProbe));
        NoireRemote.Start();

        var port = NoireRemote.Port;

        port.Should().BeGreaterThan(0);

        NoireRemote.Options.AllowFleetControl = true;
        NoireRemote.Options.EnableRemoteConsole = false;
        NoireRemote.Options.ConsoleAccess = NoireRemoteConsoleAccess.Open;
        NoireRemote.Options.SendStackTraces = true;

        NoireRemote.Port.Should().Be(port);
        NoireRemote.State.Should().Be(NoireRemoteListenerState.Listening);
    }

    [Fact]
    public async Task TurningTheConsoleOnWhileListening_ServesItWithoutMovingThePort()
    {
        NoireRemote.PublishType(typeof(SettingsProbe));
        NoireRemote.Options.EnableConsole = false;
        NoireRemote.Start();

        var port = NoireRemote.Port;

        NoireRemoteBrowserConsole.IsListening.Should().BeFalse();

        NoireRemote.Options.EnableConsole = true;

        NoireRemoteBrowserConsole.IsListening.Should().BeTrue();
        NoireRemote.Port.Should().Be(port);

        NoireRemoteBrowserConsole.Port.Should().NotBe(port).And.BeGreaterThan(0);

        await Task.CompletedTask;
    }

    [Fact]
    public void TurningTheConsoleOffWhileListening_StopsItAndKeepsTheListener()
    {
        NoireRemote.PublishType(typeof(SettingsProbe));
        NoireRemote.Options.EnableConsole = true;
        NoireRemote.Start();

        var port = NoireRemote.Port;

        NoireRemoteBrowserConsole.IsListening.Should().BeTrue();

        NoireRemote.Options.EnableConsole = false;

        NoireRemoteBrowserConsole.IsListening.Should().BeFalse();
        NoireRemote.Port.Should().Be(port);
        NoireRemote.State.Should().Be(NoireRemoteListenerState.Listening);
    }

    [Fact]
    public async Task ASettingChangedMidFlight_LeavesAnOpenCallerWorking()
    {
        NoireRemote.PublishType(typeof(SettingsProbe));
        NoireRemote.Start();

        using var client = new HttpClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", NoireRemote.Token);

        var url = "http://127.0.0.1:" + NoireRemote.Port + NoireRemotePaths.Prefix + "Settings/One";

        NoireRemote.Options.AllowFleetControl = true;
        NoireRemote.Options.LogScope = NoireRemoteLogScope.Everything;

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public void RebindingTheConsoleForItsOwnSetting_KeepsItsPort()
    {
        NoireRemote.PublishType(typeof(SettingsProbe));
        NoireRemote.Options.EnableConsole = true;
        NoireRemote.Options.ConsolePort = 0;
        NoireRemote.Start();

        var apiPort = NoireRemote.Port;
        var consolePort = NoireRemoteBrowserConsole.Port;

        consolePort.Should().BeGreaterThan(0);

        // This one rebinds the console socket and must keep its port.
        NoireRemote.Options.EnableRemoteConsole = true;

        NoireRemoteBrowserConsole.IsListening.Should().BeTrue();
        NoireRemoteBrowserConsole.Port.Should().Be(consolePort);
        NoireRemote.Port.Should().Be(apiPort);
    }

    [Fact]
    public void RestartingAListenerOnAnEphemeralPort_AsksForTheNumberItHad()
    {
        NoireRemote.PublishType(typeof(SettingsProbe));
        NoireRemote.Options.Port = 0;
        NoireRemote.Start();

        var port = NoireRemote.Port;

        NoireRemote.Stop();
        NoireRemote.Start();

        NoireRemote.Port.Should().Be(port);
    }

    [Fact]
    public void PublishingASocket_TellsWhoeverHoldsAManifest()
    {
        NoireRemote.PublishType(typeof(SettingsProbe));
        NoireRemote.Start();

        NoireRemote.PublishEvent("warm.up", new { ready = true });

        var before = NoireRemote.Manifest().Revision;

        using var socket = NoireLib.Websocket.NoireWebsocket.Publish("announced");

        var after = NoireRemote.Manifest();

        after.Revision.Should().NotBe(before);
        after.Sockets.Should().Contain(published => published.Name == "announced");
    }
}
