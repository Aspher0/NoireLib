using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// A member started in job mode. The listener runs it past the connection that started it, and this handle polls or
/// cancels it.
/// </summary>
public sealed class NoireRemoteJob
{
    private readonly NoireRemoteInstance instance;
    private NoireRemoteEnvelope? finished;

    internal NoireRemoteJob(NoireRemoteInstance instance, NoireRemoteJobStatus status)
    {
        this.instance = instance;
        Status = status;
    }

    /// <summary>
    /// Gets the job id.
    /// </summary>
    public string Id => Status.Id;

    /// <summary>
    /// Gets the state as of the last poll.
    /// </summary>
    public NoireRemoteJobState State => Status.State;

    /// <summary>
    /// Gets how far along the member reported it is, between zero and one, or null when it reports nothing.
    /// </summary>
    public double? Progress => Status.Progress;

    /// <summary>
    /// Gets the status as of the last poll.
    /// </summary>
    public NoireRemoteJobStatus Status { get; private set; }

    /// <summary>
    /// Polls the listener once and updates the state.
    /// </summary>
    /// <param name="cancellationToken">Cancels the poll.</param>
    /// <returns>A task that completes when the poll has answered.</returns>
    /// <exception cref="NoireRemoteNotFoundException">If the job id is unknown or its retention has passed.</exception>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await RemoteWireTransport.SendEnvelopeAsync(
            instance,
            HttpMethod.Get,
            NoireRemotePaths.Job(Id),
            null,
            NoireRemoteClient.Options.CallTimeout,
            cancellationToken).ConfigureAwait(false);

        if (envelope.Job != null)
            Status = envelope.Job;

        if (Status.State != NoireRemoteJobState.Running)
            finished = envelope;
    }

    // Polling never happens after a pushed answer.
    internal void Complete(NoireRemoteEnvelope envelope)
    {
        if (envelope.Job != null)
            Status = envelope.Job;

        finished = envelope;
        pushed.TrySetResult(envelope);
    }

    private readonly System.Threading.Tasks.TaskCompletionSource<NoireRemoteEnvelope> pushed
        = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool IsPushed { get; set; }

    /// <summary>
    /// Polls until the job finishes and returns its result.
    /// </summary>
    /// <typeparam name="TResult">The type the member's return value converts into.</typeparam>
    /// <param name="cancellationToken">Stops polling. Use <see cref="CancelAsync"/> to cancel the job itself.</param>
    /// <returns>The converted result.</returns>
    /// <exception cref="NoireRemoteRemoteException">If the member threw.</exception>
    /// <exception cref="NoireRemoteException">If the job was cancelled.</exception>
    public async Task<TResult> ResultAsync<TResult>(CancellationToken cancellationToken = default)
    {
        if (IsPushed)
        {
            var answer = await pushed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            return Finish<TResult>(answer);
        }

        while (true)
        {
            if (finished != null)
                return Finish<TResult>(finished);

            var wait = Status.PollAfterMs is > 0 ? Status.PollAfterMs!.Value : 250;
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits for the job to finish, discarding its result.
    /// </summary>
    /// <param name="cancellationToken">Stops polling.</param>
    /// <returns>A task that completes when the job finishes.</returns>
    public async Task WaitAsync(CancellationToken cancellationToken = default)
        => await ResultAsync<object>(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Cancels the token the member was handed. A member that never reads it keeps running.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request. The job itself keeps running.</param>
    /// <returns>A task that completes when the listener has acknowledged.</returns>
    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await RemoteWireTransport.SendEnvelopeAsync(
            instance,
            HttpMethod.Delete,
            NoireRemotePaths.Job(Id),
            null,
            NoireRemoteClient.Options.CallTimeout,
            cancellationToken).ConfigureAwait(false);

        if (envelope.Job != null)
            Status = envelope.Job;
    }

    private TResult Finish<TResult>(NoireRemoteEnvelope envelope)
    {
        if (Status.State == NoireRemoteJobState.Cancelled)
            throw new NoireRemoteException("Job " + Id + " was cancelled.");

        if (Status.State == NoireRemoteJobState.Failed)
            throw RemoteWireTransport.ToException(new NoireRemoteEnvelope { Ok = false, Error = envelope.Error }, instance);

        return envelope.ResultAs<TResult>()!;
    }
}
