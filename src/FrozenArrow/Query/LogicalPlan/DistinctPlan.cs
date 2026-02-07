namespace FrozenArrow.Query.LogicalPlan;

/// <summary>
/// Logical plan node representing a DISTINCT operation (deduplication).
/// Phase B: DISTINCT support.
/// </summary>
public sealed class DistinctPlan(LogicalPlanNode input) : LogicalPlanNode
{
    public LogicalPlanNode Input { get; } = input ?? throw new ArgumentNullException(nameof(input));

    public override string Description => "Distinct";

    public override long EstimatedRowCount
    {
        get
        {
            // Estimate: DISTINCT typically reduces row count by 50-90%
            // Conservative estimate: 70% reduction
            return (long)(Input.EstimatedRowCount * 0.3);
        }
    }

    public override IReadOnlyDictionary<string, Type> OutputSchema => Input.OutputSchema;

    public override TResult Accept<TResult>(ILogicalPlanVisitor<TResult> visitor)
    {
        // DistinctPlan doesn't need special optimization
        // Just return the plan as-is (cast to TResult)
        // This assumes TResult is LogicalPlanNode for optimizer visitors
        return (TResult)(object)this;
    }

    public override string ToString()
    {
        return $"Distinct({Input})";
    }
}
