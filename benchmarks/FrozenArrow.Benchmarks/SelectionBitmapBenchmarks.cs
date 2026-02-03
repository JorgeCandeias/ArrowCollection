using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using FrozenArrow.Query;

namespace FrozenArrow.Benchmarks;

/// <summary>
/// Benchmarks for SelectionBitmap SIMD-optimized operations.
/// Measures performance of And, Or, Not, CountSet, Any, and All operations
/// across different bitmap sizes.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ShortRunJob]
public class SelectionBitmapBenchmarks
{
    private SelectionBitmap _bitmap1;
    private SelectionBitmap _bitmap2;
    private SelectionBitmap _sparseBitmap;

    [Params(1_000, 10_000, 100_000, 1_000_000)]
    public int BitCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Create bitmaps with different patterns for realistic testing
        _bitmap1 = SelectionBitmap.Create(BitCount, initialValue: true);
        _bitmap2 = SelectionBitmap.Create(BitCount, initialValue: true);
        _sparseBitmap = SelectionBitmap.Create(BitCount, initialValue: false);

        // Set alternating pattern in bitmap2 for interesting AND/OR results
        for (int i = 0; i < BitCount; i += 2)
        {
            _bitmap2.Clear(i);
        }

        // Set sparse pattern (~5% density)
        var random = new Random(42);
        for (int i = 0; i < BitCount; i++)
        {
            if (random.NextDouble() < 0.05)
            {
                _sparseBitmap.Set(i);
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _bitmap1.Dispose();
        _bitmap2.Dispose();
        _sparseBitmap.Dispose();
    }

    #region CountSet Benchmarks

    [Benchmark]
    [BenchmarkCategory("CountSet")]
    public int CountSet_Dense()
    {
        return _bitmap1.CountSet();
    }

    [Benchmark]
    [BenchmarkCategory("CountSet")]
    public int CountSet_Alternating()
    {
        return _bitmap2.CountSet();
    }

    [Benchmark]
    [BenchmarkCategory("CountSet")]
    public int CountSet_Sparse()
    {
        return _sparseBitmap.CountSet();
    }

    #endregion

    #region And Benchmarks

    [Benchmark]
    [BenchmarkCategory("And")]
    public void And_Dense()
    {
        // Create fresh bitmap to avoid modifying the shared one
        using var bitmap = SelectionBitmap.Create(BitCount, initialValue: true);
        bitmap.And(_bitmap2);
    }

    [Benchmark]
    [BenchmarkCategory("And")]
    public void And_Sparse()
    {
        using var bitmap = SelectionBitmap.Create(BitCount, initialValue: true);
        bitmap.And(_sparseBitmap);
    }

    #endregion

    #region Or Benchmarks

    [Benchmark]
    [BenchmarkCategory("Or")]
    public void Or_FromEmpty()
    {
        using var bitmap = SelectionBitmap.Create(BitCount, initialValue: false);
        bitmap.Or(_bitmap2);
    }

    [Benchmark]
    [BenchmarkCategory("Or")]
    public void Or_Sparse()
    {
        using var bitmap = SelectionBitmap.Create(BitCount, initialValue: false);
        bitmap.Or(_sparseBitmap);
    }

    #endregion

    #region Not Benchmarks

    [Benchmark]
    [BenchmarkCategory("Not")]
    public void Not_Dense()
    {
        using var bitmap = SelectionBitmap.Create(BitCount, initialValue: true);
        bitmap.Not();
    }

    [Benchmark]
    [BenchmarkCategory("Not")]
    public void Not_Sparse()
    {
        using var bitmap = SelectionBitmap.Create(BitCount, initialValue: false);
        // Set some bits
        for (int i = 0; i < BitCount; i += 20)
        {
            bitmap.Set(i);
        }
        bitmap.Not();
    }

    #endregion

    #region AndNot Benchmarks

    [Benchmark]
    [BenchmarkCategory("AndNot")]
    public void AndNot_Dense()
    {
        using var bitmap = SelectionBitmap.Create(BitCount, initialValue: true);
        bitmap.AndNot(_bitmap2);
    }

    #endregion

    #region Any Benchmarks

    [Benchmark]
    [BenchmarkCategory("Any")]
    public bool Any_Dense()
    {
        return _bitmap1.Any();
    }

    [Benchmark]
    [BenchmarkCategory("Any")]
    public bool Any_Empty()
    {
        using var bitmap = SelectionBitmap.Create(BitCount, initialValue: false);
        return bitmap.Any();
    }

    [Benchmark]
    [BenchmarkCategory("Any")]
    public bool Any_Sparse()
    {
        return _sparseBitmap.Any();
    }

    #endregion

    #region All Benchmarks

    [Benchmark]
    [BenchmarkCategory("All")]
    public bool All_Dense()
    {
        return _bitmap1.All();
    }

    [Benchmark]
    [BenchmarkCategory("All")]
    public bool All_Alternating()
    {
        return _bitmap2.All();
    }

    #endregion

    #region Combined Operations (Realistic Query Scenario)

    [Benchmark]
    [BenchmarkCategory("Combined")]
    public int CombinedQuery_TwoPredicates()
    {
        // Simulates: WHERE col1 > X AND col2 == Y
        using var result = SelectionBitmap.Create(BitCount, initialValue: true);
        result.And(_bitmap2);
        result.And(_sparseBitmap);
        return result.CountSet();
    }

    [Benchmark]
    [BenchmarkCategory("Combined")]
    public int CombinedQuery_OrThenAnd()
    {
        // Simulates: WHERE (col1 > X OR col2 > Y) AND col3 == Z
        using var temp = SelectionBitmap.Create(BitCount, initialValue: false);
        temp.Or(_bitmap2);
        temp.Or(_sparseBitmap);
        temp.And(_bitmap1);
        return temp.CountSet();
    }

    #endregion
}
