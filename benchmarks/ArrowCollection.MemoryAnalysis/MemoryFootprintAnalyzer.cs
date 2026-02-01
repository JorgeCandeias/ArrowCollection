using System.Diagnostics;
using System.Runtime.InteropServices;
using ArrowCollection;

namespace ArrowCollection.MemoryAnalysis;

/// <summary>
/// Analyzes the long-term memory footprint of ArrowCollection vs List.
/// This is separate from BenchmarkDotNet because steady-state memory measurement
/// requires a different approach than allocation/timing benchmarks.
/// </summary>
/// <remarks>
/// <para>
/// BenchmarkDotNet's MemoryDiagnoser measures allocations during execution,
/// not retained memory after GC. This harness provides both theoretical analysis
/// and empirical measurements for collections held in memory long-term.
/// </para>
/// <para>
/// This analyzer uses Process.PrivateMemorySize64 to capture both managed heap
/// and native memory (like Arrow's NativeMemoryAllocator) in a single measurement.
/// </para>
/// </remarks>
public static class MemoryFootprintAnalyzer
{
    /// <summary>
    /// Runs the memory footprint analysis and prints results.
    /// </summary>
    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("+==============================================================================+");
        Console.WriteLine("|              LONG-TERM MEMORY FOOTPRINT ANALYSIS                             |");
        Console.WriteLine("|  Compares retained memory for List<T> vs ArrowCollection                     |");
        Console.WriteLine("+==============================================================================+");
        Console.WriteLine();

        // Theoretical analysis
        Console.WriteLine("THEORETICAL MEMORY ANALYSIS");
        Console.WriteLine("===========================");
        Console.WriteLine();
        RunTheoreticalAnalysis();

        Console.WriteLine();
        Console.WriteLine("EMPIRICAL MEMORY ANALYSIS (Process Memory)");
        Console.WriteLine("==========================================");
        Console.WriteLine();
        Console.WriteLine("Using Process.PrivateMemorySize64 to capture both managed and native memory.");
        Console.WriteLine();

        // Warm up
        WarmUp();

        // Empirical measurements
        RunEmpiricalAnalysis();

