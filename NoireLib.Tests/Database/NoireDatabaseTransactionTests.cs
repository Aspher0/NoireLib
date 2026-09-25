using FluentAssertions;
using NoireLib.Database;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Calls on one instance are serialized, and a transaction belongs to the thread that began it.</summary>
[SupportedOSPlatform("windows")]
public class NoireDatabaseTransactionTests : IDisposable
{
    #region Helpers

    private readonly string tempDirectory;
    private readonly string databaseName = $"NoireLibTests_{Guid.NewGuid():N}";
    private readonly NoireDatabase db;

    public NoireDatabaseTransactionTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"NoireLib.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        NoireDatabase.SetDatabaseDirectoryOverride(databaseName, tempDirectory);
        db = NoireDatabase.GetInstance(databaseName);
        db.Execute("CREATE TABLE IF NOT EXISTS items (id INTEGER PRIMARY KEY AUTOINCREMENT, body TEXT)");
    }

    public void Dispose()
    {
        // Only this test's database is disposed: NoireDatabase.DisposeAll is process-wide and would tear a concurrently
        // running test class's database out from under it.
        try
        {
            db.Dispose();
        }
        catch
        {
            // Best effort cleanup.
        }

        NoireDatabase.RemoveDatabaseDirectoryOverride(databaseName);

        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp directory left behind is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private static Exception? RunOnOtherThread(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });

        thread.Start();
        thread.Join();
        return caught;
    }

    private long InsertBody(string body) => db.Insert("items", new Dictionary<string, object?> { ["body"] = body });

    #endregion

    [Fact]
    public void AnotherThread_CannotNestIntoOrEndAnOpenTransaction()
    {
        db.BeginTransaction().Should().BeTrue();

        RunOnOtherThread(() => db.BeginTransaction()).Should().BeOfType<InvalidOperationException>();
        RunOnOtherThread(() => db.Commit()).Should().BeOfType<InvalidOperationException>();
        RunOnOtherThread(() => db.Rollback()).Should().BeOfType<InvalidOperationException>();
        RunOnOtherThread(() => db.RollbackAll()).Should().BeOfType<InvalidOperationException>();

        db.GetTransactionLevel().Should().Be(1);
        db.InTransaction().Should().BeTrue();

        db.Rollback().Should().BeTrue();
        db.GetTransactionLevel().Should().Be(0);
    }

    [Fact]
    public void OwningThread_NestsSavepoints_AndReleasesOwnershipOnTheOutermostCommit()
    {
        db.BeginTransaction();
        InsertBody("outer");
        db.BeginTransaction();
        InsertBody("inner");
        db.GetTransactionLevel().Should().Be(2);

        db.Rollback();
        db.GetTransactionLevel().Should().Be(1);

        db.BeginTransaction();
        InsertBody("inner kept");
        db.Commit();
        db.GetTransactionLevel().Should().Be(1);

        db.Commit();

        db.InTransaction().Should().BeFalse();
        db.Count("items").Should().Be(2);
        db.FetchScalar("SELECT COUNT(*) FROM items WHERE body = 'inner'").Should().Be(0L);

        // Released: another thread may now run a transaction of its own.
        RunOnOtherThread(() =>
        {
            db.BeginTransaction();
            InsertBody("other");
            db.Commit();
        }).Should().BeNull();

        db.Count("items").Should().Be(3);
    }

    [Fact]
    public void ConcurrentInserts_EachReturnTheIdOfTheirOwnRow()
    {
        const int threads = 4;
        const int perThread = 100;
        var mismatches = 0;
        var failures = new List<Exception>();

        var workers = new Thread[threads];
        for (var t = 0; t < threads; t++)
        {
            var prefix = $"t{t}_";
            workers[t] = new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < perThread; i++)
                    {
                        var body = prefix + i;
                        var id = InsertBody(body);
                        var stored = db.FetchScalar("SELECT body FROM items WHERE id = @p0", [id]);
                        if (!Equals(stored, body))
                            Interlocked.Increment(ref mismatches);
                    }
                }
                catch (Exception ex)
                {
                    lock (failures)
                        failures.Add(ex);
                }
            });
        }

        foreach (var worker in workers)
            worker.Start();

        foreach (var worker in workers)
            worker.Join();

        failures.Should().BeEmpty();
        mismatches.Should().Be(0);
        db.Count("items").Should().Be(threads * perThread);
    }

    [Fact]
    public void GetQueries_ReturnsASnapshot()
    {
        db.SetLogQueries(true);
        db.Count("items");

        var snapshot = db.GetQueries();
        var count = snapshot.Count;

        db.Count("items");

        snapshot.Should().HaveCount(count);
        db.GetQueryCount().Should().Be(count + 1);
    }
}
