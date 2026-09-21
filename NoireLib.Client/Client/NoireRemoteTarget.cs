using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NoireLib.Remote;

/// <summary>
/// Which instance a call is aimed at. A target is resolved at call time. A caller survives the game restarting
/// on another port.
/// </summary>
public readonly struct NoireRemoteTarget
{
    private readonly string? key;
    private readonly string? value;
    private readonly int processId;
    private readonly bool all;

    private NoireRemoteTarget(string? key, string? value, int processId, bool all)
    {
        this.key = key;
        this.value = value;
        this.processId = processId;
        this.all = all;
    }

    /// <summary>Gets the target taking whichever instance answers: the most recently started when several publish the surface.</summary>
    public static NoireRemoteTarget Any { get; } = new(null, null, 0, false);

    /// <summary>
    /// Gets the target that reaches every matching instance.
    /// </summary>
    public static NoireRemoteTarget All { get; } = new(null, null, 0, true);

    /// <summary>
    /// Aims at a character name, a character and world as <c>Name @ World</c>, or a plugin name.
    /// </summary>
    /// <param name="identity">The text to match.</param>
    /// <returns>The target.</returns>
    public static NoireRemoteTarget Identity(string identity) => new(null, identity, 0, false);

    /// <summary>
    /// Aims at a tag the instance set on itself with <see cref="NoireRemoteSelf.Set"/>.
    /// </summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The value it has to hold.</param>
    /// <returns>The target.</returns>
    public static NoireRemoteTarget Meta(string key, string value) => new(key, value, 0, false);

    /// <summary>Aims at a process id, the only handle before a character logs in and before any tag is set.</summary>
    /// <param name="processId">The process id.</param>
    /// <returns>The target.</returns>
    public static NoireRemoteTarget Process(int processId) => new(null, null, processId, false);

    /// <summary>
    /// Aims at every instance carrying a tag.
    /// </summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The value it has to hold.</param>
    /// <returns>The target.</returns>
    public static NoireRemoteTarget EveryMeta(string key, string value) => new(key, value, 0, true);

    /// <summary>
    /// Gets whether the target reaches every match.
    /// </summary>
    public bool IsAll => all;

    /// <summary>
    /// Gets whether the target names nothing. Any instance answers.
    /// </summary>
    public bool IsAny => key == null && value == null && processId == 0;

    /// <summary>
    /// Says whether an instance matches.
    /// </summary>
    /// <param name="instance">The instance to test.</param>
    /// <returns>True when the instance is one this target names.</returns>
    public bool Matches(NoireRemoteInstance instance)
    {
        if (instance == null)
            return false;

        if (key != null)
            return string.Equals(instance[key], value, StringComparison.Ordinal);

        if (processId != 0)
            return instance.ProcessId == processId;

        if (value == null)
            return true;

        // "Name @ World". The whole label, the character name and the plugin name all match.
        var separator = instance.Label.IndexOf(" @ ", StringComparison.Ordinal);
        var character = separator > 0 ? instance.Label.Substring(0, separator) : instance.Label;

        return string.Equals(instance.Label, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(character, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.Plugin, value, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (key != null)
            return (all ? "every instance with " : "") + key + "=" + value;

        if (processId != 0)
            return "process " + processId;

        if (value != null)
            return "'" + value + "'";

        return all ? "every instance" : "any instance";
    }

    // Names what is online.
    internal string Describe(IReadOnlyList<NoireRemoteInstance> online)
    {
        var text = new StringBuilder();
        text.Append("No live instance matches ").Append(ToString()).Append('.');

        if (online.Count == 0)
        {
            text.Append(" Nothing is publishing this surface.");
            return text.ToString();
        }

        text.Append(" Online: ");
        text.Append(string.Join(", ", online.Select(Summarise)));

        return text.ToString();
    }

    private static string Summarise(NoireRemoteInstance instance)
    {
        var label = string.IsNullOrEmpty(instance.Label) ? "pid " + instance.ProcessId : instance.Label;

        if (instance.Metadata.Count == 0)
            return label + " (no tags)";

        return label + " (" + string.Join(", ", instance.Metadata.Select(tag => tag.Key + "=" + tag.Value)) + ")";
    }
}
