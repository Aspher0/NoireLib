using System;

namespace NoireLib.Configuration;

/// <summary>A <see cref="NoireSetting{TValue}"/> seen without its value type.</summary>
public interface INoireSetting
{
    /// <summary>The property name.</summary>
    string Name { get; }

    /// <summary>Whether the value differs from the default.</summary>
    bool IsModified { get; }

    /// <summary>
    /// The value, boxed. Setting it applies the rules and saves.
    /// </summary>
    object? Boxed { get; set; }

    /// <summary>The property type.</summary>
    Type ValueType { get; }

    /// <summary>Sets the value back to the default.</summary>
    void Reset();
}
