using Apache.Arrow;

namespace ArrowCollection.Query;

/// <summary>
/// Helper utilities for working with DictionaryArray types in queries.
/// DictionaryArray stores values as indices into a dictionary of unique values,
/// which is more memory-efficient for low-cardinality columns.
/// </summary>
internal static class DictionaryArrayHelper
{
    /// <summary>
    /// Extracts the underlying value at a given index from a DictionaryArray.
    /// </summary>
    public static object? GetValue(DictionaryArray dictArray, int index)
    {
        if (dictArray.IsNull(index))
            return null;

        // Get the dictionary index for this row
        var dictIndex = GetDictionaryIndex(dictArray.Indices, index);
        
        // Look up the actual value in the dictionary
        return GetValueFromDictionary(dictArray.Dictionary, dictIndex);
    }

    /// <summary>
    /// Gets the dictionary index (integer) for a given row.
    /// </summary>
    public static int GetDictionaryIndex(IArrowArray indices, int rowIndex)
    {
        return indices switch
        {
            Int8Array int8 => int8.GetValue(rowIndex) ?? 0,
            Int16Array int16 => int16.GetValue(rowIndex) ?? 0,
            Int32Array int32 => int32.GetValue(rowIndex) ?? 0,
            Int64Array int64 => (int)(int64.GetValue(rowIndex) ?? 0),
            UInt8Array uint8 => uint8.GetValue(rowIndex) ?? 0,
            UInt16Array uint16 => uint16.GetValue(rowIndex) ?? 0,
            UInt32Array uint32 => (int)(uint32.GetValue(rowIndex) ?? 0),
            UInt64Array uint64 => (int)(uint64.GetValue(rowIndex) ?? 0),
            _ => throw new NotSupportedException($"Unsupported dictionary index type: {indices.GetType().Name}")
        };
    }

    /// <summary>
    /// Gets the value from the dictionary array at the specified dictionary index.
    /// </summary>
    public static object? GetValueFromDictionary(IArrowArray dictionary, int dictIndex)
    {
        return dictionary switch
        {
            StringArray str => str.GetString(dictIndex),
            Int32Array int32 => int32.GetValue(dictIndex),
            Int64Array int64 => int64.GetValue(dictIndex),
            DoubleArray dbl => dbl.GetValue(dictIndex),
            FloatArray flt => flt.GetValue(dictIndex),
            Decimal128Array dec => dec.GetValue(dictIndex),
            BooleanArray boolean => boolean.GetValue(dictIndex),
            _ => throw new NotSupportedException($"Unsupported dictionary value type: {dictionary.GetType().Name}")
        };
    }

    /// <summary>
    /// Checks if the dictionary array contains numeric values that can be aggregated.
    /// </summary>
    public static bool IsNumericDictionary(DictionaryArray dictArray)
    {
        return dictArray.Dictionary is Int32Array or Int64Array or DoubleArray or FloatArray or Decimal128Array;
    }

    /// <summary>
    /// Gets the numeric value at the given index, looking up through the dictionary.
    /// </summary>
    public static double GetNumericValue(DictionaryArray dictArray, int rowIndex)
    {
        var dictIndex = GetDictionaryIndex(dictArray.Indices, rowIndex);
        
        return dictArray.Dictionary switch
        {
            Int32Array int32 => int32.Values[dictIndex],
            Int64Array int64 => int64.Values[dictIndex],
            DoubleArray dbl => dbl.Values[dictIndex],
            FloatArray flt => flt.Values[dictIndex],
            Decimal128Array dec => (double)(dec.GetValue(dictIndex) ?? 0m),
            _ => throw new NotSupportedException($"Cannot get numeric value from {dictArray.Dictionary.GetType().Name}")
        };
    }

    /// <summary>
    /// Gets the decimal value at the given index, looking up through the dictionary.
    /// </summary>
    public static decimal GetDecimalValue(DictionaryArray dictArray, int rowIndex)
    {
        var dictIndex = GetDictionaryIndex(dictArray.Indices, rowIndex);
        
        return dictArray.Dictionary switch
        {
            Int32Array int32 => int32.Values[dictIndex],
            Int64Array int64 => int64.Values[dictIndex],
            DoubleArray dbl => (decimal)dbl.Values[dictIndex],
            FloatArray flt => (decimal)flt.Values[dictIndex],
            Decimal128Array dec => dec.GetValue(dictIndex) ?? 0m,
            _ => throw new NotSupportedException($"Cannot get decimal value from {dictArray.Dictionary.GetType().Name}")
        };
    }

    /// <summary>
    /// Executes a GroupBy operation on a dictionary-encoded column.
    /// Groups by dictionary indices (fast integer comparison) then maps back to actual keys.
    /// </summary>
    public static Dictionary<TKey, List<int>> GroupByDictionary<TKey>(
        DictionaryArray dictArray,
        ref SelectionBitmap selection) where TKey : notnull
    {
        var groups = new Dictionary<int, List<int>>(); // Group by dictionary index
        
        foreach (var rowIndex in selection.GetSelectedIndices())
        {
            if (dictArray.IsNull(rowIndex))
                continue;
                
            var dictIndex = GetDictionaryIndex(dictArray.Indices, rowIndex);
            
            if (!groups.TryGetValue(dictIndex, out var list))
            {
                list = [];
                groups[dictIndex] = list;
            }
            list.Add(rowIndex);
        }

        // Map dictionary indices back to actual key values
        var result = new Dictionary<TKey, List<int>>();
        foreach (var (dictIndex, indices) in groups)
        {
            var key = (TKey)GetValueFromDictionary(dictArray.Dictionary, dictIndex)!;
            result[key] = indices;
        }

        return result;
    }
}
