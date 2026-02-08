namespace FrozenArrow.Tests.Sql;

/// <summary>
/// Tests for SQL NULL handling support (Phase B Quick Win - Option 2).
/// </summary>
public class SqlNullTests
{
    [ArrowRecord]
    public record NullTestData
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Name")]
        public string? Name { get; init; }

        [ArrowArray(Name = "Email")]
        public string? Email { get; init; }

        [ArrowArray(Name = "Score")]
        public double? Score { get; init; }

        [ArrowArray(Name = "IsActive")]
        public bool? IsActive { get; init; }
    }

    private static FrozenArrow<NullTestData> CreateTestData()
    {
        return new List<NullTestData>
        {
            new() { Id = 1, Name = "Alice", Email = "alice@test.com", Score = 95.5, IsActive = true },
            new() { Id = 2, Name = "Bob", Email = null, Score = 87.0, IsActive = true },
            new() { Id = 3, Name = "Charlie", Email = "charlie@test.com", Score = null, IsActive = false },
            new() { Id = 4, Name = null, Email = null, Score = null, IsActive = null },
            new() { Id = 5, Name = "Eve", Email = "eve@test.com", Score = 92.0, IsActive = null }
        }.ToFrozenArrow();
    }

    [Fact]
    public void SqlQuery_IsNull_FiltersCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<NullTestData, NullTestData>(
            "SELECT * FROM data WHERE Email IS NULL");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Bob (id=2), Unnamed (id=4)
        Assert.All(list, r => Assert.Null(r.Email));
    }

    [Fact]
    public void SqlQuery_IsNotNull_FiltersCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<NullTestData, NullTestData>(
            "SELECT * FROM data WHERE Email IS NOT NULL");

        // Assert
        var list = result.ToList();
        Assert.Equal(3, list.Count); // Alice, Charlie, Eve
        Assert.All(list, r => Assert.NotNull(r.Email));
    }

    [Fact]
    public void SqlQuery_IsNullWithAnd_CombinesCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act - Find records with NULL email AND non-null name
        var result = data.ExecuteSql<NullTestData, NullTestData>(
            "SELECT * FROM data WHERE Email IS NULL AND Name IS NOT NULL");

        // Assert
        var list = result.ToList();
        Assert.Single(list); // Bob (has name, no email)
        Assert.Equal("Bob", list[0].Name);
        Assert.Null(list[0].Email);
    }

    [Fact]
    public void SqlQuery_IsNullWithOr_CombinesCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act - Find records with NULL name OR NULL email
        var result = data.ExecuteSql<NullTestData, NullTestData>(
            "SELECT * FROM data WHERE Name IS NULL OR Email IS NULL");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Bob (null email), Unnamed (null name and email)
    }

    [Fact]
    public void SqlQuery_IsNullNumericColumn_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act - Check nullable double column
        var result = data.ExecuteSql<NullTestData, NullTestData>(
            "SELECT * FROM data WHERE Score IS NULL");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Charlie, Unnamed
        Assert.All(list, r => Assert.Null(r.Score));
    }

    [Fact]
    public void SqlQuery_IsNotNullBooleanColumn_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act - Check nullable boolean column
        var result = data.ExecuteSql<NullTestData, NullTestData>(
            "SELECT * FROM data WHERE IsActive IS NOT NULL");

        // Assert
        var list = result.ToList();
        Assert.Equal(3, list.Count); // Alice, Bob, Charlie (all have IsActive value)
        Assert.All(list, r => Assert.NotNull(r.IsActive));
    }

    [Fact]
    public void SqlQuery_IsNullCount_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var count = data.ExecuteSqlScalar<NullTestData, int>(
            "SELECT COUNT(*) FROM data WHERE Name IS NULL");

        // Assert
        Assert.Equal(1, count); // Only Unnamed (id=4)
    }

    [Fact]
    public void SqlQuery_IsNotNullWithOrderBy_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<NullTestData, NullTestData>(
            "SELECT * FROM data WHERE Score IS NOT NULL ORDER BY Score DESC");

        // Assert
        var list = result.ToList();
        Assert.Equal(3, list.Count); // Alice, Eve, Bob (all have scores)
        
        // Should be sorted descending
        Assert.True(list[0].Score >= list[1].Score);
        Assert.True(list[1].Score >= list[2].Score);
    }

    [Fact]
    public void SqlQuery_ComplexNullQuery_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act - Complex: Has email AND (score > 90 OR name starts with 'E')
        var result = data.ExecuteSql<NullTestData, NullTestData>(
            "SELECT * FROM data WHERE Email IS NOT NULL AND (Score > 90 OR Name LIKE 'E%')");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Alice (score 95.5), Eve (name starts with E)
    }

    [Fact]
    public void SqlParser_IsNull_ParsesCorrectly()
    {
        // Arrange
        var schema = new Dictionary<string, Type>
        {
            ["Name"] = typeof(string),
            ["Email"] = typeof(string)
        };
        
        var columnIndexMap = new Dictionary<string, int>
        {
            ["Name"] = 0,
            ["Email"] = 1
        };

        var parser = new FrozenArrow.Query.Sql.SqlParser(schema, columnIndexMap, 100);

        // Act
        var plan = parser.Parse("SELECT * FROM data WHERE Email IS NULL");

        // Assert
        Assert.NotNull(plan);
        Console.WriteLine($"Plan: {plan}");
    }
}
