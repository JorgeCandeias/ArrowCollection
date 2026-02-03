# FrozenArrow Benchmarks

This directory contains performance benchmarks comparing FrozenArrow against alternative technologies for various operations.

## Organization Principles

Benchmarks are organized **by operation type**, not by technology. All competing technologies (List, FrozenArrow, DuckDB, etc.) appear side-by-side in each benchmark file.

### Key Rules

1. **No `Baseline = true` markers** - Let results rank naturally by speed
2. **All technologies compete in the same file** - Easy comparison
3. **Consistent naming**: `{Technology}_{Operation}` (e.g., `List_Filter_Count`, `FrozenArrow_Filter_Count`, `DuckDB_Filter_Count`)
4. **Consistent scale params** - Use `[Params(100_000, 1_000_000)]` for standard benchmarks
5. **Categories for grouping** - Use `[BenchmarkCategory]` to group related operations

## Benchmark Files

### User-Facing Benchmarks

These benchmarks measure operations that users directly interact with:

| File | Purpose | Technologies |
|------|---------|--------------|
| `FilterBenchmarks.cs` | Where clauses at various selectivities | List, FrozenArrow, DuckDB |
| `AggregationBenchmarks.cs` | Sum, Average, Min, Max (single aggregates) | List, FrozenArrow, DuckDB |
| `GroupByBenchmarks.cs` | Grouped aggregations (Count, Sum, Avg per group) | List, FrozenArrow, DuckDB |
| `PaginationBenchmarks.cs` | Take, Skip, First, Any operations | List, FrozenArrow, DuckDB |
| `SerializationSizeBenchmarks.cs` | Arrow IPC (±compression) vs Protobuf | Arrow, Protobuf |
| `WideSerializationSizeBenchmarks.cs` | Serialization for 200-column records | Arrow, Protobuf |
| `WideRecordQueryBenchmarks.cs` | Query operations on 200-column records | List, FrozenArrow |
| `FrozenArrowBenchmarks.cs` | Core construction/enumeration | List, FrozenArrow |
| `HeavyRecordBenchmarks.cs` | Construction/enumeration for 200-column records | List, FrozenArrow |

### Internal Component Benchmarks

These benchmarks measure internal components for optimization work:

| File | Purpose |
|------|---------|
| `Internals/SelectionBitmapBenchmarks.cs` | SIMD bitmap operations (AND, OR, NOT, CountSet) |
| `Internals/PredicateEvaluationBenchmarks.cs` | Column scan and predicate evaluation |

### Model Classes

| File | Purpose |
|------|---------|
| `Models/QueryBenchmarkItem.cs` | Standard 10-column model + factory |
| `HeavyBenchmarkItem.cs` | Wide 200-column model + factory |

## Data Models

### Standard Model (10 columns)
`QueryBenchmarkItem` - Used for most benchmarks:
- `Id` (int), `Name` (string), `Age` (int), `Salary` (decimal)
- `IsActive` (bool), `Category` (string), `Department` (string)
- `HireDate` (DateTime), `PerformanceScore` (double), `Region` (string)

### Wide Model (200 columns)
`HeavyBenchmarkItem` - Used for wide record benchmarks:
- 10 string properties (low cardinality)
- 5 DateTime properties (high cardinality)
- 62 int, 62 double, 61 decimal properties (sparse)

### Serialization Model (10 columns)
`SerializationBenchmarkItem` - Used for serialization benchmarks:
- Annotated with both `[ArrowRecord]` and `[ProtoContract]`

## Adding a New Technology

When adding a new technology to compare (e.g., SQLite, Parquet):

1. **Add setup/cleanup** to each relevant benchmark file
2. **Add benchmark methods** with naming: `{NewTech}_{Operation}`
3. **Add to the same categories** as existing methods
4. **Update this README** with the new technology in the tables

