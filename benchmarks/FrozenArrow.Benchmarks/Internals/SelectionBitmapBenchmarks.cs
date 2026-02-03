using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using FrozenArrow.Query;

namespace FrozenArrow.Benchmarks.Internals;

/// <summary>
/// Benchmarks for SelectionBitmap SIMD-optimized operations.
/// These are internal component benchmarks for optimization work.
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

    [Params(10_000, 100_000, 1_000_000)]
    public int BitCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _bitmap1 = SelectionBitmap.Create(BitCount, initialValue: true);
        _bitmap2 = SelectionBitmap.Create(BitCount, initialValue: true);
        _sparseBitmap = SelectionBitmap.Create(BitCount, initialValue: false);

        // Set alternating pattern in bitmap2
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

    #region CountSet

    [Benchmark]
    [BenchmarkCategory("CountSet")]
    public int CountSet_Dense() => _bitmap1.CountSet();

    [Benchmark]
    [BenchmarkCategory("CountSet")]
    public int CountSet_Alternating() => _bitmap2.CountSet();

    [Benchmark]
    [BenchmarkCategory("CountSet")]
    public int CountSet_Sparse() => _sparseBitmap.CountSet();

    #endregion

    #region And

    [Benchmark]
    [BenchmarkCategory("And")]
    public void And_Dense()
    {
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

    #region Or

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

    #region Not

    [Benchmark]
    [BenchmarkCategory("Not")]
    public void Not_Dense()
    {
        using var bitmap = SelectionBitmap.Create(BitCount, initialValue: true);
        bitmap.Not();
    }

    #endregion

    #region Any/All

    [Benchmark]
    [BenchmarkCategory("Any")]
    public bool Any_Dense() => _bitmap1.Any();

    [Benchmark]
    [BenchmarkCategory("Any")]
    public bool Any_Sparse() => _sparseBitmap.Any();

    [Benchmark]
    [BenchmarkCategory("All")]
    public bool All_Dense() => _bitmap1.All();

    [Benchmark]
    [BenchmarkCategory("All")]
    public bool All_Alternating() => _bitmap2.All();

    #endregion

    #region Combined Operations

    [Benchmark]
    [BenchmarkCategory("Combined")]
    public int CombinedQuery_TwoPredicates()
    {
        using var result = SelectionBitmap.Create(BitCount, initialValue: true);
        result.And(_bitmap2);
        result.And(_sparseBitmap);
        return result.CountSet();
    }

    [Benchmark]
    [BenchmarkCategory("Combined")]
    public int CombinedQuery_OrThenAnd()
    {
        using var temp = SelectionBitmap.Create(BitCount, initialValue: false);
        temp.Or(_bitmap2);
        temp.Or(_sparseBitmap);
        temp.And(_bitmap1);
        return temp.CountSet();
    }

    #endregion
}
