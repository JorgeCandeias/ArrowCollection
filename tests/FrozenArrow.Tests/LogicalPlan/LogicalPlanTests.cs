using FrozenArrow.Query;
using FrozenArrow.Query.LogicalPlan;

namespace FrozenArrow.Tests.LogicalPlan;

/// <summary>
/// Tests for logical plan node creation and properties.
/// </summary>
public class LogicalPlanTests
{
    private static readonly Dictionary<string, Type> TestSchema = new()
    {
        ["Id"] = typeof(int),
        ["Name"] = typeof(string),
        ["Age"] = typeof(int),
        ["Sales"] = typeof(double)
    };

    [Fact]
    public void ScanPlan_CreatesCorrectly()
    {
        // Arrange & Act
        var scan = new ScanPlan(
            tableName: "Orders",
            sourceReference: new object(),
            schema: TestSchema,
            rowCount: 1_000_000);

        // Assert
        Assert.Equal("Orders", scan.TableName);
        Assert.Equal(1_000_000, scan.EstimatedRowCount);
        Assert.Equal(TestSchema, scan.OutputSchema);
        Assert.Equal("Scan(Orders)", scan.Description);
    }

    [Fact]
    public void ScanPlan_IsImmutable()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);

        // Assert - Properties should be get-only
        Assert.IsType<string>(scan.TableName);
        Assert.IsType<long>(scan.EstimatedRowCount);
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, Type>>(scan.OutputSchema);
    }

    [Fact]
    public void FilterPlan_CreatesCorrectly()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", 2, ComparisonOperator.GreaterThan, 25)
        };

        // Act
        var filter = new FilterPlan(scan, predicates, estimatedSelectivity: 0.3);

        // Assert
        Assert.Same(scan, filter.Input);
        Assert.Equal(predicates, filter.Predicates);
        Assert.Equal(0.3, filter.EstimatedSelectivity);
        Assert.Equal(300_000, filter.EstimatedRowCount); // 1M * 0.3
        Assert.Contains("Filter", filter.Description);
        Assert.Contains("30%", filter.Description);
    }

    [Fact]
    public void FilterPlan_InheritsSchemaFromInput()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var filter = new FilterPlan(scan, new List<ColumnPredicate>(), 0.5);

        // Assert
        Assert.Equal(scan.OutputSchema, filter.OutputSchema);
    }

    [Fact]
    public void FilterPlan_RejectsInvalidSelectivity()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);

        // Act & Assert - Too low
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FilterPlan(scan, new List<ColumnPredicate>(), -0.1));

        // Act & Assert - Too high
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FilterPlan(scan, new List<ColumnPredicate>(), 1.1));
    }

    [Fact]
    public void ProjectPlan_CreatesCorrectly()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var projections = new List<ProjectionColumn>
        {
            new ProjectionColumn("Name", "CustomerName", typeof(string)),
            new ProjectionColumn("Age", "CustomerAge", typeof(int))
        };

        // Act
        var project = new ProjectPlan(scan, projections);

        // Assert
        Assert.Same(scan, project.Input);
        Assert.Equal(projections, project.Projections);
        Assert.Equal(2, project.OutputSchema.Count);
        Assert.Equal(typeof(string), project.OutputSchema["CustomerName"]);
        Assert.Equal(typeof(int), project.OutputSchema["CustomerAge"]);
        Assert.Contains("Project", project.Description);
    }

    [Fact]
    public void AggregatePlan_CreatesCorrectly_WithColumn()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);

        // Act
        var aggregate = new AggregatePlan(
            scan, 
            AggregationOperation.Sum, 
            "Sales", 
            typeof(double));

        // Assert
        Assert.Same(scan, aggregate.Input);
        Assert.Equal(AggregationOperation.Sum, aggregate.Operation);
        Assert.Equal("Sales", aggregate.ColumnName);
        Assert.Equal(typeof(double), aggregate.OutputType);
        Assert.Equal(1, aggregate.EstimatedRowCount); // Aggregates produce 1 row
        Assert.Contains("Sum(Sales)", aggregate.Description);
    }

    [Fact]
    public void AggregatePlan_CreatesCorrectly_Count()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);

        // Act
        var aggregate = new AggregatePlan(
            scan,
            AggregationOperation.Count,
            columnName: null,
            typeof(long));

        // Assert
        Assert.Null(aggregate.ColumnName);
        Assert.Contains("Count()", aggregate.Description);
    }

    [Fact]
    public void GroupByPlan_CreatesCorrectly()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var aggregations = new List<AggregationDescriptor>
        {
            new() { Operation = AggregationOperation.Sum, ColumnName = "Sales", ResultPropertyName = "TotalSales" }
        };

        // Act
        var groupBy = new GroupByPlan(
            scan,
            "Name",
            typeof(string),
            aggregations);

        // Assert
        Assert.Same(scan, groupBy.Input);
        Assert.Equal("Name", groupBy.GroupByColumn);
        Assert.Equal(typeof(string), groupBy.GroupByKeyType);
        Assert.Equal(aggregations, groupBy.Aggregations);
        Assert.Contains("GroupBy(Name)", groupBy.Description);
        Assert.True(groupBy.EstimatedRowCount < scan.EstimatedRowCount); // Groups < rows
    }

    [Fact]
    public void LimitPlan_CreatesCorrectly()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);

        // Act
        var limit = new LimitPlan(scan, 100);

        // Assert
        Assert.Same(scan, limit.Input);
        Assert.Equal(100, limit.Count);
        Assert.Equal(100, limit.EstimatedRowCount);
        Assert.Equal("Limit(100)", limit.Description);
    }

    [Fact]
    public void LimitPlan_RejectsNegativeCount()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new LimitPlan(scan, -1));
    }

    [Fact]
    public void OffsetPlan_CreatesCorrectly()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);

        // Act
        var offset = new OffsetPlan(scan, 50);

        // Assert
        Assert.Same(scan, offset.Input);
        Assert.Equal(50, offset.Count);
        Assert.Equal(999_950, offset.EstimatedRowCount);
        Assert.Equal("Offset(50)", offset.Description);
    }

    [Fact]
    public void OffsetPlan_RejectsNegativeCount()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new OffsetPlan(scan, -1));
    }

    [Fact]
    public void LogicalPlan_SupportsChaining()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", 2, ComparisonOperator.GreaterThan, 25)
        };

        // Act - Build a complex chain: Scan ? Filter ? Limit
        var filter = new FilterPlan(scan, predicates, 0.5);
        var limit = new LimitPlan(filter, 100);

        // Assert
        Assert.Same(filter, limit.Input);
        Assert.Same(scan, filter.Input);
        Assert.Equal(100, limit.EstimatedRowCount);
    }

    [Fact]
    public void LogicalPlan_NullInputsThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new FilterPlan(null!, new List<ColumnPredicate>(), 0.5));
        
        Assert.Throws<ArgumentNullException>(() =>
            new ProjectPlan(null!, new List<ProjectionColumn>()));
        
        Assert.Throws<ArgumentNullException>(() =>
            new AggregatePlan(null!, AggregationOperation.Sum, "Sales", typeof(double)));
        
        Assert.Throws<ArgumentNullException>(() =>
            new LimitPlan(null!, 100));
        
        Assert.Throws<ArgumentNullException>(() =>
            new OffsetPlan(null!, 50));
    }
}
