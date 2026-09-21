using System;

namespace NoireLib.Remote.Internal;

// Frames on a reserved socket are never reported. A caller watching traffic over the API socket would otherwise see its own frame and publish another one, forever.
internal static class RemoteTraffic
{
    internal static void Call(HttpRouterContext context, NoireRemoteCallReport report, string wire)
    {
        var hub = context.Events;

        if (!context.Options.PublishTraffic || !hub.IsWatched(NoireRemoteChannels.Traffic))
            return;

        hub.Publish(NoireRemoteChannels.Traffic, NoireRemoteTrafficTopics.Call, new NoireRemoteTrafficCall
        {
            Wire = wire,
            Endpoint = report.Endpoint,
            Member = report.Member,
            RequestId = report.RequestId,
            Ok = report.Ok,
            ErrorCode = report.ErrorCode,
            ElapsedMs = report.Elapsed.TotalMilliseconds,
            IsLoopback = report.IsLoopback,
        });
    }

    internal static void Socket(RemoteEventHub? hub, NoireRemoteOptions? options, string socket, string state, Guid connection, string address, int? code, string? reason)
    {
        if (hub == null || options == null || !options.PublishTraffic || IsReserved(socket) || !hub.IsWatched(NoireRemoteChannels.Traffic))
            return;

        hub.Publish(NoireRemoteChannels.Traffic, NoireRemoteTrafficTopics.Socket, new NoireRemoteTrafficSocket
        {
            Socket = socket,
            State = state,
            Connection = connection,
            Address = address,
            Code = code,
            Reason = reason,
        });
    }

    internal static bool WantsFrames(RemoteEventHub? hub, NoireRemoteOptions? options, string socket)
        => hub != null && options != null && options.PublishTraffic && options.PublishTrafficFrames
            && !IsReserved(socket) && hub.IsWatched(NoireRemoteChannels.Traffic);

    internal static void Frame(RemoteEventHub? hub, NoireRemoteOptions? options, string socket, string direction, Guid connection, int bytes, string? text)
    {
        if (!WantsFrames(hub, options, socket))
            return;

        hub.Publish(NoireRemoteChannels.Traffic, NoireRemoteTrafficTopics.Frame, new NoireRemoteTrafficFrame
        {
            Socket = socket,
            Direction = direction,
            Connection = connection,
            Bytes = bytes,
            Preview = Cut(text, options.MaxTrafficPreview),
        });
    }

    private static bool IsReserved(string socket)
        => socket.Length > 0 && socket[0] == '_';

    private static string? Cut(string? text, int limit)
    {
        if (text == null)
            return null;

        var ceiling = Math.Max(16, limit);

        return text.Length <= ceiling ? text : text.Substring(0, ceiling) + "...";
    }
}
