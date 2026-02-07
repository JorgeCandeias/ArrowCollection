using System.Diagnostics;
using FrozenArrow.Query.LogicalPlan;

namespace FrozenArrow.Query.Adaptive;

/// <summary>
/// Adaptive query executor that learns from execution patterns.
/// Phase 10: Automatically optimizes based on actual query behavior.
/// </summary>
public sealed class AdaptiveQueryExecutor
{
    private readonly ExecutionStatisticsTracker _statsTracker;
    private readonly bool _enabled;

    public AdaptiveQueryExecutor(bool enabled = true)
    {
        _enabled = enabled;
        _statsTracker = new ExecutionStatisticsTracker();
    }

    /// <summary>
    /// Gets the statistics tracker for monitoring and analysis.
    /// </summary>
    public ExecutionStatisticsTracker Statistics => _statsTracker;

    /// <summary>
    /// Executes a query with adaptive strategy selection.
    /// </summary>
    public TResult ExecuteAdaptive<TResult>(
        LogicalPlanNode plan,
        string queryHash,
        Func<ExecutionStrategy, TResult> executor)
    {
        if (!_enabled)
        {
            // Adaptive execution disabled - use default strategy
            return executor(ExecutionStrategy.Parallel);
        }

        // Analyze plan to get characteristics
        var (rowCount, predicateCount) = AnalyzePlan(plan);

        // Get suggested strategy based on learned patterns
        var strategy = _statsTracker.SuggestStrategy(queryHash, rowCount, predicateCount);

        // Execute with timing
        var sw = Stopwatch.StartNew();
        var result = executor(strategy);
        sw.Stop();

        // Record execution for learning
        _statsTracker.RecordExecution(queryHash, new QueryExecutionContext
        {
            Strategy = strategy,
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            RowCount = rowCount,
            PredicateCount = predicateCount
        });

        return result;
    }

    /// <summary>
    /// Analyzes a logical plan to extract characteristics.
    /// </summary>
    private (int RowCount, int PredicateCount) AnalyzePlan(LogicalPlanNode plan)
    {
        int rowCount = 0;
        int predicateCount = 0;

        // Walk the plan tree
        var current = plan;
        while (current != null)
        {
            switch (current)
            {
                case ScanPlan scan:
                    rowCount = (int)scan.EstimatedRowCount;
                    current = null;
                    break;

                case FilterPlan filter:
                    predicateCount += filter.Predicates.Count;
                    current = filter.Input;
                    break;

                case GroupByPlan groupBy:
                    current = groupBy.Input;
                    break;

                case AggregatePlan aggregate:
                    current = aggregate.Input;
                    break;

                case LimitPlan limit:
                    rowCount = Math.Min(rowCount, limit.Count);
                    current = limit.Input;
                    break;

                case OffsetPlan offset:
                    current = offset.Input;
                    break;

                case ProjectPlan project:
                    current = project.Input;
                    break;

                default:
                    current = null;
                    break;
            }
        }

        return (rowCount, predicateCount);
    }

    /// <summary>
    /// Gets recommendations for query optimization.
    /// </summary>
    public List<OptimizationRecommendation> GetRecommendations()
    {
        var recommendations = new List<OptimizationRecommendation>();
        var allStats = _statsTracker.GetAllStatistics();

        foreach (var stats in allStats.Where(s => s.ExecutionCount >= 5))
        {
            if (stats.HasImproved)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    QueryHash = stats.QueryHash,
                    Message = $"Query learned optimal strategy: {stats.OptimalStrategy}",
                    ImpactLevel = ImpactLevel.Medium
                });
            }

            if (stats.AverageExecutionTimeMs > 100)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    QueryHash = stats.QueryHash,
                    Message = $"Slow query detected (avg: {stats.AverageExecutionTimeMs:F2}ms). Consider adding indexes or caching.",
                    ImpactLevel = ImpactLevel.High
                });
            }
        }

        return recommendations;
    }
}

/// <summary>
/// Optimization recommendation based on learned patterns.
/// </summary>
public sealed class OptimizationRecommendation
{
    public string QueryHash { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public ImpactLevel ImpactLevel { get; init; }

    public override string ToString() => $"[{ImpactLevel}] {Message}";
}

/// <summary>
/// Impact level of a recommendation.
/// </summary>
public enum ImpactLevel
{
    Low,
    Medium,
    High
}
