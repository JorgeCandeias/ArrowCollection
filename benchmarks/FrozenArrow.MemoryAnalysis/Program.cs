using FrozenArrow.MemoryAnalysis;

// Run standard memory analysis
MemoryFootprintAnalyzer.Run();

// Run extreme scenario: 200-property record with 1M items
HeavyRecordMemoryAnalyzer.Run();

// Run DuckDB comparison analysis
DuckDbMemoryAnalyzer.Run();
