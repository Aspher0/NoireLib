using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>A standalone program binds its own listener, publishes a class and answers a call. Also covers host decisions and the option copy.</summary>
// Sets NoireRemoteClient's registry folder. That state is process-wide and the collection runs one class at a time.
[Collection("NoireRemote")]
public sealed class NoireRemoteHostingTests
{
    [NoireRemoteClass("Standalone")]
    public static class StandaloneProbe
    {
        [NoireRemote]
        public static Vector3 Add(Vector3 left, Vector3 right) => left + right;

        [NoireRemote]
        public static string Greet(string name = "world") => "hello " + name;
    }

    [NoireRemoteClass("Bare")]
    public static class BareProbe
    {
        [NoireRemote]
        public static int Answer() => 42;
    }

    private sealed class RecordingHost : NoireRemoteStandaloneHost
    {
        public List<string> Lines { get; } = [];

        public override void Log(NoireRemoteLogLevel level, string message, Exception? exception)
            => Lines.Add(level + " " + message);
    }

    private static NoireRemoteOptions LoopbackOptions() => new()
    {
        Port = 0,
        AutoStart = false,
        PublishDirectoryRecord = false,
        PublishCharacterIdentity = false,
        EnableLogging = false,
    };

    [Fact]
    public async Task AProgramWithNoPlugin_HostsAnEndpointAndAnswersACall()
    {
        using var server = new NoireRemoteServer(new NoireRemoteStandaloneHost { Name = "worked-example" }, LoopbackOptions());
        server.PublishType(typeof(StandaloneProbe));
        server.Start();

        server.IsListening.Should().BeTrue("nothing in the listener is of the game");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

        var body = NoireRemoteJson.Write(new NoireRemoteRequest
        {
            Args = NoireRemoteJson.ToToken(new { left = new Vector3(1, 2, 3), right = new Vector3(4, 5, 6) }),
        });

        using var response = await client.PostAsync(
            "http://127.0.0.1:" + server.Port + NoireRemotePaths.Member("Standalone", "Add"),
            new StringContent(body, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = NoireRemoteJson.Read<NoireRemoteEnvelope>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        envelope.Ok.Should().BeTrue();
        envelope.ResultAs<Vector3>().Should().Be(new Vector3(5, 7, 9));
    }

    [Fact]
    public async Task AStandaloneManifest_CarriesTheHostNameAndNotAPluginName()
    {
        using var server = new NoireRemoteServer(new NoireRemoteStandaloneHost { Name = "toolbox", Version = "3.1.4" }, LoopbackOptions());
        server.PublishType(typeof(StandaloneProbe));
        server.Start();

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

        using var response = await client.GetAsync(
            "http://127.0.0.1:" + server.Port + NoireRemotePaths.Prefix + NoireRemotePaths.Manifest,
            TestContext.Current.CancellationToken);

        var manifest = NoireRemoteJson.Read<NoireRemoteManifest>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        manifest.Plugin.Should().Be("toolbox");
        manifest.PluginVersion.Should().Be("3.1.4");
        manifest.Instance.Should().Be(server.InstanceId);
    }

    [Fact]
    public void ABareEndpointUnderTheStandaloneHost_PublishesAsBackgroundWithNoGate()
    {
        using var server = new NoireRemoteServer(new NoireRemoteStandaloneHost(), LoopbackOptions());
        server.PublishType(typeof(BareProbe));

        var member = server.GetEndpoint("Bare")!.Members.Single();

        member.Thread.Should().Be(NoireRemoteThread.Background, "there is no host thread to hop to");
        member.Requires.Should().Be(NoireRemoteReadiness.None, "the state a gate names belongs to a game that is not running");
    }

    [Fact]
    public void ABareEndpointUnderAHostWithAThread_PublishesAsFrameworkAndStateReady()
    {
        using var server = new NoireRemoteServer(new ThreadedHost(), LoopbackOptions());
        server.PublishType(typeof(BareProbe));

        var member = server.GetEndpoint("Bare")!.Members.Single();

        member.Thread.Should().Be(NoireRemoteThread.Framework);
        member.Requires.Should().Be(NoireRemoteReadiness.StateReady);
    }

    private sealed class ThreadedHost : NoireRemoteStandaloneHost
    {
        public override NoireRemoteThread DefaultThread => NoireRemoteThread.Framework;
    }

    [Fact]
    public async Task AHostThatIsNotReady_AnswersNotReadyWithSomethingToWaitOn()
    {
        using var server = new NoireRemoteServer(new RefusingHost(), LoopbackOptions());
        server.PublishType(typeof(BareProbe));
        server.Start();

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

        using var response = await client.PostAsync(
            "http://127.0.0.1:" + server.Port + NoireRemotePaths.Member("Bare", "Answer"),
            new StringContent(NoireRemoteJson.Write(new NoireRemoteRequest()), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var envelope = NoireRemoteJson.Read<NoireRemoteEnvelope>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.NotReady);
        envelope.Error.Message.Should().Contain("the cupboard is bare");
        envelope.Error.RetryAfterSeconds.Should().Be(7);
    }

    private sealed class RefusingHost : NoireRemoteStandaloneHost
    {
        public override NoireRemoteThread DefaultThread => NoireRemoteThread.Framework;

        public override NoireRemoteReadinessResult CheckReadiness(NoireRemoteReadiness requires, string route)
            => NoireRemoteReadinessResult.NotReady("'" + route + "' cannot run, the cupboard is bare.", 7);
    }

    [Fact]
    public void TheHostLogSink_ReceivesWhatTheListenerWrites()
    {
        var host = new RecordingHost();

        using var server = new NoireRemoteServer(host, LoopbackOptions());
        server.PublishType(typeof(EmptyProbe));

        host.Lines.Should().ContainSingle(line => line.Contains("with no member"));
    }

    [NoireRemoteClass("Empty")]
    public static class EmptyProbe
    {
    }

    [Fact]
    public void TheStandaloneHost_TakesItsNameFromTheEntryAssemblyUnlessTold()
    {
        var host = new NoireRemoteStandaloneHost();

        host.Identity.Name.Should().NotBeNullOrWhiteSpace();
        host.HasHostThread.Should().BeFalse();
        host.CheckReadiness(NoireRemoteReadiness.PlayerLoaded, "Any/Member").IsReady.Should().BeTrue();
    }

    [Fact]
    public void CopyFrom_CopiesEverySettablePropertyOnTheOptions()
    {
        var source = new NoireRemoteOptions();
        var properties = typeof(NoireRemoteOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanWrite && property.SetMethod!.IsPublic)
            .ToArray();

        properties.Should().NotBeEmpty();

        foreach (var property in properties)
            property.SetValue(source, Distinct(property, property.GetValue(source)));

        source.AllowedHosts.Add("copied.example");

        var target = new NoireRemoteOptions();
        target.CopyFrom(source);

        foreach (var property in properties)
        {
            property.GetValue(target).Should().Be(property.GetValue(source),
                "CopyFrom names every property once and " + property.Name + " is missing from it");
        }

        target.AllowedHosts.Should().ContainSingle().Which.Should().Be("copied.example");
        target.AllowedHosts.Should().NotBeSameAs(source.AllowedHosts);
    }

    [Fact]
    public void Clone_GoesThroughTheSameCopy()
    {
        var source = new NoireRemoteOptions { Port = 4242, MaxConnections = 7 };
        source.AllowedHosts.Add("cloned.example");

        var copy = source.Clone();

        copy.Port.Should().Be(4242);
        copy.MaxConnections.Should().Be(7);
        copy.AllowedHosts.Should().ContainSingle().Which.Should().Be("cloned.example");
    }

    private static object? Distinct(PropertyInfo property, object? current)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(bool))
            return !(bool)(current ?? false);

        if (type == typeof(int))
            return (int)(current ?? 0) + 13;

        if (type == typeof(long))
            return (long)(current ?? 0L) + 13;

        if (type == typeof(TimeSpan))
            return (TimeSpan)(current ?? TimeSpan.Zero) + TimeSpan.FromSeconds(11);

        if (type == typeof(string))
            return (current as string ?? string.Empty) + "-copied";

        if (type == typeof(NoireLib.Websocket.NoireRetryPolicy))
            return (current as NoireLib.Websocket.NoireRetryPolicy ?? NoireLib.Websocket.NoireRetryPolicy.Default)
                with { MaxDelay = TimeSpan.FromSeconds(97) };

        if (type == typeof(IPAddress))
            return IPAddress.Parse("10.11.12.13");

        if (type.IsEnum)
        {
            foreach (var value in Enum.GetValues(type))
            {
                if (!Equals(value, current))
                    return value;
            }
        }

        throw new InvalidOperationException(
            "NoireRemoteOptions." + property.Name + " is a " + type.Name + " and this test has no distinct value for it.");
    }

    [Fact]
    public void LocalAddress_ReadsAsAnAddressAndNotAsAConstant()
    {
        var address = NoireRemoteAddress.LocalAddress();

        IPAddress.TryParse(address, out _).Should().BeTrue();
    }

    [Fact]
    public void LocalAddressForALoopbackPeer_IsLoopback()
    {
        var address = NoireRemoteAddress.LocalAddressFor(new IPEndPoint(IPAddress.Loopback, 9));

        IPAddress.IsLoopback(address).Should().BeTrue();
    }

    [Fact]
    public void BroadcastAddresses_AreDirectedAndCarryNoDuplicate()
    {
        var addresses = NoireRemoteAddress.BroadcastAddresses();

        addresses.Select(address => address.ToString()).Should().OnlyHaveUniqueItems();

        foreach (var address in addresses)
            address.GetAddressBytes().Length.Should().Be(4);
    }

    [Fact]
    public void ExcludeInstance_KeepsAnInstanceOutOfDiscovery()
    {
        using var server = new NoireRemoteServer(new NoireRemoteStandaloneHost(), LoopbackOptions());
        server.Options.RegistryDirectory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "NoireRemoteTests", Guid.NewGuid().ToString("N"));
        server.Options.PublishDirectoryRecord = true;
        server.PublishType(typeof(StandaloneProbe));
        server.Start();

        var previousDirectory = NoireRemoteClient.Options.RegistryDirectory;
        var previousExclusion = NoireRemoteClient.Options.ExcludeInstance;

        try
        {
            NoireRemoteClient.Options.RegistryDirectory = server.Options.RegistryDirectory;
            NoireRemoteClient.Options.ExcludeInstance = null;
            NoireRemoteClient.Refresh();

            NoireRemoteClient.Discover("Standalone").Should().ContainSingle()
                .Which.Id.Should().Be(server.InstanceId);

            NoireRemoteClient.Options.ExcludeInstance = server.InstanceId;
            NoireRemoteClient.Refresh();

            NoireRemoteClient.Discover("Standalone").Should().BeEmpty(
                "a plugin that both serves and calls would otherwise resolve itself");
        }
        finally
        {
            NoireRemoteClient.Options.RegistryDirectory = previousDirectory;
            NoireRemoteClient.Options.ExcludeInstance = previousExclusion;
            NoireRemoteClient.Refresh();

            try
            {
                System.IO.Directory.Delete(server.Options.RegistryDirectory, true);
            }
            catch (System.IO.IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ACallerResolvesAProgramByEndpointName_WithNoAddressAndNoCredential()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NoireRemoteTests", Guid.NewGuid().ToString("N"));

        using var program = new NoireRemoteServer(new NoireRemoteStandaloneHost { Name = "route-planner" }, new NoireRemoteOptions
        {
            AutoStart = false,
            EnableLogging = false,
            PublishDirectoryRecord = true,
            PublishCharacterIdentity = false,
            RegistryDirectory = directory,
        });

        program.PublishType(typeof(StandaloneProbe));
        program.Start();

        var previousDirectory = NoireRemoteClient.Options.RegistryDirectory;
        var previousExclusion = NoireRemoteClient.Options.ExcludeInstance;

        try
        {
            NoireRemoteClient.Options.RegistryDirectory = directory;
            NoireRemoteClient.Options.ExcludeInstance = null;
            NoireRemoteClient.Refresh();

            var greeting = await NoireRemoteClient.Endpoint("Standalone")
                .CallAsync<string>("Greet", new { name = "the plugin" }, TestContext.Current.CancellationToken);

            greeting.Should().Be("hello the plugin");

            var sum = await NoireRemoteClient.Endpoint("Standalone")
                .CallAsync<Vector3>("Add", new { left = new Vector3(1, 1, 1), right = new Vector3(2, 2, 2) },
                    TestContext.Current.CancellationToken);

            sum.Should().Be(new Vector3(3, 3, 3));
        }
        finally
        {
            NoireRemoteClient.Options.RegistryDirectory = previousDirectory;
            NoireRemoteClient.Options.ExcludeInstance = previousExclusion;
            NoireRemoteClient.Refresh();

            try
            {
                System.IO.Directory.Delete(directory, true);
            }
            catch (System.IO.IOException)
            {
            }
        }
    }

    [Fact]
    public void TwoServers_BindSeparatelyAndCarryTheirOwnCredential()
    {
        using var first = new NoireRemoteServer(new NoireRemoteStandaloneHost(), LoopbackOptions());
        using var second = new NoireRemoteServer(new NoireRemoteStandaloneHost(), LoopbackOptions());

        first.PublishType(typeof(StandaloneProbe));
        second.PublishType(typeof(BareProbe));
        first.Start();
        second.Start();

        first.Port.Should().NotBe(second.Port);
        first.Token.Should().NotBe(second.Token);
        first.InstanceId.Should().NotBe(second.InstanceId);

        second.GetEndpoint("Standalone").Should().BeNull("each server owns its own route table");
    }

    [Fact]
    public void Dispose_RefusesALaterStart()
    {
        var server = new NoireRemoteServer(new NoireRemoteStandaloneHost(), LoopbackOptions());
        server.PublishType(typeof(BareProbe));
        server.Dispose();

        server.GetEndpoint("Bare").Should().BeNull();
        server.Invoking(target => target.Start()).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Reset_LeavesTheServerUsable()
    {
        using var server = new NoireRemoteServer(new NoireRemoteStandaloneHost(), LoopbackOptions());
        server.PublishType(typeof(BareProbe));
        server.Start();

        server.Reset();

        server.IsListening.Should().BeFalse();
        server.GetEndpoint("Bare").Should().BeNull();

        server.PublishType(typeof(BareProbe));
        server.Start();

        server.IsListening.Should().BeTrue();
        server.Port.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TheIdentityOfAStandaloneHost_CarriesTheClientAssemblyVersion()
    {
        var expected = typeof(NoireRemoteStandaloneHost).Assembly.GetName().Version!.ToString(3);

        new NoireRemoteStandaloneHost().Identity.LibraryVersion.Should().Be(expected);
    }

    [Fact]
    public void ReadinessResult_CarriesItsReasonAndItsWait()
    {
        NoireRemoteReadinessResult.Ready.IsReady.Should().BeTrue();
        NoireRemoteReadinessResult.Ready.Reason.Should().BeEmpty();

        var refusal = NoireRemoteReadinessResult.NotReady("no.", 4.5);

        refusal.IsReady.Should().BeFalse();
        refusal.Reason.Should().Be("no.");
        refusal.RetryAfterSeconds.Should().Be(4.5);
        refusal.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture).Should().Be("4.5");
    }
}
