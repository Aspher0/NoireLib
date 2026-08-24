using System;

namespace NoireLib.Configuration;

/// <summary>
/// Sets the configuration to be automatically saved.<br/>
/// On a class: The entire configuration is saved when any property changes.<br/>
/// On a property: The configuration is saved when the property changes.<br/>
/// On a method: The configuration is saved after the method is called.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AutoSaveAttribute : Attribute
{
}
