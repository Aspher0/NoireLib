using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// Separate from the API port. HttpSecurityGate keeps refusing every browser header.
internal sealed class HttpConsoleSocket : IDisposable
{
    private readonly NoireRemoteServer server;
    private readonly HttpRouterContext context;
    private readonly HttpConsoleGate gate;
    private readonly ConsoleLiveSocket live;
    private readonly string generated = NoireRemoteSignature.CreateToken();
    private HttpServer? socket;
    private ConsoleFleetWatcher? fleet;

    public HttpConsoleSocket(NoireRemoteServer server, HttpRouterContext context)
    {
        this.server = server;
        this.context = context;
        Sessions = new HttpConsoleSessions();
        live = new ConsoleLiveSocket(context);
        gate = new HttpConsoleGate(this, context.Host);
    }

    public HttpConsoleSessions Sessions { get; }

    public bool IsListening { get; private set; }

    public bool HasLiveSessions => live.SessionCount > 0;

    // The page decides what a refetch means for its own state.
    public void Invalidate(string scope)
        => live.Invalidate(scope);

    // The option wins whenever set. The generated key still requires a credential for a remote console.
    public string? Key
        => context.Options.ConsoleAccess == NoireRemoteConsoleAccess.Open ? null
            : !string.IsNullOrEmpty(context.Options.ConsoleKey) ? context.Options.ConsoleKey
            : context.Options.EnableRemoteConsole ? generated
            : null;

    public int Port => socket?.Port ?? 0;

    public string Origin => "http://" + (context.Options.EnableRemoteConsole ? NoireRemoteAddress.LocalAddress() : "127.0.0.1")
        + ":" + Port.ToString(CultureInfo.InvariantCulture);

    // Toggling the remote console rebinds this socket. An open page must not lose its port.
    public void Start(int fallbackPort = 0)
    {
        var wanted = context.Options.ConsolePort > 0 ? context.Options.ConsolePort : Math.Max(0, fallbackPort);

        socket = new HttpServer(
            context.Options,
            context,
            server.Token,
            context.Host,
            gate,
            RouteAsync,
            context.Options.EnableRemoteConsole,
            wanted);

        try
        {
            socket.Start();
        }
        catch (System.Net.Sockets.SocketException) when (wanted != 0 && context.Options.ConsolePort <= 0)
        {
            socket.Dispose();

            socket = new HttpServer(
                context.Options,
                context,
                server.Token,
                context.Host,
                gate,
                RouteAsync,
                context.Options.EnableRemoteConsole,
                0);

            socket.Start();
        }

        IsListening = true;

        fleet = new ConsoleFleetWatcher(context.Options, () => server.InvalidateConsole(NoireRemoteServer.ConsoleScopes.Fleet));
    }

