using System;

namespace NoireLib.Hooking;

/// <summary>
/// The outcome of comparing a hook delegate against the function at an address.
/// </summary>
/// <param name="Status">Whether the delegate matched, differed, or could not be checked.</param>
/// <param name="DelegateType">The delegate that was checked.</param>
/// <param name="Identity">The XIVClientStructs declaration at the address, when one exists.</param>
/// <param name="PassedDelegate">The checked delegate rendered as a signature.</param>
/// <param name="ExpectedDelegate">The declared function rendered as a signature.</param>
/// <param name="Difference">The first element that differed.</param>
public sealed record HookVerificationResult(
    HookVerificationStatus Status,
    Type DelegateType,
    HookIdentity? Identity,
    string PassedDelegate,
    string? ExpectedDelegate,
    string? Difference)
{
    /// <summary>
    /// Gets a value indicating whether the delegate differs from the declared function.
    /// </summary>
    public bool IsMismatch => Status == HookVerificationStatus.Mismatched;

    /// <summary>
    /// Creates a result for an address no XIVClientStructs function claims.
    /// </summary>
    /// <param name="delegateType">The delegate that was checked.</param>
    /// <param name="passedDelegate">The checked delegate rendered as a signature.</param>
    /// <returns>The result.</returns>
    public static HookVerificationResult Unverifiable(Type delegateType, string passedDelegate)
        => new(HookVerificationStatus.Unverifiable, delegateType, null, passedDelegate, null, null);

    /// <summary>
    /// Creates a result for a check that was not run.
    /// </summary>
    /// <param name="delegateType">The delegate that was not checked.</param>
    /// <returns>The result.</returns>
    public static HookVerificationResult Skipped(Type delegateType)
        => new(HookVerificationStatus.Skipped, delegateType, null, string.Empty, null, null);

    /// <summary>
    /// Returns a multi-line report naming the function, both signatures, and the first difference.
    /// </summary>
    /// <returns>The report.</returns>
    public string Describe() => Status switch
    {
        HookVerificationStatus.Mismatched =>
            $"Hooked function:   {Identity?.Name ?? "unknown"}{Environment.NewLine}" +
            $"Address:           {Identity?.ModuleRelativeAddress ?? "unknown"}{Environment.NewLine}" +
            $"Passed delegate:   {PassedDelegate}{Environment.NewLine}" +
            $"Expected delegate: {ExpectedDelegate}{Environment.NewLine}" +
            $"First difference:  {Difference}",
        HookVerificationStatus.Matched => $"{Identity?.Name ?? "unknown"} matches {PassedDelegate}.",
        HookVerificationStatus.Unverifiable => $"No XIVClientStructs function is declared at this address, so {PassedDelegate} could not be checked.",
        _ => "Verification was not run.",
    };

    /// <inheritdoc/>
    public override string ToString() => Describe();
}
