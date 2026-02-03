using FrozenArrow.Query;

namespace FrozenArrow.Profiling.Scenarios;

/// <summary>
/// Profiles aggregation performance with varying selection sparsity.
/// This scenario specifically tests the block-based bitmap iteration optimization,
/// which excels when processing sparse selections after filtering.
/// </summary>
/// <remarks>
/// The block-based iteration optimization works by:
/// 1. Processing 64 bits (rows) at a time using ulong blocks
/// 2. Skipping entire blocks that have no selected rows
/// 3. Using TrailingZeroCount to find set bits efficiently
/// 
/// This optimization is most effective when the selection is sparse (low selectivity),
/// allowing entire blocks to be skipped without examining individual rows.
/// </remarks>
public sealed class SparseAggregationScenario : BaseScenario
{
    public override string Name => "SparseAggregation";
    public override string Description => "Aggregation with varying selection sparsity (1%, 10%, 50%)";

    // Results for different selectivity levels
    private double _sum1Pct;    // ~1% selectivity (very sparse)
    private double _sum10Pct;   // ~10% selectivity (sparse)
    private double _sum50Pct;   // ~50% selectivity (dense)
    private double _sumAll;     // 100% selectivity (baseline)
    
    private int _count1Pct;
    private int _count10Pct;
    private int _count50Pct;

    public override object? RunIteration()
    {
        // 1% selectivity: Age > 63 (top ~2% of 20-64 range)
        // With uniform distribution in 20-64 (45 values), Age > 63 means Age == 64, ~2.2%
        var query1 = Data.AsQueryable();
        ConfigureParallel(query1);
        _sum1Pct = query1.Where(x => x.Age > 63).Sum(x => x.Salary);
        _count1Pct = Data.AsQueryable().Where(x => x.Age > 63).Count();

        // 10% selectivity: Age > 60 (top ~9% of 20-64 range)
        var query2 = Data.AsQueryable();
        ConfigureParallel(query2);
        _sum10Pct = query2.Where(x => x.Age > 60).Sum(x => x.Salary);
        _count10Pct = Data.AsQueryable().Where(x => x.Age > 60).Count();

        // 50% selectivity: Age > 42 (approximately middle)
        var query3 = Data.AsQueryable();
        ConfigureParallel(query3);
        _sum50Pct = query3.Where(x => x.Age > 42).Sum(x => x.Salary);
        _count50Pct = Data.AsQueryable().Where(x => x.Age > 42).Count();

        // 100% selectivity: No filter (baseline)
        var query4 = Data.AsQueryable();
        ConfigureParallel(query4);
        _sumAll = query4.Sum(x => x.Salary);

        return _sum1Pct + _sum10Pct + _sum50Pct + _sumAll;
    }

    public override (object? Result, Dictionary<string, double> PhaseMicroseconds) RunIterationWithPhases()
    {
        CurrentPhases.Clear();

        // 1% selectivity
        StartPhase("Sum1PctFilter");
        var query1 = Data.AsQueryable();
        ConfigureParallel(query1);
        _sum1Pct = query1.Where(x => x.Age > 63).Sum(x => x.Salary);
        _count1Pct = Data.AsQueryable().Where(x => x.Age > 63).Count();
        EndPhase("Sum1PctFilter");

        // 10% selectivity
        StartPhase("Sum10PctFilter");
        var query2 = Data.AsQueryable();
        ConfigureParallel(query2);
        _sum10Pct = query2.Where(x => x.Age > 60).Sum(x => x.Salary);
        _count10Pct = Data.AsQueryable().Where(x => x.Age > 60).Count();
        EndPhase("Sum10PctFilter");

        // 50% selectivity
        StartPhase("Sum50PctFilter");
        var query3 = Data.AsQueryable();
        ConfigureParallel(query3);
        _sum50Pct = query3.Where(x => x.Age > 42).Sum(x => x.Salary);
        _count50Pct = Data.AsQueryable().Where(x => x.Age > 42).Count();
        EndPhase("Sum50PctFilter");

        // 100% selectivity (no filter)
        StartPhase("Sum100PctNoFilter");
        var query4 = Data.AsQueryable();
        ConfigureParallel(query4);
        _sumAll = query4.Sum(x => x.Salary);
        EndPhase("Sum100PctNoFilter");

        return (_sum1Pct + _sum10Pct + _sum50Pct + _sumAll, new Dictionary<string, double>(CurrentPhases));
    }

    private void ConfigureParallel(IQueryable<ProfilingRecord> query)
    {
        ((ArrowQueryProvider)query.Provider).ParallelOptions = new ParallelQueryOptions
        {
            EnableParallelExecution = Config.EnableParallel
        };
    }

    public override Dictionary<string, string> GetMetadata()
    {
        var pct1 = Config.RowCount > 0 ? (double)_count1Pct / Config.RowCount * 100 : 0;
        var pct10 = Config.RowCount > 0 ? (double)_count10Pct / Config.RowCount * 100 : 0;
        var pct50 = Config.RowCount > 0 ? (double)_count50Pct / Config.RowCount * 100 : 0;

        return new Dictionary<string, string>
        {
            ["1PctSelectivity"] = $"{_count1Pct:N0} rows ({pct1:F1}%), Sum={_sum1Pct:N2}",
            ["10PctSelectivity"] = $"{_count10Pct:N0} rows ({pct10:F1}%), Sum={_sum10Pct:N2}",
            ["50PctSelectivity"] = $"{_count50Pct:N0} rows ({pct50:F1}%), Sum={_sum50Pct:N2}",
            ["100PctSelectivity"] = $"{Config.RowCount:N0} rows, Sum={_sumAll:N2}"
        };
    }
}