    // A call the page makes is byte for byte the documented call.
    private async Task<HttpRouteOutcome> RouteAsync(HttpRequestData request, bool isLoopback, CancellationToken cancellationToken)
    {
        var path = HttpQueryString.PathOf(request.Path);

        if (path.StartsWith(NoireRemotePaths.Prefix, StringComparison.Ordinal))
        {
            request.FromConsole = true;

            return await HttpRouter.RouteAsync(request, isLoopback, context, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(path, NoireRemoteConsolePaths.Session, StringComparison.Ordinal))
            return RouteSession(request, isLoopback);

        if (string.Equals(path, NoireRemoteConsolePaths.Bootstrap, StringComparison.Ordinal))
            return RouteBootstrap(isLoopback);

        // Only the API prefix is forwarded to the router.
        if (string.Equals(path, NoireRemoteConsolePaths.Live, StringComparison.Ordinal))
            return live.Route(request);

        if (path.StartsWith(NoireRemoteConsolePaths.Asset, StringComparison.Ordinal))
            return RouteAsset(path.Substring(NoireRemoteConsolePaths.Asset.Length));

        if (string.Equals(path, NoireRemoteConsolePaths.Fleet, StringComparison.Ordinal)
            || path.StartsWith(NoireRemoteConsolePaths.Fleet + "/", StringComparison.Ordinal))
            return await HttpConsoleFleet.RouteAsync(path, request, context, cancellationToken).ConfigureAwait(false);

        if (string.Equals(path, NoireRemoteConsolePaths.Page, StringComparison.Ordinal)
            || string.Equals(path, NoireRemoteConsolePaths.Page.TrimEnd('/'), StringComparison.Ordinal))
            return RoutePage();

        return HttpRouteOutcome.From(
            new HttpFailure(404, NoireRemoteErrorCodes.UnknownEndpoint, "The console serves no route at '" + path + "'."),
            context.Instance, null);
    }

    private HttpRouteOutcome RoutePage()
    {
        var nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var html = HttpConsolePage.Render(server, nonce);

        // The nonce keeps the page's script and style inline without unsafe-inline.
        var policy = "default-src 'none'; script-src 'nonce-" + nonce + "'; style-src 'nonce-" + nonce
            + "'; img-src 'self' data:; font-src 'self' data:; connect-src 'self' " + LiveOrigins()
            + "; form-action 'none'; frame-ancestors 'none'; base-uri 'none'";

        return new HttpRouteOutcome
        {
            Write = (stream, token) => HttpResponseWriter.WriteBytesAsync(
                stream,
                200,
                "text/html; charset=utf-8",
                Encoding.UTF8.GetBytes(html),
                context.Instance,
                [
                    new KeyValuePair<string, string>("Content-Security-Policy", policy),
                    new KeyValuePair<string, string>("Cross-Origin-Resource-Policy", "same-origin"),
                    new KeyValuePair<string, string>("Referrer-Policy", "no-referrer"),
                ],
                token),
        };
    }

    // 'self' does not cover a WebSocket URL in every browser.
    private string LiveOrigins()
    {
        var port = Port.ToString(CultureInfo.InvariantCulture);
        var loopback = "ws://127.0.0.1:" + port;

        return context.Options.EnableRemoteConsole ? loopback + " ws://" + NoireRemoteAddress.LocalAddress() + ":" + port : loopback;
    }

    private HttpRouteOutcome RouteSession(HttpRequestData request, bool isLoopback)
    {
        if (request.Method != "POST")
            return HttpRouteOutcome.From(
                new HttpFailure(405, NoireRemoteErrorCodes.BadMethod, "This route takes POST."),
                context.Instance, null);

        NoireRemoteConsoleGrant? body = null;

        try
        {
            body = NoireRemoteJson.Read<NoireRemoteConsoleGrant>(Encoding.UTF8.GetString(request.Body));
        }
        catch (Exception)
        {
        }

        var token = Sessions.Redeem(body?.Grant);

        if (token == null && OpensWithNothing(isLoopback))
            token = Sessions.Create();

        if (token == null && Key != null && NoireRemoteSignature.FixedTimeEquals(Key, body?.Key ?? string.Empty))
            token = Sessions.Create();

        if (token == null)
            return HttpRouteOutcome.From(
                new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "This console needs a grant or its key."),
                context.Instance, null);

        return new HttpRouteOutcome { Body = new NoireRemoteConsoleSession { Token = token } };
    }

    private bool OpensWithNothing(bool isLoopback)
        => context.Options.ConsoleAccess switch
        {
            NoireRemoteConsoleAccess.Open => true,
            NoireRemoteConsoleAccess.LoopbackOpen => isLoopback,
            _ => false,
        };

    private HttpRouteOutcome RouteBootstrap(bool isLoopback)
    {
        var style = server.Console.Style;
        var manifest = context.Manifest();

        return new HttpRouteOutcome
        {
            Body = new NoireRemoteConsoleBootstrap
            {
                Manifest = manifest,
                Title = style.Title ?? manifest.Plugin,
                Instance = context.Instance,
                ApiBaseUrl = NoireRemotePaths.Prefix,
                // Over loopback the page gets the credential. The remote secret never reaches a browser.
                Token = isLoopback ? server.Token : null,
                IsLoopback = isLoopback,
                RememberValues = context.Options.RememberValues,
                ShowQrCode = context.Options.EnableQrCode,
                Channels = manifest.Channels,
                Variables = CopyVariables(style),
                StyleSheet = ClampSheet(style.StyleSheet),
                RemoteUrl = context.Options.EnableRemoteConsole ? Origin + NoireRemoteConsolePaths.Page : null,
                LivePath = NoireRemoteConsolePaths.Live,
                LiveProtocol = ConsoleLiveSocket.SubProtocol,
                Reconnect = ConsoleLiveSocket.Schedule(),
            },
        };
    }

    // A name or value that would end the declaration early is dropped.
    private static Dictionary<string, string> CopyVariables(NoireRemoteConsoleStyle style)
    {
        var copied = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in style.Variables)
        {
            if (!IsUsableName(pair.Key) || !IsUsableValue(pair.Value))
                continue;

            copied[pair.Key.ToLowerInvariant()] = pair.Value;
        }

