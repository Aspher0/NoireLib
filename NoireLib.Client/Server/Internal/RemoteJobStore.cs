using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// A synchronous call past its deadline answers and leaves the member running.
internal sealed class RemoteJobStore : IDisposable
{
    private static readonly AsyncLocal<HttpJob?> Current = new();

    private readonly object gate = new();
    private readonly Dictionary<string, HttpJob> jobs = new(StringComparer.Ordinal);
    private readonly TimeSpan retention;

    public RemoteJobStore(TimeSpan retention)
    {
        this.retention = retention <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : retention;
    }

    internal sealed class HttpJob
    {
        public string Id = string.Empty;
        public string Route = string.Empty;
        public NoireRemoteJobState State = NoireRemoteJobState.Running;
        public double? Progress;
        public JToken? Result;
        public NoireRemoteError? Error;
        public CancellationTokenSource? Lifetime;
        public DateTime? FinishedAtUtc;

        // Raised once when the job stops. A socket pushes the answer from it.
        public Action<HttpJob>? Finished;

        public NoireRemoteJobStatus ToStatus()
            => new()
            {
                Id = Id,
                State = State,
                Progress = Progress,
                Route = Route,
                PollAfterMs = State == NoireRemoteJobState.Running ? 250 : null,
            };
    }

    public static void ReportProgress(double value)
    {
        var job = Current.Value;

        if (job == null)
            return;

        job.Progress = Math.Clamp(value, 0, 1);
    }

    public static bool IsInsideJob => Current.Value != null;

    // Read once when arguments are bound. A progress sink flushed after the member returns runs outside the flow.
    public static HttpJob? CurrentJob => Current.Value;

    public HttpJob Start(string route, Func<CancellationToken, Task<object?>> run)
    {
        Sweep();

        var job = new HttpJob
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12),
            Route = route,
            Lifetime = new CancellationTokenSource(),
        };

        lock (gate)
            jobs[job.Id] = job;

        _ = RunAsync(job, run);

        return job;
    }

    private static async Task RunAsync(HttpJob job, Func<CancellationToken, Task<object?>> run)
    {
        Current.Value = job;

        try
        {
            var result = await run(job.Lifetime!.Token).ConfigureAwait(false);
            job.Result = result == null ? null : NoireRemoteJson.ToToken(result);
            job.State = NoireRemoteJobState.Done;
        }
        catch (OperationCanceledException)
        {
            job.State = NoireRemoteJobState.Cancelled;
        }
        catch (HttpNotReadyException exception)
        {
            job.Error = new NoireRemoteError
            {
                Code = exception.Failure.Code,
                Message = exception.Failure.Message,
                RetryAfterSeconds = exception.Failure.RetryAfterSeconds,
            };
            job.State = NoireRemoteJobState.Failed;
        }
        catch (HttpBindingException exception)
        {
            job.Error = new NoireRemoteError
            {
                Code = exception.Failure.Code,
                Message = exception.Failure.Message,
                Detail = exception.Failure.Detail,
            };
            job.State = NoireRemoteJobState.Failed;
        }
        catch (Exception exception)
        {
            job.Error = new NoireRemoteError
            {
                Code = NoireRemoteErrorCodes.HandlerFault,
                Message = exception.Message,
                Detail = exception.GetType().Name,
            };
            job.State = NoireRemoteJobState.Failed;
        }
        finally
        {
            job.FinishedAtUtc = DateTime.UtcNow;
            Current.Value = null;

            try
            {
                job.Finished?.Invoke(job);
            }
            catch (Exception)
            {
                // A caller that went away does not fail a finished job.
            }
        }
    }

    public HttpJob? Get(string id)
    {
        Sweep();

        lock (gate)
            return jobs.TryGetValue(id, out var job) ? job : null;
    }

    public HttpJob? Cancel(string id)
    {
        var job = Get(id);

        if (job == null)
            return null;

        try
        {
            job.Lifetime?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return job;
    }

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        List<string>? expired = null;

        lock (gate)
        {
            foreach (var pair in jobs)
            {
                if (pair.Value.FinishedAtUtc is { } finished && now - finished > retention)
                    (expired ??= []).Add(pair.Key);
            }

            if (expired == null)
                return;

            foreach (var key in expired)
            {
                jobs[key].Lifetime?.Dispose();
                jobs.Remove(key);
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var job in jobs.Values)
            {
                try
                {
                    job.Lifetime?.Cancel();
                    job.Lifetime?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            jobs.Clear();
        }
    }
}
