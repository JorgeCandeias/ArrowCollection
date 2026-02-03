using System.Diagnostics;
using DuckDB.NET.Data;

namespace FrozenArrow.MemoryAnalysis;

/// <summary>
/// Analyzes the memory footprint of FrozenArrow vs in-process DuckDB.
/// 
/// This comparison helps identify scenarios where each approach excels:
/// - FrozenArrow: Lower baseline memory, zero-copy queries
/// - DuckDB: Query execution buffers, but mature compression
/// </summary>
public static class DuckDbMemoryAnalyzer
{
    /// <summary>
    /// Runs the DuckDB vs FrozenArrow memory comparison analysis.
    /// </summary>
    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("+==============================================================================+");
        Console.WriteLine("|         DUCKDB vs FROZENARROW MEMORY FOOTPRINT COMPARISON                    |");
        Console.WriteLine("|  Compares retained memory for in-process DuckDB vs FrozenArrow               |");
        Console.WriteLine("+==============================================================================+");
        Console.WriteLine();

        // Warm up
        WarmUp();

        // Static memory comparison
        Console.WriteLine("STATIC MEMORY FOOTPRINT (Holding Data at Rest)");
        Console.WriteLine("===============================================");
        Console.WriteLine();
        RunStaticMemoryComparison();

        // Query memory overhead
        Console.WriteLine();
        Console.WriteLine("MEMORY OVERHEAD DURING QUERY EXECUTION");
        Console.WriteLine("======================================");
        Console.WriteLine();
        RunQueryMemoryComparison();

