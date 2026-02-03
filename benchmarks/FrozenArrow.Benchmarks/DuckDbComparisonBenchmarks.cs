using FrozenArrow.Query;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using DuckDB.NET.Data;

namespace FrozenArrow.Benchmarks;

/// <summary>
/// Benchmarks comparing FrozenArrow query performance against in-process DuckDB.
/// 
/// This helps identify scenarios where FrozenArrow excels (simple filters, aggregations,
/// low memory overhead) vs where DuckDB has advantages (complex GROUP BY, query optimizer).
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ShortRunJob]
public class DuckDbComparisonBenchmarks
{
    private List<QueryBenchmarkItem> _list = null!;
    private FrozenArrow<QueryBenchmarkItem> _frozenArrow = null!;
    private DuckDBConnection _duckDbConnection = null!;

    [Params(100_000)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _list = QueryBenchmarkItemFactory.Generate(ItemCount);
        _frozenArrow = _list.ToFrozenArrow();

        // Setup in-memory DuckDB
        _duckDbConnection = new DuckDBConnection("DataSource=:memory:");
        _duckDbConnection.Open();

        // Create table
        using var createCmd = _duckDbConnection.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE benchmark_items (
                Id INTEGER,
                Name VARCHAR,
                Age INTEGER,
                Salary DECIMAL(18,2),
                IsActive BOOLEAN,
                Category VARCHAR,
                Department VARCHAR,
                HireDate TIMESTAMP,
                PerformanceScore DOUBLE,
                Region VARCHAR
            )
            """;
        createCmd.ExecuteNonQuery();

        // Use Appender for fast bulk insert
        using var appender = _duckDbConnection.CreateAppender("benchmark_items");
        foreach (var item in _list)
        {
            var row = appender.CreateRow();
            row.AppendValue(item.Id);
            row.AppendValue(item.Name);
            row.AppendValue(item.Age);
            row.AppendValue(item.Salary);
            row.AppendValue(item.IsActive);
            row.AppendValue(item.Category);
            row.AppendValue(item.Department);
            row.AppendValue(item.HireDate);
            row.AppendValue(item.PerformanceScore);
            row.AppendValue(item.Region);
            row.EndRow();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _frozenArrow.Dispose();
        _duckDbConnection.Dispose();
    }

    #region High Selectivity Filter (~5% match) - FrozenArrow expected to win

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("HighSelectivity_Count")]
    public int List_HighSelectivity_Count()
    {
        return _list.Where(x => x.Age > 55).Count();
    }

    [Benchmark]
    [BenchmarkCategory("HighSelectivity_Count")]
    public int FrozenArrow_HighSelectivity_Count()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 55).Count();
    }

    [Benchmark]
    [BenchmarkCategory("HighSelectivity_Count")]
    public int DuckDb_HighSelectivity_Count()
    {
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM benchmark_items WHERE Age > 55";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    #endregion

    #region Medium Selectivity Filter (~30% match)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MediumSelectivity_Count")]
    public int List_MediumSelectivity_Count()
    {
        return _list.Where(x => x.Age > 40).Count();
    }

    [Benchmark]
    [BenchmarkCategory("MediumSelectivity_Count")]
    public int FrozenArrow_MediumSelectivity_Count()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 40).Count();
    }

    [Benchmark]
    [BenchmarkCategory("MediumSelectivity_Count")]
    public int DuckDb_MediumSelectivity_Count()
    {
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM benchmark_items WHERE Age > 40";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    #endregion

    #region Low Selectivity Filter (~70% match) - DuckDB may catch up

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("LowSelectivity_Count")]
    public int List_LowSelectivity_Count()
    {
        return _list.Where(x => x.IsActive).Count();
    }

    [Benchmark]
    [BenchmarkCategory("LowSelectivity_Count")]
    public int FrozenArrow_LowSelectivity_Count()
    {
        return _frozenArrow.AsQueryable().Where(x => x.IsActive).Count();
    }

    [Benchmark]
    [BenchmarkCategory("LowSelectivity_Count")]
    public int DuckDb_LowSelectivity_Count()
    {
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM benchmark_items WHERE IsActive = true";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    #endregion

    #region Sum Aggregation

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Sum")]
    public decimal List_Sum()
    {
        return _list.Where(x => x.IsActive).Sum(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Sum")]
    public decimal FrozenArrow_Sum()
    {
        return _frozenArrow.AsQueryable().Where(x => x.IsActive).Sum(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Sum")]
    public decimal DuckDb_Sum()
    {
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT SUM(Salary) FROM benchmark_items WHERE IsActive = true";
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    #endregion

    #region Average Aggregation

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Average")]
    public double List_Average()
    {
        return _list.Where(x => x.Age > 40).Average(x => x.Age);
    }

    [Benchmark]
    [BenchmarkCategory("Average")]
    public double FrozenArrow_Average()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 40).Average(x => x.Age);
    }

    [Benchmark]
    [BenchmarkCategory("Average")]
    public double DuckDb_Average()
    {
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT AVG(Age) FROM benchmark_items WHERE Age > 40";
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    #endregion

    #region Min/Max Aggregation

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MinMax")]
    public decimal List_Min()
    {
        return _list.Where(x => x.Age > 40).Min(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("MinMax")]
    public decimal FrozenArrow_Min()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 40).Min(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("MinMax")]
    public decimal DuckDb_Min()
    {
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT MIN(Salary) FROM benchmark_items WHERE Age > 40";
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Max")]
    public decimal List_Max()
    {
        return _list.Where(x => x.Age > 40).Max(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Max")]
    public decimal FrozenArrow_Max()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 40).Max(x => x.Salary);
    }

    [Benchmark]
    [BenchmarkCategory("Max")]
    public decimal DuckDb_Max()
    {
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT MAX(Salary) FROM benchmark_items WHERE Age > 40";
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    #endregion

    #region GroupBy with Aggregation - DuckDB's strength

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_Sum")]
    public Dictionary<string, decimal> List_GroupBy_Sum()
    {
        return _list
            .GroupBy(x => x.Category)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Salary));
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_Sum")]
    public Dictionary<string, decimal> FrozenArrow_GroupBy_Sum()
    {
        return _frozenArrow.AsQueryable()
            .GroupBy(x => x.Category)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Salary));
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_Sum")]
    public Dictionary<string, decimal> DuckDb_GroupBy_Sum()
    {
        var result = new Dictionary<string, decimal>();
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT Category, SUM(Salary) FROM benchmark_items GROUP BY Category";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetDecimal(1);
        }
        return result;
    }

    #endregion

    #region GroupBy with Multiple Aggregations

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GroupBy_MultiAgg")]
    public Dictionary<string, (decimal Sum, double Avg, int Count)> List_GroupBy_MultiAgg()
    {
        return _list
            .GroupBy(x => x.Category)
            .ToDictionary(
                g => g.Key,
                g => (g.Sum(x => x.Salary), g.Average(x => x.Age), g.Count()));
    }

    [Benchmark]
    [BenchmarkCategory("GroupBy_MultiAgg")]
    public Dictionary<string, (decimal Sum, double Avg, int Count)> DuckDb_GroupBy_MultiAgg()
    {
        var result = new Dictionary<string, (decimal Sum, double Avg, int Count)>();
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT Category, SUM(Salary), AVG(Age), COUNT(*) FROM benchmark_items GROUP BY Category";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = (reader.GetDecimal(1), reader.GetDouble(2), reader.GetInt32(3));
        }
        return result;
    }

    #endregion

    #region Compound Filter (AND conditions)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("CompoundFilter")]
    public int List_CompoundFilter()
    {
        return _list.Where(x => x.Age > 30 && x.IsActive && x.Category == "Engineering").Count();
    }

    [Benchmark]
    [BenchmarkCategory("CompoundFilter")]
    public int FrozenArrow_CompoundFilter()
    {
        return _frozenArrow.AsQueryable()
            .Where(x => x.Age > 30 && x.IsActive && x.Category == "Engineering")
            .Count();
    }

    [Benchmark]
    [BenchmarkCategory("CompoundFilter")]
    public int DuckDb_CompoundFilter()
    {
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM benchmark_items WHERE Age > 30 AND IsActive = true AND Category = 'Engineering'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    #endregion

    #region String Equality Filter (Dictionary-encoded advantage)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("StringFilter")]
    public int List_StringFilter()
    {
        return _list.Where(x => x.Category == "Engineering").Count();
    }

    [Benchmark]
    [BenchmarkCategory("StringFilter")]
    public int FrozenArrow_StringFilter()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Category == "Engineering").Count();
    }

    [Benchmark]
    [BenchmarkCategory("StringFilter")]
    public int DuckDb_StringFilter()
    {
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM benchmark_items WHERE Category = 'Engineering'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    #endregion

    #region Filter + Materialize (ToList)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FilterToList")]
    public int List_FilterToList()
    {
        return _list.Where(x => x.Age > 55).ToList().Count;
    }

    [Benchmark]
    [BenchmarkCategory("FilterToList")]
    public int FrozenArrow_FilterToList()
    {
        return _frozenArrow.AsQueryable().Where(x => x.Age > 55).ToList().Count;
    }

    [Benchmark]
    [BenchmarkCategory("FilterToList")]
    public int DuckDb_FilterToList()
    {
        var results = new List<QueryBenchmarkItem>();
        using var cmd = _duckDbConnection.CreateCommand();
        cmd.CommandText = "SELECT * FROM benchmark_items WHERE Age > 55";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new QueryBenchmarkItem
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Age = reader.GetInt32(2),
                Salary = reader.GetDecimal(3),
                IsActive = reader.GetBoolean(4),
                Category = reader.GetString(5),
                Department = reader.GetString(6),
                HireDate = reader.GetDateTime(7),
                PerformanceScore = reader.GetDouble(8),
                Region = reader.GetString(9)
            });
        }
        return results.Count;
    }

    #endregion
}
