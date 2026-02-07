namespace FrozenArrow.Tests.Sql;

/// <summary>
/// Tests for SQL DISTINCT support (Phase B).
/// </summary>
public class SqlDistinctTests
{
    [ArrowRecord]
    public record DistinctTestData
    {
        [ArrowArray(Name = "Category")]
        public string Category { get; init; } = string.Empty;

        [ArrowArray(Name = "Value")]
        public int Value { get; init; }
    }

    [Fact]
    public void SqlQuery_Distinct_RemovesDuplicates()
    {
        // Arrange
        var data = new List<DistinctTestData>
        {
            new() { Category = "A", Value = 100 },
            new() { Category = "A", Value = 100 }, // Duplicate
            new() { Category = "B", Value = 200 },
            new() { Category = "A", Value = 100 }, // Another duplicate
            new() { Category = "C", Value = 300 }
        }.ToFrozenArrow();

        // Act
        var result = data.ExecuteSql<DistinctTestData, DistinctTestData>(
            "SELECT DISTINCT Category, Value FROM data");

        // Assert
        var list = result.ToList();
        
        // Should have 3 unique records: A/100, B/200, C/300
        Assert.Equal(3, list.Count);
        Assert.Contains(list, r => r.Category == "A" && r.Value == 100);
        Assert.Contains(list, r => r.Category == "B" && r.Value == 200);
        Assert.Contains(list, r => r.Category == "C" && r.Value == 300);
    }

    [Fact]
    public void SqlQuery_DistinctWithWhere_WorksCorrectly()
    {
        // Arrange
        var data = new List<DistinctTestData>
        {
            new() { Category = "A", Value = 100 },
            new() { Category = "A", Value = 100 }, // Duplicate
            new() { Category = "B", Value = 200 },
            new() { Category = "A", Value = 150 }, // Different value
            new() { Category = "C", Value = 300 }
        }.ToFrozenArrow();

        // Act - DISTINCT with WHERE clause
        var result = data.ExecuteSql<DistinctTestData, DistinctTestData>(
            "SELECT DISTINCT Category, Value FROM data WHERE Value >= 150");

        // Assert
        var list = result.ToList();
        
        // Should have 3 unique records: A/150, B/200, C/300
        // (A/100 is filtered out by WHERE)
        Assert.Equal(3, list.Count);
        Assert.Contains(list, r => r.Category == "A" && r.Value == 150);
        Assert.Contains(list, r => r.Category == "B" && r.Value == 200);
        Assert.Contains(list, r => r.Category == "C" && r.Value == 300);
    }

    [Fact]
    public void SqlParser_DistinctKeyword_CreatesDistinctPlan()
    {
        // Arrange
        var schema = new Dictionary<string, Type>
        {
            ["Category"] = typeof(string),
            ["Value"] = typeof(int)
        };

        var columnIndexMap = new Dictionary<string, int>
        {
            ["Category"] = 0,
            ["Value"] = 1
        };

        var parser = new FrozenArrow.Query.Sql.SqlParser(schema, columnIndexMap, 100);

        // Act
        var sql = "SELECT DISTINCT Category FROM data";
        var plan = parser.Parse(sql);

        // Assert
        Console.WriteLine($"Plan type: {plan.GetType().Name}");
        Console.WriteLine($"Plan: {plan}");

        // The plan should contain a DistinctPlan somewhere in the tree
        Assert.Contains("Distinct", plan.ToString());
    }
}
