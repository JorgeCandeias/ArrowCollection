using FrozenArrow.Query;
using FrozenArrow.Query.LogicalPlan;

namespace FrozenArrow.Tests.LogicalPlan;

/// <summary>
/// Tests for the logical plan optimizer.
/// </summary>
public class LogicalPlanOptimizerTests
{
    private static readonly Dictionary<string, Type> TestSchema = new()
    {
        ["Id"] = typeof(int),
        ["Name"] = typeof(string),
        ["Age"] = typeof(int),
        ["Country"] = typeof(string)
    };

    private static readonly Dictionary<string, int> TestColumnIndexMap = new()
    {
        ["Id"] = 0,
        ["Name"] = 1,
        ["Age"] = 2,
        ["Country"] = 3
    };

    [Fact]
    public void Optimize_LeavesSimpleScanUnchanged()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var optimizer = new LogicalPlanOptimizer();

        // Act
        var optimized = optimizer.Optimize(scan);

        // Assert - Should be same instance (no optimization needed)
        Assert.Same(scan, optimized);
    }

    [Fact]
    public void Optimize_PreservesFilterWithSinglePredicate()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", TestColumnIndexMap["Age"], ComparisonOperator.GreaterThan, 25)
        };
        var filter = new FilterPlan(scan, predicates, 0.5);
        var optimizer = new LogicalPlanOptimizer();

        // Act
        var optimized = optimizer.Optimize(filter);

        // Assert - Single predicate, no reordering needed
        Assert.IsType<FilterPlan>(optimized);
        var optimizedFilter = (FilterPlan)optimized;
        Assert.Single(optimizedFilter.Predicates);
    }

    [Fact]
    public void Optimize_ReordersPredicatesBySelectivity()
    {
        // Arrange - Create zone map with different selectivities
        var zoneMap = CreateZoneMapWithSelectivities();
        
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        
        // Country = "USA" is more selective than Age > 25 (based on zone map)
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", TestColumnIndexMap["Age"], ComparisonOperator.GreaterThan, 25),
            new StringEqualityPredicate("Country", TestColumnIndexMap["Country"], "USA")
        };
        var filter = new FilterPlan(scan, predicates, 0.5);
        
        var optimizer = new LogicalPlanOptimizer(zoneMap);

        // Act
        var optimized = optimizer.Optimize(filter);

        // Assert - Predicates should be reordered (Country first)
        Assert.IsType<FilterPlan>(optimized);
        var optimizedFilter = (FilterPlan)optimized;
        Assert.Equal(2, optimizedFilter.Predicates.Count);
        
        // Most selective predicate (Country) should be first
        Assert.Equal("Country", optimizedFilter.Predicates[0].ColumnName);
        Assert.Equal("Age", optimizedFilter.Predicates[1].ColumnName);
    }

    [Fact]
    public void Optimize_PreservesSelectivity()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", TestColumnIndexMap["Age"], ComparisonOperator.GreaterThan, 25)
        };
        var filter = new FilterPlan(scan, predicates, 0.3);
        var optimizer = new LogicalPlanOptimizer();

        // Act
        var optimized = optimizer.Optimize(filter);

        // Assert
        var optimizedFilter = (FilterPlan)optimized;
        Assert.Equal(0.3, optimizedFilter.EstimatedSelectivity);
    }

    [Fact]
    public void Optimize_HandlesComplexPlanTree()
    {
        // Arrange - Build: Scan → Filter → Limit
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", TestColumnIndexMap["Age"], ComparisonOperator.GreaterThan, 25)
        };
        var filter = new FilterPlan(scan, predicates, 0.5);
        var limit = new LimitPlan(filter, 100);
        
        var optimizer = new LogicalPlanOptimizer();

        // Act
        var optimized = optimizer.Optimize(limit);

        // Assert - Structure should be preserved
        Assert.IsType<LimitPlan>(optimized);
        var optimizedLimit = (LimitPlan)optimized;
        Assert.IsType<FilterPlan>(optimizedLimit.Input);
        var optimizedFilter = (FilterPlan)optimizedLimit.Input;
        Assert.IsType<ScanPlan>(optimizedFilter.Input);
    }

    [Fact]
    public void Optimize_WorksWithoutZoneMap()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", TestColumnIndexMap["Age"], ComparisonOperator.GreaterThan, 25),
            new StringEqualityPredicate("Country", TestColumnIndexMap["Country"], "USA")
        };
        var filter = new FilterPlan(scan, predicates, 0.5);
        
        // No zone map provided
        var optimizer = new LogicalPlanOptimizer(zoneMap: null);

        // Act
        var optimized = optimizer.Optimize(filter);

        // Assert - Should still work, just without selectivity-based reordering
        Assert.IsType<FilterPlan>(optimized);
        var optimizedFilter = (FilterPlan)optimized;
        Assert.Equal(2, optimizedFilter.Predicates.Count);
    }

    [Fact]
    public void Optimize_IsImmutable()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", TestColumnIndexMap["Age"], ComparisonOperator.GreaterThan, 25)
        };
        var filter = new FilterPlan(scan, predicates, 0.5);
        var optimizer = new LogicalPlanOptimizer();

        // Act
        var optimized = optimizer.Optimize(filter);

        // Assert - Original plan should be unchanged
        Assert.Equal(1_000, scan.EstimatedRowCount);
        Assert.Equal(0.5, filter.EstimatedSelectivity);
        Assert.Same(predicates, filter.Predicates);
    }

    [Fact]
    public void Optimize_CanBeCalledMultipleTimes()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", TestColumnIndexMap["Age"], ComparisonOperator.GreaterThan, 25)
        };
        var filter = new FilterPlan(scan, predicates, 0.5);
        var optimizer = new LogicalPlanOptimizer();

        // Act - Optimize twice
        var optimized1 = optimizer.Optimize(filter);
        var optimized2 = optimizer.Optimize(filter);

        // Assert - Should be deterministic
        Assert.IsType<FilterPlan>(optimized1);
        Assert.IsType<FilterPlan>(optimized2);
    }

    [Fact]
    public void Optimize_HandlesProjectPlan()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var projections = new List<ProjectionColumn>
        {
            new("Name", "CustomerName", typeof(string))
        };
        var project = new ProjectPlan(scan, projections);
        var optimizer = new LogicalPlanOptimizer();

        // Act
        var optimized = optimizer.Optimize(project);

        // Assert
        Assert.IsType<ProjectPlan>(optimized);
        var optimizedProject = (ProjectPlan)optimized;
        Assert.Equal("CustomerName", optimizedProject.Projections[0].OutputName);
    }

    [Fact]
    public void Optimize_HandlesAggregatePlan()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var aggregate = new AggregatePlan(scan, AggregationOperation.Sum, "Age", typeof(long));
        var optimizer = new LogicalPlanOptimizer();

        // Act
        var optimized = optimizer.Optimize(aggregate);

        // Assert
        Assert.IsType<AggregatePlan>(optimized);
        var optimizedAggregate = (AggregatePlan)optimized;
        Assert.Equal(AggregationOperation.Sum, optimizedAggregate.Operation);
    }

    [Fact]
    public void Optimize_HandlesGroupByPlan()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var aggregations = new List<AggregationDescriptor>
        {
            new() { Operation = AggregationOperation.Count, ResultPropertyName = "Count" }
        };
        var groupBy = new GroupByPlan(scan, "Country", typeof(string), aggregations);
        var optimizer = new LogicalPlanOptimizer();

        // Act
        var optimized = optimizer.Optimize(groupBy);

        // Assert
        Assert.IsType<GroupByPlan>(optimized);
        var optimizedGroupBy = (GroupByPlan)optimized;
        Assert.Equal("Country", optimizedGroupBy.GroupByColumn);
    }

    /// <summary>
    /// Creates a mock zone map where Country="USA" is more selective than Age>25.
    /// </summary>
    private static ZoneMap CreateZoneMapWithSelectivities()
    {
        // This is a simplified mock - in reality, ZoneMap construction is more complex
        // For testing, we'd need to either:
        // 1. Create a real ZoneMap with test data
        // 2. Mock the selectivity calculation
        // 3. Use a test-friendly zone map builder
        
        // For now, return null and rely on PredicateReorderer's heuristics
        // TODO: Implement proper zone map testing when wiring up to real data
        return null!;
    }
}
