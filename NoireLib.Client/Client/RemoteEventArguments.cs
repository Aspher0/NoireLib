using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace NoireLib.Remote;

// A single argument crosses as the value. Several cross as an object keyed by the delegate's parameter names.
internal static class RemoteEventArguments
{
    internal static object?[] Unpack(Type handlerType, JToken? payload)
    {
        var invoke = handlerType.GetMethod("Invoke");

        if (invoke == null)
            return [];

        var parameters = invoke.GetParameters();

        if (parameters.Length == 0)
            return [];

        if (parameters.Length == 1)
            return [Convert(payload, parameters[0].ParameterType)];

        // An EventHandler's sender never crosses the wire.
        if (handlerType == typeof(EventHandler)
            || (handlerType.IsGenericType && handlerType.GetGenericTypeDefinition() == typeof(EventHandler<>)))
        {
            return [null, Convert(payload, parameters[1].ParameterType)];
        }

        var values = new object?[parameters.Length];

        for (var index = 0; index < parameters.Length; index++)
        {
            var name = parameters[index].Name ?? ("arg" + index);
            var token = payload is JObject document ? document[name] : null;

            values[index] = Convert(token, parameters[index].ParameterType);
        }

        return values;
    }

    private static object? Convert(JToken? token, Type type)
    {
        if (token == null || token.Type == JTokenType.Null)
            return type.IsValueType && Nullable.GetUnderlyingType(type) == null ? Activator.CreateInstance(type) : null;

        return token.ToObject(type);
    }
}
