using System.Collections.Concurrent;

namespace FrozenArrow.Tests.Concurrency;

/// <summary>
/// Simplified test to debug the concurrent query execution issue.
/// </summary>
public class SimpleConcurrencyTest
{
    [ArrowRecord]
    public record SimpleConcTestRecord
    {
        [ArrowArray(Name = "Value")]
        public int Value { get; init; }
    }

    [Fact]
    public async Task SimpleConcurrentQueries_ShouldReturnCorrectResults()
    {
        // Arrange - Create test data matching the failing test
        var random = new Random(42);
        var data = Enumerable.Range(0, 50000)
            .Select(i => new SimpleConcTestRecord { Value = random.Next(0, 1000) })
            .ToList()
            .ToFrozenArrow();

        var results = new ConcurrentDictionary<int, int>();

        // Act - Run 20 concurrent queries with different thresholds (matching failing test)
        var tasks = Enumerable.Range(0, 20)
            .Select(i =>
            {
                var threshold = 400 + (i * 10); // 400, 410, 420, ..., 590
                return Task.Run(() =>
                {
                    // Count how many values are > threshold
                    var count = data.AsQueryable()
                        .Where(x => x.Value > threshold)
                        .Count();
                    
                    results[threshold] = count;
                    
                    return count;
                });
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - Verify results are monotonically decreasing
        var sortedResults = results.OrderBy(kvp => kvp.Key).ToList();
        for (int i = 1; i < sortedResults.Count; i++)
        {
            Assert.True(sortedResults[i].Value <= sortedResults[i - 1].Value,
                $"Higher threshold {sortedResults[i].Key} should have <= matches than {sortedResults[i - 1].Key}. " +
                $"Got {sortedResults[i].Value} for {sortedResults[i].Key} vs {sortedResults[i - 1].Value} for {sortedResults[i - 1].Key}");
        }
    }
}
