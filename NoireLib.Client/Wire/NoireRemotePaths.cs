using System;

namespace NoireLib.Remote;

/// <summary>
/// The route vocabulary shared by the listener and the caller.
/// </summary>
public static class NoireRemotePaths
{
    /// <summary>The wire protocol version this assembly speaks.</summary>
    public const int Protocol = 1;

    /// <summary>The prefix every route sits under, with both slashes.</summary>
    public const string Prefix = "/noire/v1/";

    /// <summary>The liveness route.</summary>
    public const string Ping = "_ping";

    /// <summary>The published surface route.</summary>
    public const string Manifest = "_manifest";

    /// <summary>The job route, followed by a slash and the job id.</summary>
    public const string Jobs = "_jobs";

    /// <summary>The event long-poll route.</summary>
    public const string Events = "_events";

    /// <summary>The held-connection event route.</summary>
    public const string Stream = "_stream";


    /// <summary>The file transfer route, followed by a slash and the transfer id for every verb but the first.</summary>
    public const string Files = "_files";

    /// <summary>The generated OpenAPI document route.</summary>
    public const string OpenApi = "_openapi";

    /// <summary>The route describing who is connected to each published socket, and acting on them.</summary>
    public const string Sockets = "_sockets";

    /// <summary>The prefix a generated client is served under, followed by the language. Only <c>python</c> so far.</summary>
    public const string Stubs = "_stubs";


    /// <summary>The prefix of a progress topic.</summary>
    public const string ProgressTopicPrefix = "progress.";

    /// <summary>The prefix of a log topic. The level follows it, lowercased.</summary>
    public const string LogTopicPrefix = "log.";

    /// <summary>The topic every metrics sample publishes under.</summary>
    public const string MetricsTopic = "metrics.sample";

    /// <summary>
    /// Builds the topic a call's progress reports publish under.
    /// </summary>
    /// <param name="jobOrRequestId">The job id, or the request id when the call is not a job.</param>
    /// <returns>The topic.</returns>
    public static string ProgressTopic(string jobOrRequestId)
        => ProgressTopicPrefix + jobOrRequestId;

    /// <summary>The socket every API is reachable on, beside the HTTP routes.</summary>
    public const string ApiSocket = "_api";

    /// <summary>The full route of the API socket, under the websocket prefix.</summary>
    public const string ApiSocketRoute = "/noire/ws/_api";

    /// <summary>The folder under the local application data where a listening instance writes its record.</summary>
    public const string RegistryFolder = @"NoireLib\http\instances";

    /// <summary>
    /// Builds the path a member call posts to.
    /// </summary>
    /// <param name="endpoint">The endpoint name.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The absolute path, starting with a slash.</returns>
    public static string Member(string endpoint, string member)
        => Prefix + endpoint + "/" + member;

    /// <summary>
    /// Builds the path a job poll or cancel targets.
    /// </summary>
    /// <param name="jobId">The job id handed back by the call that started it.</param>
    /// <returns>The absolute path, starting with a slash.</returns>
    public static string Job(string jobId)
        => Prefix + Jobs + "/" + jobId;

    /// <summary>
    /// Checks whether a name is usable as an endpoint or member name on the wire.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <returns>True when the name is non-empty, carries no slash and does not start with an underscore.</returns>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name![0] == '_')
            return false;

        foreach (var character in name)
        {
            if (character == '/' || character == '\\' || character == '?' || character == '#' || character <= ' ')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Builds the base URL of an instance.
    /// </summary>
    /// <param name="address">The address the listener is bound to.</param>
    /// <param name="port">The port the listener is bound to.</param>
    /// <returns>The base URL, with a trailing slash.</returns>
    public static string BaseUrl(string address, int port)
        => address.IndexOf(':') >= 0
            ? "http://[" + address + "]:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/"
            : "http://" + address + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";

    /// <summary>
    /// The full path of the directory folder for the current user.
    /// </summary>
    /// <returns>An absolute folder path. It is not created by this call.</returns>
    public static string DefaultRegistryDirectory()
        => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), RegistryFolder);
}
