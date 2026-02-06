using FrozenArrow.Query.LogicalPlan;

namespace FrozenArrow.Query.PhysicalPlan;

/// <summary>
/// Converts logical plans to physical execution plans.
/// Selects execution strategies based on statistics and cost estimates.
/// </summary>
public sealed class PhysicalPlanner
{
    private readonly int _parallelThreshold;
    private readonly int _simdThreshold;

    /// <summary>
    /// Creates a new physical planner with default thresholds.
    /// </summary>
    public PhysicalPlanner()
        : this(parallelThreshold: 50_000, simdThreshold: 1_000)
    {
    }

    /// <summary>
    /// Creates a new physical planner with custom thresholds.
    /// </summary>
    /// <param name="parallelThreshold">Minimum row count to use parallel execution.</param>
    /// <param name="simdThreshold">Minimum row count to use SIMD execution.</param>
    public PhysicalPlanner(int parallelThreshold, int simdThreshold)
    {
        _parallelThreshold = parallelThreshold;
        _simdThreshold = simdThreshold;
    }

    /// <summary>
    /// Converts a logical plan to a physical execution plan.
    /// </summary>
    public PhysicalPlanNode CreatePhysicalPlan(LogicalPlanNode logical)
    {
        return logical switch
        {
            ScanPlan scan => CreatePhysicalScan(scan),
            FilterPlan filter => CreatePhysicalFilter(filter),
            GroupByPlan groupBy => CreatePhysicalGroupBy(groupBy),
            AggregatePlan aggregate => CreatePhysicalAggregate(aggregate),
            LimitPlan limit => CreatePhysicalLimit(limit),
            OffsetPlan offset => CreatePhysicalOffset(offset),
            ProjectPlan project => CreatePhysicalPlan(project.Input), // Pass through for now
            _ => throw new NotSupportedException($"Logical plan type '{logical.GetType().Name}' is not supported")
        };
    }

    private PhysicalScanPlan CreatePhysicalScan(ScanPlan logical)
    {
        return new PhysicalScanPlan(logical.EstimatedRowCount);
    }

    private PhysicalFilterPlan CreatePhysicalFilter(FilterPlan logical)
    {
        var input = CreatePhysicalPlan(logical.Input);
        
        // Choose execution strategy based on row count
        var strategy = ChooseFilterStrategy(input.EstimatedRowCount, logical.Predicates.Count);

        return new PhysicalFilterPlan(
            input,
            logical.Predicates,
            logical.EstimatedSelectivity,
            strategy);
    }

    private FilterExecutionStrategy ChooseFilterStrategy(long rowCount, int predicateCount)
    {
        // For large datasets with many predicates, use parallel
        if (rowCount >= _parallelThreshold && predicateCount > 1)
        {
            return FilterExecutionStrategy.Parallel;
        }

        // For medium datasets, use SIMD if available
        if (rowCount >= _simdThreshold && System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated)
        {
            return FilterExecutionStrategy.SIMD;
        }

        // Default to sequential for small datasets
        return FilterExecutionStrategy.Sequential;
    }

    private PhysicalGroupByPlan CreatePhysicalGroupBy(GroupByPlan logical)
    {
        var input = CreatePhysicalPlan(logical.Input);
        
        // For now, always use hash aggregate (faster for most cases)
        // Future: Could choose sort-based for ordered output requirements
        var strategy = GroupByExecutionStrategy.HashAggregate;

        return new PhysicalGroupByPlan(
            input,
            logical.GroupByColumn,
            logical.GroupByKeyType,
            logical.Aggregations,
            logical.KeyPropertyName,
            strategy);
    }

    private PhysicalAggregatePlan CreatePhysicalAggregate(AggregatePlan logical)
    {
        var input = CreatePhysicalPlan(logical.Input);
        
        // Choose execution strategy based on operation and row count
        var strategy = ChooseAggregateStrategy(input.EstimatedRowCount, logical.Operation);

        return new PhysicalAggregatePlan(
            input,
            logical.Operation,
            logical.ColumnName,
            logical.OutputType,
            strategy);
    }

    private AggregateExecutionStrategy ChooseAggregateStrategy(long rowCount, AggregationOperation operation)
    {
        // Count without column is cheap, no need for parallelization
        if (operation == AggregationOperation.Count)
        {
            return AggregateExecutionStrategy.Sequential;
        }

        // For large datasets, use parallel
        if (rowCount >= _parallelThreshold)
        {
            return AggregateExecutionStrategy.Parallel;
        }

        // For medium datasets, use SIMD
        if (rowCount >= _simdThreshold && System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated)
        {
            return AggregateExecutionStrategy.SIMD;
        }

        return AggregateExecutionStrategy.Sequential;
    }

    private PhysicalLimitPlan CreatePhysicalLimit(LimitPlan logical)
    {
        var input = CreatePhysicalPlan(logical.Input);
        return new PhysicalLimitPlan(input, logical.Count);
    }

    private PhysicalOffsetPlan CreatePhysicalOffset(OffsetPlan logical)
    {
        var input = CreatePhysicalPlan(logical.Input);
        return new PhysicalOffsetPlan(input, logical.Count);
    }

    /// <summary>
    /// Estimates the cost of executing a physical plan.
    /// </summary>
    public double EstimateCost(PhysicalPlanNode plan)
    {
        return plan.EstimatedCost;
    }

    /// <summary>
    /// Compares two physical plans and returns the one with lower cost.
    /// </summary>
    public PhysicalPlanNode ChooseBetterPlan(PhysicalPlanNode plan1, PhysicalPlanNode plan2)
    {
        return plan1.EstimatedCost <= plan2.EstimatedCost ? plan1 : plan2;
    }
}
