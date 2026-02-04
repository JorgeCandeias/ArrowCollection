# FrozenArrow Benchmarks

This directory contains performance benchmarks comparing FrozenArrow against alternative technologies.

## Organization Principles

Benchmarks are organized **by operation type**, not by technology. All competing technologies (List, FrozenArrow, DuckDB) appear side-by-side in each benchmark file.

### Key Rules

1. **No `Baseline = true` markers** - Let results rank naturally by speed
2. **All technologies compete in the same file** - Easy comparison
3. **Consistent naming**: `{Technology}_{Operation}` (e.g., `List_Filter_Count`, `FrozenArrow_Filter_Count`)
4. **Consistent scale params** - Use `[Params(10_000, 100_000, 1_000_000)]` for all benchmarks
5. **ShortRunJob for all** - Fast iteration during development

## Benchmark Files

### User-Facing Benchmarks

| File | Purpose | Technologies |
|------|---------|--------------|
| `FilterBenchmarks.cs` | Where clauses at various selectivities | List, FrozenArrow, DuckDB |
| `AggregationBenchmarks.cs` | Sum, Average, Min, Max | List, FrozenArrow, DuckDB |
| `GroupByBenchmarks.cs` | Grouped aggregations | List, FrozenArrow, DuckDB |
| `PaginationBenchmarks.cs` | Take, Skip, First, Any | List, FrozenArrow, DuckDB |
| `SerializationSizeBenchmarks.cs` | Arrow IPC vs Protobuf (wide model) | Arrow, Protobuf |
| `WideRecordQueryBenchmarks.cs` | 200-column record queries | List, FrozenArrow |
| `FrozenArrowBenchmarks.cs` | Core construction/enumeration | List, FrozenArrow |

### Internal Benchmarks

| File | Purpose |
|------|---------|
| `Internals/SelectionBitmapBenchmarks.cs` | SIMD bitmap operations |
| `Internals/PredicateEvaluationBenchmarks.cs` | Column scan performance |

## Running Benchmarks

```bash
# List all benchmarks
dotnet run -c Release -- --list flat

# Run by operation type
dotnet run -c Release -- --filter *Filter*
dotnet run -c Release -- --filter *Aggregation*
dotnet run -c Release -- --filter *GroupBy*
dotnet run -c Release -- --filter *Pagination*
dotnet run -c Release -- --filter *Serialization*

# Run by technology
dotnet run -c Release -- --filter *DuckDB*
dotnet run -c Release -- --filter *List_*
dotnet run -c Release -- --filter *FrozenArrow_*
```


## Latest Results

> **Environment**: Windows 11, .NET 10.0.2, BenchmarkDotNet v0.14.0  
> **Last Updated**: 2025-01-27 (after null bitmap batch + dense block SIMD + short-circuit optimizations)

### Filter Operations

#### Filter + Count (High Selectivity ~5%)

| Method | 10K | 100K | 1M |
|--------|-----|------|-----|
| **DuckDB** | 284 탎 | 562 탎 | 837 탎 |
| List | 28 탎 | 349 탎 | 5.8 ms |
| FrozenArrow | 1.8 ms | 1.6 ms | **5.9 ms** |

*Note: FrozenArrow now competitive with List at 1M scale*

#### Filter + Count (Low Selectivity ~70%)

| Method | 10K | 100K | 1M |
|--------|-----|------|-----|
| **DuckDB** | 333 탎 | 539 탎 | 905 탎 |
| List | 39 탎 | 516 탎 | 6.8 ms |
| FrozenArrow | 1.3 ms | 752 탎 | **3.0 ms** |

*Note: FrozenArrow 2x faster than List at 1M scale for low selectivity!*

#### Filter + ToList (High Selectivity ~5%)

| Method | 10K | 100K | 1M |
|--------|-----|------|-----|
| **List** | 20 탎 | 365 탎 | 5.1 ms |
| FrozenArrow | 323 탎 | 5.3 ms | 54 ms |
| DuckDB | 870 탎 | 7.3 ms | 43 ms |

### Aggregation Operations (Filtered)

| Method | 10K | 100K | 1M |
|--------|-----|------|-----|
| **DuckDB Sum** | 290 탎 | 475 탎 | 842 탎 |
| **DuckDB Avg** | 249 탎 | 458 탎 | 809 탎 |
| **DuckDB Min** | 279 탎 | 477 탎 | 900 탎 |
| **DuckDB Max** | 268 탎 | 470 탎 | 838 탎 |
| List Sum | 50 탎 | 619 탎 | 10.1 ms |
| List Avg | 47 탎 | 484 탎 | 8.5 ms |
| List Min | 42 탎 | 1.7 ms | 11.5 ms |
| List Max | 42 탎 | 1.6 ms | 11.3 ms |
| FrozenArrow Sum | 599 탎 | 3.2 ms | **30.3 ms** |
| FrozenArrow Avg | 203 탎 | 1.7 ms | **16.5 ms** |
| FrozenArrow Min | 333 탎 | 3.3 ms | 29.2 ms |
| FrozenArrow Max | 309 탎 | 3.1 ms | 28.5 ms |

*Note: Sum improved ~15% from 35.6ms ? 30.3ms with SIMD optimizations*

### Fused Aggregation (Internal Benchmarks)

| Method | 10K | 100K | 1M | vs Traditional |
|--------|-----|------|-----|----------------|
| Fused_Average | 85 탎 | 851 탎 | 9.3 ms | Same |
| Fused_Sum_SingleFilter | 131 탎 | 1.5 ms | 14.9 ms | Same |
| Fused_Sum_MultiFilter | 185 탎 | 2.0 ms | 21.5 ms | Same |
| Fused_Min | 117 탎 | 1.4 ms | 21.8 ms | **1.6x faster** |
| **Parallel_Fused_Sum** | 192 탎 | 1.0 ms | **6.3 ms** | **3.5x faster** |

