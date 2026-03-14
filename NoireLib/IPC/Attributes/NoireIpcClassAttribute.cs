using System;

namespace NoireLib.IPC;

/// <summary>
/// Defines shared IPC metadata for a type whose annotated members are processed through attribute scanning.
/// Static types marked with this attribute are automatically registered during <see cref="NoireLibMain.Initialize(Dalamud.Plugin.IDalamudPluginInterface, Dalamud.Plugin.IDalamudPlugin)"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class NoireIpcClassAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireIpcClassAttribute"/> class.
    /// </summary>
    public NoireIpcClassAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireIpcClassAttribute"/> class with an explicit prefix.
    /// </summary>
    /// <param name="prefix">The prefix to apply to annotated methods when they do not override it individually.</param>
    public NoireIpcClassAttribute(string? prefix)
    {
        Prefix = prefix;
    }

    /// <summary>
    /// Gets or sets the prefix applied to annotated members when they do not define their own prefix.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether default prefix resolution remains enabled for annotated members.
    /// </summary>
    public bool UseDefaultPrefix { get; init; } = true;

    /// <summary>
    /// Gets or sets the default trailing generic type to use for message-oriented annotated members.
    /// </summary>
    public Type? MessageResultType { get; init; }
}
