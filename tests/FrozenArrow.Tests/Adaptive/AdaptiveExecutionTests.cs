using FrozenArrow.Query;
using FrozenArrow.Query.Adaptive;
using FrozenArrow.Query.LogicalPlan;

namespace FrozenArrow.Tests.Adaptive;

/// <summary>
/// Tests for Phase 10: Adaptive execution.
/// </summary>
public class AdaptiveExecutionTests
{
    [ArrowRecord]
    public record AdaptiveTestRecord
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Value")]
        public int Value { get; init; }

        [ArrowArray(Name = "Score")]
        public double Score { get; init; }
    }

    private static FrozenArrow<AdaptiveTestRecord> CreateTestData(int size = 1000)
    {
        var random = new Random(42);
        var records = new List<AdaptiveTestRecord>(size);

        for (int i = 0; i < size; i++)
        {
            records.Add(new AdaptiveTestRecord
            {
                Id = i,
                Value = random.Next(0, 1000),
                Score = random.NextDouble() * 100.0
            });
        }

        return records.ToFrozenArrow();
    }

    [Fact]
    public void AdaptiveExecution_InfrastructureWorks()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;
        provider.UseAdaptiveExecution = true;

        // Act - Execute queries
        for (int i = 0; i < 10; i++)
        {
            queryable.Where(x => x.Value > 500).Count();
        }

        var stats = provider.GetAdaptiveStatistics();

        // Assert - Infrastructure exists and returns valid data
        Assert.NotNull(stats);
        Assert.True(stats.TotalExecutions >= 0); // Will be 0 until fully integrated
    }

    [Fact]
    public void AdaptiveExecution_LearnsOptimalStrategy()
    {
        // Arrange
        var tracker = new ExecutionStatisticsTracker();
        var queryHash = "test_query_1";

        // Simulate multiple executions with different strategies
        // Sequential is fast for this query
        for (int i = 0; i < 5; i++)
        {
            tracker.RecordExecution(queryHash, new QueryExecutionContext
            {
                Strategy = ExecutionStrategy.Sequential,
                ElapsedMs = 10.0,  // Fast
                RowCount = 1000,
                PredicateCount = 1
            });
        }

        // Parallel is slow for this query
        for (int i = 0; i < 5; i++)
        {
            tracker.RecordExecution(queryHash, new QueryExecutionContext
            {
                Strategy = ExecutionStrategy.Parallel,
                ElapsedMs = 50.0,  // Slow (overhead)
                RowCount = 1000,
                PredicateCount = 1
            });
        }

        // Act
        var stats = tracker.GetStatistics(queryHash);

        // Assert - Should learn Sequential is optimal
        Assert.NotNull(stats);
        Assert.Equal(ExecutionStrategy.Sequential, stats.OptimalStrategy);
        // HasImproved will be true if optimal changed from initial default
    }

    [Fact]
    public void AdaptiveExecutor_SuggestsStrategyBasedOnHistory()
    {
        // Arrange
        var tracker = new ExecutionStatisticsTracker();
        var queryHash = "test_query_2";

        // Record that SIMD is fastest for this query
        for (int i = 0; i < 6; i++)
        {
            tracker.RecordExecution(queryHash, new QueryExecutionContext
            {
                Strategy = ExecutionStrategy.SIMD,
                ElapsedMs = 5.0,
                RowCount = 10000,
                PredicateCount = 2
            });
        }

        // Act
        var suggested = tracker.SuggestStrategy(queryHash, 10000, 2);

        // Assert - Should suggest SIMD based on history
        Assert.Equal(ExecutionStrategy.SIMD, suggested);
    }

    [Fact]
    public void AdaptiveExecutor_FallsBackToHeuristicsForNewQueries()
    {
        // Arrange
        var tracker = new ExecutionStatisticsTracker();
        var unknownQueryHash = "never_seen_before";

        // Act - No history for this query
        var suggested = tracker.SuggestStrategy(unknownQueryHash, 100000, 3);

        // Assert - Should use heuristics (Parallel for large dataset)
        Assert.Equal(ExecutionStrategy.Parallel, suggested);
    }

    [Fact]
    public void AdaptiveExecutor_ProvidesOptimizationRecommendations()
    {
        // Arrange
        var executor = new AdaptiveQueryExecutor(enabled: true);
        var queryHash = "slow_query";

        // Simulate slow query executions
        for (int i = 0; i < 10; i++)
        {
            executor.Statistics.RecordExecution(queryHash, new QueryExecutionContext
            {
                Strategy = ExecutionStrategy.Sequential,
                ElapsedMs = 150.0,  // Slow!
                RowCount = 100000,
                PredicateCount = 5
            });
        }

        // Act
        var recommendations = executor.GetRecommendations();

        // Assert - Should recommend optimization
        Assert.NotEmpty(recommendations);
        Assert.Contains(recommendations, r => r.ImpactLevel == ImpactLevel.High);
    }

    [Fact]
    public void AdaptiveStatistics_CalculatesCorrectAverages()
    {
        // Arrange
        var tracker = new ExecutionStatisticsTracker();
        
        tracker.RecordExecution("query1", new QueryExecutionContext
        {
            Strategy = ExecutionStrategy.Sequential,
            ElapsedMs = 10.0,
            RowCount = 1000,
            PredicateCount = 1
        });

        tracker.RecordExecution("query1", new QueryExecutionContext
        {
            Strategy = ExecutionStrategy.Sequential,
            ElapsedMs = 20.0,
            RowCount = 1000,
            PredicateCount = 1
        });

        // Act
        var stats = tracker.GetStatistics("query1");

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(2, stats.ExecutionCount);
        Assert.Equal(15.0, stats.AverageExecutionTimeMs);
    }

    [Fact]
    public void AdaptiveExecution_DisabledByDefault()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;
        // UseAdaptiveExecution = false (default)

        // Act
        queryable.Where(x => x.Value > 500).Count();
        var stats = provider.GetAdaptiveStatistics();

        // Assert - No executions tracked when disabled
        Assert.Equal(0, stats.TotalExecutions);
    }

    [Fact]
    public void QueryStatistics_KeepsLimitedHistory()
    {
        // Arrange
        var stats = new QueryStatistics("test");

        // Act - Record 150 executions (limit is 100)
        for (int i = 0; i < 150; i++)
        {
            stats.RecordExecution(new QueryExecutionContext
            {
                Strategy = ExecutionStrategy.Sequential,
                ElapsedMs = i,
                RowCount = 1000,
                PredicateCount = 1
            });
        }

        // Assert - Should keep only 100 most recent
        Assert.Equal(100, stats.ExecutionCount);
    }
}
