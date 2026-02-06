using FrozenArrow.Query;

namespace FrozenArrow.Tests.LogicalPlan;

/// <summary>
/// Tests for Phase 5: Direct logical plan execution without bridge.
/// </summary>
public class DirectExecutionTests
{
    [ArrowRecord]
    public record DirectTestRecord
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Name")]
        public string Name { get; init; } = string.Empty;

        [ArrowArray(Name = "Age")]
        public int Age { get; init; }

        [ArrowArray(Name = "Score")]
        public double Score { get; init; }

        [ArrowArray(Name = "IsActive")]
        public bool IsActive { get; init; }
    }

    private static FrozenArrow<DirectTestRecord> CreateTestData()
    {
        var records = new[]
        {
            new DirectTestRecord { Id = 1, Name = "Alice", Age = 25, Score = 85.5, IsActive = true },
            new DirectTestRecord { Id = 2, Name = "Bob", Age = 30, Score = 92.0, IsActive = true },
            new DirectTestRecord { Id = 3, Name = "Charlie", Age = 35, Score = 78.3, IsActive = false },
            new DirectTestRecord { Id = 4, Name = "David", Age = 28, Score = 88.7, IsActive = true },
            new DirectTestRecord { Id = 5, Name = "Eve", Age = 32, Score = 95.2, IsActive = true },
            new DirectTestRecord { Id = 6, Name = "Frank", Age = 27, Score = 73.8, IsActive = false },
        };

        return records.ToFrozenArrow();
    }

    [Fact]
    public void DirectExecution_SimpleFilter_MatchesBridge()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;

        // Act - Bridge execution
        provider.UseLogicalPlanExecution = true;
        provider.UseDirectLogicalPlanExecution = false;
        var bridgeResults = queryable.Where(x => x.Age > 28).ToList();

        // Act - Direct execution
        provider.UseDirectLogicalPlanExecution = true;
        var directResults = queryable.Where(x => x.Age > 28).ToList();

        // Assert
        Assert.Equal(bridgeResults.Count, directResults.Count);
        Assert.Equal(3, directResults.Count); // Bob(30), Charlie(35), Eve(32)
    }

    [Fact]
    public void DirectExecution_Count_MatchesBridge()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;

        // Act - Bridge
        provider.UseDirectLogicalPlanExecution = false;
        var bridgeCount = queryable.Where(x => x.IsActive).Count();

        // Act - Direct
        provider.UseDirectLogicalPlanExecution = true;
        var directCount = queryable.Where(x => x.IsActive).Count();

        // Assert
        Assert.Equal(bridgeCount, directCount);
        Assert.Equal(4, directCount);
    }

    [Fact]
    public void DirectExecution_Any_MatchesBaseline()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;

        // Test baseline behavior (no logical plans)
        provider.UseLogicalPlanExecution = false;
        var hasOldPeopleBaseline = queryable.Any(x => x.Age > 100);
        var hasYoungPeopleBaseline = queryable.Any(x => x.Age < 30);

        // Test with direct execution
        provider.UseLogicalPlanExecution = true;
        provider.UseDirectLogicalPlanExecution = true;
        var hasOldPeopleDirect = queryable.Any(x => x.Age > 100);
        var hasYoungPeopleDirect = queryable.Any(x => x.Age < 30);

        // Assert - direct should match baseline
        Assert.Equal(hasOldPeopleBaseline, hasOldPeopleDirect);
        Assert.Equal(hasYoungPeopleBaseline, hasYoungPeopleDirect);
    }

    [Fact]
    public void DirectExecution_First_Works()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;
        provider.UseDirectLogicalPlanExecution = true;

        // Act
        var first = queryable.Where(x => x.Age > 30).First();

        // Assert
        Assert.Equal("Charlie", first.Name);
        Assert.Equal(35, first.Age);
    }

    [Fact]
    public void DirectExecution_GroupBy_MatchesBridge()
    {
        // Arrange
        var data = new[]
        {
            new DirectTestRecord { Id = 1, Name = "Alice", Age = 25, Score = 85.5, IsActive = true },
            new DirectTestRecord { Id = 2, Name = "Bob", Age = 25, Score = 92.0, IsActive = true },
            new DirectTestRecord { Id = 3, Name = "Charlie", Age = 30, Score = 78.3, IsActive = true },
            new DirectTestRecord { Id = 4, Name = "David", Age = 30, Score = 88.7, IsActive = true },
            new DirectTestRecord { Id = 5, Name = "Eve", Age = 35, Score = 95.2, IsActive = true },
        }.ToFrozenArrow();

        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;

        // Act - Bridge
        provider.UseDirectLogicalPlanExecution = false;
        var bridgeResults = queryable
            .GroupBy(x => x.Age)
            .Select(g => new { Age = g.Key, Count = g.Count() })
            .ToList()
            .OrderBy(x => x.Age)
            .ToList();

        // Act - Direct
        provider.UseDirectLogicalPlanExecution = true;
        var directResults = queryable
            .GroupBy(x => x.Age)
            .Select(g => new { Age = g.Key, Count = g.Count() })
            .ToList()
            .OrderBy(x => x.Age)
            .ToList();

        // Assert
        Assert.Equal(bridgeResults.Count, directResults.Count);
        Assert.Equal(3, directResults.Count);
        
        Assert.Equal(25, directResults[0].Age);
        Assert.Equal(2, directResults[0].Count);
        
        Assert.Equal(30, directResults[1].Age);
        Assert.Equal(2, directResults[1].Count);
        
        Assert.Equal(35, directResults[2].Age);
        Assert.Equal(1, directResults[2].Count);
    }
}
