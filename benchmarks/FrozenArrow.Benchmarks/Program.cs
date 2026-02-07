using BenchmarkDotNet.Running;
using FrozenArrow.Benchmarks;

// Check if user wants quick benchmark (custom) or full BenchmarkDotNet
if (args.Length > 0 && args[0] == "--quick")
{
    Console.WriteLine();
    Console.WriteLine("Running Quick Performance Benchmarks...");
    Console.WriteLine();
    
    PhasePerformanceBenchmark.Run();
    
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
}
else
{
    // Use BenchmarkDotNet for rigorous benchmarking
    // Run with --help to see all options, or --list to see available benchmarks
    var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);

    if (args.Length == 0)
    {
        Console.WriteLine("FrozenArrow Benchmark Runner");
        Console.WriteLine("================================");
        Console.WriteLine();
        Console.WriteLine("Benchmarks are organized by operation type, with all technologies competing side-by-side.");
        Console.WriteLine();
        Console.WriteLine("User-Facing Benchmarks:");
    Console.WriteLine("  - FilterBenchmarks          : Where clauses at various selectivities");
    Console.WriteLine("  - AggregationBenchmarks     : Sum, Average, Min, Max operations");
    Console.WriteLine("  - GroupByBenchmarks         : Grouped aggregations (Count, Sum, Avg per group)");
    Console.WriteLine("  - PaginationBenchmarks      : Take, Skip, First, Any operations");
    Console.WriteLine("  - SerializationSizeBenchmarks: Arrow IPC vs Protobuf");
    Console.WriteLine("  - WideRecordQueryBenchmarks : Operations on 200-column records");
    Console.WriteLine("  - FrozenArrowBenchmarks     : Core construction/enumeration");
    Console.WriteLine();
    Console.WriteLine("Internal Benchmarks (for optimization work):");
    Console.WriteLine("  - Internals/SelectionBitmapBenchmarks  : SIMD bitmap operations");
    Console.WriteLine("  - Internals/PredicateEvaluationBenchmarks : Column scan performance");
    Console.WriteLine();
    Console.WriteLine("Usage examples:");
    Console.WriteLine("  dotnet run -c Release                            # Interactive selection");
    Console.WriteLine("  dotnet run -c Release -- --filter *Filter*       # Run filter benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter *Aggregation*  # Run aggregation benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter *GroupBy*      # Run GroupBy benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter *Pagination*   # Run pagination benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter *Serialization*# Run serialization benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter *WideRecord*   # Run wide record benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter *Internals*    # Run internal benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --filter *DuckDB*       # Run DuckDB methods only");
    Console.WriteLine("  dotnet run -c Release -- --list flat             # List all benchmarks");
    Console.WriteLine("  dotnet run -c Release -- --help                  # Show BenchmarkDotNet help");
    Console.WriteLine();
    Console.WriteLine("For memory footprint analysis, run the FrozenArrow.MemoryAnalysis project.");
    Console.WriteLine();
}

    switcher.Run(args);
}



