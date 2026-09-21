using System;
using System.Globalization;

namespace NoireLib.Remote.Internal;

// The only routes whose body may be something other than JSON. Every error answer is still a JSON envelope.
internal static class HttpFileRoutes
{
    public static HttpRouteOutcome Route(string[] segments, HttpRequestData request, HttpRouterContext context, string? requestId)
    {
        if (!context.Options.EnableFiles)
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.FeatureDisabled, "This listener does not serve the file routes."),
                context.Instance, requestId);

        if (segments.Length == 1)
        {
            return request.Method == "POST"
                ? Declare(request, context, requestId)
                : HttpRouteOutcome.From(
                    new HttpFailure(405, NoireRemoteErrorCodes.BadMethod, "This route takes POST."),
                    context.Instance, requestId);
        }

        var transfer = context.Files.Get(segments[1]);

        if (transfer == null)
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.FileNotFound,
                    "No transfer '" + segments[1] + "' is held. It never existed, it expired or it was consumed.", segments[1]),
                context.Instance, requestId);

        return request.Method switch
        {
            "PUT" => Put(transfer, request, context, requestId),
            "GET" => Get(transfer, request, context, requestId),
            "DELETE" => Delete(transfer, context, requestId),
            _ => HttpRouteOutcome.From(
                new HttpFailure(405, NoireRemoteErrorCodes.BadMethod, "This route takes PUT, GET or DELETE."),
                context.Instance, requestId),
        };
    }

    private static HttpRouteOutcome Declare(HttpRequestData request, HttpRouterContext context, string? requestId)
    {
        NoireRemoteFileRequest? body;

        try
        {
            body = NoireRemoteJson.Read<NoireRemoteFileRequest>(System.Text.Encoding.UTF8.GetString(request.Body));
        }
        catch (Exception)
        {
            return HttpRouteOutcome.From(
                new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "The transfer body is not valid JSON."),
                context.Instance, requestId);
        }

        if (body == null)
            return HttpRouteOutcome.From(
                new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "A transfer needs a body naming the file."),
                context.Instance, requestId);

        var failure = context.Files.Declare(body, out var transfer);

        if (failure != null)
            return HttpRouteOutcome.From(failure, context.Instance, requestId);

        return new HttpRouteOutcome { Body = context.Files.StatusOf(transfer!) };
    }

    private static HttpRouteOutcome Put(HttpFileStore.Transfer transfer, HttpRequestData request, HttpRouterContext context, string? requestId)
    {
        var offset = LongOf(request, "offset", 0);
        var failure = context.Files.Write(transfer, offset, request.Body);

        if (failure != null)
            return HttpRouteOutcome.From(failure, context.Instance, requestId);

        return new HttpRouteOutcome { Body = context.Files.StatusOf(transfer) };
    }

    private static HttpRouteOutcome Get(HttpFileStore.Transfer transfer, HttpRequestData request, HttpRouterContext context, string? requestId)
    {
        if (!transfer.Complete)
            return HttpRouteOutcome.From(
                new HttpFailure(400, NoireRemoteErrorCodes.FileIncomplete,
                    "This transfer has " + transfer.Received + " of " + transfer.Size + " bytes."),
                context.Instance, requestId);

        var bytes = transfer.ReadAll();
        var offset = LongOf(request, "offset", 0);
        var length = LongOf(request, "length", bytes.LongLength - offset);

        if (offset < 0 || offset > bytes.LongLength)
            return HttpRouteOutcome.From(
                new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "The offset runs past the file."),
                context.Instance, requestId);

        length = Math.Min(length, bytes.LongLength - offset);
        length = Math.Min(length, context.Options.MaxRequestBytes);

        var slice = new byte[length];
        Array.Copy(bytes, offset, slice, 0, length);

        return new HttpRouteOutcome
        {
            Write = (stream, token) => HttpResponseWriter.WriteBytesAsync(
                stream,
                200,
                transfer.ContentType ?? "application/octet-stream",
                slice,
                context.Instance,
                null,
                token),
        };
    }

    private static HttpRouteOutcome Delete(HttpFileStore.Transfer transfer, HttpRouterContext context, string? requestId)
    {
        context.Files.Drop(transfer.Id);

        return new HttpRouteOutcome
        {
            Body = new NoireRemoteEnvelope { Ok = true, Instance = context.Instance, Id = requestId },
        };
    }

    private static long LongOf(HttpRequestData request, string key, long fallback)
    {
        var query = HttpQueryString.Parse(request.Target);

        return query.TryGetValue(key, out var text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
    }

    // Resolved before the generic Newtonsoft path.
    public static HttpFailure? Resolve(NoireRemoteFile file, HttpRouterContext context)
    {
        var transfer = context.Files.Get(file.Id);

        if (transfer == null)
            return new HttpFailure(404, NoireRemoteErrorCodes.FileNotFound,
                "No transfer '" + file.Id + "' is held. It never existed, it expired or it was consumed.", file.Id);

        if (!transfer.Complete)
            return new HttpFailure(400, NoireRemoteErrorCodes.FileIncomplete,
                "Transfer '" + file.Id + "' is missing the bytes from " + transfer.Received + " to " + transfer.Size + ".", file.Id);

        file.Name = transfer.Name;
        file.Size = transfer.Size;
        file.Sha256 = transfer.Sha256;
        file.ContentType = transfer.ContentType;
        file.Reader = transfer.ReadAll;

        return null;
    }

    // A returned file's bytes are parked and the reference written in their place.
    public static void Park(NoireRemoteFile file, HttpRouterContext context)
    {
        if (file.Pending == null)
            return;

        var transfer = context.Files.Park(file.Name, file.Pending, file.ContentType, file.Sha256);

        file.Id = transfer.Id;
        file.Pending = null;
        file.Reader = transfer.ReadAll;
    }
}
