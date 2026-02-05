using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Apache.Arrow;

namespace FrozenArrow.Query;

/// <summary>
/// SIMD-accelerated aggregation that processes bitmap blocks directly instead of
/// iterating bit-by-bit. Uses TrailingZeroCount for sparse iteration and
/// vectorized operations for dense selections.
/// </summary>
internal static class SimdBlockAggregator
{
    /// <summary>
    /// If more than this percentage of rows are selected, use dense SIMD path.
    /// Otherwise, use sparse block-based iteration.
    /// </summary>
    private const double DenseThreshold = 0.5;

    #region Sum Operations

    /// <summary>
    /// Computes Sum using block-based bitmap iteration.
    /// </summary>
    public static long SumInt32(Int32Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) return 0;

        var selectedCount = selection.CountSet();
        if (selectedCount == 0) return 0;

        var density = (double)selectedCount / length;
        var values = array.Values;
        var blocks = selection.Blocks;
        var nullBitmap = array.NullBitmapBuffer.Span;

        // For dense selections, use masked SIMD aggregation
        if (density >= DenseThreshold && array.NullCount == 0)
        {
            return SumInt32Dense(values, blocks);
        }

        // For sparse selections or when there are nulls, use block-based iteration
        return SumInt32Sparse(values, blocks, length, array);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long SumInt32Dense(ReadOnlySpan<int> values, Span<ulong> blocks)
    {
        long sum = 0;
        int blockIndex = 0;
        int valueIndex = 0;
        
        ref int valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == ulong.MaxValue)
            {
                // All 64 bits set - sum all values in this range
                int endIndex = Math.Min(valueIndex + 64, values.Length);
                
                // Use SIMD for 8 ints at a time
                if (Vector256.IsHardwareAccelerated)
                {
                    while (valueIndex + 8 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, valueIndex));
                        // Widen to long and sum
                        var (lower, upper) = Vector256.Widen(vec);
                        sum += Vector256.Sum(lower) + Vector256.Sum(upper);
                        valueIndex += 8;
                    }
                }
                
                // Scalar remainder
                while (valueIndex < endIndex)
                {
                    sum += Unsafe.Add(ref valuesRef, valueIndex++);
                }
            }
            else if (block == 0)
            {
                // No bits set - skip this block
                valueIndex += 64;
            }
            else
            {
                // Partial block - iterate set bits using TrailingZeroCount
                var remaining = block;
                int baseIndex = blockIndex << 6;
                
                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    if (idx < values.Length)
                    {
                        sum += Unsafe.Add(ref valuesRef, idx);
                    }
                    remaining &= remaining - 1; // Clear lowest set bit
                }
                valueIndex = (blockIndex + 1) << 6;
            }
            blockIndex++;
        }

        return sum;
    }

    private static long SumInt32Sparse(ReadOnlySpan<int> values, Span<ulong> blocks, int length, Int32Array array)
    {
        long sum = 0;
        int blockIndex = 0;
        ref int valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            var remaining = block;
            int baseIndex = blockIndex << 6;

            while (remaining != 0)
            {
                var bitIndex = BitOperations.TrailingZeroCount(remaining);
                var idx = baseIndex + bitIndex;
                
                if (idx < length && !array.IsNull(idx))
                {
                    sum += Unsafe.Add(ref valuesRef, idx);
                }
                
                remaining &= remaining - 1;
            }
            blockIndex++;
        }

        return sum;
    }

    /// <summary>
    /// Computes Sum for Int64 using block-based bitmap iteration.
    /// </summary>
    public static long SumInt64(Int64Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) return 0;

        var selectedCount = selection.CountSet();
        if (selectedCount == 0) return 0;

        var values = array.Values;
        var blocks = selection.Blocks;
        long sum = 0;
        int blockIndex = 0;
        ref long valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                // All 64 bits set and no nulls - sum all values
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);

                if (Vector256.IsHardwareAccelerated)
                {
                    while (startIndex + 4 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        sum += Vector256.Sum(vec);
                        startIndex += 4;
                    }
                }

                while (startIndex < endIndex)
                {
                    sum += Unsafe.Add(ref valuesRef, startIndex++);
                }
            }
            else
            {
                // Sparse iteration
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        sum += Unsafe.Add(ref valuesRef, idx);
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        return sum;
    }

    /// <summary>
    /// Computes Sum for Double using block-based bitmap iteration.
    /// </summary>
    public static double SumDouble(DoubleArray array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) return 0;

        var selectedCount = selection.CountSet();
        if (selectedCount == 0) return 0;

        var values = array.Values;
        var blocks = selection.Blocks;
        double sum = 0;
        int blockIndex = 0;
        ref double valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                // All 64 bits set and no nulls
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);

                if (Vector256.IsHardwareAccelerated)
                {
                    while (startIndex + 4 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        sum += Vector256.Sum(vec);
                        startIndex += 4;
                    }
                }

                while (startIndex < endIndex)
                {
                    sum += Unsafe.Add(ref valuesRef, startIndex++);
                }
            }
            else
            {
                // Sparse iteration
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        sum += Unsafe.Add(ref valuesRef, idx);
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        return sum;
    }

    /// <summary>
    /// Computes Sum for Decimal.
    /// </summary>
    public static decimal SumDecimal(Decimal128Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) return 0;

        var blocks = selection.Blocks;
        decimal sum = 0;
        int blockIndex = 0;

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            var remaining = block;
            int baseIndex = blockIndex << 6;

            while (remaining != 0)
            {
                var bitIndex = BitOperations.TrailingZeroCount(remaining);
                var idx = baseIndex + bitIndex;
                
                if (idx < length && !array.IsNull(idx))
                {
                    sum += array.GetValue(idx)!.Value;
                }
                
                remaining &= remaining - 1;
            }
            blockIndex++;
        }

        return sum;
    }

    #endregion

    #region Average Operations

    /// <summary>
    /// Computes Average for Int32 using block-based iteration.
    /// </summary>
    public static double AverageInt32(Int32Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) return 0;

        var values = array.Values;
        var blocks = selection.Blocks;
        long sum = 0;
        int count = 0;
        int blockIndex = 0;
        ref int valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);
                int blockCount = endIndex - startIndex;

                if (Vector256.IsHardwareAccelerated)
                {
                    while (startIndex + 8 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        var (lower, upper) = Vector256.Widen(vec);
                        sum += Vector256.Sum(lower) + Vector256.Sum(upper);
                        startIndex += 8;
                    }
                }

                while (startIndex < endIndex)
                {
                    sum += Unsafe.Add(ref valuesRef, startIndex++);
                }
                count += blockCount;
            }
            else
            {
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        sum += Unsafe.Add(ref valuesRef, idx);
                        count++;
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        return count > 0 ? (double)sum / count : throw new InvalidOperationException("Sequence contains no elements");
    }

    /// <summary>
    /// Computes Average for Int64 using block-based iteration.
    /// </summary>
    public static double AverageInt64(Int64Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) return 0;

        var values = array.Values;
        var blocks = selection.Blocks;
        long sum = 0;
        int count = 0;
        int blockIndex = 0;
        ref long valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);
                int blockCount = endIndex - startIndex;

                if (Vector256.IsHardwareAccelerated)
                {
                    while (startIndex + 4 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        sum += Vector256.Sum(vec);
                        startIndex += 4;
                    }
                }

                while (startIndex < endIndex)
                {
                    sum += Unsafe.Add(ref valuesRef, startIndex++);
                }
                count += blockCount;
            }
            else
            {
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        sum += Unsafe.Add(ref valuesRef, idx);
                        count++;
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        return count > 0 ? (double)sum / count : throw new InvalidOperationException("Sequence contains no elements");
    }

    /// <summary>
    /// Computes Average for Double using block-based iteration.
    /// </summary>
    public static double AverageDouble(DoubleArray array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) return 0;

        var values = array.Values;
        var blocks = selection.Blocks;
        double sum = 0;
        int count = 0;
        int blockIndex = 0;
        ref double valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);
                int blockCount = endIndex - startIndex;

                if (Vector256.IsHardwareAccelerated)
                {
                    while (startIndex + 4 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        sum += Vector256.Sum(vec);
                        startIndex += 4;
                    }
                }

                while (startIndex < endIndex)
                {
                    sum += Unsafe.Add(ref valuesRef, startIndex++);
                }
                count += blockCount;
            }
            else
            {
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        sum += Unsafe.Add(ref valuesRef, idx);
                        count++;
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        return count > 0 ? sum / count : throw new InvalidOperationException("Sequence contains no elements");
    }

    /// <summary>
    /// Computes Average for Decimal using block-based iteration.
    /// </summary>
    public static decimal AverageDecimal(Decimal128Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) return 0;

        var blocks = selection.Blocks;
        decimal sum = 0;
        int count = 0;
        int blockIndex = 0;

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            var remaining = block;
            int baseIndex = blockIndex << 6;

            while (remaining != 0)
            {
                var bitIndex = BitOperations.TrailingZeroCount(remaining);
                var idx = baseIndex + bitIndex;
                
                
                if (idx < length && !array.IsNull(idx))
                {
                    sum += array.GetValue(idx)!.Value;
                    count++;
                }
                
                remaining &= remaining - 1;
            }
            blockIndex++;
        }

        return count > 0 ? sum / count : throw new InvalidOperationException("Sequence contains no elements");
    }

    #endregion

    #region Min Operations

    /// <summary>
    /// Computes Min for Int32 using block-based iteration.
    /// </summary>
    public static int MinInt32(Int32Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) throw new InvalidOperationException("Sequence contains no elements.");

        var values = array.Values;
        var blocks = selection.Blocks;
        int min = int.MaxValue;
        bool hasValue = false;
        int blockIndex = 0;
        ref int valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);

                if (Vector256.IsHardwareAccelerated && endIndex - startIndex >= 8)
                {
                    var minVec = Vector256.Create(int.MaxValue);
                    while (startIndex + 8 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        minVec = Vector256.Min(minVec, vec);
                        startIndex += 8;
                    }
                    // Horizontal min
                    var lower = minVec.GetLower();
                    var upper = minVec.GetUpper();
                    var combined = Vector128.Min(lower, upper);
                    min = Math.Min(min, Math.Min(
                        Math.Min(combined.GetElement(0), combined.GetElement(1)),
                        Math.Min(combined.GetElement(2), combined.GetElement(3))));
                    hasValue = true;
                }

                while (startIndex < endIndex)
                {
                    var value = Unsafe.Add(ref valuesRef, startIndex++);
                    if (value < min)
                    {
                        min = value;
                        hasValue = true;
                    }
                }
            }
            else
            {
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        var value = Unsafe.Add(ref valuesRef, idx);
                        if (value < min)
                        {
                            min = value;
                            hasValue = true;
                        }
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        if (!hasValue) throw new InvalidOperationException("Sequence contains no elements.");
        return min;
    }

    /// <summary>
    /// Computes Min for Int64 using block-based iteration.
    /// </summary>
    public static long MinInt64(Int64Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) throw new InvalidOperationException("Sequence contains no elements.");

        var values = array.Values;
        var blocks = selection.Blocks;
        long min = long.MaxValue;
        bool hasValue = false;
        int blockIndex = 0;
        ref long valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);

                if (Vector256.IsHardwareAccelerated && endIndex - startIndex >= 4)
                {
                    var minVec = Vector256.Create(long.MaxValue);
                    while (startIndex + 4 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        minVec = Vector256.Min(minVec, vec);
                        startIndex += 4;
                    }
                    var lower = minVec.GetLower();
                    var upper = minVec.GetUpper();
                    var combined = Vector128.Min(lower, upper);
                    min = Math.Min(min, Math.Min(combined.GetElement(0), combined.GetElement(1)));
                    hasValue = true;
                }

                while (startIndex < endIndex)
                {
                    var value = Unsafe.Add(ref valuesRef, startIndex++);
                    if (value < min)
                    {
                        min = value;
                        hasValue = true;
                    }
                }
            }
            else
            {
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        var value = Unsafe.Add(ref valuesRef, idx);
                        if (value < min)
                        {
                            min = value;
                            hasValue = true;
                        }
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        if (!hasValue) throw new InvalidOperationException("Sequence contains no elements.");
        return min;
    }

    /// <summary>
    /// Computes Min for Double using block-based iteration.
    /// </summary>
    public static double MinDouble(DoubleArray array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) throw new InvalidOperationException("Sequence contains no elements.");

        var values = array.Values;
        var blocks = selection.Blocks;
        double min = double.MaxValue;
        bool hasValue = false;
        int blockIndex = 0;
        ref double valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);

                if (Vector256.IsHardwareAccelerated && endIndex - startIndex >= 4)
                {
                    var minVec = Vector256.Create(double.MaxValue);
                    while (startIndex + 4 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        minVec = Vector256.Min(minVec, vec);
                        startIndex += 4;
                    }
                    var lower = minVec.GetLower();
                    var upper = minVec.GetUpper();
                    var combined = Vector128.Min(lower, upper);
                    min = Math.Min(min, Math.Min(combined.GetElement(0), combined.GetElement(1)));
                    hasValue = true;
                }

                while (startIndex < endIndex)
                {
                    var value = Unsafe.Add(ref valuesRef, startIndex++);
                    if (value < min)
                    {
                        min = value;
                        hasValue = true;
                    }
                }
            }
            else
            {
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        var value = Unsafe.Add(ref valuesRef, idx);
                        if (value < min)
                        {
                            min = value;
                            hasValue = true;
                        }
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        if (!hasValue) throw new InvalidOperationException("Sequence contains no elements.");
        return min;
    }

    /// <summary>
    /// Computes Min for Decimal using block-based iteration.
    /// </summary>
    public static decimal MinDecimal(Decimal128Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) throw new InvalidOperationException("Sequence contains no elements.");

        var blocks = selection.Blocks;
        decimal min = decimal.MaxValue;
        bool hasValue = false;
        int blockIndex = 0;

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            var remaining = block;
            int baseIndex = blockIndex << 6;

            while (remaining != 0)
            {
                var bitIndex = BitOperations.TrailingZeroCount(remaining);
                var idx = baseIndex + bitIndex;
                
                if (idx < length && !array.IsNull(idx))
                {
                    var value = array.GetValue(idx)!.Value;
                    if (value < min)
                    {
                        min = value;
                        hasValue = true;
                    }
                }
                
                remaining &= remaining - 1;
            }
            blockIndex++;
        }

        if (!hasValue) throw new InvalidOperationException("Sequence contains no elements.");
        return min;
    }

    #endregion

    #region Max Operations

    /// <summary>
    /// Computes Max for Int32 using block-based iteration.
    /// </summary>
    public static int MaxInt32(Int32Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) throw new InvalidOperationException("Sequence contains no elements.");

        var values = array.Values;
        var blocks = selection.Blocks;
        int max = int.MinValue;
        bool hasValue = false;
        int blockIndex = 0;
        ref int valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);

                if (Vector256.IsHardwareAccelerated && endIndex - startIndex >= 8)
                {
                    var maxVec = Vector256.Create(int.MinValue);
                    while (startIndex + 8 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        maxVec = Vector256.Max(maxVec, vec);
                        startIndex += 8;
                    }
                    var lower = maxVec.GetLower();
                    var upper = maxVec.GetUpper();
                    var combined = Vector128.Max(lower, upper);
                    max = Math.Max(max, Math.Max(
                        Math.Max(combined.GetElement(0), combined.GetElement(1)),
                        Math.Max(combined.GetElement(2), combined.GetElement(3))));
                    hasValue = true;
                }

                while (startIndex < endIndex)
                {
                    var value = Unsafe.Add(ref valuesRef, startIndex++);
                    if (value > max)
                    {
                        max = value;
                        hasValue = true;
                    }
                }
            }
            else
            {
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        var value = Unsafe.Add(ref valuesRef, idx);
                        if (value > max)
                        {
                            max = value;
                            hasValue = true;
                        }
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        if (!hasValue) throw new InvalidOperationException("Sequence contains no elements.");
        return max;
    }

    /// <summary>
    /// Computes Max for Int64 using block-based iteration.
    /// </summary>
    public static long MaxInt64(Int64Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) throw new InvalidOperationException("Sequence contains no elements.");

        var values = array.Values;
        var blocks = selection.Blocks;
        long max = long.MinValue;
        bool hasValue = false;
        int blockIndex = 0;
        ref long valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);

                if (Vector256.IsHardwareAccelerated && endIndex - startIndex >= 4)
                {
                    var maxVec = Vector256.Create(long.MinValue);
                    while (startIndex + 4 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        maxVec = Vector256.Max(maxVec, vec);
                        startIndex += 4;
                    }
                    var lower = maxVec.GetLower();
                    var upper = maxVec.GetUpper();
                    var combined = Vector128.Max(lower, upper);
                    max = Math.Max(max, Math.Max(combined.GetElement(0), combined.GetElement(1)));
                    hasValue = true;
                }

                while (startIndex < endIndex)
                {
                    var value = Unsafe.Add(ref valuesRef, startIndex++);
                    if (value > max)
                    {
                        max = value;
                        hasValue = true;
                    }
                }
            }
            else
            {
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        var value = Unsafe.Add(ref valuesRef, idx);
                        if (value > max)
                        {
                            max = value;
                            hasValue = true;
                        }
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        if (!hasValue) throw new InvalidOperationException("Sequence contains no elements.");
        return max;
    }

    /// <summary>
    /// Computes Max for Double using block-based iteration.
    /// </summary>
    public static double MaxDouble(DoubleArray array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) throw new InvalidOperationException("Sequence contains no elements.");

        var values = array.Values;
        var blocks = selection.Blocks;
        double max = double.MinValue;
        bool hasValue = false;
        int blockIndex = 0;
        ref double valuesRef = ref MemoryMarshal.GetReference(values);

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            if (block == ulong.MaxValue && array.NullCount == 0)
            {
                int startIndex = blockIndex << 6;
                int endIndex = Math.Min(startIndex + 64, length);

                if (Vector256.IsHardwareAccelerated && endIndex - startIndex >= 4)
                {
                    var maxVec = Vector256.Create(double.MinValue);
                    while (startIndex + 4 <= endIndex)
                    {
                        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex));
                        maxVec = Vector256.Max(maxVec, vec);
                        startIndex += 4;
                    }
                    var lower = maxVec.GetLower();
                    var upper = maxVec.GetUpper();
                    var combined = Vector128.Max(lower, upper);
                    max = Math.Max(max, Math.Max(combined.GetElement(0), combined.GetElement(1)));
                    hasValue = true;
                }

                while (startIndex < endIndex)
                {
                    var value = Unsafe.Add(ref valuesRef, startIndex++);
                    if (value > max)
                    {
                        max = value;
                        hasValue = true;
                    }
                }
            }
            else
            {
                var remaining = block;
                int baseIndex = blockIndex << 6;

                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var idx = baseIndex + bitIndex;
                    
                    if (idx < length && !array.IsNull(idx))
                    {
                        var value = Unsafe.Add(ref valuesRef, idx);
                        if (value > max)
                        {
                            max = value;
                            hasValue = true;
                        }
                    }
                    
                    remaining &= remaining - 1;
                }
            }
            blockIndex++;
        }

        if (!hasValue) throw new InvalidOperationException("Sequence contains no elements.");
        return max;
    }

    /// <summary>
    /// Computes Max for Decimal using block-based iteration.
    /// </summary>
    public static decimal MaxDecimal(Decimal128Array array, ref SelectionBitmap selection)
    {
        var length = array.Length;
        if (length == 0) throw new InvalidOperationException("Sequence contains no elements.");

        var blocks = selection.Blocks;
        decimal max = decimal.MinValue;
        bool hasValue = false;
        int blockIndex = 0;

        foreach (var block in blocks)
        {
            if (block == 0)
            {
                blockIndex++;
                continue;
            }

            var remaining = block;
            int baseIndex = blockIndex << 6;

            while (remaining != 0)
            {
                var bitIndex = BitOperations.TrailingZeroCount(remaining);
                var idx = baseIndex + bitIndex;
                
                if (idx < length && !array.IsNull(idx))
                {
                    var value = array.GetValue(idx)!.Value;
                    if (value > max)
                    {
                        max = value;
                        hasValue = true;
                    }
                }
                
                remaining &= remaining - 1;
            }
            blockIndex++;
        }

        if (!hasValue) throw new InvalidOperationException("Sequence contains no elements.");
        return max;
    }

    #endregion

    #region Dispatch Methods

    /// <summary>
    /// Dispatches Sum to the appropriate typed method.
    /// </summary>
    public static object ExecuteSum(IArrowArray column, ref SelectionBitmap selection, Type resultType)
    {
        return column switch
        {
            Int32Array int32Array => ConvertResult(SumInt32(int32Array, ref selection), resultType),
            Int64Array int64Array => ConvertResult(SumInt64(int64Array, ref selection), resultType),
            DoubleArray doubleArray => ConvertResult(SumDouble(doubleArray, ref selection), resultType),
            Decimal128Array decimalArray => ConvertResult(SumDecimal(decimalArray, ref selection), resultType),
            _ => throw new NotSupportedException($"Sum is not supported for column type {column.GetType().Name}")
        };
    }

    /// <summary>
    /// Dispatches Average to the appropriate typed method.
    /// </summary>
    public static object ExecuteAverage(IArrowArray column, ref SelectionBitmap selection, Type resultType)
    {
        return column switch
        {
            Int32Array int32Array => ConvertResult(AverageInt32(int32Array, ref selection), resultType),
            Int64Array int64Array => ConvertResult(AverageInt64(int64Array, ref selection), resultType),
            DoubleArray doubleArray => ConvertResult(AverageDouble(doubleArray, ref selection), resultType),
            Decimal128Array decimalArray => ConvertResult(AverageDecimal(decimalArray, ref selection), resultType),
            _ => throw new NotSupportedException($"Average is not supported for column type {column.GetType().Name}")
        };
    }

    /// <summary>
    /// Dispatches Min to the appropriate typed method.
    /// </summary>
    public static object ExecuteMin(IArrowArray column, ref SelectionBitmap selection, Type resultType)
    {
        return column switch
        {
            Int32Array int32Array => ConvertResult(MinInt32(int32Array, ref selection), resultType),
            Int64Array int64Array => ConvertResult(MinInt64(int64Array, ref selection), resultType),
            DoubleArray doubleArray => ConvertResult(MinDouble(doubleArray, ref selection), resultType),
            Decimal128Array decimalArray => ConvertResult(MinDecimal(decimalArray, ref selection), resultType),
            _ => throw new NotSupportedException($"Min is not supported for column type {column.GetType().Name}")
        };
    }

    /// <summary>
    /// Dispatches Max to the appropriate typed method.
    /// </summary>
    public static object ExecuteMax(IArrowArray column, ref SelectionBitmap selection, Type resultType)
    {
        return column switch
        {
            Int32Array int32Array => ConvertResult(MaxInt32(int32Array, ref selection), resultType),
            Int64Array int64Array => ConvertResult(MaxInt64(int64Array, ref selection), resultType),
            DoubleArray doubleArray => ConvertResult(MaxDouble(doubleArray, ref selection), resultType),
            Decimal128Array decimalArray => ConvertResult(MaxDecimal(decimalArray, ref selection), resultType),
            _ => throw new NotSupportedException($"Max is not supported for column type {column.GetType().Name}")
        };
    }

    private static object ConvertResult(object value, Type targetType)
    {
        if (value.GetType() == targetType)
            return value;
        return Convert.ChangeType(value, targetType);
    }

    #endregion
}
