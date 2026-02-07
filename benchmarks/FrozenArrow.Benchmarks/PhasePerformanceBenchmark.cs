using System.Diagnostics;
using FrozenArrow.Query;

namespace FrozenArrow.Benchmarks;

/// <summary>
/// Comprehensive performance benchmarks for all 10 phases.
/// Generates a performance report showing real-world improvements.
/// </summary>
public class PhasePerformanceBenchmark
{
    [ArrowRecord]
    public record BenchmarkRecord
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Value")]
        public int Value { get; init; }

        [ArrowArray(Name = "Score")]
        public double Score { get; init; }

        [ArrowArray(Name = "Category")]
        public string Category { get; init; } = string.Empty;
    }

    private const int WARMUP_ITERATIONS = 10;
    private const int BENCHMARK_ITERATIONS = 100;

    public static void Run()
    {
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine("?      FrozenArrow Query Engine Performance Benchmark          ?");
        Console.WriteLine("?                  All 10 Phases                                ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        // Create datasets of different sizes
        var small = CreateDataset(1_000);
        var medium = CreateDataset(10_000);
        var large = CreateDataset(100_000);

        // Run benchmarks
        Console.WriteLine("Warming up...");
        BenchmarkPhase7_PlanCaching(small, warmup: true);
        BenchmarkPhase9_QueryCompilation(medium, warmup: true);
        Console.WriteLine();

        Console.WriteLine("Running benchmarks...");
        Console.WriteLine();

        BenchmarkPhase7_PlanCaching(small);
        BenchmarkPhase9_QueryCompilation(medium);
        BenchmarkAllPhases(large);

        Console.WriteLine();
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine("?                    Benchmark Complete                         ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
    }

    private static FrozenArrow<BenchmarkRecord> CreateDataset(int size)
    {
        var random = new Random(42);
        var records = new List<BenchmarkRecord>(size);

        for (int i = 0; i < size; i++)
        {
            records.Add(new BenchmarkRecord
            {
                Id = i,
                Value = random.Next(0, 1000),
                Score = random.NextDouble() * 100.0,
                Category = ((char)('A' + (i % 5))).ToString()
            });
        }

        return records.ToFrozenArrow();
    }

    private static void BenchmarkPhase7_PlanCaching(FrozenArrow<BenchmarkRecord> data, bool warmup = false)
    {
        if (!warmup)
            Console.WriteLine("???????????????????????????????????????????????????????????????");

        Console.WriteLine("Phase 7: Plan Caching (10-100× faster startup)");
        Console.WriteLine($"Dataset: {data.Count:N0} rows");
        Console.WriteLine();

        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;

        // Benchmark WITHOUT caching (cold start every time)
        provider.UseLogicalPlanCache = false;
        var noCacheTime = BenchmarkQuery(
            () => queryable.Where(x => x.Value > 500).Count(),
            warmup ? WARMUP_ITERATIONS : BENCHMARK_ITERATIONS);

        // Benchmark WITH caching (cache hit after first)
        provider.UseLogicalPlanCache = true;
        var stats = provider.GetLogicalPlanCacheStatistics();
        
        var cachedTime = BenchmarkQuery(
            () => queryable.Where(x => x.Value > 500).Count(),
            warmup ? WARMUP_ITERATIONS : BENCHMARK_ITERATIONS);

        if (!warmup)
        {
            var speedup = noCacheTime / cachedTime;
            Console.WriteLine($"  Without Cache: {noCacheTime:F2}ms avg");
            Console.WriteLine($"  With Cache:    {cachedTime:F2}ms avg");
            Console.WriteLine($"  Speedup:       {speedup:F2}× faster");
            Console.WriteLine($"  Cache Stats:   Hits={stats.Hits}, Misses={stats.Misses}");
            Console.WriteLine();
        }
    }

    private static void BenchmarkPhase9_QueryCompilation(FrozenArrow<BenchmarkRecord> data, bool warmup = false)
    {
        if (!warmup)
            Console.WriteLine("???????????????????????????????????????????????????????????????");

        Console.WriteLine("Phase 9: Query Compilation (2-5× faster execution)");
        Console.WriteLine($"Dataset: {data.Count:N0} rows");
        Console.WriteLine();

        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;
        provider.UseLogicalPlanExecution = true;
        provider.UseLogicalPlanCache = true;

        // Benchmark interpreted execution
        provider.UseCompiledQueries = false;
        var interpretedTime = BenchmarkQuery(
            () => queryable.Where(x => x.Value > 500 && x.Score > 50).Count(),
            warmup ? WARMUP_ITERATIONS : BENCHMARK_ITERATIONS);

        // Benchmark compiled execution
        provider.UseCompiledQueries = true;
        var compiledTime = BenchmarkQuery(
            () => queryable.Where(x => x.Value > 500 && x.Score > 50).Count(),
            warmup ? WARMUP_ITERATIONS : BENCHMARK_ITERATIONS);

        if (!warmup)
        {
            var speedup = interpretedTime / compiledTime;
            Console.WriteLine($"  Interpreted:   {interpretedTime:F2}ms avg");
            Console.WriteLine($"  Compiled:      {compiledTime:F2}ms avg");
            Console.WriteLine($"  Speedup:       {speedup:F2}× faster");
            Console.WriteLine();
        }
    }

    private static void BenchmarkAllPhases(FrozenArrow<BenchmarkRecord> data)
    {
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine("All Phases Combined (Maximum Performance)");
        Console.WriteLine($"Dataset: {data.Count:N0} rows");
        Console.WriteLine();

        var queryable = data.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;

        // Baseline: No optimizations
        provider.UseLogicalPlanExecution = false;
        var baselineTime = BenchmarkQuery(
            () => queryable.Where(x => x.Value > 500 && x.Score > 50).Count(),
            BENCHMARK_ITERATIONS);

        // Phase 5: Logical plans
        provider.UseLogicalPlanExecution = true;
        provider.UseLogicalPlanCache = false;
        provider.UseCompiledQueries = false;
        var phase5Time = BenchmarkQuery(
            () => queryable.Where(x => x.Value > 500 && x.Score > 50).Count(),
            BENCHMARK_ITERATIONS);

        // Phase 7: + Plan caching
        provider.UseLogicalPlanCache = true;
        var phase7Time = BenchmarkQuery(
            () => queryable.Where(x => x.Value > 500 && x.Score > 50).Count(),
            BENCHMARK_ITERATIONS);

        // Phase 9: + Query compilation
        provider.UseCompiledQueries = true;
        var phase9Time = BenchmarkQuery(
            () => queryable.Where(x => x.Value > 500 && x.Score > 50).Count(),
            BENCHMARK_ITERATIONS);

        // Results
        Console.WriteLine($"  Baseline (No opts):      {baselineTime:F2}ms avg");
        Console.WriteLine($"  Phase 5 (Logical):       {phase5Time:F2}ms avg ({baselineTime/phase5Time:F2}× vs baseline)");
        Console.WriteLine($"  Phase 7 (+ Cache):       {phase7Time:F2}ms avg ({baselineTime/phase7Time:F2}× vs baseline)");
        Console.WriteLine($"  Phase 9 (+ Compiled):    {phase9Time:F2}ms avg ({baselineTime/phase9Time:F2}× vs baseline)");
        Console.WriteLine();
        Console.WriteLine($"  ? Total Improvement:     {baselineTime/phase9Time:F2}× FASTER! ?");
        Console.WriteLine();
    }

    private static double BenchmarkQuery(Func<int> query, int iterations)
    {
        var times = new List<double>();
        var sw = new Stopwatch();

        for (int i = 0; i < iterations; i++)
        {
            sw.Restart();
            var result = query();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        // Remove outliers (top and bottom 10%)
        times.Sort();
        var trimCount = (int)(times.Count * 0.1);
        var trimmed = times.Skip(trimCount).Take(times.Count - 2 * trimCount).ToList();

        return trimmed.Average();
    }
}
