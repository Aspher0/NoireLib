using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;

namespace NoireLib.Remote.Internal;

// A misspelled key is refused. Silently taking a default would do something else.
internal static class RemoteArgumentBinder
{
    internal sealed class HttpCallContext
    {
        public string Route = string.Empty;
        public string RequestId = string.Empty;
        public Action<string, object?>? PublishProgress;
        public Func<NoireRemoteFile, HttpFailure?>? ResolveFile;

        public HttpProgressContext? Progress(string route)
        {
            if (PublishProgress == null)
                return null;

            var job = RemoteJobStore.CurrentJob;

            return new HttpProgressContext(
                route,
                RequestId,
                job?.Id,
                PublishProgress,
                job == null ? null : value => job.Progress = value);
        }
    }

    public static HttpFailure? Bind(NoireRemoteMemberInfo member, JToken? args, CancellationToken cancellationToken, HttpCallContext? call, out object?[] values)
    {
        values = new object?[member.Parameters.Count];

        if (args is JArray positional)
            return BindPositional(member, positional, cancellationToken, call, values);

        JObject named;

        if (args == null || args.Type == JTokenType.Null || args.Type == JTokenType.Undefined)
            named = [];
        else if (args is JObject supplied)
            named = supplied;
        else
            return new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "The args field reads as an object or an array.");

        var progress = call?.Progress(member.Route);

        foreach (var property in named.Properties())
        {
            if (Find(member, property.Name) == null)
                return new HttpFailure(400, NoireRemoteErrorCodes.ArgumentUnknown,
                    "'" + member.Route + "' takes no argument named '" + property.Name + "'.", property.Name);
        }

        for (var index = 0; index < member.Parameters.Count; index++)
        {
            var parameter = member.Parameters[index];

            if (parameter.IsCancellationToken)
            {
                values[index] = cancellationToken;
                continue;
            }

            if (parameter.IsProgress)
            {
                values[index] = progress == null ? null : parameter.ProgressFactory!(progress);
                continue;
            }

            var value = named.GetValue(parameter.Name, StringComparison.OrdinalIgnoreCase);

            if (value == null || value.Type == JTokenType.Undefined)
            {
                if (parameter.IsRequired)
                    return new HttpFailure(400, NoireRemoteErrorCodes.ArgumentMissing,
                        "'" + member.Route + "' needs an argument named '" + parameter.Name + "'.", parameter.Name);

                values[index] = parameter.DefaultValue;
                continue;
            }

            var failure = Convert(member, parameter, value, call, out var converted);

            if (failure != null)
                return failure;

            values[index] = converted;
        }

        return null;
    }

    private static HttpFailure? BindPositional(NoireRemoteMemberInfo member, JArray positional, CancellationToken cancellationToken, HttpCallContext? call, object?[] values)
    {
        var supplied = 0;
        var progress = call?.Progress(member.Route);

        for (var index = 0; index < member.Parameters.Count; index++)
        {
            var parameter = member.Parameters[index];

            if (parameter.IsCancellationToken)
            {
                values[index] = cancellationToken;
                continue;
            }

            if (parameter.IsProgress)
            {
                values[index] = progress == null ? null : parameter.ProgressFactory!(progress);
                continue;
            }

            if (supplied >= positional.Count)
            {
                if (parameter.IsRequired)
                    return new HttpFailure(400, NoireRemoteErrorCodes.ArgumentMissing,
                        "'" + member.Route + "' needs an argument named '" + parameter.Name + "'.", parameter.Name);

                values[index] = parameter.DefaultValue;
                continue;
            }

            var failure = Convert(member, parameter, positional[supplied], call, out var converted);

            if (failure != null)
                return failure;

            values[index] = converted;
            supplied++;
        }

        if (supplied < positional.Count)
            return new HttpFailure(400, NoireRemoteErrorCodes.ArgumentUnknown,
                "'" + member.Route + "' takes " + supplied + " arguments and " + positional.Count + " were sent.");

        return null;
    }

    private static HttpFailure? Convert(NoireRemoteMemberInfo member, NoireRemoteParameterInfo parameter, JToken value, out object? converted)
        => Convert(member, parameter, value, null, out converted);

    private static HttpFailure? Convert(
        NoireRemoteMemberInfo member,
        NoireRemoteParameterInfo parameter,
        JToken value,
        HttpCallContext? call,
        out object? converted)
    {
        converted = null;

        try
        {
            converted = NoireRemoteJson.FromToken(value, parameter.ClrType);

            if (converted is NoireRemoteFile file && call?.ResolveFile != null)
                return call.ResolveFile(file);

            return null;
        }
        catch (Exception exception)
        {
            var received = value.ToString(Newtonsoft.Json.Formatting.None);

            if (received.Length > 200)
                received = received.Substring(0, 200) + "...";

            return new HttpFailure(400, NoireRemoteErrorCodes.ArgumentInvalid,
                "'" + member.Route + "' wants a " + parameter.JsonType + " for '" + parameter.Name + "' and received " + received + ". " + exception.Message,
                parameter.Name);
        }
    }

    private static NoireRemoteParameterInfo? Find(NoireRemoteMemberInfo member, string name)
    {
        foreach (var parameter in member.Parameters)
        {
            if (!parameter.IsCancellationToken && !parameter.IsProgress && string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase))
                return parameter;
        }

        return null;
    }

    public static IReadOnlyList<string> NearNames(IReadOnlyList<string> candidates, string wanted)
    {
        var near = new List<string>();

        foreach (var candidate in candidates)
        {
            if (candidate.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)
                || wanted.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
                || candidate.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0
                || wanted.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                near.Add(candidate);
        }

        if (near.Count == 0)
            return candidates.Count <= 12 ? candidates : [];

        return near;
    }
}