Example:
```csharp
// In FilterBenchmarks.cs
[GlobalSetup]
public void Setup()
{
    // ... existing setup ...
    _sqliteConnection = SetupSqlite(_list);
}

[Benchmark]
[BenchmarkCategory("Filter_Count_HighSelectivity")]
public int SQLite_Filter_Count_HighSelectivity()
{
    // Implementation
}
```

## Adding a New Operation

When adding a new operation type:

1. **Create a new file** if it doesn't fit existing categories
2. **Include all technologies** that support the operation
3. **Use consistent params** (`[Params(100_000, 1_000_000)]` for standard)
4. **Add categories** for sub-operations
5. **Update this README**

## Running Benchmarks

```bash
# List all available benchmarks
dotnet run -c Release -- --list flat

# Run all benchmarks (takes a long time!)
dotnet run -c Release

# Run by operation type
dotnet run -c Release -- --filter *Filter*
dotnet run -c Release -- --filter *Aggregation*
dotnet run -c Release -- --filter *GroupBy*
dotnet run -c Release -- --filter *Pagination*
dotnet run -c Release -- --filter *Serialization*
dotnet run -c Release -- --filter *WideRecord*

# Run by technology
dotnet run -c Release -- --filter *DuckDB*
dotnet run -c Release -- --filter *List_*
dotnet run -c Release -- --filter *FrozenArrow_*

# Run internal benchmarks
dotnet run -c Release -- --filter *Internals*

# Run with specific scale
dotnet run -c Release -- --filter *Filter* --anyCategories "1M"

# Short run for quick validation
dotnet run -c Release -- --filter *Filter* --job short
```

## Memory Analysis

For detailed memory footprint analysis, use the separate project:

```bash
dotnet run -c Release --project ../FrozenArrow.MemoryAnalysis
```

## Latest Results

> **Note**: Results are from Windows 11, .NET 10.0.2, BenchmarkDotNet v0.14.0

### Filter + Count (100K items)

| Method | High Selectivity (~5%) | Low Selectivity (~70%) |
|--------|------------------------|------------------------|
| List | 274 ?s | 388 ?s |
| FrozenArrow | 928 ?s | 640 ?s |
| DuckDB | 311 ?s | 296 ?s |

### Filter + Count (1M items)

| Method | High Selectivity (~5%) | Low Selectivity (~70%) |
|--------|------------------------|------------------------|
| List | 4.7 ms | 6.0 ms |
| FrozenArrow | 9.3 ms | 7.7 ms |
| DuckDB | 522 ?s | 455 ?s |

### Aggregations (1M items, filtered)

| Method | Sum | Average | Min | Max |
|--------|-----|---------|-----|-----|
| List | 10.3 ms | 8.2 ms | 11.1 ms | 11.2 ms |
| FrozenArrow | 35.7 ms | 16.1 ms | 29.4 ms | 29.6 ms |
| DuckDB | 504 ?s | 598 ?s | 515 ?s | 514 ?s |

### GroupBy + Aggregates (1M items)

| Method | Count | Sum | Average |
|--------|-------|-----|---------|
| List | 24.8 ms | 42.5 ms | 31.3 ms |
| FrozenArrow | 9.8 ms | 53.1 ms | 35.0 ms |
| DuckDB | 4.6 ms | 5.3 ms | 3.2 ms |

### Serialization (100K items, standard model)

| Method | Time | Size |
|--------|------|------|
| Arrow (No Compression) | 7.0 ms | 12.7 MB |
| Arrow + LZ4 | 10.2 ms | 6.9 MB |
| Arrow + Zstd | 19.3 ms | 3.8 MB |
| Protobuf | 46.2 ms | 66.9 MB |

### Key Insights

- **DuckDB dominates aggregations at scale** (8-22x faster than alternatives)
- **FrozenArrow wins GroupBy+Count** (2.5x faster than List due to columnar counting)
- **List wins for short-circuit operations** (Any/First nearly instant)
- **Arrow serialization is 5-7x faster than Protobuf** and produces smaller output
- **FrozenArrow shines on wide records** where reconstruction cost is avoided

See the main [README.md](../../README.md) for detailed analysis and guidance on when to use each technology.
