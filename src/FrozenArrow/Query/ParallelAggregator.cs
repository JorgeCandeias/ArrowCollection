using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Apache.Arrow;

namespace FrozenArrow.Query;

/// <summary>
/// Executes aggregate operations in parallel across data chunks with final reduction.
/// </summary>
internal static class ParallelAggregator
{
    /// <summary>
    /// Minimum row count to enable parallel aggregation.
    /// Below this threshold, sequential aggregation is faster.
    /// </summary>
    private const int ParallelThreshold = 50_000;

    /// <summary>
    /// Default chunk size for parallel aggregation (64K rows).
    /// Larger than predicate chunks because aggregation has less per-row overhead.
    /// </summary>
    private const int DefaultChunkSize = 65_536;

    #region Parallel Sum

    /// <summary>
    /// Computes Sum in parallel using partial sums with reduction.
    /// </summary>
    public static object ExecuteSumParallel(
        IArrowArray column,
        ref SelectionBitmap selection,
        Type resultType,
        ParallelQueryOptions? options = null)
    {
        var length = column.Length;
        var enableParallel = options?.EnableParallelExecution ?? true;
        var threshold = options?.ParallelThreshold ?? ParallelThreshold;

        // Fall back to sequential for small datasets
        if (!enableParallel || length < threshold)
        {
            return ColumnAggregator.ExecuteSum(column, ref selection, resultType);
        }

        return column switch
        {
            Int32Array int32Array => ConvertResult(SumInt32Parallel(int32Array, ref selection, options), resultType),
            Int64Array int64Array => ConvertResult(SumInt64Parallel(int64Array, ref selection, options), resultType),
            DoubleArray doubleArray => ConvertResult(SumDoubleParallel(doubleArray, ref selection, options), resultType),
            Decimal128Array decimalArray => ConvertResult(SumDecimalParallel(decimalArray, ref selection, options), resultType),
            _ => ColumnAggregator.ExecuteSum(column, ref selection, resultType) // Fallback
        };
    }

