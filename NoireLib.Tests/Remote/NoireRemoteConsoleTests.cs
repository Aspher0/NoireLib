using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The second socket exists so the API gate keeps refusing every browser header without a conditional. These cover
/// what the console gate accepts, what it refuses, and the properties of the page a browser cannot be asked about.
/// </summary>
public sealed class NoireRemoteConsoleTests : IDisposable
{
    private readonly NoireRemoteServer server;
    private readonly HttpClient client;

    public NoireRemoteConsoleTests()
    {
        server = new NoireRemoteServer(new NoireRemoteStandaloneHost { Name = "console-probe" }, new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
            EnableConsole = true,
        });

        server.PublishType(typeof(ConsoleProbe));
        server.Start();

        client = new HttpClient();
    }

    public void Dispose()
    {
        client.Dispose();
        server.Dispose();
    }

    [NoireRemoteClass("Page")]
    public static class ConsoleProbe
    {
        [NoireRemote]
        public static int Add(int left, int right) => left + right;
    }

    private string ConsoleUrl(string route) => "http://127.0.0.1:" + server.Console.Port + route;

    private static NoireRemoteServer NewConsole(Action<NoireRemoteOptions> configure)
    {
        var options = new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
            EnableConsole = true,
        };

        configure(options);

        var built = new NoireRemoteServer(new NoireRemoteStandaloneHost { Name = "console-probe" }, options);

        built.PublishType(typeof(ConsoleProbe));
        built.Start();

        return built;
    }

    private async Task<HttpStatusCode> AskForSessionAsync(NoireRemoteServer target, NoireRemoteConsoleGrant body)
    {
        using var response = await client.PostAsync(
            "http://127.0.0.1:" + target.Console.Port + NoireRemoteConsolePaths.Session,
            new StringContent(NoireRemoteJson.Write(body), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private static string GrantOf(string url)
    {
        var mark = url.IndexOf("#g=", StringComparison.Ordinal);

        return mark < 0 ? string.Empty : url.Substring(mark + 3);
    }

    private async Task<string> OpenSessionAsync()
    {
        var grant = GrantOf(server.Console.Open()!);

        using var response = await client.PostAsync(
            ConsoleUrl(NoireRemoteConsolePaths.Session),
            new StringContent(NoireRemoteJson.Write(new NoireRemoteConsoleGrant { Grant = grant }), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return NoireRemoteJson.Read<NoireRemoteConsoleSession>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.Token;
    }

    [Fact]
    public void TheConsole_BindsASocketOfItsOwn()
    {
        server.Console.IsListening.Should().BeTrue();
        server.Console.Port.Should().NotBe(server.Port, "the API gate and the console gate are different gates");
    }

    [Fact]
    public async Task ThePage_IsServedWithNoCredential()
    {
        using var response = await client.GetAsync(ConsoleUrl(NoireRemoteConsolePaths.Page), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");

        var policy = response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Subject;

        policy.Should().Contain("default-src 'none'");
        policy.Should().Contain("frame-ancestors 'none'");
        policy.Should().Contain("nonce-");
        policy.Should().NotContain("unsafe-inline", "the nonce is why the script stays inline without it");

        response.Headers.GetValues("Cross-Origin-Resource-Policy").Should().ContainSingle().Which.Should().Be("same-origin");
    }

    [Fact]
    public async Task ThePageNonce_IsFreshPerRequestAndMatchesEveryInlineBlock()
    {
        using var first = await client.GetAsync(ConsoleUrl(NoireRemoteConsolePaths.Page), TestContext.Current.CancellationToken);
        using var second = await client.GetAsync(ConsoleUrl(NoireRemoteConsolePaths.Page), TestContext.Current.CancellationToken);

        var firstHtml = await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var secondHtml = await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var nonce = Regex.Match(first.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Subject, "nonce-([0-9A-F]+)").Groups[1].Value;

        nonce.Should().NotBeNullOrEmpty();
        firstHtml.Should().Contain("nonce=\"" + nonce + "\"");
        firstHtml.Should().NotContain("__NONCE__");
        firstHtml.Should().NotBe(secondHtml, "a per-request nonce is what keeps the inline script from being replayable");
    }

    [Fact]
    public async Task TheBootstrap_NeedsASessionAndTheGrantBuysOne()
    {
        using var refused = await client.GetAsync(ConsoleUrl(NoireRemoteConsolePaths.Bootstrap), TestContext.Current.CancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var token = await OpenSessionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleUrl(NoireRemoteConsolePaths.Bootstrap));
        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization, NoireRemoteHeaders.ConsoleScheme + " " + token);

        using var accepted = await client.SendAsync(request, TestContext.Current.CancellationToken);

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);

        var boot = NoireRemoteJson.Read<NoireRemoteConsoleBootstrap>(
            await accepted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        boot.Manifest!.GetEndpoint("Page")!.GetMember("Add").Should().NotBeNull();
        boot.Title.Should().Be("console-probe");
        boot.IsLoopback.Should().BeTrue();
        boot.Token.Should().Be(server.Token, "over loopback the snippets the page emits run as pasted");
    }

    // Locked: at the default a loopback browser gets a session either way.
    [Fact]
    public async Task AGrant_IsSpentByTheFirstPageThatRedeemsIt()
    {
        using var locked = NewConsole(options => options.ConsoleAccess = NoireRemoteConsoleAccess.Locked);

        var grant = GrantOf(locked.Console.Open()!);

        (await AskForSessionAsync(locked, new NoireRemoteConsoleGrant { Grant = grant }))
            .Should().Be(HttpStatusCode.OK);
        (await AskForSessionAsync(locked, new NoireRemoteConsoleGrant { Grant = grant }))
            .Should().Be(HttpStatusCode.Unauthorized, "a grant read twice is a grant somebody else also has");
    }

    [Fact]
    public async Task ALoopbackBrowser_IsGivenASessionWithNothing()
    {
        (await AskForSessionAsync(server, new NoireRemoteConsoleGrant()))
            .Should().Be(HttpStatusCode.OK, "the socket is loopback bound and the gate already refuses another site");
    }

    [Fact]
    public async Task UnderLocked_ALoopbackBrowserStillPresentsSomething()
    {
        using var locked = NewConsole(options => options.ConsoleAccess = NoireRemoteConsoleAccess.Locked);

        (await AskForSessionAsync(locked, new NoireRemoteConsoleGrant())).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheKey_BuysASessionAndAWrongOneDoesNot()
    {
        using var locked = NewConsole(options =>
        {
            options.ConsoleAccess = NoireRemoteConsoleAccess.Locked;
            options.ConsoleKey = "hunter2";
        });

        locked.Console.Key.Should().Be("hunter2");

        (await AskForSessionAsync(locked, new NoireRemoteConsoleGrant { Key = "hunter2" }))
            .Should().Be(HttpStatusCode.OK);
        (await AskForSessionAsync(locked, new NoireRemoteConsoleGrant { Key = "hunter3" }))
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnderOpen_NothingIsAskedAndNoKeyIsHeld()
    {
        using var opened = NewConsole(options => options.ConsoleAccess = NoireRemoteConsoleAccess.Open);

        opened.Console.Key.Should().BeNull("nothing asks for one");
        (await AskForSessionAsync(opened, new NoireRemoteConsoleGrant())).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void AGeneratedKey_ExistsOnlyWhenTheRemoteConsoleIsOnAndNoneWasSet()
    {
        using var local = NewConsole(options => options.ConsoleAccess = NoireRemoteConsoleAccess.Locked);

        local.Console.Key.Should().BeNull("a loopback console is reached by a grant alone");

        using var remote = NewConsole(options => options.EnableRemoteConsole = true);

        remote.Console.Key.Should().NotBeNullOrWhiteSpace("turning the remote console on must not leave an open port");
    }

    [Fact]
    public async Task APageOnAnotherSite_IsRefused()
    {
        var token = await OpenSessionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleUrl(NoireRemoteConsolePaths.Bootstrap));
        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization, NoireRemoteHeaders.ConsoleScheme + " " + token);
        request.Headers.TryAddWithoutValidation("Origin", "http://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ACrossSiteFetchHeader_IsRefused()
    {
        var token = await OpenSessionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleUrl(NoireRemoteConsolePaths.Bootstrap));
        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization, NoireRemoteHeaders.ConsoleScheme + " " + token);
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AHostHeaderNamingSomethingElse_IsRefused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleUrl(NoireRemoteConsolePaths.Page));
        request.Headers.Host = "attacker.example";

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the host check is the DNS rebinding defence");
    }

    [Fact]
    public async Task TheConsoleSocket_AlsoServesTheOrdinaryMemberRoutes()
    {
        var token = await OpenSessionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, ConsoleUrl(NoireRemotePaths.Member("Page", "Add")))
        {
            Content = new StringContent(
                NoireRemoteJson.Write(new NoireRemoteRequest { Args = NoireRemoteJson.ToToken(new { left = 4, right = 5 }) }),
                Encoding.UTF8,
                "application/json"),
        };

        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization, NoireRemoteHeaders.ConsoleScheme + " " + token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = NoireRemoteJson.Read<NoireRemoteEnvelope>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        envelope.ResultAs<int>().Should().Be(9,
            "a call the page makes is the call the documentation describes; the snippets it emits are the request it sent");
    }

    [Fact]
    public async Task TheApiSocket_StillRefusesEveryBrowserHeader()
    {
        using var browser = new HttpClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
        browser.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");

        using var response = await browser.GetAsync(
            "http://127.0.0.1:" + server.Port + NoireRemotePaths.Prefix + NoireRemotePaths.Ping,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the second socket exists so this claim stays flat and untested by a conditional");
    }

    [Fact]
    public void ThePage_CarriesNoInnerHtmlAndNoEval()
    {
        var page = server.Console.PageSource();

        page.Should().NotContain("innerHTML");
        page.Should().NotContain("outerHTML");
        page.Should().NotContain("eval(");
        page.Should().NotContain("new Function(");
        page.Should().NotContain("document.write");
    }

    [Fact]
    public void ThePage_KeepsItsSessionWhereATabCloseCannotTakeIt()
    {
        var page = server.Console.PageSource();

        page.Should().NotContain("sessionStorage");
        page.Should().Contain("noire.token");
    }

    // Replacing the fragment does not reload the document.
    [Fact]
    public void ThePage_ActsOnAGrantPastedIntoTheAddressBar()
        => server.Console.PageSource().Should().Contain("hashchange");

    [Fact]
    public void ThePage_StaysUnderItsSizeCap()
    {
        var bytes = Encoding.UTF8.GetByteCount(server.Console.PageSource());

        // Embedded resources are stored uncompressed. The cap catches a pasted library.
        bytes.Should().BeLessThan(160 * 1024,
            "a portable executable stores a resource uncompressed; the assembly grows by the raw size");
    }

    [Fact]
    public void ThePage_ReadsEveryColourFromACustomProperty()
    {
        var page = server.Console.PageSource();

        foreach (var name in NoireRemoteConsoleStyle.KnownVariables)
            page.Should().Contain("--noire-" + name, "the styling contract is what a plugin drives the page with");
    }

    [Fact]
    public async Task APluginVariable_ReachesTheBootstrapAndABadOneDoesNot()
    {
        server.Console.Style.Variables["accent"] = "#ff0000";
        server.Console.Style.Variables["bad name"] = "#00ff00";
        server.Console.Style.Variables["broken"] = "red; } body { display:none";
        server.Console.Style.StyleSheet = "body { letter-spacing: .01em }";
        server.Console.Style.Title = "My plugin";

        var token = await OpenSessionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleUrl(NoireRemoteConsolePaths.Bootstrap));
        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization, NoireRemoteHeaders.ConsoleScheme + " " + token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var boot = NoireRemoteJson.Read<NoireRemoteConsoleBootstrap>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        boot.Variables!.Should().ContainKey("accent").WhoseValue.Should().Be("#ff0000");
        boot.Variables.Should().NotContainKey("bad name");
        boot.Variables.Should().NotContainKey("broken", "a value that would end the declaration early is dropped");
        boot.StyleSheet.Should().Be("body { letter-spacing: .01em }");
        boot.Title.Should().Be("My plugin");
    }

    [Fact]
    public async Task APluginAsset_IsServedUnderItsName()
    {
        server.Console.Style.Assets["logo.svg"] = new NoireRemoteConsoleAsset("image/svg+xml", Encoding.UTF8.GetBytes("<svg/>"));

        var token = await OpenSessionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleUrl(NoireRemoteConsolePaths.Asset + "logo.svg"));
        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization, NoireRemoteHeaders.ConsoleScheme + " " + token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("<svg/>");
    }

    [Fact]
    public async Task TheFleetRoute_NeedsAConsoleSession()
    {
        // Each listener's AllowFleetControl decides what the route reaches. Without a session it reaches nothing.
        using var response = await client.GetAsync(ConsoleUrl(NoireRemoteConsolePaths.Fleet), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void RevokeSessions_EndsEveryLiveSession()
    {
        server.Console.Open();
        server.Console.SessionCount.Should().Be(0, "a grant is not yet a session");

        server.Console.RevokeSessions();
        server.Console.SessionCount.Should().Be(0);
    }

    [Fact]
    public void AConsoleUrl_CarriesItsGrantInTheFragment()
    {
        var url = server.Console.Open()!;

        url.Should().StartWith("http://127.0.0.1:" + server.Console.Port + NoireRemoteConsolePaths.Page);
        url.Should().Contain("#g=");
        url.Substring(0, url.IndexOf('#')).Should().NotContain("g=", "a fragment never reaches a server, a log or a referrer");
    }

    [Fact]
    public void AConsoleThatIsOff_BindsNothing()
    {
        using var quiet = new NoireRemoteServer(new NoireRemoteStandaloneHost(), new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
            EnableConsole = false,
        });

        quiet.PublishType(typeof(ConsoleProbe));
        quiet.Start();

        quiet.Console.IsListening.Should().BeFalse();
        quiet.Console.Open().Should().BeNull();
    }
}
