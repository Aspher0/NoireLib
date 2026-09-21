using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

// This file plays three plugins publishing the same API. The analyzer flags a second declaration of a route.
#pragma warning disable NoireLib_005

/// <summary>
/// A console driving a fleet: which listeners it may reach, what makes two members the same call, and what happens when
/// a listener changed under the page. Consent belongs to the listener being driven, and a call only goes where the
/// member carries the contract the page showed.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteFleetConsoleTests : IDisposable
{
    private readonly string registry = Path.Combine(Path.GetTempPath(), "NoireRemoteTests", Guid.NewGuid().ToString("N"));
    private readonly string previousRegistry = NoireRemoteClient.Options.RegistryDirectory;
    private readonly Guid? previousExclusion = NoireRemoteClient.Options.ExcludeInstance;
    private readonly List<IDisposable> owned = [];
    private readonly HttpClient client = new();

    public NoireRemoteFleetConsoleTests()
    {
        Directory.CreateDirectory(registry);
        NoireRemoteClient.Options.RegistryDirectory = registry;
        NoireRemoteClient.Options.ExcludeInstance = null;
        NoireRemoteClient.Refresh();
    }

    public void Dispose()
    {
        foreach (var item in owned)
            item.Dispose();

        client.Dispose();
        NoireRemoteClient.Options.RegistryDirectory = previousRegistry;
        NoireRemoteClient.Options.ExcludeInstance = previousExclusion;
        NoireRemoteClient.Refresh();

        try
        {
            Directory.Delete(registry, true);
        }
        catch (IOException)
        {
        }
    }

    public sealed class Spot
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    public sealed class Place
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    public sealed class Node
    {
        public string Name { get; set; } = string.Empty;

        public List<Node> Children { get; set; } = [];
    }

    [NoireRemoteClass("Fleet", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class FleetA
    {
        /// <summary>Moves somewhere.</summary>
        [NoireRemote]
        public static Spot Move(double x, double y = 1) => new() { X = x, Y = y };

        [NoireRemote(Transports = NoireRemoteTransport.Websocket)]
        public static string Ready() => "ready";

        [NoireRemote]
        public static Node Tree() => new() { Name = "root" };
    }

    [NoireRemoteClass("Fleet", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class FleetB
    {
        /// <summary>Worded differently, same call.</summary>
        [NoireRemote]
        public static Place Move(double x, double y = 1) => new() { X = x * 10, Y = y };

        [NoireRemote(Transports = NoireRemoteTransport.Websocket)]
        public static string Ready() => "also ready";

        [NoireRemote]
        public static Node Tree() => new() { Name = "other root" };
    }

    [NoireRemoteClass("Fleet", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class FleetChangedDefault
    {
        [NoireRemote]
        public static Spot Move(double x, double y = 2) => new() { X = x, Y = y };
    }

    [Fact]
    public void TheSameCall_CarriesTheSameContract_WhateverThePluginWordingOrTypeName()
    {
        var a = Contract(Publish("plugin-a", typeof(FleetA)), "Move");
        var b = Contract(Publish("plugin-b", typeof(FleetB)), "Move");

        a.Should().NotBeNullOrEmpty();
        b.Should().Be(a, "a type is compared by its shape and a description changes nothing about the call");
    }

    [Fact]
    public void ADifferentDefault_IsADifferentContract()
    {
        var a = Contract(Publish("plugin-a", typeof(FleetA)), "Move");
        var changed = Contract(Publish("plugin-c", typeof(FleetChangedDefault)), "Move");

        changed.Should().NotBe(a);
    }

    [Fact]
    public void ARecursiveType_HasAContract()
    {
        var a = Contract(Publish("plugin-a", typeof(FleetA)), "Tree");
        var b = Contract(Publish("plugin-b", typeof(FleetB)), "Tree");

        a.Should().NotBeNullOrEmpty().And.Be(b);
    }

    [Fact]
    public async Task AListenerThatDidNotOptIn_IsNotListed_AndOneThatDidIs()
    {
        var host = Publish("host", typeof(FleetA), console: true);
        var shy = Publish("shy", typeof(FleetB));
        var willing = Publish("willing", typeof(FleetB), allowFleet: true);

        var hosts = (await ReadFleetAsync(host)).Hosts;

        hosts.Should().Contain(entry => entry.Instance == host.InstanceId && entry.IsSelf);
        hosts.Should().Contain(entry => entry.Instance == willing.InstanceId);
        hosts.Should().NotContain(entry => entry.Instance == shy.InstanceId);
    }

    [Fact]
    public async Task AFleetCall_ReachesEveryListenerWithTheContract()
    {
        var host = Publish("host", typeof(FleetA), console: true);
        var other = Publish("other", typeof(FleetB), allowFleet: true);
        var contract = Contract(host, "Move");

        var answer = await CallAsync(host, "Move", [host.InstanceId, other.InstanceId], contract, new JObject { ["x"] = 3 });

        answer.Total.Should().Be(2);
        answer.Answered.Should().Be(2);
        answer.Outcomes.Single(row => row.Instance == host.InstanceId).Result!["x"]!.Value<double>().Should().Be(3);
        answer.Outcomes.Single(row => row.Instance == other.InstanceId).Result!["x"]!.Value<double>().Should().Be(30);
    }

    [Fact]
    public async Task AListenerWhoseContractChanged_IsRefusedAlone()
    {
        var host = Publish("host", typeof(FleetA), console: true);
        var changed = Publish("changed", typeof(FleetChangedDefault), allowFleet: true);

        var answer = await CallAsync(host, "Move", [host.InstanceId, changed.InstanceId], Contract(host, "Move"), new JObject { ["x"] = 1 });

        answer.Answered.Should().Be(1);
        answer.Outcomes.Single(row => row.Instance == changed.InstanceId).Error!.Code.Should().Be(NoireRemoteErrorCodes.ContractChanged);
    }

    [Fact]
    public async Task AListenerThatWithdrewConsent_IsRefusedAtTheCall()
    {
        var host = Publish("host", typeof(FleetA), console: true);
        var other = Publish("other", typeof(FleetB), allowFleet: true);

        other.Options.AllowFleetControl = false;

        await WaitForRecordAsync(other, allowed: false);

        var answer = await CallAsync(host, "Move", [other.InstanceId], Contract(host, "Move"), new JObject { ["x"] = 1 });

        answer.Answered.Should().Be(0);
        answer.Outcomes.Single().Ok.Should().BeFalse();
    }

    [Fact]
    public async Task AMemberServedOverTheSocketAlone_IsCalledThere()
    {
        var host = Publish("host", typeof(FleetA), console: true);
        var other = Publish("other", typeof(FleetB), allowFleet: true);

        var answer = await CallAsync(host, "Ready", [other.InstanceId], Contract(host, "Ready"), new JObject());

        answer.Answered.Should().Be(1, "a member served only over the socket still answers there: "
            + answer.Outcomes.Single().Error?.Code + " " + answer.Outcomes.Single().Error?.Message);
        answer.Outcomes.Single().Result!.Value<string>().Should().Be("also ready");
    }

    private NoireRemoteServer Publish(string plugin, Type type, bool allowFleet = false, bool console = false)
    {
        var server = new NoireRemoteServer(new NoireRemoteStandaloneHost { Name = plugin, Version = "1.0.0" }, new NoireRemoteOptions
        {
            AutoStart = false,
            EnableLogging = false,
            PublishDirectoryRecord = true,
            PublishCharacterIdentity = false,
            RegistryDirectory = registry,
            AllowFleetControl = allowFleet,
            EnableConsole = console,
        });

        server.PublishType(type);
        server.Start();
        owned.Add(server);
        NoireRemoteClient.Refresh();

        return server;
    }

    private static string Contract(NoireRemoteServer server, string member)
        => server.Manifest().Endpoints.Single(endpoint => endpoint.Name == "Fleet").Members.Single(item => item.Name == member).Contract!;

    private async Task<string> SessionAsync(NoireRemoteServer host)
    {
        using var response = await client.PostAsync(
            "http://127.0.0.1:" + host.Console.Port + NoireRemoteConsolePaths.Session,
            new StringContent("{}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        return NoireRemoteJson.Read<NoireRemoteConsoleSession>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.Token;
    }

    private async Task<NoireRemoteFleetListing> ReadFleetAsync(NoireRemoteServer host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + host.Console.Port + NoireRemoteConsolePaths.Fleet);

        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization, NoireRemoteHeaders.ConsoleScheme + " " + await SessionAsync(host));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        return NoireRemoteJson.Read<NoireRemoteFleetListing>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<NoireRemoteBroadcastWire> CallAsync(NoireRemoteServer host, string member, Guid[] instances, string contract, JObject args)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + host.Console.Port + NoireRemoteConsolePaths.Fleet + "/Fleet/" + member)
        {
            Content = new StringContent(
                NoireRemoteJson.Write(new NoireRemoteFleetCall { Instances = instances, Contract = contract, Args = args }),
                Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization, NoireRemoteHeaders.ConsoleScheme + " " + await SessionAsync(host));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);

        return NoireRemoteJson.Read<NoireRemoteBroadcastWire>(text)!;
    }

    private async Task WaitForRecordAsync(NoireRemoteServer server, bool allowed)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            NoireRemoteClient.Refresh();

            var record = NoireRemoteDirectory.ReadAll(registry).FirstOrDefault(entry => entry.Instance == server.InstanceId);

            if (record != null && record.AllowFleetControl == allowed)
                return;

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The record never said what the setting says.");
    }
}
