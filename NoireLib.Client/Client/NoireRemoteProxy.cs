using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// The proxy behind <see cref="NoireRemoteApi.As{TInterface}"/>. It is public because
/// <see cref="DispatchProxy.Create{T, TProxy}"/> needs a public type. Construct it through the endpoint instead.
/// </summary>
public class NoireRemoteProxy : DispatchProxy
{
    private NoireRemoteApi endpoint = null!;

    /// <summary>
    /// Builds a typed client over an endpoint and checks the interface against the listener's manifest.
    /// </summary>
    /// <typeparam name="TInterface">The interface describing the endpoint's members.</typeparam>
    /// <param name="endpoint">The endpoint the calls go to.</param>
    /// <returns>A proxy implementing the interface.</returns>
    /// <exception cref="NoireRemoteContractException">If the interface disagrees with the published surface.</exception>
    public static TInterface Create<TInterface>(NoireRemoteApi endpoint) where TInterface : class
    {
        if (!typeof(TInterface).IsInterface)
            throw new ArgumentException("A typed client is built from an interface.", nameof(TInterface));

        CheckContract(typeof(TInterface), endpoint);

        var proxy = DispatchProxy.Create<TInterface, NoireRemoteProxy>();
        ((NoireRemoteProxy)(object)proxy).endpoint = endpoint;

        return proxy;
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null)
            throw new NoireRemoteException("The proxy was invoked without a method.");

        var parameters = targetMethod.GetParameters();
        var arguments = new Dictionary<string, object?>(parameters.Length, StringComparer.OrdinalIgnoreCase);
        var cancellationToken = CancellationToken.None;

        for (var index = 0; index < parameters.Length; index++)
        {
            var value = args != null && index < args.Length ? args[index] : null;

            if (parameters[index].ParameterType == typeof(CancellationToken))
            {
                cancellationToken = value as CancellationToken? ?? CancellationToken.None;
                continue;
            }

            arguments[parameters[index].Name ?? ("arg" + index)] = value;
        }

        var member = ResolveName(targetMethod);
        var returnType = targetMethod.ReturnType;

        if (returnType == typeof(Task))
            return endpoint.CallAsync(member, arguments, cancellationToken);

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var call = CallAsyncMethod.MakeGenericMethod(resultType);
            return call.Invoke(endpoint, [member, arguments, cancellationToken]);
        }

        if (returnType == typeof(void))
        {
            endpoint.CallAsync(member, arguments, cancellationToken).GetAwaiter().GetResult();
            return null;
        }

        var synchronous = CallAsyncMethod.MakeGenericMethod(returnType);
        var task = (Task)synchronous.Invoke(endpoint, [member, arguments, cancellationToken])!;
        task.GetAwaiter().GetResult();

        return task.GetType().GetProperty(nameof(Task<int>.Result))!.GetValue(task);
    }

    private static readonly MethodInfo CallAsyncMethod = ResolveCallAsync();

    private static MethodInfo ResolveCallAsync()
    {
        foreach (var method in typeof(NoireRemoteApi).GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (method.Name != nameof(NoireRemoteApi.CallAsync) || !method.IsGenericMethodDefinition)
                continue;

            return method;
        }

        throw new InvalidOperationException("The generic CallAsync overload is missing.");
    }

    private static string ResolveName(MethodInfo method)
    {
        var attribute = method.GetCustomAttribute<NoireRemoteAttribute>();
        return string.IsNullOrWhiteSpace(attribute?.Name) ? method.Name : attribute!.Name!;
    }

    private static void CheckContract(Type contract, NoireRemoteApi endpoint)
    {
        NoireRemoteManifest manifest;

        try
        {
            manifest = endpoint.ManifestAsync().GetAwaiter().GetResult();
        }
        catch (NoireRemoteNotFoundException)
        {
            // The manifest route can be off. A disagreement then surfaces at call time.
            return;
        }

        var published = manifest.GetEndpoint(endpoint.Name);

        if (published == null)
            throw new NoireRemoteContractException("The instance does not publish an endpoint named '" + endpoint.Name + "'.");

        foreach (var method in contract.GetMethods())
        {
            var name = ResolveName(method);
            var member = published.GetMember(name);

            if (member == null)
                throw new NoireRemoteContractException("Endpoint '" + endpoint.Name + "' publishes no member named '" + name + "'.");

            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(CancellationToken))
                    continue;

                var found = false;

                foreach (var candidate in member.Parameters)
                {
                    if (string.Equals(candidate.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    throw new NoireRemoteContractException(
                        "Member '" + endpoint.Name + "/" + name + "' takes no parameter named '" + parameter.Name + "'.");
            }
        }
    }
}
