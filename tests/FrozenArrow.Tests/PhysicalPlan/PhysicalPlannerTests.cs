using FrozenArrow.Query;
using FrozenArrow.Query.LogicalPlan;
using FrozenArrow.Query.PhysicalPlan;

namespace FrozenArrow.Tests.PhysicalPlan;

/// <summary>
/// Tests for physical plan generation and strategy selection (Phase 6 Foundation).
/// </summary>
public class PhysicalPlannerTests
{
    [Fact]
    public void CreatePhysicalPlan_SimpleScan_CreatesPhysicalScan()
    {
        // Arrange
        var logical = new ScanPlan("test", new object(), new Dictionary<string, Type>(), 1000);
        var planner = new PhysicalPlanner();

        // Act
        var physical = planner.CreatePhysicalPlan(logical);

        // Assert
        Assert.IsType<PhysicalScanPlan>(physical);
        var scan = (PhysicalScanPlan)physical;
        Assert.Equal(1000, scan.RowCount);
    }

    [Fact]
    public void PhysicalScan_HasReasonableCost()
    {
        // Arrange
        var scan = new PhysicalScanPlan(10_000);

        // Act
        var cost = scan.EstimatedCost;

        // Assert
        Assert.True(cost > 0);
        Assert.True(cost < 100); // Should be reasonable for 10K rows
    }

    [Fact]
    public void PhysicalFilter_Description_IsReadable()
    {
        // Arrange
        var scan = new PhysicalScanPlan(10_000);
        var predicate = new Int32ComparisonPredicate("Value", 0, ComparisonOperator.GreaterThan, 100);
        var filter = new PhysicalFilterPlan(
            scan,
            [predicate],
            0.5,
            FilterExecutionStrategy.SIMD);

        // Act
        var description = filter.Description;

        // Assert
        Assert.Contains("Filter", description);
        Assert.Contains("SIMD", description);
        Assert.Contains("50%", description); // Selectivity
    }

    [Fact]
    public void PhysicalFilter_SIMDHasLowerCostThanSequential()
    {
        // Arrange
        var scan = new PhysicalScanPlan(10_000);
        var predicate = new Int32ComparisonPredicate("Value", 0, ComparisonOperator.GreaterThan, 100);
        
        var sequentialFilter = new PhysicalFilterPlan(scan, [predicate], 0.5, FilterExecutionStrategy.Sequential);
        var simdFilter = new PhysicalFilterPlan(scan, [predicate], 0.5, FilterExecutionStrategy.SIMD);

        // Act
        var sequentialCost = sequentialFilter.EstimatedCost;
        var simdCost = simdFilter.EstimatedCost;

        // Assert - SIMD should have lower cost
        Assert.True(simdCost < sequentialCost);
    }

    [Fact]
    public void PhysicalPlanner_ChoosesBetterPlan()
    {
        // Arrange
        var scan = new PhysicalScanPlan(10_000);
        var predicate = new Int32ComparisonPredicate("Value", 0, ComparisonOperator.GreaterThan, 100);
        
        var plan1 = new PhysicalFilterPlan(scan, [predicate], 0.5, FilterExecutionStrategy.Sequential);
        var plan2 = new PhysicalFilterPlan(scan, [predicate], 0.5, FilterExecutionStrategy.SIMD);

        var planner = new PhysicalPlanner();

        // Act
        var better = planner.ChooseBetterPlan(plan1, plan2);

        // Assert - Should choose SIMD (lower cost)
        Assert.Same(plan2, better);
    }
}
