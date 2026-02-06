namespace FrozenArrow.Query.PhysicalPlan;

/// <summary>
/// Physical plan for scanning data (table scan or index scan).
/// </summary>
public sealed class PhysicalScanPlan : PhysicalPlanNode
{
    public PhysicalScanPlan(long rowCount)
    {
        RowCount = rowCount;
    }

    /// <summary>
    /// Gets the number of rows to scan.
    /// </summary>
    public long RowCount { get; }

    public override string Description => $"Scan({RowCount} rows)";

    // Scan cost is proportional to row count (sequential read)
    public override double EstimatedCost => RowCount * 0.001; // 0.001 cost units per row

    public override long EstimatedRowCount => RowCount;

    public override TResult Accept<TResult>(IPhysicalPlanVisitor<TResult> visitor)
    {
        return visitor.Visit(this);
    }
}

/// <summary>
/// Physical plan for filtering with a specific execution strategy.
/// </summary>
public sealed class PhysicalFilterPlan : PhysicalPlanNode
{
    public PhysicalFilterPlan(
        PhysicalPlanNode input,
        IReadOnlyList<ColumnPredicate> predicates,
        double selectivity,
        FilterExecutionStrategy strategy)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Predicates = predicates ?? throw new ArgumentNullException(nameof(predicates));
        Selectivity = selectivity;
        ExecutionStrategy = strategy;
    }

    /// <summary>
    /// Gets the input plan to filter.
    /// </summary>
    public PhysicalPlanNode Input { get; }

    /// <summary>
    /// Gets the predicates to evaluate.
    /// </summary>
    public IReadOnlyList<ColumnPredicate> Predicates { get; }

    /// <summary>
    /// Gets the estimated selectivity (0.0 to 1.0).
    /// </summary>
    public double Selectivity { get; }

    /// <summary>
    /// Gets the execution strategy for this filter.
    /// </summary>
    public FilterExecutionStrategy ExecutionStrategy { get; }

    public override string Description => 
        $"Filter[{ExecutionStrategy}]({Predicates.Count} predicates, {Selectivity:P0} selectivity)";

    // Filter cost = input cost + (rows * predicate cost * strategy multiplier)
    public override double EstimatedCost
    {
        get
        {
            var inputCost = Input.EstimatedCost;
            var evaluationCost = Input.EstimatedRowCount * Predicates.Count * 0.0001;
            var strategyMultiplier = ExecutionStrategy switch
            {
                FilterExecutionStrategy.SIMD => 0.25,      // SIMD is 4x faster
                FilterExecutionStrategy.Parallel => 0.5,    // Parallel is 2x faster
                FilterExecutionStrategy.Sequential => 1.0,
                _ => 1.0
            };
            return inputCost + (evaluationCost * strategyMultiplier);
        }
    }

    public override long EstimatedRowCount => (long)(Input.EstimatedRowCount * Selectivity);

    public override TResult Accept<TResult>(IPhysicalPlanVisitor<TResult> visitor)
    {
        return visitor.Visit(this);
    }
}

/// <summary>
/// Execution strategy for filter operations.
/// </summary>
public enum FilterExecutionStrategy
{
    /// <summary>
    /// Sequential evaluation (single-threaded, scalar operations).
    /// </summary>
    Sequential,

    /// <summary>
    /// SIMD vectorized evaluation (single-threaded, vector operations).
    /// </summary>
    SIMD,

    /// <summary>
    /// Parallel evaluation (multi-threaded).
    /// </summary>
    Parallel
}

/// <summary>
/// Physical plan for GROUP BY operations with a specific strategy.
/// </summary>
public sealed class PhysicalGroupByPlan : PhysicalPlanNode
{
    public PhysicalGroupByPlan(
        PhysicalPlanNode input,
        string groupByColumn,
        Type groupByKeyType,
        IReadOnlyList<AggregationDescriptor> aggregations,
        string? keyPropertyName,
        GroupByExecutionStrategy strategy)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        GroupByColumn = groupByColumn ?? throw new ArgumentNullException(nameof(groupByColumn));
        GroupByKeyType = groupByKeyType ?? throw new ArgumentNullException(nameof(groupByKeyType));
        Aggregations = aggregations ?? throw new ArgumentNullException(nameof(aggregations));
        KeyPropertyName = keyPropertyName;
        ExecutionStrategy = strategy;
    }

    public PhysicalPlanNode Input { get; }
    public string GroupByColumn { get; }
    public Type GroupByKeyType { get; }
    public IReadOnlyList<AggregationDescriptor> Aggregations { get; }
    public string? KeyPropertyName { get; }
    public GroupByExecutionStrategy ExecutionStrategy { get; }

    public override string Description => 
        $"GroupBy[{ExecutionStrategy}]({GroupByColumn}) ? [{string.Join(", ", Aggregations.Select(a => a.Operation))}]";

    // GroupBy cost = input cost + (rows * hash cost) + (unique groups * aggregate cost)
    public override double EstimatedCost
    {
        get
        {
            var inputCost = Input.EstimatedCost;
            var hashCost = Input.EstimatedRowCount * 0.0002; // Hash table lookup
            var uniqueGroups = EstimatedRowCount;
            var aggregateCost = uniqueGroups * Aggregations.Count * 0.0001;
            
            var strategyMultiplier = ExecutionStrategy switch
            {
                GroupByExecutionStrategy.HashAggregate => 1.0,
                GroupByExecutionStrategy.SortedAggregate => 1.5, // Sort overhead
                _ => 1.0
            };
            
            return inputCost + ((hashCost + aggregateCost) * strategyMultiplier);
        }
    }

    // Estimate unique groups as sqrt of input rows (heuristic)
    public override long EstimatedRowCount => (long)Math.Sqrt(Input.EstimatedRowCount);

    public override TResult Accept<TResult>(IPhysicalPlanVisitor<TResult> visitor)
    {
        return visitor.Visit(this);
    }
}

