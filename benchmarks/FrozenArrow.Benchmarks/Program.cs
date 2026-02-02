using BenchmarkDotNet.Running;
using FrozenArrow.Benchmarks;

// Use BenchmarkSwitcher for flexible benchmark selection
// Run with --help to see all options, or --list to see available benchmarks
// Examples:
//   dotnet run -c Release                              # Interactive selection
//   dotnet run -c Release -- --filter "*Query*"        # Run all query benchmarks
//   dotnet run -c Release -- --filter "*HighSelectivity*"  # Run specific category
//   dotnet run -c Release -- --list flat               # List all benchmarks

var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);

if (args.Length == 0)
{
    Console.WriteLine("FrozenArrow Benchmark Runner");
    Console.WriteLine("================================");
    Console.WriteLine();
    Console.WriteLine("Available benchmark classes:");
    Console.WriteLine("  - FrozenArrowBenchmarks         : Core construction/enumeration benchmarks");
    Console.WriteLine("  - StructVsClassBenchmarks       : Struct vs class comparison");
    Console.WriteLine("  - HeavyRecordBenchmarks         : 200-property record benchmarks");
    Console.WriteLine("  - ArrowQueryBenchmarks          : ArrowQuery vs List vs Enumerable (10K-100K items)");
    Console.WriteLine("  - LargeScaleQueryBenchmarks     : ArrowQuery at 1M scale (filters, aggregates, groupby, multi-agg)");
    Console.WriteLine("  - WideRecordQueryBenchmarks     : ArrowQuery benchmarks for wide records (200 columns)");
    Console.WriteLine("  - DuckDbComparisonBenchmarks    : FrozenArrow vs in-process DuckDB comparison");
    Console.WriteLine();
    Console.WriteLine("Usage examples:");
    Console.WriteLine("  dotnet run -c Release                                    # Interactive selection");
    Console.WriteLine("  dotnet run -c Release -- --filter *Query*                # Run all query benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter *LargeScale*           # Run 1M-scale benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter *GroupBy*              # Run GroupBy benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter *MultiAggregate*       # Run multi-aggregate benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter ArrowQueryBenchmarks*  # Run specific class");
    Console.WriteLine("  dotnet run -c Release -- --list flat                     # List all benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --help                          # Show BenchmarkDotNet help");
    Console.WriteLine();
    Console.WriteLine("For memory footprint analysis, run the FrozenArrow.MemoryAnalysis project.");
    Console.WriteLine();
}

switcher.Run(args);


