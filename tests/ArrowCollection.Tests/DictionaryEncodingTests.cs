using Apache.Arrow;
using Xunit;
using static ArrowCollection.Tests.ArrowCollectionTests;

namespace ArrowCollection.Tests;

/// <summary>
/// Tests for dictionary encoding functionality.
/// </summary>
public class DictionaryEncodingTests
{
    [Fact]
    public void LowCardinalityStrings_ShouldUseDictionaryEncoding()
    {
        // Arrange: Create items with low cardinality strings (only 3 distinct values)
        var items = new List<SimpleItem>();
        var categories = new[] { "A", "B", "C" };
        
        for (int i = 0; i < 10000; i++)
        {
            items.Add(new SimpleItem
            {
                Id = i,
                Name = categories[i % 3],
                Value = i * 1.5
            });
        }

        // Act
        using var collection = items.ToArrowCollection();

        // Assert
        Assert.NotNull(collection.BuildStatistics);
        var nameStats = collection.BuildStatistics.ColumnStatistics["Name"];
        
        // Verify statistics detected low cardinality
        Assert.Equal(3, nameStats.DistinctCount);
        Assert.Equal(10000, nameStats.TotalCount);
        Assert.True(nameStats.ShouldUseDictionaryEncoding());
        Assert.Equal(ColumnEncoding.Dictionary, nameStats.RecommendedEncoding);

        // Verify data round-trips correctly
        var roundTripped = collection.ToList();
        Assert.Equal(10000, roundTripped.Count);
        Assert.All(roundTripped, item => Assert.Contains(item.Name, categories));
    }

    [Fact]
    public void HighCardinalityStrings_ShouldUsePrimitiveEncoding()
    {
        // Arrange: Create items with high cardinality strings (all unique)
        var items = new List<SimpleItem>();
        
        for (int i = 0; i < 1000; i++)
        {
            items.Add(new SimpleItem
            {
                Id = i,
                Name = $"UniqueValue_{i}",
                Value = i * 1.5
            });
        }

        // Act
        using var collection = items.ToArrowCollection();

        // Assert
        Assert.NotNull(collection.BuildStatistics);
        var nameStats = collection.BuildStatistics.ColumnStatistics["Name"];
        
        // Verify statistics detected high cardinality
        Assert.Equal(1000, nameStats.DistinctCount);
        Assert.Equal(1000, nameStats.TotalCount);
        Assert.False(nameStats.ShouldUseDictionaryEncoding());
        Assert.Equal(ColumnEncoding.Primitive, nameStats.RecommendedEncoding);

        // Verify data round-trips correctly
        var roundTripped = collection.ToList();
        Assert.Equal(1000, roundTripped.Count);
        Assert.Equal("UniqueValue_500", roundTripped[500].Name);
    }

    [Fact]
    public void LowCardinalityIntegers_ShouldUseDictionaryEncoding()
    {
        // Arrange: Create items with low cardinality integers (only 5 distinct values)
        var items = new List<SimpleItem>();
        var values = new[] { 0, 10, 20, 30, 40 };
        
        for (int i = 0; i < 10000; i++)
        {
            items.Add(new SimpleItem
            {
                Id = values[i % 5], // Low cardinality
                Name = $"Item_{i}",
                Value = i * 1.5
            });
        }

        // Act
        using var collection = items.ToArrowCollection();

        // Assert
        Assert.NotNull(collection.BuildStatistics);
        var idStats = collection.BuildStatistics.ColumnStatistics["Id"];
        
        // Verify statistics detected low cardinality
        Assert.Equal(5, idStats.DistinctCount);
        Assert.Equal(10000, idStats.TotalCount);
        Assert.True(idStats.ShouldUseDictionaryEncoding());

        // Verify data round-trips correctly
        var roundTripped = collection.ToList();
        Assert.Equal(10000, roundTripped.Count);
        Assert.All(roundTripped, item => Assert.Contains(item.Id, values));
    }

    [Fact]
    public void LowCardinalityDoubles_ShouldUseDictionaryEncoding()
    {
        // Arrange: Create items with low cardinality doubles (only 4 distinct values)
        var items = new List<SimpleItem>();
        var prices = new[] { 9.99, 19.99, 29.99, 49.99 };
        
        for (int i = 0; i < 10000; i++)
        {
            items.Add(new SimpleItem
            {
                Id = i,
                Name = $"Product_{i}",
                Value = prices[i % 4] // Low cardinality
            });
        }

        // Act
        using var collection = items.ToArrowCollection();

        // Assert
        Assert.NotNull(collection.BuildStatistics);
        var valueStats = collection.BuildStatistics.ColumnStatistics["Value"];
        
        // Verify statistics detected low cardinality
        Assert.Equal(4, valueStats.DistinctCount);
        Assert.Equal(10000, valueStats.TotalCount);
        Assert.True(valueStats.ShouldUseDictionaryEncoding());

        // Verify data round-trips correctly
        var roundTripped = collection.ToList();
        Assert.Equal(10000, roundTripped.Count);
        Assert.All(roundTripped, item => Assert.Contains(item.Value, prices));
    }

    [Fact]
    public void BuildStatistics_ReportsCorrectEstimatedSavings()
    {
        // Arrange: Create items with mixed cardinality
        var items = new List<SimpleItem>();
        var categories = new[] { "Electronics", "Books", "Clothing" };
        
        for (int i = 0; i < 10000; i++)
        {
            items.Add(new SimpleItem
            {
                Id = i, // High cardinality
                Name = categories[i % 3], // Low cardinality
                Value = i * 0.01 // High cardinality
            });
        }

        // Act
        using var collection = items.ToArrowCollection();

        // Assert
        Assert.NotNull(collection.BuildStatistics);
        
        // Strings should have savings
        var nameStats = collection.BuildStatistics.ColumnStatistics["Name"];
        Assert.True(nameStats.ShouldUseDictionaryEncoding());
        
        // Check that estimated savings is positive
        var estimatedSavings = collection.BuildStatistics.EstimateMemorySavings();
        Assert.True(estimatedSavings > 0, $"Expected positive savings but got {estimatedSavings}");
    }
}
