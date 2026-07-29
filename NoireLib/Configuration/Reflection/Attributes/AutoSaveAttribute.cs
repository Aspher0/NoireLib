using System;

namespace NoireLib.Configuration;

/// <summary>
/// Marks a property to save the configuration when its value changes, or a method to save it after it runs.
/// Only applies to classes that inherit from NoireConfigBase, and the member must be virtual to be intercepted.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AutoSaveAttribute : Attribute
{
}
