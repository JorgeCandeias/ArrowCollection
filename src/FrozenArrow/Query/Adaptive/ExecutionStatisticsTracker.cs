using System.Collections.Concurrent;
using System.Diagnostics;

namespace FrozenArrow.Query.Adaptive;

/// <summary>
/// Tracks execution statistics for adaptive query optimization.
/// Phase 10: Learns from actual query patterns to optimize automatically.
/// </summary>
public sealed class ExecutionStatisticsTracker
{
    private readonly ConcurrentDictionary<string, QueryStatistics> _statistics = new();
    private readonly object _lock = new();
    
    /// <summary>
    /// Records execution statistics for a query.
    /// </summary>
    public void RecordExecution(string queryHash, QueryExecutionContext context)
    {
        var stats = _statistics.GetOrAdd(queryHash, _ => new QueryStatistics(queryHash));
        stats.RecordExecution(context);
    }

    /// <summary>
    /// Gets statistics for a specific query.
    /// </summary>
    public QueryStatistics? GetStatistics(string queryHash)
    {
        return _statistics.TryGetValue(queryHash, out var stats) ? stats : null;
    }

    /// <summary>
    /// Gets all tracked query statistics.
    /// </summary>
    public IReadOnlyList<QueryStatistics> GetAllStatistics()
    {
        return _statistics.Values.ToList();
    }

    /// <summary>
    /// Suggests optimal strategy based on learned patterns.
    /// </summary>
    public ExecutionStrategy SuggestStrategy(string queryHash, int rowCount, int predicateCount)
    {
        var stats = GetStatistics(queryHash);
        
        if (stats != null && stats.ExecutionCount >= 5)
        {
            // Have enough data - use learned strategy
            return stats.OptimalStrategy;
        }

        // Fall back to heuristics
        return EstimateStrategy(rowCount, predicateCount);
    }

    private ExecutionStrategy EstimateStrategy(int rowCount, int predicateCount)
    {
        // Default heuristics (can be overridden by learning)
        if (rowCount < 1_000)
        {
            return ExecutionStrategy.Sequential;
        }
        
        if (rowCount < 50_000)
        {
            return ExecutionStrategy.SIMD;
        }

        return ExecutionStrategy.Parallel;
    }

    /// <summary>
    /// Clears all statistics (useful for testing or reset).
    /// </summary>
    public void Clear()
    {
        _statistics.Clear();
    }

    /// <summary>
    /// Gets summary statistics across all queries.
    /// </summary>
    public AdaptiveStatisticsSummary GetSummary()
    {
        var allStats = _statistics.Values.ToList();
        
        return new AdaptiveStatisticsSummary
        {
            TotalQueries = allStats.Count,
            TotalExecutions = allStats.Sum(s => s.ExecutionCount),
            AverageExecutionTime = allStats.Any() ? allStats.Average(s => s.AverageExecutionTimeMs) : 0.0,
            AdaptiveImprovements = allStats.Count(s => s.HasImproved)
        };
    }
}

/// <summary>
/// Statistics for a specific query pattern.
/// </summary>
public sealed class QueryStatistics
{
    private readonly object _lock = new();
    private readonly List<ExecutionMeasurement> _measurements = new();
    private ExecutionStrategy _optimalStrategy = ExecutionStrategy.Sequential;

    public string QueryHash { get; }
    public int ExecutionCount => _measurements.Count;
    public double AverageExecutionTimeMs => _measurements.Average(m => m.ExecutionTimeMs);
    public ExecutionStrategy OptimalStrategy => _optimalStrategy;
    public bool HasImproved { get; private set; }

    public QueryStatistics(string queryHash)
    {
        QueryHash = queryHash;
    }

    public void RecordExecution(QueryExecutionContext context)
    {
        lock (_lock)
        {
            var measurement = new ExecutionMeasurement
            {
                Strategy = context.Strategy,
                ExecutionTimeMs = context.ElapsedMs,
                RowCount = context.RowCount,
                PredicateCount = context.PredicateCount,
                Timestamp = DateTime.UtcNow
            };

            _measurements.Add(measurement);

            // Keep only last 100 measurements
            if (_measurements.Count > 100)
            {
                _measurements.RemoveAt(0);
            }

            // Analyze and update optimal strategy
            UpdateOptimalStrategy();
        }
    }

    private void UpdateOptimalStrategy()
    {
        if (_measurements.Count < 3) return;

        // Group by strategy and compute average time
        var strategyPerformance = _measurements
            .GroupBy(m => m.Strategy)
            .Select(g => new
            {
                Strategy = g.Key,
                AvgTime = g.Average(m => m.ExecutionTimeMs),
                Count = g.Count()
            })
            .Where(x => x.Count >= 2)  // Need at least 2 samples
            .OrderBy(x => x.AvgTime)
            .ToList();

        if (strategyPerformance.Any())
        {
            var newOptimal = strategyPerformance.First().Strategy;
            
            if (newOptimal != _optimalStrategy)
            {
                _optimalStrategy = newOptimal;
                HasImproved = true;
            }
        }
    }

    public override string ToString()
    {
        return $"Query: {QueryHash}, Executions: {ExecutionCount}, " +
               $"Avg Time: {AverageExecutionTimeMs:F2}ms, Optimal: {OptimalStrategy}";
    }
}

/// <summary>
/// Context for a single query execution.
/// </summary>
public sealed class QueryExecutionContext
{
    public ExecutionStrategy Strategy { get; init; }
    public double ElapsedMs { get; init; }
    public int RowCount { get; init; }
    public int PredicateCount { get; init; }
}

/// <summary>
/// Measurement of a single execution.
/// </summary>
internal sealed class ExecutionMeasurement
{
    public ExecutionStrategy Strategy { get; init; }
    public double ExecutionTimeMs { get; init; }
    public int RowCount { get; init; }
    public int PredicateCount { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Execution strategy enum.
/// </summary>
public enum ExecutionStrategy
{
    Sequential,
    SIMD,
    Parallel,
    Compiled
}

/// <summary>
/// Summary statistics across all queries.
/// </summary>
public sealed class AdaptiveStatisticsSummary
{
    public int TotalQueries { get; init; }
    public long TotalExecutions { get; init; }
    public double AverageExecutionTime { get; init; }
    public int AdaptiveImprovements { get; init; }

    public override string ToString()
    {
        return $"Queries: {TotalQueries}, Executions: {TotalExecutions}, " +
               $"Avg Time: {AverageExecutionTime:F2}ms, Improvements: {AdaptiveImprovements}";
    }
}
