using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// Calls one member of someone else's published surface, in the shape of a delegate. The NoireRemote counterpart
/// of <c>NoireIpcConsumer</c>. A property of this type in a class carrying <see cref="NoireRemoteClassAttribute"/>
/// is filled when the type is bound.
/// </summary>
/// <typeparam name="TDelegate">The signature the member is called through.</typeparam>
public sealed class NoireRemoteConsumer<TDelegate> where TDelegate : Delegate
{
    private readonly NoireRemoteClient client;
    private readonly NoireRemoteTarget? target;
    private readonly NoireRemoteTransport transport;
    private readonly TimeSpan? wait;

    internal NoireRemoteConsumer(NoireRemoteClient client, string member)
        : this(client, member, null, NoireRemoteTransport.Inherit, null)
    {
    }

    private NoireRemoteConsumer(NoireRemoteClient client, string member, NoireRemoteTarget? target, NoireRemoteTransport transport, TimeSpan? wait)
    {
        this.client = client;
        this.target = target;
        this.transport = transport;
        this.wait = wait;

        Member = member;
        Invoke = Build();
    }

    /// <summary>
    /// Gets the surface the member belongs to.
    /// </summary>
    public string Api => client.Api;

    /// <summary>
    /// Gets the member name on the wire.
    /// </summary>
    public string Member { get; }

    /// <summary>
    /// Gets the delegate the member is called through.
    /// </summary>
    public TDelegate Invoke { get; }

    /// <summary>
    /// Gets the last failure this consumer saw, or null when nothing has failed.
    /// </summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Gets whether an instance publishing the surface is online. Reading it reads the discovery folder.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                client.Resolve(target ?? NoireRemoteTarget.Any, TimeSpan.Zero);
                return true;
            }
            catch (NoireRemoteException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Aims this consumer at a character name, a character and world, or a plugin name.
    /// </summary>
    /// <param name="identity">The text to match.</param>
    /// <returns>A consumer calling the same member on that instance.</returns>
    public NoireRemoteConsumer<TDelegate> On(string identity)
        => new(client, Member, NoireRemoteTarget.Identity(identity), transport, wait);

    /// <summary>
    /// Aims this consumer at a tag the instance set on itself.
    /// </summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The value it has to hold.</param>
    /// <returns>A consumer calling the same member on the matching instance.</returns>
    public NoireRemoteConsumer<TDelegate> On(string key, string value)
        => new(client, Member, NoireRemoteTarget.Meta(key, value), transport, wait);

    /// <summary>
    /// Aims this consumer at a process id.
    /// </summary>
    /// <param name="processId">The process id.</param>
    /// <returns>A consumer calling the same member on that process.</returns>
    public NoireRemoteConsumer<TDelegate> On(int processId)
        => new(client, Member, NoireRemoteTarget.Process(processId), transport, wait);

    /// <summary>
    /// Takes the call over a given wire.
    /// </summary>
    /// <param name="over">The wire to use.</param>
    /// <returns>A consumer calling the same member on that wire.</returns>
    public NoireRemoteConsumer<TDelegate> Over(NoireRemoteTransport over)
        => new(client, Member, target, over, wait);

    /// <summary>
    /// Waits for an absent target to appear before failing.
    /// </summary>
    /// <param name="window">How long to wait.</param>
    /// <returns>A consumer that waits.</returns>
    public NoireRemoteConsumer<TDelegate> WaitUpTo(TimeSpan window)
        => new(client, Member, target, transport, window);

    /// <summary>
    /// Calls the member on every instance publishing the surface.
    /// </summary>
    /// <returns>A view that answers once per instance.</returns>
    public NoireRemoteView OnAll() => client.OnAll();

    private NoireRemoteView View()
    {
        var view = new NoireRemoteView(client, target, transport, wait);

        return view;
    }

    // Keyed by the delegate's own parameter names.
    private TDelegate Build()
    {
        var invoke = typeof(TDelegate).GetMethod("Invoke")!;
        var parameters = invoke.GetParameters();
        var arguments = parameters.Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();

        var names = Expression.Constant(parameters.Select(parameter => parameter.Name ?? "arg").ToArray());
        var values = Expression.NewArrayInit(
            typeof(object),
            arguments.Select(argument => Expression.Convert(argument, typeof(object))));

        var self = Expression.Constant(this);
        var returnType = invoke.ReturnType;

        MethodInfo method;
        Expression body;

        if (returnType == typeof(Task))
        {
            method = typeof(NoireRemoteConsumer<TDelegate>).GetMethod(nameof(CallAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;
            body = Expression.Call(self, method, names, values);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            method = typeof(NoireRemoteConsumer<TDelegate>)
                .GetMethod(nameof(CallAsync) + "Of", BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(returnType.GetGenericArguments()[0]);

            body = Expression.Call(self, method, names, values);
        }
        else if (returnType == typeof(void))
        {
            method = typeof(NoireRemoteConsumer<TDelegate>).GetMethod(nameof(Send), BindingFlags.Instance | BindingFlags.NonPublic)!;
            body = Expression.Call(self, method, names, values);
        }
        else
        {
            method = typeof(NoireRemoteConsumer<TDelegate>)
                .GetMethod(nameof(CallSync), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(returnType);

            body = Expression.Call(self, method, names, values);
        }

        return Expression.Lambda<TDelegate>(body, arguments).Compile();
    }

    private Task CallAsync(string[] names, object?[] values)
        => View().InvokeAsync(Member, Pack(names, values));

    private Task<TResult> CallAsyncOf<TResult>(string[] names, object?[] values)
        => View().InvokeAsync<TResult>(Member, Pack(names, values));

    private TResult CallSync<TResult>(string[] names, object?[] values)
    {
        try
        {
            // A synchronous signature blocks the calling thread by the caller's choice.
            return View().InvokeAsync<TResult>(Member, Pack(names, values)).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            LastError = exception;
            throw;
        }
    }

    // A void signature is not awaited. A failure surfaces in LastError only.
    private void Send(string[] names, object?[] values)
    {
        _ = View().InvokeAsync(Member, Pack(names, values)).ContinueWith(
            task => LastError = task.Exception?.GetBaseException(),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // Func<int, Task<int>> names its parameter "arg" whatever the member calls it. Arguments cross positionally. A name only travels when the caller wrote one.
    private static object? Pack(string[] names, object?[] values)
    {
        if (values.Length == 0)
            return null;

        if (names.Any(IsSynthetic))
            return values;

        var arguments = new Dictionary<string, object?>(names.Length, StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < names.Length; index++)
            arguments[names[index]] = values[index];

        return arguments;
    }

    private static bool IsSynthetic(string name)
        => string.IsNullOrEmpty(name) || (name.StartsWith("arg", StringComparison.Ordinal) && (name.Length == 3 || int.TryParse(name.AsSpan(3), out _)));
}
