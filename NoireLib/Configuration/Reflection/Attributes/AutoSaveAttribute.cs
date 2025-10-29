using System;

namespace NoireLib.Configuration;

/// <summary>
/// Marks a property to automatically save the configuration when its value changes.
/// Only works on properties of classes that inherit from NoireConfigBase.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AutoSaveAttribute : Attribute
{
}
