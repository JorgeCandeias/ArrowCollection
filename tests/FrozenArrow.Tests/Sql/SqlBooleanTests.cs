namespace FrozenArrow.Tests.Sql;

/// <summary>
/// Tests for SQL boolean predicate support (Phase B Quick Win).
/// </summary>
public class SqlBooleanTests
{
    [ArrowRecord]
    public record BooleanTestData
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Name")]
        public string Name { get; init; } = string.Empty;

        [ArrowArray(Name = "IsActive")]
        public bool IsActive { get; init; }

        [ArrowArray(Name = "IsVerified")]
        public bool IsVerified { get; init; }
    }

    private static FrozenArrow<BooleanTestData> CreateTestData()
    {
        return new List<BooleanTestData>
        {
            new() { Id = 1, Name = "Alice", IsActive = true, IsVerified = true },
            new() { Id = 2, Name = "Bob", IsActive = false, IsVerified = true },
            new() { Id = 3, Name = "Charlie", IsActive = true, IsVerified = false },
            new() { Id = 4, Name = "Diana", IsActive = false, IsVerified = false }
        }.ToFrozenArrow();
    }

    [Fact]
    public void SqlQuery_BooleanTrue_FiltersCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<BooleanTestData, BooleanTestData>(
            "SELECT * FROM data WHERE IsActive = true");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Alice, Charlie
        Assert.All(list, r => Assert.True(r.IsActive));
    }

    [Fact]
    public void SqlQuery_BooleanFalse_FiltersCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<BooleanTestData, BooleanTestData>(
            "SELECT * FROM data WHERE IsActive = false");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Bob, Diana
        Assert.All(list, r => Assert.False(r.IsActive));
    }

    [Fact]
    public void SqlQuery_BooleanNotEqual_FiltersCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<BooleanTestData, BooleanTestData>(
            "SELECT * FROM data WHERE IsVerified != false");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Alice, Bob (IsVerified = true)
        Assert.All(list, r => Assert.True(r.IsVerified));
    }

    [Fact]
    public void SqlQuery_BooleanWithNumericOne_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act - Using 1 for true
        var result = data.ExecuteSql<BooleanTestData, BooleanTestData>(
            "SELECT * FROM data WHERE IsActive = 1");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Alice, Charlie
        Assert.All(list, r => Assert.True(r.IsActive));
    }

    [Fact]
    public void SqlQuery_BooleanWithNumericZero_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act - Using 0 for false
        var result = data.ExecuteSql<BooleanTestData, BooleanTestData>(
            "SELECT * FROM data WHERE IsActive = 0");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Bob, Diana
        Assert.All(list, r => Assert.False(r.IsActive));
    }

    [Fact]
    public void SqlQuery_BooleanAnd_CombinesCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<BooleanTestData, BooleanTestData>(
            "SELECT * FROM data WHERE IsActive = true AND IsVerified = true");

        // Assert
        var list = result.ToList();
        Assert.Single(list); // Only Alice
        Assert.Equal("Alice", list[0].Name);
        Assert.True(list[0].IsActive);
        Assert.True(list[0].IsVerified);
    }

    [Fact]
    public void SqlQuery_BooleanOr_CombinesCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<BooleanTestData, BooleanTestData>(
            "SELECT * FROM data WHERE IsActive = false OR IsVerified = false");

        // Assert
        var list = result.ToList();
        Assert.Equal(3, list.Count); // Bob, Charlie, Diana
    }

    [Fact]
    public void SqlQuery_BooleanCount_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var count = data.ExecuteSqlScalar<BooleanTestData, int>(
            "SELECT COUNT(*) FROM data WHERE IsActive = true");

        // Assert
        Assert.Equal(2, count);
    }
}
