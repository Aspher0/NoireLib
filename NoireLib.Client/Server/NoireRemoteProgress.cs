using NoireLib.Remote.Internal;

namespace NoireLib.Remote;

/// <summary>
/// How a member running as a job says how far along it is.
/// </summary>
public static class NoireRemoteProgress
{
    /// <summary>
    /// Reports how far along the member running this job is. It does nothing outside a job.
    /// </summary>
    /// <param name="progress">A value between zero and one. It is clamped.</param>
    public static void Report(double progress)
        => RemoteJobStore.ReportProgress(progress);

    /// <summary>
    /// Gets whether the calling code is running inside a job.
    /// </summary>
    public static bool IsInsideJob => RemoteJobStore.IsInsideJob;
}
