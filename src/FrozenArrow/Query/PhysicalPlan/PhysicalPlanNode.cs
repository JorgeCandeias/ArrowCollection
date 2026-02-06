namespace FrozenArrow.Query.PhysicalPlan;

/// <summary>
/// Base class for physical execution plans.
/// Physical plans represent HOW to execute a query, with specific strategies and optimizations.
/// </summary>
public abstract class PhysicalPlanNode
{
    /// <summary>
    /// Gets a human-readable description of this physical plan node.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Gets the estimated cost of executing this plan node.
    /// Used for comparing different execution strategies.
    /// </summary>
    public abstract double EstimatedCost { get; }

    /// <summary>
    /// Gets the estimated number of rows this plan will produce.
    /// </summary>
    public abstract long EstimatedRowCount { get; }

    /// <summary>
    /// Gets the physical properties of the output (ordering, partitioning, etc.).
    /// </summary>
    public virtual PhysicalProperties OutputProperties { get; } = PhysicalProperties.Unordered;

    /// <summary>
    /// Accepts a visitor for traversing the physical plan tree.
    /// </summary>
    public abstract TResult Accept<TResult>(IPhysicalPlanVisitor<TResult> visitor);
}

/// <summary>
/// Physical properties of data (ordering, partitioning, etc.).
/// </summary>
public sealed class PhysicalProperties
{
    public static readonly PhysicalProperties Unordered = new(isOrdered: false, isPartitioned: false);
    public static readonly PhysicalProperties Ordered = new(isOrdered: true, isPartitioned: false);
    public static readonly PhysicalProperties Partitioned = new(isOrdered: false, isPartitioned: true);

    private PhysicalProperties(bool isOrdered, bool isPartitioned)
    {
        IsOrdered = isOrdered;
        IsPartitioned = isPartitioned;
    }

    /// <summary>
    /// Gets whether the data is ordered.
    /// </summary>
    public bool IsOrdered { get; }

    /// <summary>
    /// Gets whether the data is partitioned across multiple threads.
    /// </summary>
    public bool IsPartitioned { get; }
}

/// <summary>
/// Visitor interface for traversing physical plan trees.
/// </summary>
public interface IPhysicalPlanVisitor<TResult>
{
    TResult Visit(PhysicalScanPlan scan);
    TResult Visit(PhysicalFilterPlan filter);
    TResult Visit(PhysicalGroupByPlan groupBy);
    TResult Visit(PhysicalAggregatePlan aggregate);
    TResult Visit(PhysicalLimitPlan limit);
    TResult Visit(PhysicalOffsetPlan offset);
}
