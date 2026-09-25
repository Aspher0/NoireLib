using FluentAssertions;
using NoireLib.Database;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Identifiers are always quoted by EscapeColumn. Expressions only reach SQL through the explicit raw methods.</summary>
[SupportedOSPlatform("windows")]
public class NoireDatabaseEscapingTests : IDisposable
{
    #region Helpers

    // Tests in one class run sequentially: the model reads the current test's database from a static.
    private sealed class ThingModel : NoireDbModelBase<ThingModel>
    {
        internal static string CurrentDatabase = string.Empty;
        internal static string CurrentDirectory = string.Empty;

        protected override string DatabaseName => CurrentDatabase;
        protected override string? TableName => "things";
        protected override string PrimaryKey => "id";
        protected override bool LoadDatabaseOnInit => false;
        protected override string? DatabaseDirectoryOverride => CurrentDirectory;

        [NoireDbColumn("name", Type = "TEXT")]
        public string? Name
        {
            get => GetColumn<string?>("name");
            set => SetColumn("name", value);
        }
    }

    private readonly string tempDirectory;
    private readonly string databaseName = $"NoireLibTests_{Guid.NewGuid():N}";

    public NoireDatabaseEscapingTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"NoireLib.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        ThingModel.CurrentDatabase = databaseName;
        ThingModel.CurrentDirectory = tempDirectory;
    }

    public void Dispose()
    {
        // Only this test's database is disposed: NoireDatabase.DisposeAll is process-wide and would tear a concurrently
        // running test class's database out from under it.
        try
        {
            NoireDatabase.GetInstance(databaseName).Dispose();
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

    // ToSql never touches the database.
    private static QueryBuilder<ThingModel> Builder() => new("things", null!);

    #endregion

    [Fact]
    public void EscapeColumn_QuotesParenthesizedInput_AsOneIdentifier()
    {
        NoireDatabase.EscapeColumn("id) FROM things; DROP TABLE things; --")
            .Should().Be("\"id) FROM things; DROP TABLE things; --\"");

        NoireDatabase.EscapeColumn("COUNT(*)").Should().Be("\"COUNT(*)\"");
    }

    [Fact]
    public void EscapeColumn_DoublesEmbeddedQuotes()
    {
        NoireDatabase.EscapeColumn("na\"me").Should().Be("\"na\"\"me\"");
        NoireDatabase.EscapeColumn("x\" OR 1=1 --").Should().Be("\"x\"\" OR 1=1 --\"");
    }

    [Fact]
    public void EscapeColumn_QuotesEachPartOfAQualifiedName()
    {
        NoireDatabase.EscapeColumn("things.name").Should().Be("\"things\".\"name\"");
    }

    [Fact]
    public void EscapeColumn_RejectsAnEmptyIdentifier()
    {
        var act = () => NoireDatabase.EscapeColumn(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Builder_QuotesParenthesizedColumnArguments()
    {
        var (sql, _) = Builder()
            .Where("name) = 1 OR (1", "x")
            .OrderBy("LOWER(name)")
            .Having("COUNT(*)", ">", 1)
            .ToSql();

        sql.Should().Contain("WHERE \"name) = 1 OR (1\" = @p0");
        sql.Should().Contain("HAVING \"COUNT(*)\" > @p1");
        sql.Should().Contain("ORDER BY \"LOWER(name)\" ASC");
    }

    [Fact]
    public void RawMethods_WriteExpressions_WithBindingsInClauseOrder()
    {
        var (sql, parameters) = Builder()
            .Where("name", "a")
            .GroupBy("name")
            .HavingRaw("COUNT(*) > ?", [2])
            .OrHavingRaw("MAX(id) = ?", [7])
            .OrderByRaw("COUNT(*) DESC")
            .ToSql();

        sql.Should().Be("SELECT * FROM \"things\" WHERE \"name\" = @p0 GROUP BY name HAVING (COUNT(*) > @p1) OR (MAX(id) = @p2) ORDER BY COUNT(*) DESC");
        parameters.Should().Equal("a", 2, 7);
    }

    [Fact]
    public void Aggregates_EscapeTheirColumn_AndKeepTheWildcard()
    {
        ThingModel.Make(new System.Collections.Generic.Dictionary<string, object?> { ["name"] = "one" }).Save().Should().BeTrue();

        var db = ThingModel.GetDatabase().SetLogQueries(true);

        ThingModel.Query().Count().Should().Be(1);
        ThingModel.Query().Count("name").Should().Be(1);
        ThingModel.Query().Max("name").Should().Be("one");

        var queries = db.GetQueries().Select(query => query.Sql).ToList();
        queries.Should().Contain(sql => sql.StartsWith("SELECT COUNT(*) as count FROM"));
        queries.Should().Contain(sql => sql.StartsWith("SELECT COUNT(\"name\") as count FROM"));
        queries.Should().Contain(sql => sql.StartsWith("SELECT MAX(\"name\") as max FROM"));

        db.Count("things", column: "name").Should().Be(1);
        db.GetQueries().Last().Sql.Should().Be("SELECT COUNT(\"name\") FROM \"things\"");
    }

    [Fact]
    public void Aggregates_DoNotExecuteSqlSmuggledThroughTheColumn()
    {
        ThingModel.Make(new System.Collections.Generic.Dictionary<string, object?> { ["name"] = "kept" }).Save().Should().BeTrue();

        // Written raw, this would count the rows and then delete them; quoted, it is one identifier.
        try
        {
            ThingModel.Query().Count("*) as count FROM things; DELETE FROM things; --");
        }
        catch
        {
            // Whether SQLite accepts the quoted name is irrelevant; only the second statement must never run.
        }

        ThingModel.All().Should().ContainSingle(model => model.Name == "kept");
    }
}