        // Summary
        Console.WriteLine();
        Console.WriteLine("ANALYSIS SUMMARY");
        Console.WriteLine("================");
        PrintSummary();
    }

    private static void WarmUp()
    {
        Console.WriteLine("Warming up...");
        
        // Warm up FrozenArrow
        var warmupList = GenerateItems(1000);
        using var warmupFrozen = warmupList.ToFrozenArrow();
        _ = warmupFrozen.AsQueryable().Where(x => x.Age > 30).Count();

        // Warm up DuckDB
        using var warmupConn = new DuckDBConnection("DataSource=:memory:");
        warmupConn.Open();
        using var cmd = warmupConn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteScalar();

        ForceGC();
        Console.WriteLine("Warmup complete.");
        Console.WriteLine();
    }

    private static void RunStaticMemoryComparison()
    {
        var itemCounts = new[] { 10_000, 100_000, 500_000, 1_000_000 };

        Console.WriteLine($"{"Items",-12} {"List<T>",-15} {"FrozenArrow",-15} {"DuckDB",-15} {"FA vs List",-12} {"FA vs Duck",-12}");
        Console.WriteLine(new string('-', 81));

        foreach (var count in itemCounts)
        {
            var (listMemory, frozenArrowMemory, duckDbMemory) = MeasureStaticMemory(count);

            var faVsListRatio = (double)frozenArrowMemory / listMemory;
            var faVsDuckRatio = (double)frozenArrowMemory / duckDbMemory;

            Console.WriteLine($"{count,-12:N0} {FormatBytes(listMemory),-15} {FormatBytes(frozenArrowMemory),-15} {FormatBytes(duckDbMemory),-15} {faVsListRatio,-12:P0} {faVsDuckRatio,-12:P0}");
        }
    }

    private static (long ListMemory, long FrozenArrowMemory, long DuckDbMemory) MeasureStaticMemory(int itemCount)
    {
        long listMemory, frozenArrowMemory, duckDbMemory;

        // Measure List<T>
        {
            ForceGC();
            var baseline = GetProcessMemory();

            var list = GenerateItems(itemCount);
            ForceGC();
            listMemory = GetProcessMemory() - baseline;

            // Keep reference alive
            GC.KeepAlive(list);
        }

        ForceGC();

        // Measure FrozenArrow
        {
            ForceGC();
            var baseline = GetProcessMemory();

            var list = GenerateItems(itemCount);
            var frozen = list.ToFrozenArrow();
            list = null; // Allow list to be collected
            ForceGC();
            frozenArrowMemory = GetProcessMemory() - baseline;

            // Cleanup
            frozen.Dispose();
        }

        ForceGC();

        // Measure DuckDB
        {
            ForceGC();
            var baseline = GetProcessMemory();

            using var conn = new DuckDBConnection("DataSource=:memory:");
            conn.Open();
            CreateAndPopulateDuckDbTable(conn, itemCount);
            ForceGC();
            duckDbMemory = GetProcessMemory() - baseline;
        }

        ForceGC();

        return (listMemory, frozenArrowMemory, duckDbMemory);
    }

    private static void RunQueryMemoryComparison()
    {
        const int itemCount = 500_000;

        Console.WriteLine($"Dataset size: {itemCount:N0} items");
        Console.WriteLine();
        Console.WriteLine($"{"Query Type",-30} {"FrozenArrow",-15} {"DuckDB",-15} {"Ratio",-12}");
        Console.WriteLine(new string('-', 72));

        // Setup data
        var list = GenerateItems(itemCount);
        using var frozen = list.ToFrozenArrow();

        using var conn = new DuckDBConnection("DataSource=:memory:");
        conn.Open();
        CreateAndPopulateDuckDbTable(conn, list);

        list = null;
        ForceGC();

        // Count query (no materialization)
        var (faCountMem, duckCountMem) = MeasureQueryMemory(
            () => frozen.AsQueryable().Where(x => x.Age > 40).Count(),
            () =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM items WHERE Age > 40";
                return Convert.ToInt32(cmd.ExecuteScalar());
            });

        Console.WriteLine($"{"Count (no materialization)",-30} {FormatBytes(faCountMem),-15} {FormatBytes(duckCountMem),-15} {(double)faCountMem / Math.Max(duckCountMem, 1),-12:P0}");

        // Sum aggregation
        var (faSumMem, duckSumMem) = MeasureQueryMemory(
            () => frozen.AsQueryable().Where(x => x.IsActive).Sum(x => x.Salary),
            () =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT SUM(Salary) FROM items WHERE IsActive = true";
                return Convert.ToDecimal(cmd.ExecuteScalar());
            });

        Console.WriteLine($"{"Sum aggregation",-30} {FormatBytes(faSumMem),-15} {FormatBytes(duckSumMem),-15} {(double)faSumMem / Math.Max(duckSumMem, 1),-12:P0}");

        // ToList (full materialization, ~5% selectivity)
        var (faToListMem, duckToListMem) = MeasureQueryMemory(
            () => frozen.AsQueryable().Where(x => x.Age > 55).ToList(),
            () =>
            {
                var results = new List<MemoryAnalysisItem>();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM items WHERE Age > 55";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new MemoryAnalysisItem
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Age = reader.GetInt32(2),
                        Salary = reader.GetDecimal(3),
                        IsActive = reader.GetBoolean(4),
                        Category = reader.GetString(5),
                        Department = reader.GetString(6)
                    });
                }
                return results;
            });

        Console.WriteLine($"{"ToList (~5% selectivity)",-30} {FormatBytes(faToListMem),-15} {FormatBytes(duckToListMem),-15} {(double)faToListMem / Math.Max(duckToListMem, 1),-12:P0}");

        // GroupBy aggregation
        var (faGroupMem, duckGroupMem) = MeasureQueryMemory(
            () => frozen.AsQueryable().GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.Sum(x => x.Salary)),
            () =>
            {
                var result = new Dictionary<string, decimal>();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Category, SUM(Salary) FROM items GROUP BY Category";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result[reader.GetString(0)] = reader.GetDecimal(1);
                }
                return result;
            });

        Console.WriteLine($"{"GroupBy + Sum",-30} {FormatBytes(faGroupMem),-15} {FormatBytes(duckGroupMem),-15} {(double)faGroupMem / Math.Max(duckGroupMem, 1),-12:P0}");
    }

    private static (long FrozenArrowMemory, long DuckDbMemory) MeasureQueryMemory<T>(Func<T> frozenArrowQuery, Func<T> duckDbQuery)
    {
        const int iterations = 5;

        // Measure FrozenArrow
        ForceGC();
        var faBaseline = GetProcessMemory();
        for (int i = 0; i < iterations; i++)
        {
            var result = frozenArrowQuery();
            GC.KeepAlive(result);
        }
        var faPeak = GetProcessMemory();
        ForceGC();
        var faMemory = Math.Max(0, faPeak - faBaseline);

        // Measure DuckDB
        ForceGC();
        var duckBaseline = GetProcessMemory();
        for (int i = 0; i < iterations; i++)
        {
            var result = duckDbQuery();
            GC.KeepAlive(result);
        }
        var duckPeak = GetProcessMemory();
        ForceGC();
        var duckMemory = Math.Max(0, duckPeak - duckBaseline);

        return (faMemory, duckMemory);
    }

    private static void PrintSummary()
    {
        Console.WriteLine("""
            Key Findings:
            
            FrozenArrow advantages:
            ? Lower static memory footprint (columnar + compression)
            ? Zero-copy queries - no intermediate buffers for simple aggregations
            ? Selective materialization - only matching rows are deserialized
            ? No SQL parsing overhead - direct method calls
            
            DuckDB advantages:
            ? Mature query optimizer for complex queries
            ? Excellent for multi-table JOINs (not supported in FrozenArrow)
            ? SQL familiarity for analytics workloads
            ? Built-in compression and statistics
            
            Recommendation:
            - Use FrozenArrow for: read-heavy workloads, simple filters/aggregations,
              memory-constrained environments, .NET-native APIs
            - Use DuckDB for: complex analytical queries, multi-table joins,
              ad-hoc SQL queries, data exploration
            """);
    }

    private static void CreateAndPopulateDuckDbTable(DuckDBConnection conn, int itemCount)
    {
        var items = GenerateItems(itemCount);
        CreateAndPopulateDuckDbTable(conn, items);
    }

    private static void CreateAndPopulateDuckDbTable(DuckDBConnection conn, List<MemoryAnalysisItem> items)
    {
        using var createCmd = conn.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE items (
                Id INTEGER,
                Name VARCHAR,
                Age INTEGER,
                Salary DECIMAL(18,2),
                IsActive BOOLEAN,
                Category VARCHAR,
                Department VARCHAR
            )
            """;
        createCmd.ExecuteNonQuery();

        // Use Appender for fast bulk insert
        using var appender = conn.CreateAppender("items");
        foreach (var item in items)
        {
            var row = appender.CreateRow();
            row.AppendValue(item.Id);
            row.AppendValue(item.Name);
            row.AppendValue(item.Age);
            row.AppendValue(item.Salary);
            row.AppendValue(item.IsActive);
            row.AppendValue(item.Category);
            row.AppendValue(item.Department);
            row.EndRow();
        }
    }

    private static List<MemoryAnalysisItem> GenerateItems(int count)
    {
        var categories = new[] { "Engineering", "Marketing", "Sales", "HR", "Finance", "Operations" };
        var departments = new[] { "Dept_A", "Dept_B", "Dept_C", "Dept_D", "Dept_E" };
        var random = new Random(42);
        var items = new List<MemoryAnalysisItem>(count);

        for (int i = 0; i < count; i++)
        {
            var age = 20 + random.Next(41);
            items.Add(new MemoryAnalysisItem
            {
                Id = i,
                Name = $"Person_{i}",
                Age = age,
                Salary = 40000 + (age - 20) * 1000 + random.Next(-5000, 15000),
                IsActive = random.NextDouble() < 0.7,
                Category = categories[random.Next(categories.Length)],
                Department = departments[random.Next(departments.Length)]
            });
        }

        return items;
    }

    private static void ForceGC()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static long GetProcessMemory()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "N/A";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}

/// <summary>
/// Item class for memory analysis benchmarks.
/// </summary>
[ArrowRecord]
public partial class MemoryAnalysisItem
{
    [ArrowArray] public int Id { get; set; }
    [ArrowArray] public string Name { get; set; } = string.Empty;
    [ArrowArray] public int Age { get; set; }
    [ArrowArray] public decimal Salary { get; set; }
    [ArrowArray] public bool IsActive { get; set; }
    [ArrowArray] public string Category { get; set; } = string.Empty;
    [ArrowArray] public string Department { get; set; } = string.Empty;
}
