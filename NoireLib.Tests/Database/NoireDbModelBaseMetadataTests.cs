using FluentAssertions;
using NoireLib.Database;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Metadata overrides are read once per type, and accessors never hand out the model's own dictionaries.</summary>
[SupportedOSPlatform("windows")]
public class NoireDbModelBaseMetadataTests
{
    #region Helpers

    // Each test uses its own model type: the metadata cache is process-wide per type.
    private sealed class CountingModel : NoireDbModelBase<CountingModel>
    {
        internal static int CastsReads;
        internal static int RulesReads;

        protected override string DatabaseName => "unused";
        protected override string? TableName => "counting";
        protected override string PrimaryKey => "id";
        protected override bool LoadDatabaseOnInit => false;

        protected override IReadOnlyDictionary<string, DbColumnCast> Casts
        {
            get
            {
                Interlocked.Increment(ref CastsReads);
                return new Dictionary<string, DbColumnCast> { ["level"] = DbColumnCast.Integer };
            }
        }

        protected override IReadOnlyDictionary<string, IReadOnlyList<DbValidationRuleDefinition>> ValidationRules
        {
            get
            {
                Interlocked.Increment(ref RulesReads);
                return new Dictionary<string, IReadOnlyList<DbValidationRuleDefinition>>
                {
                    ["level"] = [new(DbValidationRule.Min, ["1"])]
                };
            }
        }
    }

    private sealed class PlainModel : NoireDbModelBase<PlainModel>
    {
        protected override string DatabaseName => "unused";
        protected override string? TableName => "plain";
        protected override string PrimaryKey => "id";
        protected override bool LoadDatabaseOnInit => false;
    }

    #endregion

    [Fact]
    public void MetadataOverrides_AreReadOncePerModelType_AndStillApply()
    {
        var first = new CountingModel();
        first.SetColumn("level", "5");
        first.SetColumn("level", "0");
        first.Validate().Should().BeFalse();
        first.Validate().Should().BeFalse();

        var second = new CountingModel();
        second.SetColumn("level", "3");
        second.Validate().Should().BeTrue();

        first["level"].Should().Be(0);
        second["level"].Should().Be(3);
        CountingModel.CastsReads.Should().Be(1);
        CountingModel.RulesReads.Should().Be(1);
    }

    [Fact]
    public void GetColumns_CannotBeCastBackToMutateTheModel()
    {
        var model = new PlainModel();
        model.SetColumn("name", "before");

        var columns = model.GetColumns();

        (columns as Dictionary<string, object?>).Should().BeNull();
        var act = () => ((IDictionary<string, object?>)columns)["name"] = "after";
        act.Should().Throw<NotSupportedException>();

        model["name"].Should().Be("before");
        model.GetChanges()["name"].To.Should().Be("before");
    }

    [Fact]
    public void GetColumns_FollowsLaterChanges()
    {
        var model = new PlainModel();
        var columns = model.GetColumns();

        model.SetColumn("name", "set later");

        columns["name"].Should().Be("set later");
    }

    [Fact]
    public void GetChangesAndGetErrors_CannotBeCastBack()
    {
        var model = new PlainModel();
        model.SetColumn("name", "value");

        (model.GetChanges() as Dictionary<string, (object?, object?)>).Should().BeNull();
        (model.GetErrors() as Dictionary<string, List<string>>).Should().BeNull();
    }

    [Fact]
    public void ToDictionary_ReturnsADetachedCopy()
    {
        var model = new PlainModel();
        model.SetColumn("name", "original");

        var copy = model.ToDictionary();
        ((Dictionary<string, object?>)copy)["name"] = "changed";
        model.SetColumn("other", 1);

        model["name"].Should().Be("original");
        copy.ContainsKey("other").Should().BeFalse();
        copy.ContainsKey("NAME").Should().BeTrue();
    }

    [Fact]
    public void ToJson_WritesTheColumns()
    {
        var model = new PlainModel();
        model.SetColumn("name", "json");

        model.ToJson().Should().Contain("\"name\": \"json\"");
    }
}
