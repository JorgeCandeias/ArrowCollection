using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using FrozenArrow.Query;

namespace FrozenArrow.Benchmarks;

/// <summary>
/// Benchmarks comparing SIMD-optimized predicate evaluation against baseline.
/// Tests Int32 and Double column comparisons at various selectivities.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ShortRunJob]
public class PredicateEvaluationBenchmarks
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

    #region Int32 Predicate (Age column) - High Selectivity (~5%)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Int32_HighSelectivity")]
    public int List_Int32_HighSelectivity()
    {
        return _list.Where(x => x.Age > 55).Count();
    }

    [Benchmark]
    [BenchmarkCategory("Int32_HighSelectivity")]
    public int FrozenArrow_Int32_HighSelectivity()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 55).Count();
    }

    #endregion

    #region Int32 Predicate (Age column) - Medium Selectivity (~50%)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Int32_MediumSelectivity")]
    public int List_Int32_MediumSelectivity()
    {
        return _list.Where(x => x.Age > 30).Count();
    }

    [Benchmark]
    [BenchmarkCategory("Int32_MediumSelectivity")]
    public int FrozenArrow_Int32_MediumSelectivity()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 30).Count();
    }

    #endregion

    #region Int32 Predicate (Age column) - Low Selectivity (~95%)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Int32_LowSelectivity")]
    public int List_Int32_LowSelectivity()
    {
        return _list.Where(x => x.Age > 10).Count();
    }

    [Benchmark]
    [BenchmarkCategory("Int32_LowSelectivity")]
    public int FrozenArrow_Int32_LowSelectivity()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 10).Count();
    }

    #endregion

    #region Double Predicate (PerformanceScore column) - High Selectivity (~5%)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Double_HighSelectivity")]
    public int List_Double_HighSelectivity()
    {
        return _list.Where(x => x.PerformanceScore > 4.5).Count();
    }

    [Benchmark]
    [BenchmarkCategory("Double_HighSelectivity")]
    public int FrozenArrow_Double_HighSelectivity()
    {
        return _frozenArrow.AsQueryable().Where(x => x.PerformanceScore > 4.5).Count();
    }

    #endregion

    #region Double Predicate (PerformanceScore column) - Medium Selectivity (~50%)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Double_MediumSelectivity")]
    public int List_Double_MediumSelectivity()
    {
        return _list.Where(x => x.PerformanceScore > 2.5).Count();
    }

    [Benchmark]
    [BenchmarkCategory("Double_MediumSelectivity")]
    public int FrozenArrow_Double_MediumSelectivity()
    {
        return _frozenArrow.AsQueryable().Where(x => x.PerformanceScore > 2.5).Count();
    }

    #endregion

    #region Combined Int32 + String Predicate

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Combined_Int32_String")]
    public int List_Combined_Int32_String()
    {
        return _list.Where(x => x.Age > 40 && x.Category == "Senior").Count();
    }

    [Benchmark]
    [BenchmarkCategory("Combined_Int32_String")]
    public int FrozenArrow_Combined_Int32_String()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 40 && x.Category == "Senior").Count();
    }

    #endregion

    #region Sum with Filter (Tests SIMD Aggregation)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SumWithFilter")]
    public decimal List_SumWithFilter()
    {
        return _list.Where(x => x.Age > 30).Sum(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("SumWithFilter")]
    public decimal FrozenArrow_SumWithFilter()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 30).Sum(x => x.Salary);
    }

    #endregion

    #region Multiple Predicates (Tests predicate combination efficiency)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MultiPredicate")]
    public int List_MultiPredicate()
    {
        return _list
            .Where(x => x.Age > 25)
            .Where(x => x.Age < 55)
            .Where(x => x.IsActive)
            .Count();
    }

    [Benchmark]
    [BenchmarkCategory("MultiPredicate")]
    public int FrozenArrow_MultiPredicate()
    {
        return _frozenArrow.AsQueryable()
            .Where(x => x.Age > 25)
            .Where(x => x.Age < 55)
            .Where(x => x.IsActive)
            .Count();
    }

    #endregion
}
