using BenchmarkDotNet.Running;
using ArrowCollection.Benchmarks;

// Check for command-line arguments
if (args.Length > 0 && args[0] == "--memory-footprint")
{
    // Run the memory footprint analyzer (separate from BenchmarkDotNet)
    MemoryFootprintAnalyzer.Run();
    return;
}

if (args.Length > 0 && args[0] == "--struct-comparison")
{
    // Run struct vs class comparison benchmarks
    BenchmarkRunner.Run<StructVsClassBenchmarks>();
    return;
}

if (args.Length > 0 && args[0] == "--all-benchmarks")
{
    // Run all BenchmarkDotNet benchmarks
    BenchmarkRunner.Run<ArrowCollectionBenchmarks>();
    BenchmarkRunner.Run<StructVsClassBenchmarks>();
    return;
}

// Default: run the main benchmarks
Console.WriteLine("ArrowCollection Benchmark Runner");
Console.WriteLine("================================");
Console.WriteLine();
Console.WriteLine("Available options:");
Console.WriteLine("  --memory-footprint   : Run long-term memory footprint analysis");
Console.WriteLine("  --struct-comparison  : Run struct vs class comparison benchmarks");
Console.WriteLine("  --all-benchmarks     : Run all BenchmarkDotNet benchmarks");
Console.WriteLine("  (no args)            : Run main ArrowCollection benchmarks");
Console.WriteLine();

BenchmarkRunner.Run<ArrowCollectionBenchmarks>();


