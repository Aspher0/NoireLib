namespace NoireLib.Hooking;

/// <summary>The outcome of comparing a hook delegate against the function at an address.</summary>
public enum HookVerificationStatus
{
    /// <summary>The delegate matches the function XIVClientStructs declares at that address.</summary>
    Matched,

    /// <summary>The delegate differs from the function XIVClientStructs declares at that address.</summary>
    Mismatched,

    /// <summary>XIVClientStructs declares no function at that address, so nothing could be compared.</summary>
    Unverifiable,

    /// <summary>Verification was not run.</summary>
    Skipped,
}