        // Cardinality impact
        Console.WriteLine();
        Console.WriteLine("STRING CARDINALITY IMPACT");
        Console.WriteLine("=========================");
        Console.WriteLine("Arrow's columnar format benefits low-cardinality string columns.");
        Console.WriteLine();
        RunCardinalityAnalysis();
    }

    private static void RunTheoreticalAnalysis()
    {
        Console.WriteLine("For a class with 7 fields (int, 3x string refs, double, bool, DateTime):");
        Console.WriteLine();
        
        // Calculate theoretical sizes
        var objectHeaderSize = IntPtr.Size == 8 ? 16 : 8; // Method table + sync block
        var intSize = 4;
        var stringRefSize = IntPtr.Size; // Reference size
        var doubleSize = 8;
        var boolSize = 1;
        var dateTimeSize = 8;
        
        // Padding for alignment
        var classFieldsSize = intSize + (3 * stringRefSize) + doubleSize + boolSize + dateTimeSize;
        var classTotalSize = objectHeaderSize + classFieldsSize;
        // Round up to pointer alignment
        classTotalSize = (classTotalSize + IntPtr.Size - 1) / IntPtr.Size * IntPtr.Size;
        
        Console.WriteLine($"  Per-object overhead in List<T>:");
        Console.WriteLine($"    Object header:    {objectHeaderSize} bytes");
        Console.WriteLine($"    Fields:           ~{classFieldsSize} bytes");
        Console.WriteLine($"    Total per item:   ~{classTotalSize} bytes (+ string content)");
        Console.WriteLine();
        
        // Arrow format
        Console.WriteLine($"  Arrow columnar format (per item, amortized):");
        Console.WriteLine($"    int column:       4 bytes");
        Console.WriteLine($"    3x string cols:   ~variable (offset arrays + deduplicated data)");
        Console.WriteLine($"    double column:    8 bytes");
        Console.WriteLine($"    bool column:      1 bit (packed)");
        Console.WriteLine($"    DateTime column:  8 bytes (stored as int64 timestamp)");
        Console.WriteLine($"    Fixed overhead:   ~20.125 bytes/item + string data");
        Console.WriteLine();
        
        // Show scaling
        int[] itemCounts = [10_000, 100_000, 1_000_000];
        var avgStringLen = 12; // "Category_XX"
        
        Console.WriteLine("  Estimated memory usage (assuming ~12 char average string length):");
        Console.WriteLine();
        Console.WriteLine("  +-------------+-----------------+-----------------+--------------+");
        Console.WriteLine("  |   Items     |   List<T> (MB)  |   Arrow (MB)    |   Savings    |");
        Console.WriteLine("  +-------------+-----------------+-----------------+--------------+");
        
        foreach (var count in itemCounts)
        {
            // List estimate: object overhead + string objects (header + length + chars)
            var stringObjectSize = objectHeaderSize + 4 + (avgStringLen * 2); // header + length + UTF16 chars
            var listPerItem = classTotalSize + (3 * stringObjectSize); // 3 string fields
            var listTotalMB = (count * listPerItem) / (1024.0 * 1024.0);
            
            // Arrow estimate: fixed-width columns + string data (stored once if repeated)
            // With 100 unique categories repeated across items:
            var uniqueStrings = Math.Min(count, 100);
            var arrowFixedPerItem = 4 + 8 + (1.0 / 8) + 8; // int + double + bool-bit + datetime
            var arrowStringOverhead = 4 * 3; // 3 offset arrays (4 bytes per item)
            var arrowStringData = uniqueStrings * avgStringLen * 3; // Actual string bytes (3 columns)
            var arrowTotalMB = (count * (arrowFixedPerItem + arrowStringOverhead) + arrowStringData) / (1024.0 * 1024.0);
            
            var savingsPercent = (1.0 - arrowTotalMB / listTotalMB) * 100;
            
            Console.WriteLine($"  | {count,11:N0} | {listTotalMB,15:F2} | {arrowTotalMB,15:F2} | {savingsPercent,10:F1}% |");
        }
        
        Console.WriteLine("  +-------------+-----------------+-----------------+--------------+");
        Console.WriteLine();
        Console.WriteLine("  * Arrow savings increase with more items due to string deduplication");
        Console.WriteLine("  * Actual results depend on string cardinality and content");
    }

    private static void WarmUp()
    {
        Console.WriteLine("Warming up...");
        
        // Create and dispose some collections to warm up JIT
        var warmupItems = GenerateItems(1000, stringCardinality: 100);
        var warmupList = warmupItems.ToList();
        using var warmupCollection = warmupItems.ToArrowCollection();
        
        GC.KeepAlive(warmupList);
        GC.KeepAlive(warmupCollection);
        
        ForceFullGC();
        Console.WriteLine("Warmup complete.");
        Console.WriteLine();
    }

    private static void RunEmpiricalAnalysis()
    {
        int[] sizes = [10_000, 100_000, 1_000_000];

        Console.WriteLine("+-------------+------------------+------------------+--------------+");
        Console.WriteLine("|   Items     |  List<T> (MB)    |  Arrow (MB)      |   Savings    |");
        Console.WriteLine("|             | (process memory) | (process memory) |              |");
        Console.WriteLine("+-------------+------------------+------------------+--------------+");

        foreach (var size in sizes)
        {
            var (listMemory, arrowMemory) = MeasureProcessMemory(size);
            
            var listMB = listMemory / (1024.0 * 1024.0);
            var arrowMB = arrowMemory / (1024.0 * 1024.0);
            var savings = listMemory > 0 ? (1.0 - (double)arrowMemory / listMemory) * 100 : 0;
            
            Console.WriteLine($"| {size,11:N0} | {listMB,16:F3} | {arrowMB,16:F3} | {savings,10:F1}% |");
        }

        Console.WriteLine("+-------------+------------------+------------------+--------------+");
        Console.WriteLine();
        Console.WriteLine("  * Measurements use Process.PrivateMemorySize64");
        Console.WriteLine("  * Captures both managed heap AND native memory (Arrow buffers)");
    }

    private static (long listMemory, long arrowMemory) MeasureProcessMemory(int itemCount)
    {
        var process = Process.GetCurrentProcess();
        
        // Measure List<T> memory footprint
        ForceFullGC();
        process.Refresh();
        var beforeList = process.PrivateMemorySize64;
        
        var list = GenerateItems(itemCount, stringCardinality: 100);
        
        ForceFullGC();
        process.Refresh();
        var afterList = process.PrivateMemorySize64;
        var listMemory = afterList - beforeList;
        
        // Keep list alive until we've measured, then release
        GC.KeepAlive(list);
        list = null;
        ForceFullGC();
        
        // Small delay to let memory settle
        Thread.Sleep(100);
        
        // Measure ArrowCollection memory footprint
        ForceFullGC();
        process.Refresh();
        var beforeArrow = process.PrivateMemorySize64;
        
        var arrowCollection = GenerateItemsEnumerable(itemCount, stringCardinality: 100).ToArrowCollection();
        
        ForceFullGC();
        process.Refresh();
        var afterArrow = process.PrivateMemorySize64;
        var arrowMemory = afterArrow - beforeArrow;
        
        // Cleanup
        GC.KeepAlive(arrowCollection);
        arrowCollection.Dispose();
        ForceFullGC();
        
        // Small delay to let memory settle
        Thread.Sleep(100);
        
        return (listMemory, arrowMemory);
    }

    private static void RunCardinalityAnalysis()
    {
        const int itemCount = 100_000;
        int[] cardinalities = [10, 100, 1000, 10000, itemCount];
        
        Console.WriteLine($"Testing with {itemCount:N0} items, varying string uniqueness:");
        Console.WriteLine("(Using actual process memory measurements)");
        Console.WriteLine();
        Console.WriteLine("+---------------+------------------+------------------+------------------+");
        Console.WriteLine("|  Unique Strs  |  List<T> (MB)    |  Arrow (MB)      |  Savings         |");
        Console.WriteLine("+---------------+------------------+------------------+------------------+");
        
        var process = Process.GetCurrentProcess();
        
        foreach (var cardinality in cardinalities)
        {
            // Measure List<T>
            ForceFullGC();
            process.Refresh();
            var beforeList = process.PrivateMemorySize64;
            
            var list = GenerateItems(itemCount, stringCardinality: cardinality);
            
            ForceFullGC();
            process.Refresh();
            var listMemory = process.PrivateMemorySize64 - beforeList;
            var listMB = listMemory / (1024.0 * 1024.0);
            
            GC.KeepAlive(list);
            list = null;
            ForceFullGC();
            Thread.Sleep(50);
            
            // Measure Arrow
            ForceFullGC();
            process.Refresh();
            var beforeArrow = process.PrivateMemorySize64;
            
            var arrowCollection = GenerateItemsEnumerable(itemCount, stringCardinality: cardinality).ToArrowCollection();
            
            ForceFullGC();
            process.Refresh();
            var arrowMemory = process.PrivateMemorySize64 - beforeArrow;
            var arrowMB = arrowMemory / (1024.0 * 1024.0);
            
            var savings = listMemory > 0 ? (1.0 - (double)arrowMemory / listMemory) * 100 : 0;
            
            Console.WriteLine($"| {cardinality,13:N0} | {listMB,16:F2} | {arrowMB,16:F2} | {savings,14:F1}% |");
            
            GC.KeepAlive(arrowCollection);
            arrowCollection.Dispose();
            ForceFullGC();
            Thread.Sleep(50);
        }
        
        Console.WriteLine("+---------------+------------------+------------------+------------------+");
        Console.WriteLine();
        Console.WriteLine("  Key insight: Arrow's columnar format excels when:");
        Console.WriteLine("  - String columns have low cardinality (many repeated values)");
        Console.WriteLine("  - Data has many rows (amortizes metadata overhead)");
        Console.WriteLine("  - Fields are fixed-width primitives (int, double, bool, DateTime)");
        Console.WriteLine();
        Console.WriteLine("  Note: With dictionary encoding, Arrow stores each unique string once,");
        Console.WriteLine("        then uses compact indices to reference them.");
    }

    private static void ForceFullGC()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private static List<MemoryTestItem> GenerateItems(int count, int stringCardinality)
    {
        var baseDate = DateTime.UtcNow;
        var categories = Enumerable.Range(0, stringCardinality).Select(i => $"Category_{i}").ToArray();
        
        return Enumerable.Range(0, count).Select(i => new MemoryTestItem
        {
            Id = i,
            Category1 = categories[i % categories.Length],
            Category2 = categories[(i + 33) % categories.Length],
            Category3 = categories[(i + 67) % categories.Length],
            Value = i * 1.5,
            IsActive = i % 2 == 0,
            CreatedAt = baseDate.AddSeconds(-i)
        }).ToList();
    }

    private static IEnumerable<MemoryTestItem> GenerateItemsEnumerable(int count, int stringCardinality)
    {
        var baseDate = DateTime.UtcNow;
        var categories = Enumerable.Range(0, stringCardinality).Select(i => $"Category_{i}").ToArray();
        
        for (int i = 0; i < count; i++)
        {
            yield return new MemoryTestItem
            {
                Id = i,
                Category1 = categories[i % categories.Length],
                Category2 = categories[(i + 33) % categories.Length],
                Category3 = categories[(i + 67) % categories.Length],
                Value = i * 1.5,
                IsActive = i % 2 == 0,
                CreatedAt = baseDate.AddSeconds(-i)
            };
        }
    }
}

/// <summary>
/// Test item for memory footprint analysis.
/// </summary>
[ArrowRecord]
public class MemoryTestItem
{
    [ArrowArray]
    public int Id { get; set; }
    
    [ArrowArray]
    public string Category1 { get; set; } = "";
    
    [ArrowArray]
    public string Category2 { get; set; } = "";
    
    [ArrowArray]
    public string Category3 { get; set; } = "";
    
    [ArrowArray]
    public double Value { get; set; }
    
    [ArrowArray]
    public bool IsActive { get; set; }
    
    [ArrowArray]
    public DateTime CreatedAt { get; set; }
}
