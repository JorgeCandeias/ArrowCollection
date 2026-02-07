namespace FrozenArrow.Tests.Sql;

/// <summary>
/// Tests for SQL DateTime predicate support (Phase B Quick Win).
/// </summary>
public class SqlDateTimeTests
{
    [ArrowRecord]
    public record DateTimeTestData
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Name")]
        public string Name { get; init; } = string.Empty;

        [ArrowArray(Name = "BirthDate")]
        public DateTime BirthDate { get; init; }

        [ArrowArray(Name = "JoinDate")]
        public DateTime JoinDate { get; init; }
    }

    private static FrozenArrow<DateTimeTestData> CreateTestData()
    {
        return new List<DateTimeTestData>
        {
            new() { Id = 1, Name = "Alice", BirthDate = new DateTime(1990, 5, 15), JoinDate = new DateTime(2020, 1, 10) },
            new() { Id = 2, Name = "Bob", BirthDate = new DateTime(2005, 8, 22), JoinDate = new DateTime(2021, 6, 15) },
            new() { Id = 3, Name = "Charlie", BirthDate = new DateTime(1985, 3, 8), JoinDate = new DateTime(2019, 11, 5) },
            new() { Id = 4, Name = "Diana", BirthDate = new DateTime(2010, 12, 1), JoinDate = new DateTime(2022, 3, 20) }
        }.ToFrozenArrow();
    }

    [Fact]
    public void SqlQuery_DateTimeGreaterThan_FiltersCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<DateTimeTestData, DateTimeTestData>(
            "SELECT * FROM data WHERE BirthDate > '2000-01-01'");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Bob (2005), Diana (2010)
        Assert.All(list, r => Assert.True(r.BirthDate > new DateTime(2000, 1, 1)));
    }

    [Fact]
    public void SqlQuery_DateTimeLessThan_FiltersCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<DateTimeTestData, DateTimeTestData>(
            "SELECT * FROM data WHERE BirthDate < '1990-01-01'");

        // Assert
        var list = result.ToList();
        Assert.Single(list); // Charlie (1985)
        Assert.Equal("Charlie", list[0].Name);
    }

    [Fact]
    public void SqlQuery_DateTimeEqual_FiltersCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<DateTimeTestData, DateTimeTestData>(
            "SELECT * FROM data WHERE BirthDate = '1990-05-15'");

        // Assert
        var list = result.ToList();
        Assert.Single(list); // Alice
        Assert.Equal("Alice", list[0].Name);
        Assert.Equal(new DateTime(1990, 5, 15), list[0].BirthDate);
    }

    [Fact]
    public void SqlQuery_DateTimeBetween_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act - Simulate BETWEEN using AND
        var result = data.ExecuteSql<DateTimeTestData, DateTimeTestData>(
            "SELECT * FROM data WHERE BirthDate >= '1985-01-01' AND BirthDate < '2000-01-01'");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Charlie (1985), Alice (1990)
    }

    [Fact]
    public void SqlQuery_DateTimeJoinDateFilter_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act - Filter by JoinDate
        var result = data.ExecuteSql<DateTimeTestData, DateTimeTestData>(
            "SELECT * FROM data WHERE JoinDate >= '2021-01-01'");

        // Assert
        var list = result.ToList();
        Assert.Equal(2, list.Count); // Bob (2021-06), Diana (2022-03)
    }

    [Fact]
    public void SqlQuery_DateTimeWithOrderBy_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.ExecuteSql<DateTimeTestData, DateTimeTestData>(
            "SELECT * FROM data WHERE BirthDate > '1980-01-01' ORDER BY BirthDate ASC");

        // Assert
        var list = result.ToList();
        Assert.Equal(4, list.Count);
        
        // Should be sorted by BirthDate ascending
        Assert.True(list[0].BirthDate <= list[1].BirthDate);
        Assert.True(list[1].BirthDate <= list[2].BirthDate);
        Assert.True(list[2].BirthDate <= list[3].BirthDate);
    }

    [Fact]
    public void SqlQuery_DateTimeCount_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var count = data.ExecuteSqlScalar<DateTimeTestData, int>(
            "SELECT COUNT(*) FROM data WHERE JoinDate >= '2020-01-01'");

        // Assert
        Assert.Equal(3, count); // Alice (2020-01), Bob (2021-06), Diana (2022-03) - Charlie joined in 2019
    }
}
