using FrozenArrow.Query;
using FrozenArrow.Query.LogicalPlan;

namespace FrozenArrow.Tests.LogicalPlan;

/// <summary>
/// Tests for the visitor pattern implementation on logical plans.
/// </summary>
public class LogicalPlanVisitorTests
{
    private static readonly Dictionary<string, Type> TestSchema = new()
    {
        ["Id"] = typeof(int),
        ["Name"] = typeof(string)
    };

    [Fact]
    public void Visitor_CanTraverseScanPlan()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var visitor = new CountingVisitor();

        // Act
        scan.Accept(visitor);

        // Assert
        Assert.Equal(1, visitor.ScanCount);
        Assert.Equal(0, visitor.FilterCount);
    }

    [Fact]
    public void Visitor_CanTraverseFilterPlan()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var filter = new FilterPlan(scan, [], 0.5);
        var visitor = new CountingVisitor();

        // Act
        filter.Accept(visitor);

        // Assert
        Assert.Equal(1, visitor.FilterCount);
    }

    [Fact]
    public void Visitor_CanTraverseComplexPlan()
    {
        // Arrange - Build: Scan → Filter → Project → Limit
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var filter = new FilterPlan(scan, [], 0.5);
        var project = new ProjectPlan(filter, []);
        var limit = new LimitPlan(project, 100);
        
        var visitor = new CountingVisitor();

        // Act
        limit.Accept(visitor);

        // Assert
        Assert.Equal(1, visitor.LimitCount);
    }

    [Fact]
    public void Visitor_CanCollectDescriptions()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var filter = new FilterPlan(scan, [], 0.5);
        var limit = new LimitPlan(filter, 100);
        
        var visitor = new DescriptionCollector();

        // Act
        var result = limit.Accept(visitor);

        // Assert
        Assert.Contains("Limit", result);
    }

    [Fact]
    public void Visitor_CanTransformPlan()
    {
        // Arrange
        var scan = new ScanPlan("Orders", new object(), TestSchema, 1_000);
        var filter = new FilterPlan(scan, [], 0.5);
        
        // Visitor that wraps every plan in a Limit
        var visitor = new WrapInLimitVisitor();

        // Act
        var result = filter.Accept(visitor);

        // Assert
        Assert.IsType<LimitPlan>(result);
    }

    /// <summary>
    /// Test visitor that counts each plan type encountered.
    /// </summary>
    private class CountingVisitor : ILogicalPlanVisitor<int>
    {
        public int ScanCount { get; private set; }
        public int FilterCount { get; private set; }
        public int ProjectCount { get; private set; }
        public int AggregateCount { get; private set; }
        public int GroupByCount { get; private set; }
        public int LimitCount { get; private set; }
        public int OffsetCount { get; private set; }

        public int Visit(ScanPlan plan)
        {
            ScanCount++;
            return 1;
        }

        public int Visit(FilterPlan plan)
        {
            FilterCount++;
            return 1 + plan.Input.Accept(this);
        }

        public int Visit(ProjectPlan plan)
        {
            ProjectCount++;
            return 1 + plan.Input.Accept(this);
        }

        public int Visit(AggregatePlan plan)
        {
            AggregateCount++;
            return 1 + plan.Input.Accept(this);
        }

        public int Visit(GroupByPlan plan)
        {
            GroupByCount++;
            return 1 + plan.Input.Accept(this);
        }

        public int Visit(LimitPlan plan)
        {
            LimitCount++;
            return 1 + plan.Input.Accept(this);
        }

        public int Visit(OffsetPlan plan)
        {
            OffsetCount++;
            return 1 + plan.Input.Accept(this);
        }
    }

    /// <summary>
    /// Test visitor that collects descriptions.
    /// </summary>
    private class DescriptionCollector : ILogicalPlanVisitor<string>
    {
        public string Visit(ScanPlan plan) => plan.Description;

        public string Visit(FilterPlan plan) => 
            $"{plan.Description} → {plan.Input.Accept(this)}";

        public string Visit(ProjectPlan plan) => 
            $"{plan.Description} → {plan.Input.Accept(this)}";

        public string Visit(AggregatePlan plan) => 
            $"{plan.Description} → {plan.Input.Accept(this)}";

        public string Visit(GroupByPlan plan) => 
            $"{plan.Description} → {plan.Input.Accept(this)}";

        public string Visit(LimitPlan plan) => 
            $"{plan.Description} → {plan.Input.Accept(this)}";

        public string Visit(OffsetPlan plan) => 
            $"{plan.Description} → {plan.Input.Accept(this)}";
    }

    /// <summary>
    /// Test visitor that transforms plans by wrapping in Limit.
    /// </summary>
    private class WrapInLimitVisitor : ILogicalPlanVisitor<LogicalPlanNode>
    {
        public LogicalPlanNode Visit(ScanPlan plan) => 
            new LimitPlan(plan, 1000);

        public LogicalPlanNode Visit(FilterPlan plan) => 
            new LimitPlan(plan, 1000);

        public LogicalPlanNode Visit(ProjectPlan plan) => 
            new LimitPlan(plan, 1000);

        public LogicalPlanNode Visit(AggregatePlan plan) => 
            new LimitPlan(plan, 1000);

        public LogicalPlanNode Visit(GroupByPlan plan) => 
            new LimitPlan(plan, 1000);

        public LogicalPlanNode Visit(LimitPlan plan) => 
            plan; // Already limited

        public LogicalPlanNode Visit(OffsetPlan plan) => 
            new LimitPlan(plan, 1000);
    }
}
