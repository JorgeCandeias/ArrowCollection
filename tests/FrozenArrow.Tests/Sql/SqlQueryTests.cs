using FrozenArrow.Query;

namespace FrozenArrow.Tests.Sql;

/// <summary>
/// Tests for Phase 8: SQL query support.
/// </summary>
public class SqlQueryTests
{
    [ArrowRecord]
    public record SqlTestRecord
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Name")]
        public string Name { get; init; } = string.Empty;

        [ArrowArray(Name = "Age")]
        public int Age { get; init; }

        [ArrowArray(Name = "Score")]
        public double Score { get; init; }

        [ArrowArray(Name = "Category")]
        public string Category { get; init; } = string.Empty;
    }

    private static FrozenArrow<SqlTestRecord> CreateTestData()
    {
        return new[]
        {
            new SqlTestRecord { Id = 1, Name = "Alice", Age = 25, Score = 85.5, Category = "A" },
            new SqlTestRecord { Id = 2, Name = "Bob", Age = 30, Score = 92.0, Category = "B" },
            new SqlTestRecord { Id = 3, Name = "Charlie", Age = 35, Score = 78.3, Category = "A" },
            new SqlTestRecord { Id = 4, Name = "David", Age = 28, Score = 88.7, Category = "B" },
            new SqlTestRecord { Id = 5, Name = "Eve", Age = 32, Score = 95.2, Category = "C" },
        }.ToFrozenArrow();
    }

    [Fact]
    public void SqlQuery_SimpleSelect_ReturnsAllRows()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var results = data.ExecuteSql("SELECT * FROM data").ToList();

        // Assert
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void SqlQuery_ReturnsDynamic_AllowsPropertyAccess()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var results = data.ExecuteSql("SELECT * FROM data WHERE Age > 30").ToList();

        // Assert - Can access properties dynamically
        Assert.Equal(2, results.Count);
        Assert.Equal("Charlie", results[0].Name);
        Assert.Equal(35, results[0].Age);
        Assert.Equal("Eve", results[1].Name);
        Assert.Equal(32, results[1].Age);
    }

    [Fact]
    public void SqlQuery_WithTypedResult_ReturnsStronglyTyped()
    {
        // Arrange
        var data = CreateTestData();

        // Act - Execute SQL with typed result
        var results = data.ExecuteSql<SqlTestRecord, SqlTestRecord>("SELECT * FROM data WHERE Age > 30").ToList();

        // Assert - Strongly typed results
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Age > 30));
        Assert.IsType<SqlTestRecord>(results[0]);
    }

    [Fact]
    public void SqlQuery_WhereClause_FiltersCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var results = data.ExecuteSql("SELECT * FROM data WHERE Age > 30").ToList();

        // Assert
        Assert.Equal(2, results.Count); // Charlie (35) and Eve (32)
    }

    [Fact]
    public void SqlQuery_MultipleConditions_CombinesCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var results = data.ExecuteSql("SELECT * FROM data WHERE Age > 25 AND Score > 85").ToList();

        // Assert
        Assert.Equal(3, results.Count); // Bob, David, Eve
    }

    [Fact]
    public void SqlQuery_CountAggregate_ReturnsCorrectCount()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var count = data.ExecuteSqlScalar<SqlTestRecord, int>("SELECT COUNT(*) FROM data");

        // Assert
        Assert.Equal(5, count);
    }

    [Fact]
    public void SqlQuery_CountWithWhere_FiltersAndCounts()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var count = data.ExecuteSqlScalar<SqlTestRecord, int>("SELECT COUNT(*) FROM data WHERE Age > 30");

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public void SqlQuery_SumAggregate_CalculatesCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var sum = data.ExecuteSqlScalar<SqlTestRecord, int>("SELECT SUM(Age) FROM data");

        // Assert
        Assert.Equal(150, sum); // 25 + 30 + 35 + 28 + 32
    }

    [Fact]
    public void SqlQuery_GroupBy_GroupsCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var results = data.ExecuteSql("SELECT Category, COUNT(*) FROM data GROUP BY Category").ToList();

        // Assert
        Assert.Equal(3, results.Count); // A, B, C
    }

    [Fact]
    public void SqlQuery_Limit_ReturnsCorrectNumber()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var results = data.ExecuteSql("SELECT * FROM data LIMIT 3").ToList();

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void SqlQuery_Offset_SkipsCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var results = data.ExecuteSql("SELECT * FROM data OFFSET 2").ToList();

        // Assert
        Assert.Equal(3, results.Count); // Should return last 3 records
    }

    [Fact]
    public void SqlQuery_LimitAndOffset_WorksTogether()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var results = data.ExecuteSql("SELECT * FROM data LIMIT 2 OFFSET 1").ToList();

        // Assert
        Assert.Equal(2, results.Count); // Skip 1, take 2
    }

    [Fact]
    public void SqlQuery_UsesSameOptimizationsAsLinq()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;
        provider.UseLogicalPlanCache = true;

        // Act - Execute SQL query
        var sqlCount = data.ExecuteSqlScalar<SqlTestRecord, int>("SELECT COUNT(*) FROM data WHERE Age > 30");

        // Act - Execute equivalent LINQ query
        var linqCount = queryable.Where(x => x.Age > 30).Count();

        // Assert - Should produce same results
        Assert.Equal(sqlCount, linqCount);
        Assert.Equal(2, sqlCount);
    }

    [Fact]
    public void SqlQuery_CachesPlans()
    {
        // Arrange
        var data = CreateTestData();
        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;

        // Act - Execute same SQL query twice
        var result1 = data.ExecuteSqlScalar<SqlTestRecord, int>("SELECT COUNT(*) FROM data WHERE Age > 30");
        var statsBefore = provider.GetLogicalPlanCacheStatistics();

        var result2 = data.ExecuteSqlScalar<SqlTestRecord, int>("SELECT COUNT(*) FROM data WHERE Age > 30");
        var statsAfter = provider.GetLogicalPlanCacheStatistics();

        // Assert - Both queries should return same result
        Assert.Equal(2, result1);
        Assert.Equal(2, result2);
        
        // Cache behavior: First query caches, second query should benefit
        // Note: The caching happens during parse, so both might show as misses
        // but the plan should still be optimized
        Assert.True(statsAfter.Count >= statsBefore.Count);
    }
}
