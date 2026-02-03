using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Apache.Arrow;



namespace FrozenArrow.Query;

/// <summary>
/// Provides block-based bitmap iteration for efficient aggregation.
/// Instead of checking each bit individually, processes 64 bits at a time
/// using TrailingZeroCount to find set bits.
/// </summary>
/// <remarks>
/// For sparse selections (typical after filtering), this is significantly faster
/// than iterating through every row:
/// - Dense iteration: O(n) where n = total rows
/// - Block iteration: O(k) where k = selected rows + blocks checked
/// 
/// For a 50% selection rate on 1M rows:
/// - Dense: 1M loop iterations with bit checks
/// - Block: ~15.6K block loads + ~500K value accesses (no wasted iterations)
/// 
/// Dense Block Optimization:
/// When a block is fully selected (all 64 bits set), we use SIMD to sum
/// all 64 contiguous values at once instead of iterating bit-by-bit.
/// This provides up to 8x speedup for dense selections.
/// 
/// Null Bitmap Optimization:
/// When hasNulls is true, the caller should pre-apply the null bitmap to the selection
/// using SelectionBitmap.AndWithNullBitmapRange() before calling these methods.
/// This eliminates per-element IsNull() checks in the inner loop.
/// </remarks>
internal static class BlockBasedAggregator
{
    /// <summary>
    /// Computes sum of Int32 values using block-based bitmap iteration.
    /// For nullable columns, call AndWithNullBitmapRange first to pre-apply null mask.
    /// Uses SIMD vectorization for fully-selected (dense) blocks.
    /// </summary>
    /// <param name="array">The Int32 array to sum.</param>
    /// <param name="selectionBuffer">Selection bitmap buffer (pre-masked with null bitmap if applicable).</param>
    /// <param name="startRow">First row index (inclusive).</param>
    /// <param name="endRow">Last row index (exclusive).</param>
    /// <param name="nullsPreApplied">True if null bitmap was already ANDed into selection.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long SumInt32BlockBased(
        Int32Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow,
        bool nullsPreApplied = false)
    {
        var values = array.Values;
        var hasNulls = array.NullCount > 0 && !nullsPreApplied;
        
        long sum = 0;
        
        // Calculate block range
        int startBlock = startRow >> 6;      // startRow / 64
        int endBlock = (endRow - 1) >> 6;    // Last block containing bits
        
        ref int valuesRef = ref Unsafe.AsRef(in values[0]);
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            
            if (block == 0) continue; // Skip empty blocks
            
            // Mask to only include bits within our range
            int blockStartBit = blockIndex << 6;
            
            // Mask off bits before startRow
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            // Mask off bits at or after endRow
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            // Process set bits using TrailingZeroCount
            // No null check needed if nullsPreApplied is true
            if (hasNulls)
            {
                var nullBitmap = array.NullBitmapBuffer.Span;
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!IsNull(nullBitmap, rowIndex))
                    {
                        sum += Unsafe.Add(ref valuesRef, rowIndex);
                    }
                    
                    block &= block - 1; // Clear lowest set bit
                }
            }
            else
            {
                // DENSE BLOCK OPTIMIZATION: When all 64 bits are set, use SIMD
                // This is much faster than iterating bit-by-bit
                if (block == ulong.MaxValue)
                {
                    sum += SumInt32DenseBlock(ref valuesRef, blockStartBit);
                }
                else
                {
                    // Sparse block - iterate set bits using TrailingZeroCount
                    while (block != 0)
                    {
                        int bitIndex = BitOperations.TrailingZeroCount(block);
                        int rowIndex = blockStartBit + bitIndex;
                        sum += Unsafe.Add(ref valuesRef, rowIndex);
                        block &= block - 1;
                    }
                }
            }
        }
        
        return sum;
    }

    /// <summary>
    /// SIMD-optimized sum of 64 contiguous Int32 values.
    /// Processes 8 values at a time using AVX2, widening to Int64 to prevent overflow.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long SumInt32DenseBlock(ref int valuesRef, int startIndex)
    {
        long sum = 0;
        int i = 0;

        // AVX2 path: Process 8 Int32 values at a time, widen to Int64 to prevent overflow
        if (Vector256.IsHardwareAccelerated)
        {
            // Process 64 values in 8 iterations of 8 values each
            for (; i <= 56; i += 8)
            {
                var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex + i));
                // Widen Int32 to Int64 to prevent overflow during sum
                var (lower, upper) = Vector256.Widen(vec);
                sum += Vector256.Sum(lower) + Vector256.Sum(upper);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            // SSE path: Process 4 Int32 values at a time
            for (; i <= 60; i += 4)
            {
                var vec = Vector128.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex + i));
                var (lower, upper) = Vector128.Widen(vec);
                sum += Vector128.Sum(lower) + Vector128.Sum(upper);
            }
        }

        // Scalar remainder (handles cases where SIMD is not available or remaining elements)
        for (; i < 64; i++)
        {
            sum += Unsafe.Add(ref valuesRef, startIndex + i);
        }

        return sum;
    }

    // Legacy overload for backward compatibility
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long SumInt32BlockBased(
        Int32Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        return SumInt32BlockBased(array, selectionBuffer, startRow, endRow, nullsPreApplied: false);
    }

    /// <summary>
    /// Computes sum of Int64 values using block-based bitmap iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long SumInt64BlockBased(
        Int64Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        var values = array.Values;
        var nullBitmap = array.NullBitmapBuffer.Span;
        var hasNulls = array.NullCount > 0;
        
        long sum = 0;
        
        int startBlock = startRow >> 6;
        int endBlock = (endRow - 1) >> 6;
        
        ref long valuesRef = ref Unsafe.AsRef(in values[0]);
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            if (block == 0) continue;
            
            int blockStartBit = blockIndex << 6;
            
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            if (hasNulls)
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!IsNull(nullBitmap, rowIndex))
                    {
                        sum += Unsafe.Add(ref valuesRef, rowIndex);
                    }
                    
                    block &= block - 1;
                }
            }
            else
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    sum += Unsafe.Add(ref valuesRef, rowIndex);
                    block &= block - 1;
                }
            }
        }
        
        return sum;
    }

    /// <summary>
    /// Computes sum of Double values using block-based bitmap iteration.
    /// For nullable columns, call AndWithNullBitmapRange first to pre-apply null mask.
    /// Uses SIMD vectorization for fully-selected (dense) blocks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double SumDoubleBlockBased(
        DoubleArray array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow,
        bool nullsPreApplied = false)
    {
        var values = array.Values;
        var hasNulls = array.NullCount > 0 && !nullsPreApplied;
        
        double sum = 0;
        
        int startBlock = startRow >> 6;
        int endBlock = (endRow - 1) >> 6;
        
        ref double valuesRef = ref Unsafe.AsRef(in values[0]);
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            if (block == 0) continue;
            
            int blockStartBit = blockIndex << 6;
            
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            if (hasNulls)
            {
                var nullBitmap = array.NullBitmapBuffer.Span;
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!IsNull(nullBitmap, rowIndex))
                    {
                        sum += Unsafe.Add(ref valuesRef, rowIndex);
                    }
                    
                    block &= block - 1;
                }
            }
            else
            {
                // DENSE BLOCK OPTIMIZATION: When all 64 bits are set, use SIMD
                if (block == ulong.MaxValue)
                {
                    sum += SumDoubleDenseBlock(ref valuesRef, blockStartBit);
                }
                else
                {
                    // Sparse block - iterate set bits using TrailingZeroCount
                    while (block != 0)
                    {
                        int bitIndex = BitOperations.TrailingZeroCount(block);
                        int rowIndex = blockStartBit + bitIndex;
                        sum += Unsafe.Add(ref valuesRef, rowIndex);
                        block &= block - 1;
                    }
                }
            }
        }
        
        return sum;
    }

    /// <summary>
    /// SIMD-optimized sum of 64 contiguous Double values.
    /// Processes 4 values at a time using AVX2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SumDoubleDenseBlock(ref double valuesRef, int startIndex)
    {
        double sum = 0;
        int i = 0;

        // AVX2 path: Process 4 Double values at a time
        if (Vector256.IsHardwareAccelerated)
        {
            var sumVec = Vector256<double>.Zero;
            // Process 64 values in 16 iterations of 4 values each
            for (; i <= 60; i += 4)
            {
                var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex + i));
                sumVec = Vector256.Add(sumVec, vec);
            }
            sum = Vector256.Sum(sumVec);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            // SSE path: Process 2 Double values at a time
            var sumVec = Vector128<double>.Zero;
            for (; i <= 62; i += 2)
            {
                var vec = Vector128.LoadUnsafe(ref Unsafe.Add(ref valuesRef, startIndex + i));
                sumVec = Vector128.Add(sumVec, vec);
            }
            sum = Vector128.Sum(sumVec);
        }

        // Scalar remainder
        for (; i < 64; i++)
        {
            sum += Unsafe.Add(ref valuesRef, startIndex + i);
        }

        return sum;
    }

    // Legacy overload for backward compatibility
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double SumDoubleBlockBased(
        DoubleArray array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        return SumDoubleBlockBased(array, selectionBuffer, startRow, endRow, nullsPreApplied: false);
    }

    /// <summary>
    /// Computes sum of Decimal values using block-based bitmap iteration.
    /// For nullable columns, call AndWithNullBitmapRange first to pre-apply null mask.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static decimal SumDecimalBlockBased(
        Decimal128Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow,
        bool nullsPreApplied = false)
    {
        var hasNulls = array.NullCount > 0 && !nullsPreApplied;
        
        decimal sum = 0;
        
        int startBlock = startRow >> 6;
        int endBlock = (endRow - 1) >> 6;
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            if (block == 0) continue;
            
            int blockStartBit = blockIndex << 6;
            
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            if (hasNulls)
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!array.IsNull(rowIndex))
                    {
                        sum += array.GetValue(rowIndex)!.Value;
                    }
                    
                    block &= block - 1;
                }
            }
            else
            {
                // No nulls OR nulls pre-applied - faster path without null checks
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    sum += array.GetValue(rowIndex)!.Value;
                    block &= block - 1;
                }
            }
        }
        
        return sum;
    }

    // Legacy overload for backward compatibility
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static decimal SumDecimalBlockBased(
        Decimal128Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        return SumDecimalBlockBased(array, selectionBuffer, startRow, endRow, nullsPreApplied: false);
    }

    /// <summary>
    /// Computes average of Int32 values using block-based bitmap iteration.
    /// For nullable columns, call AndWithNullBitmapRange first to pre-apply null mask.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (long sum, int count) SumAndCountInt32BlockBased(
        Int32Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow,
        bool nullsPreApplied = false)
    {
        var values = array.Values;
        var hasNulls = array.NullCount > 0 && !nullsPreApplied;
        
        long sum = 0;
        int count = 0;
        
        int startBlock = startRow >> 6;
        int endBlock = (endRow - 1) >> 6;
        
        ref int valuesRef = ref Unsafe.AsRef(in values[0]);
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            if (block == 0) continue;
            
            int blockStartBit = blockIndex << 6;
            
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            if (hasNulls)
            {
                var nullBitmap = array.NullBitmapBuffer.Span;
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!IsNull(nullBitmap, rowIndex))
                    {
                        sum += Unsafe.Add(ref valuesRef, rowIndex);
                        count++;
                    }
                    
                    block &= block - 1;
                }
            }
            else
            {
                // DENSE BLOCK OPTIMIZATION: When all 64 bits are set, use SIMD
                if (block == ulong.MaxValue)
                {
                    sum += SumInt32DenseBlock(ref valuesRef, blockStartBit);
                    count += 64;
                }
                else
                {
                    // Sparse block - iterate set bits
                    while (block != 0)
                    {
                        int bitIndex = BitOperations.TrailingZeroCount(block);
                        int rowIndex = blockStartBit + bitIndex;
                        sum += Unsafe.Add(ref valuesRef, rowIndex);
                        count++;
                        block &= block - 1;
                    }
                }
            }
        }
        
        return (sum, count);
    }

    // Legacy overload for backward compatibility
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (long sum, int count) SumAndCountInt32BlockBased(
        Int32Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        return SumAndCountInt32BlockBased(array, selectionBuffer, startRow, endRow, nullsPreApplied: false);
    }

    /// <summary>
    /// Computes sum and count of Double values using block-based bitmap iteration.
    /// For nullable columns, call AndWithNullBitmapRange first to pre-apply null mask.
    /// Uses SIMD vectorization for fully-selected (dense) blocks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (double sum, int count) SumAndCountDoubleBlockBased(
        DoubleArray array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow,
        bool nullsPreApplied = false)
    {
        var values = array.Values;
        var hasNulls = array.NullCount > 0 && !nullsPreApplied;
        
        double sum = 0;
        int count = 0;
        
        int startBlock = startRow >> 6;
        int endBlock = (endRow - 1) >> 6;
        
        ref double valuesRef = ref Unsafe.AsRef(in values[0]);
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            if (block == 0) continue;
            
            int blockStartBit = blockIndex << 6;
            
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            if (hasNulls)
            {
                var nullBitmap = array.NullBitmapBuffer.Span;
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!IsNull(nullBitmap, rowIndex))
                    {
                        sum += Unsafe.Add(ref valuesRef, rowIndex);
                        count++;
                    }
                    
                    block &= block - 1;
                }
            }
            else
            {
                // DENSE BLOCK OPTIMIZATION: When all 64 bits are set, use SIMD
                if (block == ulong.MaxValue)
                {
                    sum += SumDoubleDenseBlock(ref valuesRef, blockStartBit);
                    count += 64;
                }
                else
                {
                    // Sparse block - iterate set bits
                    while (block != 0)
                    {
                        int bitIndex = BitOperations.TrailingZeroCount(block);
                        int rowIndex = blockStartBit + bitIndex;
                        sum += Unsafe.Add(ref valuesRef, rowIndex);
                        count++;
                        block &= block - 1;
                    }
                }
            }
        }
        
        return (sum, count);
    }

    // Legacy overload for backward compatibility
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (double sum, int count) SumAndCountDoubleBlockBased(
        DoubleArray array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        return SumAndCountDoubleBlockBased(array, selectionBuffer, startRow, endRow, nullsPreApplied: false);
    }

    /// <summary>
    /// Computes sum and count of Decimal values using block-based bitmap iteration.
    /// For nullable columns, call AndWithNullBitmapRange first to pre-apply null mask.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (decimal sum, int count) SumAndCountDecimalBlockBased(
        Decimal128Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow,
        bool nullsPreApplied = false)
    {
        var hasNulls = array.NullCount > 0 && !nullsPreApplied;
        
        decimal sum = 0;
        int count = 0;
        
        int startBlock = startRow >> 6;
        int endBlock = (endRow - 1) >> 6;
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            if (block == 0) continue;
            
            int blockStartBit = blockIndex << 6;
            
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            if (hasNulls)
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!array.IsNull(rowIndex))
                    {
                        sum += array.GetValue(rowIndex)!.Value;
                        count++;
                    }
                    
                    block &= block - 1;
                }
            }
            else
            {
                // No nulls OR nulls pre-applied - faster path without null checks
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    sum += array.GetValue(rowIndex)!.Value;
                    count++;
                    block &= block - 1;
                }
            }
        }
        
        return (sum, count);
    }

    /// <summary>
    /// Finds minimum Int32 value using block-based bitmap iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (int min, bool hasValue) MinInt32BlockBased(
        Int32Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        var values = array.Values;
        var nullBitmap = array.NullBitmapBuffer.Span;
        var hasNulls = array.NullCount > 0;
        
        int min = int.MaxValue;
        bool hasValue = false;
        
        int startBlock = startRow >> 6;
        int endBlock = (endRow - 1) >> 6;
        
        ref int valuesRef = ref Unsafe.AsRef(in values[0]);
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            if (block == 0) continue;
            
            int blockStartBit = blockIndex << 6;
            
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            if (hasNulls)
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!IsNull(nullBitmap, rowIndex))
                    {
                        var value = Unsafe.Add(ref valuesRef, rowIndex);
                        if (!hasValue || value < min)
                        {
                            min = value;
                            hasValue = true;
                        }
                    }
                    
                    block &= block - 1;
                }
            }
            else
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    var value = Unsafe.Add(ref valuesRef, rowIndex);
                    if (!hasValue || value < min)
                    {
                        min = value;
                        hasValue = true;
                    }
                    block &= block - 1;
                }
            }
        }
        
        return (min, hasValue);
    }

    /// <summary>
    /// Finds minimum Double value using block-based bitmap iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (double min, bool hasValue) MinDoubleBlockBased(
        DoubleArray array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        var values = array.Values;
        var nullBitmap = array.NullBitmapBuffer.Span;
        var hasNulls = array.NullCount > 0;
        
        double min = double.MaxValue;
        bool hasValue = false;
        
        int startBlock = startRow >> 6;
        int endBlock = (endRow - 1) >> 6;
        
        ref double valuesRef = ref Unsafe.AsRef(in values[0]);
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            if (block == 0) continue;
            
            int blockStartBit = blockIndex << 6;
            
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            if (hasNulls)
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!IsNull(nullBitmap, rowIndex))
                    {
                        var value = Unsafe.Add(ref valuesRef, rowIndex);
                        if (!hasValue || value < min)
                        {
                            min = value;
                            hasValue = true;
                        }
                    }
                    
                    block &= block - 1;
                }
            }
            else
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    var value = Unsafe.Add(ref valuesRef, rowIndex);
                    if (!hasValue || value < min)
                    {
                        min = value;
                        hasValue = true;
                    }
                    block &= block - 1;
                }
            }
        }
        
        return (min, hasValue);
    }

    /// <summary>
    /// Finds maximum Int32 value using block-based bitmap iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (int max, bool hasValue) MaxInt32BlockBased(
        Int32Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        var values = array.Values;
        var nullBitmap = array.NullBitmapBuffer.Span;
        var hasNulls = array.NullCount > 0;
        
        int max = int.MinValue;
        bool hasValue = false;
        
        int startBlock = startRow >> 6;
        int endBlock = (endRow - 1) >> 6;
        
        ref int valuesRef = ref Unsafe.AsRef(in values[0]);
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            if (block == 0) continue;
            
            int blockStartBit = blockIndex << 6;
            
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            if (hasNulls)
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!IsNull(nullBitmap, rowIndex))
                    {
                        var value = Unsafe.Add(ref valuesRef, rowIndex);
                        if (!hasValue || value > max)
                        {
                            max = value;
                            hasValue = true;
                        }
                    }
                    
                    block &= block - 1;
                }
            }
            else
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    var value = Unsafe.Add(ref valuesRef, rowIndex);
                    if (!hasValue || value > max)
                    {
                        max = value;
                        hasValue = true;
                    }
                    block &= block - 1;
                }
            }
        }
        
        return (max, hasValue);
    }

    /// <summary>
    /// Finds maximum Double value using block-based bitmap iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (double max, bool hasValue) MaxDoubleBlockBased(
        DoubleArray array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        var values = array.Values;
        var nullBitmap = array.NullBitmapBuffer.Span;
        var hasNulls = array.NullCount > 0;
        
        double max = double.MinValue;
        bool hasValue = false;
        
        int startBlock = startRow >> 6;
        int endBlock = (endRow - 1) >> 6;
        
        ref double valuesRef = ref Unsafe.AsRef(in values[0]);
        
        for (int blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            ulong block = selectionBuffer[blockIndex];
            if (block == 0) continue;
            
            int blockStartBit = blockIndex << 6;
            
            if (blockStartBit < startRow)
            {
                int bitsToSkip = startRow - blockStartBit;
                block &= ~((1UL << bitsToSkip) - 1);
            }
            
            int blockEndBit = blockStartBit + 64;
            if (blockEndBit > endRow)
            {
                int bitsToKeep = endRow - blockStartBit;
                if (bitsToKeep < 64)
                {
                    block &= (1UL << bitsToKeep) - 1;
                }
            }
            
            if (block == 0) continue;
            
            if (hasNulls)
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    
                    if (!IsNull(nullBitmap, rowIndex))
                    {
                        var value = Unsafe.Add(ref valuesRef, rowIndex);
                        if (!hasValue || value > max)
                        {
                            max = value;
                            hasValue = true;
                        }
                    }
                    
                    block &= block - 1;
                }
            }
            else
            {
                while (block != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(block);
                    int rowIndex = blockStartBit + bitIndex;
                    var value = Unsafe.Add(ref valuesRef, rowIndex);
                    if (!hasValue || value > max)
                    {
                        max = value;
                        hasValue = true;
                    }
                    block &= block - 1;
                }
            }
        }
        
        return (max, hasValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNull(ReadOnlySpan<byte> nullBitmap, int index)
    {
        if (nullBitmap.IsEmpty) return false;
        return (nullBitmap[index >> 3] & (1 << (index & 7))) == 0;
    }
}
