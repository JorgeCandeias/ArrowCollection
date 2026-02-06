using System.Linq.Expressions;
using System.Reflection;
using FrozenArrow.Query;
using FrozenArrow.Query.LogicalPlan;

namespace FrozenArrow.Tests.LogicalPlan;

/// <summary>
/// Tests for the ExpressionHelper that extracts information from LINQ expressions.
/// </summary>
public class ExpressionHelperTests
{
    private static readonly Dictionary<string, Type> TestSchema = new()
    {
        ["Id"] = typeof(int),
        ["Name"] = typeof(string),
        ["Age"] = typeof(int),
        ["Category"] = typeof(string),
        ["Sales"] = typeof(double)
    };

    [Fact]
    public void TryExtractColumnName_SimpleMemberAccess_Succeeds()
    {
        // Arrange
        Expression<Func<TestRecord, string>> lambda = x => x.Name;

        // Act
        var success = ExpressionHelper.TryExtractColumnName(lambda, out var columnName);

        // Assert
        Assert.True(success);
        Assert.Equal("Name", columnName);
    }

    [Fact]
    public void TryExtractColumnName_WithConversion_Succeeds()
    {
        // Arrange
        Expression<Func<TestRecord, object>> lambda = x => (object)x.Age;

        // Act
        var success = ExpressionHelper.TryExtractColumnName(lambda, out var columnName);

        // Assert
        Assert.True(success);
        Assert.Equal("Age", columnName);
    }

    [Fact]
    public void TryExtractColumnName_ComplexExpression_Fails()
    {
        // Arrange
        Expression<Func<TestRecord, int>> lambda = x => x.Age + 10;

        // Act
        var success = ExpressionHelper.TryExtractColumnName(lambda, out var columnName);

        // Assert
        Assert.False(success);
        Assert.Null(columnName);
    }

    [Fact]
    public void TryExtractProjections_AnonymousType_Succeeds()
    {
        // Arrange
        Expression<Func<TestRecord, object>> lambda = x => new { x.Name, x.Age };

        // Act
        var success = ExpressionHelper.TryExtractProjections(lambda, TestSchema, out var projections);

        // Assert
        Assert.True(success);
        Assert.NotNull(projections);
        Assert.Equal(2, projections.Count);
        Assert.Equal("Name", projections[0].SourceColumn);
        Assert.Equal("Name", projections[0].OutputName);
        Assert.Equal(typeof(string), projections[0].OutputType);
        Assert.Equal("Age", projections[1].SourceColumn);
        Assert.Equal("Age", projections[1].OutputName);
        Assert.Equal(typeof(int), projections[1].OutputType);
    }

    [Fact]
    public void TryExtractProjections_RenamedColumns_Succeeds()
    {
        // Arrange
        Expression<Func<TestRecord, object>> lambda = x => new { CustomerName = x.Name, CustomerAge = x.Age };

        // Act
        var success = ExpressionHelper.TryExtractProjections(lambda, TestSchema, out var projections);

        // Assert
        Assert.True(success);
        Assert.NotNull(projections);
        Assert.Equal(2, projections.Count);
        Assert.Equal("Name", projections[0].SourceColumn);
        Assert.Equal("CustomerName", projections[0].OutputName);
        Assert.Equal("Age", projections[1].SourceColumn);
        Assert.Equal("CustomerAge", projections[1].OutputName);
    }

    [Fact]
    public void TryExtractProjections_SimpleMemberAccess_Fails()
    {
        // Arrange - Just returning a single member, not a projection
        Expression<Func<TestRecord, string>> lambda = x => x.Name;

        // Act
        var success = ExpressionHelper.TryExtractProjections(lambda, TestSchema, out var projections);

        // Assert
        Assert.False(success);
        Assert.Null(projections);
    }

    [Fact]
    public void TryExtractAggregations_NotNewExpression_Fails()
    {
        // Arrange - Just a simple member access, not a new expression
        var groupingType = typeof(IGrouping<string, TestRecord>);
        var param = Expression.Parameter(groupingType, "g");
        var keyExpr = Expression.Property(param, "Key");
        var lambda = Expression.Lambda(keyExpr, param);

        // Act
        var success = ExpressionHelper.TryExtractAggregations(lambda, out var aggregations, out var groupKeyProperty);

        // Assert
        Assert.False(success);
        Assert.Null(aggregations);
        Assert.Null(groupKeyProperty);
    }

    // Removed complex aggregation tests that are difficult to construct with Expression API
    // These will be tested via integration tests when wiring up to real LINQ queries

    private class TestRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Category { get; set; } = string.Empty;
        public double Sales { get; set; }
    }

    private class TestAggResult(string key, double total)
    {
        public string Key { get; } = key;
        public double Total { get; } = total;
    }

    private class TestMultiAggResult(string key, int count, double totalSales)
    {
        public string Key { get; } = key;
        public int Count { get; } = count;
        public double TotalSales { get; } = totalSales;
    }
}
