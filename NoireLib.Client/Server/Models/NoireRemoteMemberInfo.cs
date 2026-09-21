using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// One published member: its route, how it runs and what it takes.
/// </summary>
public sealed class NoireRemoteMemberInfo
{
    private long callCount;

    internal NoireRemoteMemberInfo(
        string endpoint,
        string name,
        Func<object?[], CancellationToken, Task<object?>> invoker,
        IReadOnlyList<NoireRemoteParameterInfo> parameters,
        Type returnClrType,
        string returnJsonType,
        NoireRemoteThread thread,
        NoireRemoteReadiness requires,
        NoireRemoteCallMode mode,
        TimeSpan timeout,
        NoireRemoteAccess access)
    {
        Endpoint = endpoint;
        Name = name;
        Invoker = invoker;
        Parameters = parameters;
        ReturnClrType = returnClrType;
        ReturnJsonType = returnJsonType;
        Thread = thread;
        Requires = requires;
        Mode = mode;
        Timeout = timeout;
        Access = access;
    }

    /// <summary>
    /// Gets the endpoint this member belongs to.
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// Gets the member name on the wire.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the route, endpoint and member joined by a slash.
    /// </summary>
    public string Route => Endpoint + "/" + Name;

    /// <summary>
    /// Gets the thread the member runs on.
    /// </summary>
    public NoireRemoteThread Thread { get; }

    /// <summary>
    /// Gets the readiness gate the member passes before it runs.
    /// </summary>
    public NoireRemoteReadiness Requires { get; }

    /// <summary>
    /// Gets whether a call answers with the result or with a job to poll.
    /// </summary>
    public NoireRemoteCallMode Mode { get; }

    /// <summary>
    /// Gets the deadline a call gets when it asks for none.
    /// </summary>
    public TimeSpan Timeout { get; }

    /// <summary>
    /// Gets whether the member is reachable from another machine.
    /// </summary>
    public NoireRemoteAccess Access { get; }

    /// <summary>
    /// Gets the parameters, in declaration order.
    /// </summary>
    public IReadOnlyList<NoireRemoteParameterInfo> Parameters { get; }

    /// <summary>
    /// Gets the return type, or <see cref="void"/> when the member returns nothing.
    /// </summary>
    public Type ReturnClrType { get; }

    /// <summary>
    /// Gets the JSON type the manifest reports for the return value.
    /// </summary>
    public string ReturnJsonType { get; }

    /// <summary>
    /// Gets how many times the member has been called since it was published.
    /// </summary>
    public long CallCount => Interlocked.Read(ref callCount);

    /// <summary>
    /// Gets when the member was last called, or null when it never has been.
    /// </summary>
    public DateTimeOffset? LastCalledUtc { get; private set; }

    /// <summary>
    /// Gets the last exception the member threw, or null when it never has.
    /// </summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Gets what the member does, taken from <see cref="NoireRemoteAttribute"/>.
    /// </summary>
    public string? Summary { get; internal set; }

    /// <summary>
    /// Gets whether a console asks before firing the member.
    /// </summary>
    public bool Confirm { get; internal set; }

    /// <summary>
    /// Gets the group a console lists the member under, or null when it names none.
    /// </summary>
    public string? Group { get; internal set; }

    /// <summary>
    /// Gets where the member sits in its group.
    /// </summary>
    public int Order { get; internal set; }

    /// <summary>
    /// Gets whether the member is on its way out.
    /// </summary>
    public bool Deprecated { get; internal set; }

    /// <summary>
    /// Gets the payload type of the member's progress reports, or null when it takes no reporter.
    /// </summary>
    public Type? ProgressClrType { get; internal set; }

    /// <summary>
    /// Gets the wires the member is reachable on, already resolved: the member's own setting, then the class's,
    /// then <see cref="NoireRemoteTransport.All"/>.
    /// </summary>
    public NoireRemoteTransport Transports { get; internal set; } = NoireRemoteTransport.All;

    /// <summary>Gets whether the member only reads. True for a published property.</summary>
    public bool ReadOnly { get; internal set; }

    internal System.Reflection.MethodInfo? Source { get; set; }

    internal Func<object?[], CancellationToken, Task<object?>> Invoker { get; }

    internal void RecordCall(Exception? error)
    {
        Interlocked.Increment(ref callCount);
        LastCalledUtc = DateTimeOffset.UtcNow;

        if (error != null)
            LastError = error;
    }
}
