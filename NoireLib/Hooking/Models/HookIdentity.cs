using System;

namespace NoireLib.Hooking;

/// <summary>
/// What XIVClientStructs declares about the function at an address.
/// </summary>
/// <param name="Name">The function in <c>Client::Game::GameMain.ExecuteCommand</c> form.</param>
/// <param name="DeclaringType">The XIVClientStructs type declaring the function, or null when only its name is known.</param>
/// <param name="FunctionName">The function name on its own.</param>
/// <param name="ExpectedDelegateType">The delegate XIVClientStructs declares for the function, when it declares one.</param>
/// <param name="Address">The resolved address.</param>
public sealed record HookIdentity(
    string Name,
    Type? DeclaringType,
    string FunctionName,
    Type? ExpectedDelegateType,
    nint Address)
{
    /// <summary>
    /// Gets the address written relative to its module, as <c>ffxiv_dx11.exe+0xB30F70</c>.
    /// </summary>
    public string ModuleRelativeAddress { get; init; } = $"0x{Address:X}";

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({ModuleRelativeAddress})";
}