/// <summary>
/// Execution strategy for GROUP BY operations.
/// </summary>
public enum GroupByExecutionStrategy
{
    /// <summary>
    /// Hash-based aggregation (unsorted output).
    /// </summary>
    HashAggregate,

    /// <summary>
    /// Sort-based aggregation (sorted output, higher cost).
    /// </summary>
    SortedAggregate
}

/// <summary>
/// Physical plan for simple aggregations.
/// </summary>
public sealed class PhysicalAggregatePlan : PhysicalPlanNode
{
    public PhysicalAggregatePlan(
        PhysicalPlanNode input,
        AggregationOperation operation,
        string? columnName,
        Type outputType,
        AggregateExecutionStrategy strategy)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Operation = operation;
        ColumnName = columnName;
        OutputType = outputType ?? throw new ArgumentNullException(nameof(outputType));
        ExecutionStrategy = strategy;
    }

    public PhysicalPlanNode Input { get; }
    public AggregationOperation Operation { get; }
    public string? ColumnName { get; }
    public Type OutputType { get; }
    public AggregateExecutionStrategy ExecutionStrategy { get; }

    public override string Description => 
        $"Aggregate[{ExecutionStrategy}]({Operation}({ColumnName ?? ""}))";

    public override double EstimatedCost
    {
        get
        {
            var inputCost = Input.EstimatedCost;
            var aggregateCost = Input.EstimatedRowCount * 0.0001;
            
            var strategyMultiplier = ExecutionStrategy switch
            {
                AggregateExecutionStrategy.SIMD => 0.25,
                AggregateExecutionStrategy.Parallel => 0.5,
                AggregateExecutionStrategy.Sequential => 1.0,
                _ => 1.0
            };
            
            return inputCost + (aggregateCost * strategyMultiplier);
        }
    }

    public override long EstimatedRowCount => 1; // Aggregates return single value

    public override TResult Accept<TResult>(IPhysicalPlanVisitor<TResult> visitor)
    {
        return visitor.Visit(this);
    }
}

/// <summary>
/// Execution strategy for aggregate operations.
/// </summary>
public enum AggregateExecutionStrategy
{
    Sequential,
    SIMD,
    Parallel
}

/// <summary>
/// Physical plan for LIMIT operations.
/// </summary>
public sealed class PhysicalLimitPlan : PhysicalPlanNode
{
    public PhysicalLimitPlan(PhysicalPlanNode input, int count)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Count = count;
    }

    public PhysicalPlanNode Input { get; }
    public int Count { get; }

    public override string Description => $"Limit({Count})";

    // Limit reduces cost proportionally
    public override double EstimatedCost => Input.EstimatedCost * (Count / (double)Input.EstimatedRowCount);

    public override long EstimatedRowCount => Math.Min(Count, Input.EstimatedRowCount);

    public override TResult Accept<TResult>(IPhysicalPlanVisitor<TResult> visitor)
    {
        return visitor.Visit(this);
    }
}

/// <summary>
/// Physical plan for OFFSET operations.
/// </summary>
public sealed class PhysicalOffsetPlan : PhysicalPlanNode
{
    public PhysicalOffsetPlan(PhysicalPlanNode input, int count)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Count = count;
    }

    public PhysicalPlanNode Input { get; }
    public int Count { get; }

    public override string Description => $"Offset({Count})";

    // Offset still needs to scan skipped rows
    public override double EstimatedCost => Input.EstimatedCost;

    public override long EstimatedRowCount => Math.Max(0, Input.EstimatedRowCount - Count);

    public override TResult Accept<TResult>(IPhysicalPlanVisitor<TResult> visitor)
    {
        return visitor.Visit(this);
    }
}
