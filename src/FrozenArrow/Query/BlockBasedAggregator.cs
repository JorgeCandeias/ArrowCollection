using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
/// </remarks>
internal static class BlockBasedAggregator
{
    /// <summary>
    /// Computes sum of Int32 values using block-based bitmap iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long SumInt32BlockBased(
        Int32Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        var values = array.Values;
        var nullBitmap = array.NullBitmapBuffer.Span;
        var hasNulls = array.NullCount > 0;
        
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
                    
                    block &= block - 1; // Clear lowest set bit
                }
            }
            else
            {
                // No nulls - faster path
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
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double SumDoubleBlockBased(
        DoubleArray array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        var values = array.Values;
        var nullBitmap = array.NullBitmapBuffer.Span;
        var hasNulls = array.NullCount > 0;
        
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
    /// Computes sum of Decimal values using block-based bitmap iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static decimal SumDecimalBlockBased(
        Decimal128Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        var hasNulls = array.NullCount > 0;
        
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

    /// <summary>
    /// Computes average of Int32 values using block-based bitmap iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (long sum, int count) SumAndCountInt32BlockBased(
        Int32Array array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        var values = array.Values;
        var nullBitmap = array.NullBitmapBuffer.Span;
        var hasNulls = array.NullCount > 0;
        
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
        
        return (sum, count);
    }

    /// <summary>
    /// Computes sum and count of Double values using block-based bitmap iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (double sum, int count) SumAndCountDoubleBlockBased(
        DoubleArray array,
        ulong[] selectionBuffer,
        int startRow,
        int endRow)
    {
        var values = array.Values;
        var nullBitmap = array.NullBitmapBuffer.Span;
        var hasNulls = array.NullCount > 0;
        
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
