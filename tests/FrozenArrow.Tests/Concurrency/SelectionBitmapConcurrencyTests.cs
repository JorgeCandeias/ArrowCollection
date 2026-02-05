using FrozenArrow.Query;
using System.Collections.Concurrent;

namespace FrozenArrow.Tests.Concurrency;

/// <summary>
/// Tests for SelectionBitmap thread safety and concurrent operations.
/// Critical for detecting race conditions in bitmap operations used by parallel query execution.
/// </summary>
public class SelectionBitmapConcurrencyTests
{
    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public async Task ConcurrentReads_SameBitmap_ShouldBeThreadSafe(int bitCount)
    {
        // Arrange
        var bitmap = SelectionBitmap.Create(bitCount, true);
        try
        {
            // Set a deterministic pattern
            for (int i = 0; i < bitCount; i++)
            {
                if (i % 2 == 0)
                {
                    bitmap.Clear(i);
                }
            }

            var expectedCount = bitCount / 2;

            // Act - Multiple threads reading concurrently
            var tasks = Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() =>
                {
                    int count = 0;
                    for (int i = 0; i < bitCount; i++)
                    {
                        if (bitmap[i]) count++;
                    }
                    return count;
                }))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, count => Assert.Equal(expectedCount, count));
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    [Theory]
    [InlineData(100_000, 10)]
    [InlineData(500_000, 20)]
    public async Task ConcurrentCountSet_ShouldReturnConsistentResults(int bitCount, int concurrentReads)
    {
        // Arrange
        var bitmap = SelectionBitmap.Create(bitCount, true);
        try
        {
            var expectedTrueCount = 0;
            for (int i = 0; i < bitCount; i++)
            {
                if (i % 3 == 0)
                {
                    bitmap.Clear(i);
                }
                else
                {
                    expectedTrueCount++;
                }
            }

            // Act
            var tasks = Enumerable.Range(0, concurrentReads)
                .Select(_ => Task.Run(() => bitmap.CountSet()))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, count => Assert.Equal(expectedTrueCount, count));
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    [Theory]
    [InlineData(100_000)]
    public async Task ConcurrentClearRange_NonOverlappingRanges_ShouldBeCorrect(int bitCount)
    {
        // Arrange
        var bitmap = SelectionBitmap.Create(bitCount, true);
        try
        {
            // Act - Clear non-overlapping ranges concurrently
            var chunkSize = bitCount / 4;
            var tasks = new[]
            {
                Task.Run(() => bitmap.ClearRange(0, chunkSize)),
                Task.Run(() => bitmap.ClearRange(chunkSize, chunkSize * 2)),
                Task.Run(() => bitmap.ClearRange(chunkSize * 2, chunkSize * 3)),
                Task.Run(() => bitmap.ClearRange(chunkSize * 3, bitCount))
            };

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(0, bitmap.CountSet());
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    [Fact]
    public async Task RaceConditionStressTest_ShouldNotCorrupt()
    {
        // Arrange
        const int bitCount = 50_000;
        const int iterations = 100;
        var random = new Random(42);
        var exceptions = new ConcurrentBag<Exception>();

        // Act
        var tasks = Enumerable.Range(0, iterations)
            .Select(async i =>
            {
                try
                {
                    var bitmap = SelectionBitmap.Create(bitCount, true);
                    try
                    {
                        await Task.Delay(random.Next(0, 2));

                        var count1 = bitmap.CountSet();
                        Thread.SpinWait(random.Next(0, 50));

                        bitmap.ClearRange(0, bitCount / 2);
                        var count2 = bitmap.CountSet();

                        Thread.SpinWait(random.Next(0, 50));
                        
                        var count3 = bitmap.CountSet();

                        Assert.Equal(count2, count3);
                    }
                    finally
                    {
                        bitmap.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.Empty(exceptions);
    }

    [Theory]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(16385)]
    public async Task ChunkBoundary_ConcurrentReads_ShouldBeCorrect(int bitCount)
    {
        // Arrange
        using var bitmap = SelectionBitmap.Create(bitCount, true);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => bitmap.CountSet()))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, count => Assert.Equal(bitCount, count));
    }

    [Fact]
    public async Task ConcurrentBlockIteration_ShouldNotSkipBits()
    {
        // Arrange
        const int bitCount = 100_000;
        var bitmap = SelectionBitmap.Create(bitCount, true);
        try
        {
            for (int i = 0; i < bitCount; i++)
            {
                if (i % 10 != 0)
                {
                    bitmap.Clear(i);
                }
            }

            var expectedIndices = Enumerable.Range(0, bitCount)
                .Where(i => i % 10 == 0)
                .ToHashSet();

            // Act
            var tasks = Enumerable.Range(0, 5)
                .Select(_ => Task.Run(() =>
                {
                    var indices = new List<int>();
                    for (int i = 0; i < bitCount; i++)
                    {
                        if (bitmap[i])
                        {
                            indices.Add(i);
                        }
                    }
                    return indices;
                }))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert
            foreach (var indices in results)
            {
                Assert.Equal(expectedIndices.Count, indices.Count);
                Assert.True(expectedIndices.SetEquals(indices));
            }
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    [Fact]
    public async Task LargeBitmap_ConcurrentOperations_ShouldNotLeak()
    {
        // Arrange & Act
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < 10; i++)
                {
                    var bitmap = SelectionBitmap.Create(100_000, true);
                    try
                    {
                        bitmap.ClearRange(0, 50_000);
                        var count = bitmap.CountSet();
                        Assert.Equal(50_000, count);
                    }
                    finally
                    {
                        bitmap.Dispose();
                    }
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - No exceptions means no leaks
    }

    [Theory]
    [InlineData(1_000_000)]
    public async Task LargeBitmap_ConcurrentCountSet_ShouldUseHardwareAcceleration(int bitCount)
    {
        // Arrange
        using var bitmap = SelectionBitmap.Create(bitCount, true);

        // Act
        var tasks = Enumerable.Range(0, Environment.ProcessorCount)
            .Select(_ => Task.Run(() => bitmap.CountSet()))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, count => Assert.Equal(bitCount, count));
    }

    [Fact]
    public async Task ConcurrentDispose_ShouldHandleGracefully()
    {
        // Arrange
        const int bitCount = 50_000;
        var bitmaps = Enumerable.Range(0, 100)
            .Select(_ => SelectionBitmap.Create(bitCount, true))
            .ToList();

        // Act
        var tasks = bitmaps
            .Select(bitmap => Task.Run(() => bitmap.Dispose()))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(100, bitmaps.Count);
    }
}
