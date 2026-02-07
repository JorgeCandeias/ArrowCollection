namespace FrozenArrow.Tests.Sql;

/// <summary>
/// Tests for SQL column projection support (Phase B Part 3).
/// </summary>
public class SqlProjectionTests
{
    [ArrowRecord]
    public record ProjectionTestData
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Name")]
        public string Name { get; init; } = string.Empty;

        [ArrowArray(Name = "Email")]
        public string Email { get; init; } = string.Empty;

        [ArrowArray(Name = "Score")]
        public double Score { get; init; }

        [ArrowArray(Name = "Age")]
        public int Age { get; init; }
    }

    private static FrozenArrow<ProjectionTestData> CreateTestData()
    {
        return new List<ProjectionTestData>
        {
            new() { Id = 1, Name = "Alice", Email = "alice@test.com", Score = 92.0, Age = 25 },
            new() { Id = 2, Name = "Bob", Email = "bob@test.com", Score = 78.5, Age = 30 },
            new() { Id = 3, Name = "Charlie", Email = "charlie@test.com", Score = 85.5, Age = 28 }
        }.ToFrozenArrow();
    }

    [Fact]
    public void SqlQuery_SelectStar_ReturnsAllColumns()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<ProjectionTestData, ProjectionTestData>(
            "SELECT * FROM data");

        // Assert
        var list = result.ToList();
        Assert.Equal(3, list.Count);
        
        // Verify all columns present
        Assert.Equal(1, list[0].Id);
        Assert.Equal("Alice", list[0].Name);
        Assert.Equal("alice@test.com", list[0].Email);
        Assert.Equal(92.0, list[0].Score);
        Assert.Equal(25, list[0].Age);
    }

    [Fact]
    public void SqlQuery_SelectTwoColumns_CreatesProjectPlan()
    {
        // Act - Parse only, execution returns ProjectedRow which can't be cast to original type
        // This is a known limitation - SQL projection changes the result type
        var schema = new Dictionary<string, Type>
        {
            ["Id"] = typeof(int),
            ["Name"] = typeof(string),
            ["Email"] = typeof(string),
            ["Score"] = typeof(double),
            ["Age"] = typeof(int)
        };
        
        var columnIndexMap = new Dictionary<string, int>
        {
            ["Id"] = 0,
            ["Name"] = 1,
            ["Email"] = 2,
            ["Score"] = 3,
            ["Age"] = 4
        };

        var parser = new FrozenArrow.Query.Sql.SqlParser(schema, columnIndexMap, 3);
        var plan = parser.Parse("SELECT Name, Score FROM data");

        // Assert - Verify projection plan was created
        Assert.Contains("Project", plan.ToString());
    }

    [Fact]
    public void SqlQuery_SelectWithAlias_ParsesCorrectly()
    {
        // Arrange - Test parsing only
        var schema = new Dictionary<string, Type>
        {
            ["Name"] = typeof(string),
            ["Age"] = typeof(int),
            ["Score"] = typeof(double)
        };
        
        var columnIndexMap = new Dictionary<string, int>
        {
            ["Name"] = 0,
            ["Age"] = 1,
            ["Score"] = 2
        };

        var parser = new FrozenArrow.Query.Sql.SqlParser(schema, columnIndexMap, 100);

        // Act
        var plan = parser.Parse("SELECT Name AS FullName, Age AS Years FROM data");

        // Assert - Verify it parses without error
        Assert.NotNull(plan);
        Console.WriteLine($"Plan: {plan}");
    }

    [Fact]
    public void SqlQuery_SelectSingleColumn_ParsesCorrectly()
    {
        // Arrange
        var schema = new Dictionary<string, Type>
        {
            ["Name"] = typeof(string),
            ["Age"] = typeof(int)
        };
        
        var columnIndexMap = new Dictionary<string, int>
        {
            ["Name"] = 0,
            ["Age"] = 1
        };

        var parser = new FrozenArrow.Query.Sql.SqlParser(schema, columnIndexMap, 100);

        // Act
        var plan = parser.Parse("SELECT Name FROM data");

        // Assert
        Assert.Contains("Project", plan.ToString());
    }

    [Fact]
    public void SqlQuery_SelectWithWhereAndProjection_ParsesCorrectly()
    {
        // Arrange
        var schema = new Dictionary<string, Type>
        {
            ["Name"] = typeof(string),
            ["Score"] = typeof(double),
            ["Age"] = typeof(int),
            ["Email"] = typeof(string)
        };
        
        var columnIndexMap = new Dictionary<string, int>
        {
            ["Name"] = 0,
            ["Score"] = 1,
            ["Age"] = 2,
            ["Email"] = 3
        };

        var parser = new FrozenArrow.Query.Sql.SqlParser(schema, columnIndexMap, 100);

        // Act - Verify parsing works
        var plan = parser.Parse("SELECT Name, Score FROM data WHERE Age >= 28");

        // Assert - Check that projection was created (2 columns out of 4)
        Assert.NotNull(plan);
        Console.WriteLine($"Plan: {plan}");
        Assert.Contains("Project", plan.ToString());
    }

    [Fact]
    public void SqlQuery_SelectWithOrderBy_ParsesCorrectly()
    {
        // Arrange
        var schema = new Dictionary<string, Type>
        {
            ["Name"] = typeof(string),
            ["Age"] = typeof(int)
        };
        
        var columnIndexMap = new Dictionary<string, int>
        {
            ["Name"] = 0,
            ["Age"] = 1
        };

        var parser = new FrozenArrow.Query.Sql.SqlParser(schema, columnIndexMap, 100);

        // Act
        var plan = parser.Parse("SELECT Name, Age FROM data ORDER BY Age DESC");

        // Assert
        Assert.Contains("Sort", plan.ToString());
    }

    [Fact]
    public void SqlParser_SelectColumns_CreatesProjectPlan()
    {
        // Arrange
        var schema = new Dictionary<string, Type>
        {
            ["Name"] = typeof(string),
            ["Age"] = typeof(int),
            ["Score"] = typeof(double),
            ["Email"] = typeof(string)
        };
        
        var columnIndexMap = new Dictionary<string, int>
        {
            ["Name"] = 0,
            ["Age"] = 1,
            ["Score"] = 2,
            ["Email"] = 3
        };

        var parser = new FrozenArrow.Query.Sql.SqlParser(schema, columnIndexMap, 100);

        // Act
        var sql = "SELECT Name, Age FROM data";
        var plan = parser.Parse(sql);

        // Assert
        Console.WriteLine($"Plan type: {plan.GetType().Name}");
        Console.WriteLine($"Plan: {plan}");
        
        // The plan should contain projection (2 columns out of 4)
        Assert.Contains("Project", plan.ToString());
    }

    [Fact]
    public void SqlQuery_InvalidColumn_ThrowsException()
    {
        // Arrange
        var data = CreateTestData();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            data.ExecuteSql<ProjectionTestData, ProjectionTestData>(
                "SELECT NonExistentColumn FROM data"));
    }
}