        return copied;
    }

    private static bool IsUsableName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name!.Length > 40)
            return false;

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
                return false;
        }

        return true;
    }

    private static bool IsUsableValue(string? value)
        => !string.IsNullOrEmpty(value)
            && value!.Length <= 200
            && value.IndexOf('<') < 0
            && value.IndexOf(';') < 0
            && value.IndexOf('}') < 0;

    private static string? ClampSheet(string? sheet)
        => sheet == null || sheet.Length <= 64 * 1024 ? sheet : sheet.Substring(0, 64 * 1024);

    private HttpRouteOutcome RouteAsset(string name)
    {
        if (!server.Console.Style.Assets.TryGetValue(NoireRemoteConsolePaths.Prefix.Length > 0 ? HttpQueryString.Decode(name) : name, out var asset))
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.UnknownEndpoint, "The console serves no asset named '" + name + "'."),
                context.Instance, null);

        return new HttpRouteOutcome
        {
            Write = (stream, token) => HttpResponseWriter.WriteBytesAsync(
                stream, 200, asset.ContentType, asset.Bytes, context.Instance,
                [new KeyValuePair<string, string>("Cross-Origin-Resource-Policy", "same-origin")], token),
        };
    }

    public void Dispose()
    {
        IsListening = false;
        fleet?.Dispose();
        fleet = null;
        live.Dispose();
        socket?.Dispose();
        socket = null;
        Sessions.RevokeAll();
    }
}

// Separate from HttpSecurityGate. The gate keeps refusing every browser header.
internal sealed class HttpConsoleGate(HttpConsoleSocket socket, INoireRemoteHost host) : IHttpGate
{
    private readonly HttpSecurityGate lockout = new(host);

    public HttpFailure? CheckHeaders(
        HttpRequestData request,
        NoireRemoteOptions options,
        string token,
        int port,
        bool isLoopback,
        string remoteAddress,
        out bool signed)
    {
        signed = false;

        var path = HttpQueryString.PathOf(request.Path);

        // The DNS rebinding defence. A name that is not this listener's never reaches a route.
        if (!HttpSecurityGate.HostIsAccepted(request.Header("host"), options, port))
            return new HttpFailure(403, NoireRemoteErrorCodes.Forbidden, "The host header does not name this console.");

        var origin = request.Header("origin");

        if (!string.IsNullOrEmpty(origin) && !string.Equals(origin, socket.Origin, StringComparison.OrdinalIgnoreCase))
            return new HttpFailure(403, NoireRemoteErrorCodes.Forbidden, "A page on another site cannot reach this console.");

        var site = request.Header("sec-fetch-site");

        if (!string.IsNullOrEmpty(site) && site != "same-origin" && site != "none")
            return new HttpFailure(403, NoireRemoteErrorCodes.Forbidden, "A page on another site cannot reach this console.");

        // A browser opening a link cannot set a header.
        if (string.Equals(path, NoireRemoteConsolePaths.Page, StringComparison.Ordinal)
            || string.Equals(path, NoireRemoteConsolePaths.Page.TrimEnd('/'), StringComparison.Ordinal))
            return null;

        if (string.Equals(path, NoireRemoteConsolePaths.Session, StringComparison.Ordinal))
            return null;

        var authorization = request.Header("authorization");

        // A browser cannot set a header on a WebSocket handshake. The session token comes in the sub-protocol list.
        if (string.IsNullOrEmpty(authorization) && string.Equals(path, NoireRemoteConsolePaths.Live, StringComparison.Ordinal))
        {
            return socket.Sessions.Accept(ConsoleLiveSocket.TokenOf(request.Header("sec-websocket-protocol")))
                ? null
                : new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "That console session is not one this listener holds.");
        }

        if (string.IsNullOrEmpty(authorization) || !authorization!.StartsWith(NoireRemoteHeaders.ConsoleScheme + " ", StringComparison.OrdinalIgnoreCase))
            return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "This route needs a console session.");

        if (!socket.Sessions.Accept(authorization.Substring(NoireRemoteHeaders.ConsoleScheme.Length + 1).Trim()))
            return new HttpFailure(401, NoireRemoteErrorCodes.Unauthorized, "That console session is not one this listener holds.");

        return null;
    }

    public HttpFailure? CheckBody(HttpRequestData request, NoireRemoteOptions options, string remoteAddress, bool signed)
    {
        _ = lockout;
        return null;
    }
}
