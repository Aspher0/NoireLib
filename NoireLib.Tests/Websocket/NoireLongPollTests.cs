using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks the long-polling loop: the cursor, the waits and backoffs, and the refusal that ends the run.</summary>
public sealed class NoireLongPollTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task RunAsync_IssuesTheFirstPollWithNoCursor()
    {
        var seen = new List<string?>();
        using var stop = new CancellationTokenSource();

        var loop = Build((cursor, _) =>
        {
            seen.Add(cursor);
            stop.Cancel();
            return Task.FromResult(Answer(200, "{\"cursor\":\"a\"}", cursor));
        });

        await loop.RunAsync(stop.Token);

        seen.Should().HaveCount(1);
        seen[0].Should().BeNull("the server is asked for whatever it has when nothing has been read yet");
    }

    [Fact]
    public async Task RunAsync_CarriesTheCursorFromEachAnswerIntoTheNextPoll()
    {
        var answers = new Queue<string>();
        answers.Enqueue("{\"cursor\":\"a\"}");
        answers.Enqueue("{\"cursor\":\"b\"}");
        answers.Enqueue("{\"cursor\":\"c\"}");

        var seen = new List<string?>();
        using var stop = new CancellationTokenSource();

        var loop = Build((cursor, _) =>
        {
            seen.Add(cursor);

            if (answers.Count == 0)
            {
                stop.Cancel();
                return Task.FromResult(Answer(200, "{}", cursor));
            }

            return Task.FromResult(Answer(200, answers.Dequeue(), cursor));
        });

        await loop.RunAsync(stop.Token);

        seen.Should().Equal(new string?[] { null, "a", "b", "c" });
        loop.Cursor.Should().Be("c");
    }

    [Fact]
    public async Task RunAsync_LeavesTheFailureScheduleWhereItIs_WhenAnAnswerIsEmpty()
    {
        var waits = new List<TimeSpan>();
        var step = 0;
        using var stop = new CancellationTokenSource();

        var loop = Build(
            (cursor, _) =>
            {
                step++;

                switch (step)
                {
                    case 1:
                    case 3:
                        return Task.FromException<NoireLongPollResponse>(new IOException("the server is down"));

                    case 2:
                        return Task.FromResult(Answer(200, string.Empty, cursor));

                    default:
                        stop.Cancel();
                        return Task.FromResult(Answer(200, "{}", cursor));
                }
            },
            retry: NoireRetryPolicy.Default with { Jitter = 0 },
            emptyPollDelay: TimeSpan.FromSeconds(30),
            wait: (delay, _) =>
            {
                waits.Add(delay);
                return Task.CompletedTask;
            });

        await loop.RunAsync(stop.Token);

        waits.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task RunAsync_StopsOnARefusalTheServerWillRepeat(int status)
    {
        var polls = 0;
        var failures = new List<Exception>();
        var reached = new List<NoireSocketState>();
        using var stop = new CancellationTokenSource();

        var loop = Build(
            (cursor, _) =>
            {
                polls++;

                if (polls > 4)
                    stop.Cancel();

                return Task.FromResult(Answer(status, "refused", cursor));
            },
            retry: NoireRetryPolicy.Default with { Jitter = 0 },
            fail: failures.Add,
            setState: reached.Add);

        await loop.RunAsync(stop.Token);

        polls.Should().Be(1);
        reached.Should().Contain(NoireSocketState.Faulted);
        failures.Should().ContainSingle().Which.Should().BeOfType<NoireSocketConnectException>();
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task RunAsync_RetriesAStatusTheServerAsksToBeRetried(int status)
    {
        var polls = 0;
        var reached = new List<NoireSocketState>();

        var loop = Build(
            (cursor, _) =>
            {
                polls++;
                return Task.FromResult(Answer(status, "not now", cursor));
            },
            retry: NoireRetryPolicy.Default with { Jitter = 0, MaxAttempts = 2 },
            setState: reached.Add);

        await loop.RunAsync(CancellationToken.None);

        polls.Should().Be(3, "two retries are allowed, and the third refusal spends the last attempt");
        reached.Should().Contain(NoireSocketState.Reconnecting).And.Contain(NoireSocketState.Faulted);
    }

    [Fact]
    public async Task RunAsync_DeliversEveryAnswerIncludingAnEmptyOne()
    {
        var delivered = new List<NoireLongPollResponse>();
        var step = 0;
        using var stop = new CancellationTokenSource();

        var loop = Build(
            (cursor, _) =>
            {
                step++;

                if (step >= 2)
                    stop.Cancel();

                return Task.FromResult(Answer(step == 1 ? 204 : 200, step == 1 ? string.Empty : "{}", cursor));
            },
            setState: _ => { },
            deliver: delivered.Add);

        await loop.RunAsync(stop.Token);

        delivered.Should().HaveCount(2);
        delivered[0].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ConnectAsync_RefusesABodyWithoutAPost()
    {
        using var client = new NoireLongPollClient("http://127.0.0.1:1/poll", new NoireLongPollOptions { Body = new { id = 1 } });

        var act = () => { _ = client.ConnectAsync(TestContext.Current.CancellationToken); };

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Body*")
            .WithMessage("*Method*")
            .WithMessage("*Post*");
    }

    [Fact]
    public void PollOnceAsync_RefusesABodyWithoutAPost()
    {
        using var client = new NoireLongPollClient("http://127.0.0.1:1/poll", new NoireLongPollOptions { Body = new { id = 1 } });

        var act = () => { _ = client.PollOnceAsync(TestContext.Current.CancellationToken); };

        act.Should().Throw<ArgumentException>();
    }

    private static LongPollLoop Build(
        Func<string?, CancellationToken, Task<NoireLongPollResponse>> poll,
        NoireRetryPolicy? retry = null,
        TimeSpan? emptyPollDelay = null,
        string? initialCursor = null,
        Action<NoireLongPollResponse>? deliver = null,
        Action<Exception>? fail = null,
        Action<NoireSocketState>? setState = null,
        Func<TimeSpan, CancellationToken, Task>? wait = null)
        => new(
            retry ?? NoireRetryPolicy.None,
            emptyPollDelay ?? TimeSpan.Zero,
            initialCursor,
            poll,
            ReadCursorField,
            deliver ?? (_ => { }),
            fail ?? (_ => { }),
            setState ?? (_ => { }),
            wait ?? ((_, _) => Task.CompletedTask));

    private static string? ReadCursorField(NoireLongPollResponse response)
        => string.IsNullOrWhiteSpace(response.Body) ? null : JToken.Parse(response.Body).SelectToken("cursor")?.ToString();

    private static NoireLongPollResponse Answer(int status, string body, string? cursor)
        => new(status, NoHeaders, body, cursor);
}
