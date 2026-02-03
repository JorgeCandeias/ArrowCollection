using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using FrozenArrow.Query;

namespace FrozenArrow.Benchmarks.Internals;

/// <summary>
/// Benchmarks comparing sequential vs parallel aggregation.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ShortRunJob]
public class ParallelAggregationBenchmarks
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

    #region Sum - Sequential vs Parallel

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Sum")]
    public decimal Sequential_Sum()
    {
        var query = _frozenArrow.AsQueryable();
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions { EnableParallelExecution = false };
        return query.Where(x => x.IsActive).Sum(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Sum")]
    public decimal Parallel_Sum()
    {
        var query = _frozenArrow.AsQueryable();
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions { EnableParallelExecution = true };
        return query.Where(x => x.IsActive).Sum(x => x.Salary);
    }

    #endregion

    #region Average - Sequential vs Parallel

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Average")]
    public double Sequential_Average()
    {
        var query = _frozenArrow.AsQueryable();
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions { EnableParallelExecution = false };
        return query.Where(x => x.Age > 30).Average(x => x.PerformanceScore);
    }

    [Benchmark]
    [BenchmarkCategory("Average")]
    public double Parallel_Average()
    {
        var query = _frozenArrow.AsQueryable();
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions { EnableParallelExecution = true };
        return query.Where(x => x.Age > 30).Average(x => x.PerformanceScore);
    }

    #endregion

    #region Min - Sequential vs Parallel

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Min")]
    public decimal Sequential_Min()
    {
        var query = _frozenArrow.AsQueryable();
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions { EnableParallelExecution = false };
        return query.Where(x => x.Age > 40).Min(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Min")]
    public decimal Parallel_Min()
    {
        var query = _frozenArrow.AsQueryable();
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions { EnableParallelExecution = true };
        return query.Where(x => x.Age > 40).Min(x => x.Salary);
    }

    #endregion

    #region Max - Sequential vs Parallel

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Max")]
    public decimal Sequential_Max()
    {
        var query = _frozenArrow.AsQueryable();
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions { EnableParallelExecution = false };
        return query.Where(x => x.Age > 40).Max(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Max")]
    public decimal Parallel_Max()
    {
        var query = _frozenArrow.AsQueryable();
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions { EnableParallelExecution = true };
        return query.Where(x => x.Age > 40).Max(x => x.Salary);
    }

    #endregion

    #region Full Pipeline - Filter + Aggregate

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FullPipeline")]
    public decimal Sequential_FullPipeline()
    {
        var query = _frozenArrow.AsQueryable();
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions { EnableParallelExecution = false };
        return query
            .Where(x => x.Age > 25 && x.Age < 50 && x.IsActive)
            .Sum(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("FullPipeline")]
    public decimal Parallel_FullPipeline()
    {
        var query = _frozenArrow.AsQueryable();
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions { EnableParallelExecution = true };
        return query
            .Where(x => x.Age > 25 && x.Age < 50 && x.IsActive)
            .Sum(x => x.Salary);
    }

    #endregion
}
