using System;
using System.Collections.Generic;
using System.Reflection;

namespace NoireLib.Remote;

/// <summary>
/// One parameter of a published member, as the manifest describes it.
/// </summary>
public sealed class NoireRemoteParameterInfo
{
    internal NoireRemoteParameterInfo(string name, Type clrType, string jsonType, bool isRequired, object? defaultValue, IReadOnlyList<string>? enumValues, bool isCancellationToken)
    {
        Name = name;
        ClrType = clrType;
        JsonType = jsonType;
        IsRequired = isRequired;
        DefaultValue = defaultValue;
        EnumValues = enumValues;
        IsCancellationToken = isCancellationToken;
    }

    /// <summary>Gets the parameter name. A caller sends it as the key.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets the declared type.
    /// </summary>
    public Type ClrType { get; }

    /// <summary>
    /// Gets the JSON type the manifest reports.
    /// </summary>
    public string JsonType { get; }

    /// <summary>
    /// Gets whether a call has to supply the parameter.
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>
    /// Gets the value used when a call leaves the parameter out.
    /// </summary>
    public object? DefaultValue { get; }

    /// <summary>
    /// Gets the accepted names when the parameter is an enum, otherwise null.
    /// </summary>
    public IReadOnlyList<string>? EnumValues { get; }

    /// <summary>Gets whether the parameter is the cancellation token. It never appears on the wire.</summary>
    public bool IsCancellationToken { get; }

    /// <summary>Gets whether the parameter is the progress reporter. It never appears on the wire.</summary>
    public bool IsProgress { get; internal set; }

    /// <summary>
    /// Gets what the parameter means, taken from <see cref="NoireRemoteParamAttribute"/>.
    /// </summary>
    public string? Summary { get; internal set; }

    /// <summary>
    /// Gets the smallest accepted value, or not a number when the parameter carries no bound.
    /// </summary>
    public double Minimum { get; internal set; } = double.NaN;

    /// <summary>
    /// Gets the largest accepted value, or not a number when the parameter carries no bound.
    /// </summary>
    public double Maximum { get; internal set; } = double.NaN;

    /// <summary>
    /// Gets the step a console's number field moves by, or not a number when the parameter names none.
    /// </summary>
    public double Step { get; internal set; } = double.NaN;

    /// <summary>
    /// Gets the example value a console offers, or null when the parameter names none.
    /// </summary>
    public string? Example { get; internal set; }

    /// <summary>
    /// Gets the control a console draws for the parameter.
    /// </summary>
    public NoireRemoteControl Control { get; internal set; } = NoireRemoteControl.Auto;

    internal ParameterInfo? Source { get; set; }

    internal Type? ProgressType { get; set; }

    internal Func<HttpProgressContext, object>? ProgressFactory { get; set; }
}

/// <summary>What a progress report carries beside its value.</summary>
/// <param name="Route">The route the member was called on.</param>
/// <param name="Id">The request id. It is also the progress topic's suffix when there is no job.</param>
/// <param name="JobId">The job id, or null when the call is not a job.</param>
/// <param name="Publish">Where a report goes.</param>
/// <param name="SetJobProgress">Moves the job's own progress field, or null when the call is not a job.</param>
public sealed record HttpProgressContext(
    string Route,
    string Id,
    string? JobId,
    Action<string, object?> Publish,
    Action<double>? SetJobProgress);
