using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using FrozenArrow.Query;

namespace FrozenArrow.Benchmarks;

/// <summary>
/// Benchmarks for SIMD-optimized grouped aggregation operations.
/// Tests GroupBy with Sum, Count, Average, Min, Max across different group cardinalities.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ShortRunJob]
public class GroupedAggregationBenchmarks
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

    #region GroupBy Category (Low Cardinality ~8 groups) with Sum

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_LowCardinality_Sum")]
    public int List_GroupBy_Category_Sum()
    {
        return _list
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Salary) })
            .ToList()
            .Count;
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_LowCardinality_Sum")]
    public int FrozenArrow_GroupBy_Category_Sum()
    {
        return _frozenArrow.AsQueryable()
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Salary) })
            .ToList()
            .Count;
    }

    #endregion

    #region GroupBy Category (Low Cardinality) with Count

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_LowCardinality_Count")]
    public int List_GroupBy_Category_Count()
    {
        return _list
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToList()
            .Count;
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_LowCardinality_Count")]
    public int FrozenArrow_GroupBy_Category_Count()
    {
        return _frozenArrow.AsQueryable()
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToList()
            .Count;
    }

    #endregion

    #region GroupBy Category with Multiple Aggregates

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_MultiAggregate")]
    public int List_GroupBy_MultiAggregate()
    {
        return _list
            .GroupBy(x => x.Category)
            .Select(g => new 
            { 
                Category = g.Key, 
                Total = g.Sum(x => x.Salary),
                Count = g.Count(),
                AvgAge = g.Average(x => x.Age)
            })
            .ToList()
            .Count;
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_MultiAggregate")]
    public int FrozenArrow_GroupBy_MultiAggregate()
    {
        return _frozenArrow.AsQueryable()
            .GroupBy(x => x.Category)
            .Select(g => new 
            { 
                Category = g.Key, 
                Total = g.Sum(x => x.Salary),
                Count = g.Count(),
                AvgAge = g.Average(x => x.Age)
            })
            .ToList()
            .Count;
    }

    #endregion

    #region GroupBy with Filter + Sum

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_WithFilter_Sum")]
    public int List_GroupBy_WithFilter_Sum()
    {
        return _list
            .Where(x => x.IsActive)
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Salary) })
            .ToList()
            .Count;
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_WithFilter_Sum")]
    public int FrozenArrow_GroupBy_WithFilter_Sum()
    {
        return _frozenArrow.AsQueryable()
            .Where(x => x.IsActive)
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Salary) })
            .ToList()
            .Count;
    }

    #endregion

    #region GroupBy Department (Medium Cardinality ~5 groups) with Min/Max

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_MinMax")]
    public int List_GroupBy_MinMax()
    {
        return _list
            .GroupBy(x => x.Department)
            .Select(g => new 
            { 
                Department = g.Key, 
                MinAge = g.Min(x => x.Age),
                MaxAge = g.Max(x => x.Age)
            })
            .ToList()
            .Count;
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_MinMax")]
    public int FrozenArrow_GroupBy_MinMax()
    {
        return _frozenArrow.AsQueryable()
            .GroupBy(x => x.Department)
            .Select(g => new 
            { 
                Department = g.Key, 
                MinAge = g.Min(x => x.Age),
                MaxAge = g.Max(x => x.Age)
            })
            .ToList()
            .Count;
    }

    #endregion

    #region GroupBy ToDictionary (Optimized Path)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_ToDictionary")]
    public int List_GroupBy_ToDictionary_Count()
    {
        return _list
            .GroupBy(x => x.Category)
            .ToDictionary(g => g.Key, g => g.Count())
            .Count;
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_ToDictionary")]
    public int FrozenArrow_GroupBy_ToDictionary_Count()
    {
        return _frozenArrow.AsQueryable()
            .GroupBy(x => x.Category)
            .ToDictionary(g => g.Key, g => g.Count())
            .Count;
    }

    #endregion

    #region GroupBy Region (Medium Cardinality ~5 groups) with Average

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_Average")]
    public int List_GroupBy_Average()
    {
        return _list
            .GroupBy(x => x.Region)
            .Select(g => new { Region = g.Key, AvgSalary = g.Average(x => (double)x.Salary) })
            .ToList()
            .Count;
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_Average")]
    public int FrozenArrow_GroupBy_Average()
    {
        return _frozenArrow.AsQueryable()
            .GroupBy(x => x.Region)
            .Select(g => new { Region = g.Key, AvgSalary = g.Average(x => (double)x.Salary) })
            .ToList()
            .Count;
    }

    #endregion

    #region Large Groups (Tests SIMD aggregation within groups)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_LargeGroups")]
    public int List_GroupBy_IsActive_Sum()
    {
        // IsActive creates only 2 groups, each with many items (tests SIMD per-group sum)
        return _list
            .GroupBy(x => x.IsActive)
            .Select(g => new { IsActive = g.Key, Total = g.Sum(x => x.Salary) })
            .ToList()
            .Count;
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_LargeGroups")]
    public int FrozenArrow_GroupBy_IsActive_Sum()
    {
        return _frozenArrow.AsQueryable()
            .GroupBy(x => x.IsActive)
            .Select(g => new { IsActive = g.Key, Total = g.Sum(x => x.Salary) })
            .ToList()
            .Count;
    }

    #endregion
}