### Parallel vs Sequential Aggregation (Internal Benchmarks)

| Method | 100K | 1M | Speedup |
|--------|------|-----|---------|
| Sequential_Sum | 3.6 ms | 36.7 ms | baseline |
| **Parallel_Sum** | 3.4 ms | **30.5 ms** | 1.2x |
| Sequential_Average | 1.7 ms | 16.7 ms | baseline |
| **Parallel_Average** | 1.2 ms | **8.0 ms** | **2.1x** |
| Sequential_Min | 3.3 ms | 31.0 ms | baseline |
| **Parallel_Min** | 2.6 ms | **20.1 ms** | 1.5x |
| Sequential_FullPipeline | 4.6 ms | 45.7 ms | baseline |
| **Parallel_FullPipeline** | 2.5 ms | **21.2 ms** | **2.2x** |

### GroupBy Operations

| Method | 10K | 100K | 1M |
|--------|-----|------|-----|
| **DuckDB Count** | 1.1 ms | 2.7 ms | 4.5 ms |
| **DuckDB Sum** | 1.2 ms | 3.4 ms | 5.1 ms |
| FrozenArrow Count | 78 탎 | 873 탎 | 9.9 ms |
| FrozenArrow Sum | 428 ?s | 4.9 ms | 50.7 ms |
| List Count | 159 ?s | 2.2 ms | 23.2 ms |
| List Sum | 165 ?s | 2.8 ms | 41.7 ms |


### Pagination Operations

| Method | 10K | 100K | 1M |
|--------|-----|------|-----|
| **List Any** | 2 탎 | 2 탎 | 4 탎 |
| **List First** | 1 탎 | 2 탎 | 4 탎 |
| **List Take** | 4 탎 | 7 탎 | 13 탎 |
| **List Skip+Take** | 18 탎 | 20 탎 | 36 탎 |
| FrozenArrow Any | 976 탎 | 152 탎 | **824 탎** |
| FrozenArrow First | 1.0 ms | 164 탎 | **829 탎** |
| FrozenArrow Take | 1.3 ms | 838 탎 | 30.5 ms |
| FrozenArrow Skip+Take | 1.2 ms | 817 탎 | 32.4 ms |
| DuckDB Any | 394 탎 | 515 탎 | 701 탎 |
| DuckDB First | 248 탎 | 372 탎 | 449 탎 |
| DuckDB Take | 336 탎 | 413 탎 | 608 탎 |
| DuckDB Skip+Take | 335 탎 | 498 탎 | 597 탎 |

*Note: FrozenArrow Any/First now use streaming evaluation - 824탎 at 1M is 8x improvement over previous 6.8ms baseline*

### Serialization (Standard Model - 10 columns)

| Method | 10K | 100K | 1M |
|--------|-----|------|-----|
| **Arrow (No Compression)** | 276 탎 | 2.0 ms | 9.6 ms |
| Arrow + LZ4 | 1.0 ms | 5.9 ms | 40.7 ms |
| Arrow + Zstd | 3.7 ms | 18.9 ms | 135 ms |
| Protobuf | 3.6 ms | 42.4 ms | 245 ms |

### Serialization (Wide Model - 200 columns)

| Method | 10K | 100K | 1M |
|--------|-----|------|-----|
| **Arrow (No Compression)** | 2.0 ms | 38 ms | 225 ms |
| Protobuf | 10.3 ms | 103 ms | 935 ms |
| Arrow + LZ4 | 11.0 ms | 107 ms | 1,232 ms |
| Arrow + Zstd | 19 ms | 228 ms | 2,351 ms |


## Key Insights

### DuckDB Dominates

- **Aggregations**: 10-50x faster than alternatives at 1M scale
- **Filtered counts**: Consistently fastest across all selectivities
- **GroupBy**: 5-10x faster than List or FrozenArrow

### List Wins Short-Circuit

- **Any/First**: Nearly instant (nanoseconds) when data matches early
- **Simple materialization**: Lowest overhead for returning objects

### FrozenArrow Sweet Spots

- **GroupBy + Count**: 2.4x faster than List (columnar counting)
- **Memory-constrained scenarios**: See Memory Analysis for savings
- **.NET-native API**: No SQL strings, pure LINQ

### Arrow Serialization

- **2.5x faster than Protobuf** for uncompressed writes
- **Zstd compression**: 62% smaller than Protobuf at 1M items
- Best for storage/archival where size matters

## When to Use Each

| Scenario | Best Choice | Why |
|----------|-------------|-----|
| Aggregations at scale | **DuckDB** | 10-50x faster |
| Short-circuit ops (Any/First) | **List<T>** | O(1) when data matches |
| Memory-constrained | **FrozenArrow** | 70-77% memory savings |
| .NET-native LINQ API | **FrozenArrow** | No SQL, pure C# |
| Complex JOINs | **DuckDB** | Not supported in FrozenArrow |
| Serialization speed | **Arrow** | 2.5x faster than Protobuf |
| Serialization size | **Arrow + Zstd** | 62% smaller than Protobuf |

## Adding a New Technology

1. Add setup/cleanup to each relevant benchmark file
2. Add benchmark methods with naming: `{NewTech}_{Operation}`
3. Add to same categories as existing methods
4. Update this README with results

See the main [README.md](../../README.md) for complete guidance.
