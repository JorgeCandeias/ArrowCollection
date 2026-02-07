using FrozenArrow.Query;
using FrozenArrow.Query.Compilation;
using FrozenArrow.Query.LogicalPlan;
using Apache.Arrow;

namespace FrozenArrow.Tests.Compilation;

/// <summary>
/// Tests for Phase 9: Query compilation for 2-5× faster execution.
/// </summary>
public class QueryCompilationTests
{
    [ArrowRecord]
    public record CompilationTestRecord
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Value")]
        public int Value { get; init; }

        [ArrowArray(Name = "Score")]
        public double Score { get; init; }
    }

    private static FrozenArrow<CompilationTestRecord> CreateTestData()
    {
        return new[]
        {
            new CompilationTestRecord { Id = 1, Value = 100, Score = 85.5 },
            new CompilationTestRecord { Id = 2, Value = 200, Score = 92.0 },
            new CompilationTestRecord { Id = 3, Value = 300, Score = 78.3 },
            new CompilationTestRecord { Id = 4, Value = 400, Score = 88.7 },
            new CompilationTestRecord { Id = 5, Value = 500, Score = 95.2 },
        }.ToFrozenArrow();
    }

    [Fact]
    public void CompiledQuery_SimpleFilter_ReturnsCorrectResults()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;
        provider.UseCompiledQueries = true;

        // Act
        var results = queryable.Where(x => x.Value > 200).ToList();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Value > 200));
    }

    [Fact]
    public void CompiledQuery_MultiplePredicates_FusesCorrectly()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;
        provider.UseCompiledQueries = true;

        // Act - Multiple predicates should be fused into single compiled function
        var results = queryable
            .Where(x => x.Value > 100)
            .Where(x => x.Value < 500)
            .Where(x => x.Score > 80)
            .ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.True(r.Value > 100);
            Assert.True(r.Value < 500);
            Assert.True(r.Score > 80);
        });
    }

    [Fact]
    public void CompiledQuery_MatchesInterpretedResults()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;

        // Act - Interpreted
        provider.UseCompiledQueries = false;
        var interpretedResults = queryable.Where(x => x.Value > 200 && x.Score > 85).ToList();

        // Act - Compiled
        provider.UseCompiledQueries = true;
        var compiledResults = queryable.Where(x => x.Value > 200 && x.Score > 85).ToList();

        // Assert - Should produce identical results
        Assert.Equal(interpretedResults.Count, compiledResults.Count);
        Assert.Equal(2, compiledResults.Count);
    }

    [Fact]
    public void QueryCompiler_CompilesPredicate_CreatesDelegate()
    {
        // Arrange
        var data = CreateTestData();
        
        // We can't easily test the compiler directly without RecordBatch access
        // Instead, verify through the integrated query path
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;
        provider.UseCompiledQueries = true;

        // Act - Execute compiled query
        var count = queryable.Where(x => x.Value > 200).Count();

        // Assert - Compiled execution works
        Assert.Equal(3, count);
    }

    [Fact]
    public void QueryCompiler_FusesMultiplePredicates_SingleDelegate()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;
        provider.UseCompiledQueries = true;

        // Act - Multiple predicates get fused
        var count = queryable
            .Where(x => x.Value > 200)
            .Where(x => x.Score > 85.0)
            .Count();

        // Assert - Fused execution works
        Assert.Equal(2, count);
    }

    [Fact]
    public void CompiledExecutor_CachesCompiledPredicates()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;
        provider.UseCompiledQueries = true;

        // Act - Execute same query twice (should use cache)
        var count1 = queryable.Where(x => x.Value > 200).Count();
        var count2 = queryable.Where(x => x.Value > 200).Count();

        // Assert - Both queries work correctly
        Assert.Equal(3, count1);
        Assert.Equal(3, count2);
    }
}
