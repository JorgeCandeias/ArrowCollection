using FrozenArrow.Query;
using FrozenArrow.Query.LogicalPlan;

namespace FrozenArrow.Tests.LogicalPlan;

/// <summary>
/// Tests for plan explanation and visualization.
/// </summary>
public class LogicalPlanExplainTests
{
    private static readonly Dictionary<string, Type> TestSchema = new()
    {
        ["Id"] = typeof(int),
        ["Name"] = typeof(string),
        ["Age"] = typeof(int),
        ["Sales"] = typeof(double)
    };

    [Fact]
    public void Explain_ScanPlan_ShowsTableAndRowCount()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var explainer = new LogicalPlanExplainer();

        // Act
        var explanation = explainer.Explain(scan);

        // Assert
        Assert.Contains("Scan(Orders)", explanation);
        Assert.Contains("1,000,000 rows", explanation);
    }

    [Fact]
    public void Explain_FilterPlan_ShowsSelectivity()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", 2, ComparisonOperator.GreaterThan, 25)
        };
        var filter = new FilterPlan(scan, predicates, estimatedSelectivity: 0.3);
        var explainer = new LogicalPlanExplainer();

        // Act
        var explanation = explainer.Explain(filter);

        // Assert
        Assert.Contains("Filter", explanation);
        Assert.Contains("30%", explanation);
        Assert.Contains("300,000 rows", explanation);
        Assert.Contains("Scan(Orders)", explanation);
    }

    [Fact]
    public void Explain_ComplexPlan_ShowsHierarchy()
    {
        // Arrange - Build: Scan → Filter → Limit
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", 2, ComparisonOperator.GreaterThan, 25),
            new StringEqualityPredicate("Name", 1, "Alice")
        };
        var filter = new FilterPlan(scan, predicates, 0.25);
        var limit = new LimitPlan(filter, 100);
        var explainer = new LogicalPlanExplainer();

        // Act
        var explanation = explainer.Explain(limit);

        // Assert
        Assert.Contains("Limit(100)", explanation);
        Assert.Contains("100 rows", explanation);
        Assert.Contains("Filter", explanation);
        Assert.Contains("Scan(Orders)", explanation);
        
        // Should show hierarchy (Limit appears before Filter in output)
        var limitIndex = explanation.IndexOf("Limit");
        var filterIndex = explanation.IndexOf("Filter");
        var scanIndex = explanation.IndexOf("Scan");
        Assert.True(limitIndex < filterIndex);
        Assert.True(filterIndex < scanIndex);
    }

    [Fact]
    public void Explain_AggregatePlan_ShowsOperation()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var aggregate = new AggregatePlan(scan, AggregationOperation.Sum, "Sales", typeof(double));
        var explainer = new LogicalPlanExplainer();

        // Act
        var explanation = explainer.Explain(aggregate);

        // Assert
        Assert.Contains("Sum(Sales)", explanation);
        Assert.Contains("1 rows", explanation); // Aggregates produce 1 row
    }

    [Fact]
    public void Explain_GroupByPlan_ShowsGroupKeyAndAggregations()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var aggregations = new List<AggregationDescriptor>
        {
            new() { Operation = AggregationOperation.Sum, ColumnName = "Sales", ResultPropertyName = "TotalSales" },
            new() { Operation = AggregationOperation.Count, ResultPropertyName = "Count" }
        };
        var groupBy = new GroupByPlan(scan, "Name", typeof(string), aggregations);
        var explainer = new LogicalPlanExplainer();

        // Act
        var explanation = explainer.Explain(groupBy);

        // Assert
        Assert.Contains("GroupBy(Name)", explanation);
        Assert.Contains("Sum(Sales)", explanation);
        Assert.Contains("Count", explanation);
        Assert.Contains("groups", explanation);
    }

    [Fact]
    public void Explain_ProjectPlan_ShowsProjections()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var projections = new List<ProjectionColumn>
        {
            new("Name", "CustomerName", typeof(string)),
            new("Age", "CustomerAge", typeof(int))
        };
        var project = new ProjectPlan(scan, projections);
        var explainer = new LogicalPlanExplainer();

        // Act
        var explanation = explainer.Explain(project);

        // Assert
        Assert.Contains("Project", explanation);
        Assert.Contains("CustomerName", explanation);
        Assert.Contains("CustomerAge", explanation);
    }

    [Fact]
    public void Explain_OffsetPlan_ShowsSkipCount()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var offset = new OffsetPlan(scan, 50);
        var explainer = new LogicalPlanExplainer();

        // Act
        var explanation = explainer.Explain(offset);

        // Assert
        Assert.Contains("Offset(50)", explanation);
        Assert.Contains("950 rows", explanation);
    }

    [Fact]
    public void Explain_IsConsistent()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var explainer = new LogicalPlanExplainer();

        // Act - Explain twice
        var explanation1 = explainer.Explain(scan);
        var explanation2 = explainer.Explain(scan);

        // Assert - Should be identical
        Assert.Equal(explanation1, explanation2);
    }

    [Fact]
    public void Description_IsHumanReadable()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000_000);
        var predicates = new List<ColumnPredicate>
        {
            new Int32ComparisonPredicate("Age", 2, ComparisonOperator.GreaterThan, 25)
        };
        var filter = new FilterPlan(scan, predicates, 0.3);

        // Act
        var description = filter.Description;

        // Assert
        Assert.Contains("Filter", description);
        Assert.Contains("predicate", description);
        Assert.Contains("30%", description);
        Assert.DoesNotContain("null", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", description, StringComparison.OrdinalIgnoreCase);
    }
}
