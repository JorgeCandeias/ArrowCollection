namespace FrozenArrow.Tests.Linq;

/// <summary>
/// Tests to verify LINQ NULL handling support (existing feature).
/// </summary>
public class LinqNullTests
{
    [ArrowRecord]
    public record LinqNullTestData
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Name")]
        public string? Name { get; init; }

        [ArrowArray(Name = "Score")]
        public double? Score { get; init; }
    }

    private static FrozenArrow<LinqNullTestData> CreateTestData()
    {
        return new List<LinqNullTestData>
        {
            new() { Id = 1, Name = "Alice", Score = 95.5 },
            new() { Id = 2, Name = "Bob", Score = null },
            new() { Id = 3, Name = null, Score = 87.0 }
        }.ToFrozenArrow();
    }

    [Fact]
    public void LinqQuery_WhereNull_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.AsQueryable()
            .Where(x => x.Name == null)
            .ToList();

        // Assert
        Assert.Single(result); // Id=3
        Assert.Null(result[0].Name);
    }

    [Fact]
    public void LinqQuery_WhereNotNull_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.AsQueryable()
            .Where(x => x.Name != null)
            .ToList();

        // Assert
        Assert.Equal(2, result.Count); // Alice, Bob
        Assert.All(result, r => Assert.NotNull(r.Name));
    }

    [Fact]
    public void LinqQuery_WhereNullableHasValue_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var result = data.AsQueryable()
            .Where(x => x.Score.HasValue)
            .ToList();

        // Assert
        Assert.Equal(2, result.Count); // Alice, Unnamed
        Assert.All(result, r => Assert.True(r.Score.HasValue));
    }

    [Fact]
    public void LinqQuery_CountWithNull_WorksCorrectly()
    {
        // Arrange
        var data = CreateTestData();

        // Act
        var count = data.AsQueryable()
            .Count(x => x.Score == null);

        // Assert
        Assert.Equal(1, count); // Bob
    }
}
