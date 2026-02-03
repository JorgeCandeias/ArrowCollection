using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using FrozenArrow.Query;

namespace FrozenArrow.Benchmarks;

/// <summary>
/// Benchmarks for SIMD-optimized aggregation operations (Sum, Average, Min, Max).
/// Tests both dense and sparse selections to validate the selectivity-based optimization path.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ShortRunJob]
public class AggregationBenchmarks
{
    private List<QueryBenchmarkItem> _list = null!;
    private FrozenArrow<QueryBenchmarkItem> _frozenArrow = null!;

    [Params(100_000, 1_000_000)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _list = QueryBenchmarkItemFactory.Generate(ItemCount);
        _frozenArrow = _list.ToFrozenArrow();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _frozenArrow.Dispose();
    }

    #region Sum - No Filter (100% selectivity, should use SIMD dense path)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Sum_NoFilter")]
    public decimal List_Sum_NoFilter()
    {
        return _list.Sum(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Sum_NoFilter")]
    public decimal FrozenArrow_Sum_NoFilter()
    {
        return _frozenArrow.AsQueryable().Sum(x => x.Salary);
    }

    #endregion

    #region Sum - Dense Selection (~70% selectivity)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Sum_Dense")]
    public decimal List_Sum_Dense()
    {
        return _list.Where(x => x.IsActive).Sum(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Sum_Dense")]
    public decimal FrozenArrow_Sum_Dense()
    {
        return _frozenArrow.AsQueryable().Where(x => x.IsActive).Sum(x => x.Salary);
    }

    #endregion

    #region Sum - Sparse Selection (~5% selectivity)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Sum_Sparse")]
    public decimal List_Sum_Sparse()
    {
        return _list.Where(x => x.Age > 55).Sum(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Sum_Sparse")]
    public decimal FrozenArrow_Sum_Sparse()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 55).Sum(x => x.Salary);
    }

    #endregion

    #region Average - Dense Selection

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Average_Dense")]
    public double List_Average_Dense()
    {
        return _list.Where(x => x.IsActive).Average(x => (double)x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Average_Dense")]
    public double FrozenArrow_Average_Dense()
    {
        return _frozenArrow.AsQueryable().Where(x => x.IsActive).Average(x => (double)x.Salary);
    }

    #endregion

    #region Min - Dense Selection

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Min_Dense")]
    public int List_Min_Dense()
    {
        return _list.Where(x => x.IsActive).Min(x => x.Age);
    }

    [Benchmark]
    [BenchmarkCategory("Min_Dense")]
    public int FrozenArrow_Min_Dense()
    {
        return _frozenArrow.AsQueryable().Where(x => x.IsActive).Min(x => x.Age);
    }

    #endregion

    #region Max - Dense Selection

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Max_Dense")]
    public int List_Max_Dense()
    {
        return _list.Where(x => x.IsActive).Max(x => x.Age);
    }

    [Benchmark]
    [BenchmarkCategory("Max_Dense")]
    public int FrozenArrow_Max_Dense()
    {
        return _frozenArrow.AsQueryable().Where(x => x.IsActive).Max(x => x.Age);
    }

    #endregion

    #region Count - Various Selectivities

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Count")]
    public int List_Count_Dense()
    {
        return _list.Where(x => x.IsActive).Count();
    }

    [Benchmark]
    [BenchmarkCategory("Count")]
    public int FrozenArrow_Count_Dense()
    {
        return _frozenArrow.AsQueryable().Where(x => x.IsActive).Count();
    }

    [Benchmark]
    [BenchmarkCategory("Count")]
    public int List_Count_Sparse()
    {
        return _list.Where(x => x.Age > 55).Count();
    }

    [Benchmark]
    [BenchmarkCategory("Count")]
    public int FrozenArrow_Count_Sparse()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 55).Count();
    }

    #endregion

    #region GroupBy with Sum (Tests grouped aggregation)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_Sum")]
    public int List_GroupBy_Sum()
    {
        return _list
            .Where(x => x.IsActive)
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Salary) })
            .ToList()
            .Count;
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_Sum")]
    public int FrozenArrow_GroupBy_Sum()
    {
        return _frozenArrow.AsQueryable()
            .Where(x => x.IsActive)
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Salary) })
            .ToList()
            .Count;
    }

    #endregion
}