    private static unsafe long SumInt32Parallel(Int32Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialSums = new long[chunkCount];

        // Pin the values buffer and selection for parallel access
        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        
        // Get pointer to underlying data
        ref readonly int valuesRef = ref MemoryMarshal.GetReference(array.Values);
        int* valuesPtr = (int*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            long chunkSum = 0;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    chunkSum += valuesPtr[i];
                }
            }
            partialSums[chunkIndex] = chunkSum;
        });

        // Reduce: sum all partial sums
        long total = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            total += partialSums[i];
        }
        return total;
    }

    private static unsafe long SumInt64Parallel(Int64Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialSums = new long[chunkCount];

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly long valuesRef = ref MemoryMarshal.GetReference(array.Values);
        long* valuesPtr = (long*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            long chunkSum = 0;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    chunkSum += valuesPtr[i];
                }
            }
            partialSums[chunkIndex] = chunkSum;
        });

        long total = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            total += partialSums[i];
        }
        return total;
    }

    private static unsafe double SumDoubleParallel(DoubleArray array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialSums = new double[chunkCount];

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly double valuesRef = ref MemoryMarshal.GetReference(array.Values);
        double* valuesPtr = (double*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            double chunkSum = 0;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    chunkSum += valuesPtr[i];
                }
            }
            partialSums[chunkIndex] = chunkSum;
        });

        double total = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            total += partialSums[i];
        }
        return total;
    }

    private static unsafe decimal SumDecimalParallel(Decimal128Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialSums = new decimal[chunkCount];

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        var parallelOptions = CreateParallelOptions(options);

        // Decimal128Array doesn't have a simple Values span, so we must use GetValue
        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            decimal chunkSum = 0;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    chunkSum += array.GetValue(i)!.Value;
                }
            }
            partialSums[chunkIndex] = chunkSum;
        });

        decimal total = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            total += partialSums[i];
        }
        return total;
    }

    #endregion

    #region Parallel Average

    /// <summary>
    /// Computes Average in parallel using partial (sum, count) pairs with reduction.
    /// </summary>
    public static object ExecuteAverageParallel(
        IArrowArray column,
        ref SelectionBitmap selection,
        Type resultType,
        ParallelQueryOptions? options = null)
    {
        var length = column.Length;
        var enableParallel = options?.EnableParallelExecution ?? true;
        var threshold = options?.ParallelThreshold ?? ParallelThreshold;

        if (!enableParallel || length < threshold)
        {
            return ColumnAggregator.ExecuteAverage(column, ref selection, resultType);
        }

        return column switch
        {
            Int32Array int32Array => ConvertResult(AverageInt32Parallel(int32Array, ref selection, options), resultType),
            Int64Array int64Array => ConvertResult(AverageInt64Parallel(int64Array, ref selection, options), resultType),
            DoubleArray doubleArray => ConvertResult(AverageDoubleParallel(doubleArray, ref selection, options), resultType),
            Decimal128Array decimalArray => ConvertResult(AverageDecimalParallel(decimalArray, ref selection, options), resultType),
            _ => ColumnAggregator.ExecuteAverage(column, ref selection, resultType)
        };
    }

    private static unsafe double AverageInt32Parallel(Int32Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialSums = new long[chunkCount];
        var partialCounts = new int[chunkCount];

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly int valuesRef = ref MemoryMarshal.GetReference(array.Values);
        int* valuesPtr = (int*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            long chunkSum = 0;
            int count = 0;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    chunkSum += valuesPtr[i];
                    count++;
                }
            }
            partialSums[chunkIndex] = chunkSum;
            partialCounts[chunkIndex] = count;
        });

        // Reduce
        long totalSum = 0;
        int totalCount = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            totalSum += partialSums[i];
            totalCount += partialCounts[i];
        }
        return totalCount > 0 ? (double)totalSum / totalCount : 0;
    }

    private static unsafe double AverageInt64Parallel(Int64Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialSums = new long[chunkCount];
        var partialCounts = new int[chunkCount];

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly long valuesRef = ref MemoryMarshal.GetReference(array.Values);
        long* valuesPtr = (long*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            long chunkSum = 0;
            int count = 0;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    chunkSum += valuesPtr[i];
                    count++;
                }
            }
            partialSums[chunkIndex] = chunkSum;
            partialCounts[chunkIndex] = count;
        });

        long totalSum = 0;
        int totalCount = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            totalSum += partialSums[i];
            totalCount += partialCounts[i];
        }
        return totalCount > 0 ? (double)totalSum / totalCount : 0;
    }

    private static unsafe double AverageDoubleParallel(DoubleArray array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialSums = new double[chunkCount];
        var partialCounts = new int[chunkCount];

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly double valuesRef = ref MemoryMarshal.GetReference(array.Values);
        double* valuesPtr = (double*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            double chunkSum = 0;
            int count = 0;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    chunkSum += valuesPtr[i];
                    count++;
                }
            }
            partialSums[chunkIndex] = chunkSum;
            partialCounts[chunkIndex] = count;
        });

        double totalSum = 0;
        int totalCount = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            totalSum += partialSums[i];
            totalCount += partialCounts[i];
        }
        return totalCount > 0 ? totalSum / totalCount : 0;
    }

    private static unsafe decimal AverageDecimalParallel(Decimal128Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialSums = new decimal[chunkCount];
        var partialCounts = new int[chunkCount];

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            decimal chunkSum = 0;
            int count = 0;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    chunkSum += array.GetValue(i)!.Value;
                    count++;
                }
            }
            partialSums[chunkIndex] = chunkSum;
            partialCounts[chunkIndex] = count;
        });

        decimal totalSum = 0;
        int totalCount = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            totalSum += partialSums[i];
            totalCount += partialCounts[i];
        }
        return totalCount > 0 ? totalSum / totalCount : 0;
    }

    #endregion

    #region Parallel Min

    /// <summary>
    /// Computes Min in parallel using partial minimums with reduction.
    /// </summary>
    public static object ExecuteMinParallel(
        IArrowArray column,
        ref SelectionBitmap selection,
        Type resultType,
        ParallelQueryOptions? options = null)
    {
        var length = column.Length;
        var enableParallel = options?.EnableParallelExecution ?? true;
        var threshold = options?.ParallelThreshold ?? ParallelThreshold;

        if (!enableParallel || length < threshold)
        {
            return ColumnAggregator.ExecuteMin(column, ref selection, resultType);
        }

        return column switch
        {
            Int32Array int32Array => ConvertResult(MinInt32Parallel(int32Array, ref selection, options), resultType),
            Int64Array int64Array => ConvertResult(MinInt64Parallel(int64Array, ref selection, options), resultType),
            DoubleArray doubleArray => ConvertResult(MinDoubleParallel(doubleArray, ref selection, options), resultType),
            Decimal128Array decimalArray => ConvertResult(MinDecimalParallel(decimalArray, ref selection, options), resultType),
            _ => ColumnAggregator.ExecuteMin(column, ref selection, resultType)
        };
    }

    private static unsafe int MinInt32Parallel(Int32Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialMins = new int[chunkCount];
        var hasValues = new bool[chunkCount];

        // Initialize to max value
        for (int i = 0; i < chunkCount; i++) partialMins[i] = int.MaxValue;

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly int valuesRef = ref MemoryMarshal.GetReference(array.Values);
        int* valuesPtr = (int*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            int chunkMin = int.MaxValue;
            bool foundValue = false;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    var value = valuesPtr[i];
                    if (!foundValue || value < chunkMin)
                    {
                        chunkMin = value;
                        foundValue = true;
                    }
                }
            }
            partialMins[chunkIndex] = chunkMin;
            hasValues[chunkIndex] = foundValue;
        });

        // Reduce: find min of all partial mins
        int globalMin = int.MaxValue;
        bool hasAnyValue = false;
        for (int i = 0; i < chunkCount; i++)
        {
            if (hasValues[i] && partialMins[i] < globalMin)
            {
                globalMin = partialMins[i];
                hasAnyValue = true;
            }
        }

        if (!hasAnyValue)
            throw new InvalidOperationException("Sequence contains no elements.");
        return globalMin;
    }

    private static unsafe long MinInt64Parallel(Int64Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialMins = new long[chunkCount];
        var hasValues = new bool[chunkCount];

        for (int i = 0; i < chunkCount; i++) partialMins[i] = long.MaxValue;

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly long valuesRef = ref MemoryMarshal.GetReference(array.Values);
        long* valuesPtr = (long*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            long chunkMin = long.MaxValue;
            bool foundValue = false;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    var value = valuesPtr[i];
                    if (!foundValue || value < chunkMin)
                    {
                        chunkMin = value;
                        foundValue = true;
                    }
                }
            }
            partialMins[chunkIndex] = chunkMin;
            hasValues[chunkIndex] = foundValue;
        });

        long globalMin = long.MaxValue;
        bool hasAnyValue = false;
        for (int i = 0; i < chunkCount; i++)
        {
            if (hasValues[i] && partialMins[i] < globalMin)
            {
                globalMin = partialMins[i];
                hasAnyValue = true;
            }
        }

        if (!hasAnyValue)
            throw new InvalidOperationException("Sequence contains no elements.");
        return globalMin;
    }

    private static unsafe double MinDoubleParallel(DoubleArray array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialMins = new double[chunkCount];
        var hasValues = new bool[chunkCount];

        for (int i = 0; i < chunkCount; i++) partialMins[i] = double.MaxValue;

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly double valuesRef = ref MemoryMarshal.GetReference(array.Values);
        double* valuesPtr = (double*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            double chunkMin = double.MaxValue;
            bool foundValue = false;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    var value = valuesPtr[i];
                    if (!foundValue || value < chunkMin)
                    {
                        chunkMin = value;
                        foundValue = true;
                    }
                }
            }
            partialMins[chunkIndex] = chunkMin;
            hasValues[chunkIndex] = foundValue;
        });

        double globalMin = double.MaxValue;
        bool hasAnyValue = false;
        for (int i = 0; i < chunkCount; i++)
        {
            if (hasValues[i] && partialMins[i] < globalMin)
            {
                globalMin = partialMins[i];
                hasAnyValue = true;
            }
        }

        if (!hasAnyValue)
            throw new InvalidOperationException("Sequence contains no elements.");
        return globalMin;
    }

    private static unsafe decimal MinDecimalParallel(Decimal128Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialMins = new decimal[chunkCount];
        var hasValues = new bool[chunkCount];

        for (int i = 0; i < chunkCount; i++) partialMins[i] = decimal.MaxValue;

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            decimal chunkMin = decimal.MaxValue;
            bool foundValue = false;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    var value = array.GetValue(i)!.Value;
                    if (!foundValue || value < chunkMin)
                    {
                        chunkMin = value;
                        foundValue = true;
                    }
                }
            }
            partialMins[chunkIndex] = chunkMin;
            hasValues[chunkIndex] = foundValue;
        });

        decimal globalMin = decimal.MaxValue;
        bool hasAnyValue = false;
        for (int i = 0; i < chunkCount; i++)
        {
            if (hasValues[i] && partialMins[i] < globalMin)
            {
                globalMin = partialMins[i];
                hasAnyValue = true;
            }
        }

        if (!hasAnyValue)
            throw new InvalidOperationException("Sequence contains no elements.");
        return globalMin;
    }

    #endregion

    #region Parallel Max

    /// <summary>
    /// Computes Max in parallel using partial maximums with reduction.
    /// </summary>
    public static object ExecuteMaxParallel(
        IArrowArray column,
        ref SelectionBitmap selection,
        Type resultType,
        ParallelQueryOptions? options = null)
    {
        var length = column.Length;
        var enableParallel = options?.EnableParallelExecution ?? true;
        var threshold = options?.ParallelThreshold ?? ParallelThreshold;

        if (!enableParallel || length < threshold)
        {
            return ColumnAggregator.ExecuteMax(column, ref selection, resultType);
        }

        return column switch
        {
            Int32Array int32Array => ConvertResult(MaxInt32Parallel(int32Array, ref selection, options), resultType),
            Int64Array int64Array => ConvertResult(MaxInt64Parallel(int64Array, ref selection, options), resultType),
            DoubleArray doubleArray => ConvertResult(MaxDoubleParallel(doubleArray, ref selection, options), resultType),
            Decimal128Array decimalArray => ConvertResult(MaxDecimalParallel(decimalArray, ref selection, options), resultType),
            _ => ColumnAggregator.ExecuteMax(column, ref selection, resultType)
        };
    }

    private static unsafe int MaxInt32Parallel(Int32Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialMaxs = new int[chunkCount];
        var hasValues = new bool[chunkCount];

        for (int i = 0; i < chunkCount; i++) partialMaxs[i] = int.MinValue;

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly int valuesRef = ref MemoryMarshal.GetReference(array.Values);
        int* valuesPtr = (int*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            int chunkMax = int.MinValue;
            bool foundValue = false;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    var value = valuesPtr[i];
                    if (!foundValue || value > chunkMax)
                    {
                        chunkMax = value;
                        foundValue = true;
                    }
                }
            }
            partialMaxs[chunkIndex] = chunkMax;
            hasValues[chunkIndex] = foundValue;
        });

        int globalMax = int.MinValue;
        bool hasAnyValue = false;
        for (int i = 0; i < chunkCount; i++)
        {
            if (hasValues[i] && partialMaxs[i] > globalMax)
            {
                globalMax = partialMaxs[i];
                hasAnyValue = true;
            }
        }

        if (!hasAnyValue)
            throw new InvalidOperationException("Sequence contains no elements.");
        return globalMax;
    }

    private static unsafe long MaxInt64Parallel(Int64Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialMaxs = new long[chunkCount];
        var hasValues = new bool[chunkCount];

        for (int i = 0; i < chunkCount; i++) partialMaxs[i] = long.MinValue;

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly long valuesRef = ref MemoryMarshal.GetReference(array.Values);
        long* valuesPtr = (long*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            long chunkMax = long.MinValue;
            bool foundValue = false;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    var value = valuesPtr[i];
                    if (!foundValue || value > chunkMax)
                    {
                        chunkMax = value;
                        foundValue = true;
                    }
                }
            }
            partialMaxs[chunkIndex] = chunkMax;
            hasValues[chunkIndex] = foundValue;
        });

        long globalMax = long.MinValue;
        bool hasAnyValue = false;
        for (int i = 0; i < chunkCount; i++)
        {
            if (hasValues[i] && partialMaxs[i] > globalMax)
            {
                globalMax = partialMaxs[i];
                hasAnyValue = true;
            }
        }

        if (!hasAnyValue)
            throw new InvalidOperationException("Sequence contains no elements.");
        return globalMax;
    }

    private static unsafe double MaxDoubleParallel(DoubleArray array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialMaxs = new double[chunkCount];
        var hasValues = new bool[chunkCount];

        for (int i = 0; i < chunkCount; i++) partialMaxs[i] = double.MinValue;

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        ref readonly double valuesRef = ref MemoryMarshal.GetReference(array.Values);
        double* valuesPtr = (double*)Unsafe.AsPointer(ref Unsafe.AsRef(in valuesRef));

        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            double chunkMax = double.MinValue;
            bool foundValue = false;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    var value = valuesPtr[i];
                    if (!foundValue || value > chunkMax)
                    {
                        chunkMax = value;
                        foundValue = true;
                    }
                }
            }
            partialMaxs[chunkIndex] = chunkMax;
            hasValues[chunkIndex] = foundValue;
        });

        double globalMax = double.MinValue;
        bool hasAnyValue = false;
        for (int i = 0; i < chunkCount; i++)
        {
            if (hasValues[i] && partialMaxs[i] > globalMax)
            {
                globalMax = partialMaxs[i];
                hasAnyValue = true;
            }
        }

        if (!hasAnyValue)
            throw new InvalidOperationException("Sequence contains no elements.");
        return globalMax;
    }

    private static unsafe decimal MaxDecimalParallel(Decimal128Array array, ref SelectionBitmap selection, ParallelQueryOptions? options)
    {
        var length = array.Length;
        var chunkSize = options?.ChunkSize ?? DefaultChunkSize;
        var chunkCount = (length + chunkSize - 1) / chunkSize;
        var partialMaxs = new decimal[chunkCount];
        var hasValues = new bool[chunkCount];

        for (int i = 0; i < chunkCount; i++) partialMaxs[i] = decimal.MinValue;

        SelectionBitmap* selectionPtr = (SelectionBitmap*)Unsafe.AsPointer(ref selection);
        var parallelOptions = CreateParallelOptions(options);

        Parallel.For(0, chunkCount, parallelOptions, chunkIndex =>
        {
            var startRow = chunkIndex * chunkSize;
            var endRow = Math.Min(startRow + chunkSize, length);
            ref var sel = ref Unsafe.AsRef<SelectionBitmap>(selectionPtr);

            decimal chunkMax = decimal.MinValue;
            bool foundValue = false;
            for (int i = startRow; i < endRow; i++)
            {
                if (sel[i] && !array.IsNull(i))
                {
                    var value = array.GetValue(i)!.Value;
                    if (!foundValue || value > chunkMax)
                    {
                        chunkMax = value;
                        foundValue = true;
                    }
                }
            }
            partialMaxs[chunkIndex] = chunkMax;
            hasValues[chunkIndex] = foundValue;
        });

        decimal globalMax = decimal.MinValue;
        bool hasAnyValue = false;
        for (int i = 0; i < chunkCount; i++)
        {
            if (hasValues[i] && partialMaxs[i] > globalMax)
            {
                globalMax = partialMaxs[i];
                hasAnyValue = true;
            }
        }

        if (!hasAnyValue)
            throw new InvalidOperationException("Sequence contains no elements.");
        return globalMax;
    }

    #endregion

    #region Helpers

    private static ParallelOptions CreateParallelOptions(ParallelQueryOptions? options)
    {
        var parallelOptions = new ParallelOptions();
        if (options?.MaxDegreeOfParallelism > 0)
        {
            parallelOptions.MaxDegreeOfParallelism = options.MaxDegreeOfParallelism;
        }
        return parallelOptions;
    }

    private static object ConvertResult(object value, Type targetType)
    {
        if (targetType == typeof(int)) return Convert.ToInt32(value);
        if (targetType == typeof(long)) return Convert.ToInt64(value);
        if (targetType == typeof(double)) return Convert.ToDouble(value);
        if (targetType == typeof(float)) return Convert.ToSingle(value);
        if (targetType == typeof(decimal)) return Convert.ToDecimal(value);
        return value;
    }

    #endregion
}
