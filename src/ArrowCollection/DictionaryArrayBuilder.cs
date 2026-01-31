using Apache.Arrow;
using Apache.Arrow.Memory;
using Apache.Arrow.Types;

namespace ArrowCollection;

/// <summary>
/// Builds dictionary-encoded Arrow arrays from collected values.
/// Uses the column statistics to decide whether dictionary encoding is beneficial.
/// </summary>
public static class DictionaryArrayBuilder
{
    /// <summary>
    /// Default cardinality threshold below which dictionary encoding is used.
    /// </summary>
    public const double DefaultDictionaryThreshold = 0.5;

    /// <summary>
    /// Builds a string array, using dictionary encoding if beneficial based on statistics.
    /// </summary>
    public static IArrowArray BuildStringArray(
        IReadOnlyList<string?> values,
        ColumnStatistics? statistics,
        MemoryAllocator allocator,
        double dictionaryThreshold = DefaultDictionaryThreshold)
    {
        if (statistics?.ShouldUseDictionaryEncoding(dictionaryThreshold) == true)
        {
            return BuildDictionaryEncodedStringArray(values, allocator);
        }

        return BuildPrimitiveStringArray(values, allocator);
    }

    /// <summary>
    /// Builds a dictionary-encoded string array.
    /// </summary>
    public static DictionaryArray BuildDictionaryEncodedStringArray(
        IReadOnlyList<string?> values,
        MemoryAllocator allocator)
    {
        // Build dictionary of unique values
        var dictionary = new Dictionary<string, int>();
        var dictionaryValues = new List<string>();
        var indices = new int[values.Count];
        var nullBitmap = new bool[values.Count];

        for (int i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (value is null)
            {
                nullBitmap[i] = false; // null
                indices[i] = 0; // placeholder
            }
            else
            {
                nullBitmap[i] = true; // not null
                if (!dictionary.TryGetValue(value, out var index))
                {
                    index = dictionaryValues.Count;
                    dictionary[value] = index;
                    dictionaryValues.Add(value);
                }
                indices[i] = index;
            }
        }

        // Build dictionary array (the unique values)
        var dictBuilder = new StringArray.Builder();
        foreach (var value in dictionaryValues)
        {
            dictBuilder.Append(value);
        }
        var dictArray = dictBuilder.Build(allocator);

        // Build indices array
        // Choose index type based on dictionary size
        IArrowArray indicesArray;
        IArrowType indexType;

        if (dictionaryValues.Count <= byte.MaxValue)
        {
            var indicesBuilder = new UInt8Array.Builder();
            for (int i = 0; i < values.Count; i++)
            {
                if (nullBitmap[i])
                    indicesBuilder.Append((byte)indices[i]);
                else
                    indicesBuilder.AppendNull();
            }
            indicesArray = indicesBuilder.Build(allocator);
            indexType = UInt8Type.Default;
        }
        else if (dictionaryValues.Count <= ushort.MaxValue)
        {
            var indicesBuilder = new UInt16Array.Builder();
            for (int i = 0; i < values.Count; i++)
            {
                if (nullBitmap[i])
                    indicesBuilder.Append((ushort)indices[i]);
                else
                    indicesBuilder.AppendNull();
            }
            indicesArray = indicesBuilder.Build(allocator);
            indexType = UInt16Type.Default;
        }
        else
        {
            var indicesBuilder = new Int32Array.Builder();
            for (int i = 0; i < values.Count; i++)
            {
                if (nullBitmap[i])
                    indicesBuilder.Append(indices[i]);
                else
                    indicesBuilder.AppendNull();
            }
            indicesArray = indicesBuilder.Build(allocator);
            indexType = Int32Type.Default;
        }

        var dictType = new DictionaryType(indexType, StringType.Default, false);
        return new DictionaryArray(dictType, indicesArray, dictArray);
    }

    private static StringArray BuildPrimitiveStringArray(
        IReadOnlyList<string?> values,
        MemoryAllocator allocator)
    {
        var builder = new StringArray.Builder();
        foreach (var value in values)
        {
            if (value is null)
                builder.AppendNull();
            else
                builder.Append(value);
        }
        return builder.Build(allocator);
    }

    /// <summary>
    /// Builds an int32 array, using dictionary encoding if beneficial based on statistics.
    /// </summary>
    public static IArrowArray BuildInt32Array(
        IReadOnlyList<int> values,
        ColumnStatistics? statistics,
        MemoryAllocator allocator,
        double dictionaryThreshold = DefaultDictionaryThreshold)
    {
        if (statistics?.ShouldUseDictionaryEncoding(dictionaryThreshold) == true)
        {
            return BuildDictionaryEncodedInt32Array(values, allocator);
        }

        return BuildPrimitiveInt32Array(values, allocator);
    }

    /// <summary>
    /// Builds a dictionary-encoded int32 array.
    /// </summary>
    public static DictionaryArray BuildDictionaryEncodedInt32Array(
        IReadOnlyList<int> values,
        MemoryAllocator allocator)
    {
        var dictionary = new Dictionary<int, int>();
        var dictionaryValues = new List<int>();
        var indices = new int[values.Count];

        for (int i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (!dictionary.TryGetValue(value, out var index))
            {
                index = dictionaryValues.Count;
                dictionary[value] = index;
                dictionaryValues.Add(value);
            }
            indices[i] = index;
        }

        // Build dictionary array
        var dictBuilder = new Int32Array.Builder();
        foreach (var value in dictionaryValues)
        {
            dictBuilder.Append(value);
        }
        var dictArray = dictBuilder.Build(allocator);

        // Build indices array with appropriate size
        IArrowArray indicesArray;
        IArrowType indexType;

        if (dictionaryValues.Count <= byte.MaxValue)
        {
            var indicesBuilder = new UInt8Array.Builder();
            foreach (var idx in indices)
                indicesBuilder.Append((byte)idx);
            indicesArray = indicesBuilder.Build(allocator);
            indexType = UInt8Type.Default;
        }
        else if (dictionaryValues.Count <= ushort.MaxValue)
        {
            var indicesBuilder = new UInt16Array.Builder();
            foreach (var idx in indices)
                indicesBuilder.Append((ushort)idx);
            indicesArray = indicesBuilder.Build(allocator);
            indexType = UInt16Type.Default;
        }
        else
        {
            var indicesBuilder = new Int32Array.Builder();
            foreach (var idx in indices)
                indicesBuilder.Append(idx);
            indicesArray = indicesBuilder.Build(allocator);
            indexType = Int32Type.Default;
        }

        var dictType = new DictionaryType(indexType, Int32Type.Default, false);
        return new DictionaryArray(dictType, indicesArray, dictArray);
    }

    private static Int32Array BuildPrimitiveInt32Array(
        IReadOnlyList<int> values,
        MemoryAllocator allocator)
    {
        var builder = new Int32Array.Builder().Reserve(values.Count);
        foreach (var value in values)
            builder.Append(value);
        return builder.Build(allocator);
    }

    /// <summary>
    /// Builds a double array, using dictionary encoding if beneficial based on statistics.
    /// </summary>
    public static IArrowArray BuildDoubleArray(
        IReadOnlyList<double> values,
        ColumnStatistics? statistics,
        MemoryAllocator allocator,
        double dictionaryThreshold = DefaultDictionaryThreshold)
    {
        if (statistics?.ShouldUseDictionaryEncoding(dictionaryThreshold) == true)
        {
            return BuildDictionaryEncodedDoubleArray(values, allocator);
        }

        return BuildPrimitiveDoubleArray(values, allocator);
    }

    /// <summary>
    /// Builds a dictionary-encoded double array.
    /// </summary>
    public static DictionaryArray BuildDictionaryEncodedDoubleArray(
        IReadOnlyList<double> values,
        MemoryAllocator allocator)
    {
        var dictionary = new Dictionary<double, int>();
        var dictionaryValues = new List<double>();
        var indices = new int[values.Count];

        for (int i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (!dictionary.TryGetValue(value, out var index))
            {
                index = dictionaryValues.Count;
                dictionary[value] = index;
                dictionaryValues.Add(value);
            }
            indices[i] = index;
        }

        var dictBuilder = new DoubleArray.Builder();
        foreach (var value in dictionaryValues)
            dictBuilder.Append(value);
        var dictArray = dictBuilder.Build(allocator);

        IArrowArray indicesArray;
        IArrowType indexType;

        if (dictionaryValues.Count <= byte.MaxValue)
        {
            var indicesBuilder = new UInt8Array.Builder();
            foreach (var idx in indices)
                indicesBuilder.Append((byte)idx);
            indicesArray = indicesBuilder.Build(allocator);
            indexType = UInt8Type.Default;
        }
        else if (dictionaryValues.Count <= ushort.MaxValue)
        {
            var indicesBuilder = new UInt16Array.Builder();
            foreach (var idx in indices)
                indicesBuilder.Append((ushort)idx);
            indicesArray = indicesBuilder.Build(allocator);
            indexType = UInt16Type.Default;
        }
        else
        {
            var indicesBuilder = new Int32Array.Builder();
            foreach (var idx in indices)
                indicesBuilder.Append(idx);
            indicesArray = indicesBuilder.Build(allocator);
            indexType = Int32Type.Default;
        }

        var dictType = new DictionaryType(indexType, DoubleType.Default, false);
        return new DictionaryArray(dictType, indicesArray, dictArray);
    }

    private static DoubleArray BuildPrimitiveDoubleArray(
        IReadOnlyList<double> values,
        MemoryAllocator allocator)
    {
        var builder = new DoubleArray.Builder().Reserve(values.Count);
        foreach (var value in values)
            builder.Append(value);
        return builder.Build(allocator);
    }

    /// <summary>
    /// Builds a decimal array, using dictionary encoding if beneficial based on statistics.
    /// </summary>
    public static IArrowArray BuildDecimalArray(
        IReadOnlyList<decimal> values,
        ColumnStatistics? statistics,
        MemoryAllocator allocator,
        double dictionaryThreshold = DefaultDictionaryThreshold)
    {
        if (statistics?.ShouldUseDictionaryEncoding(dictionaryThreshold) == true)
        {
            return BuildDictionaryEncodedDecimalArray(values, allocator);
        }

        return BuildPrimitiveDecimalArray(values, allocator);
    }

    /// <summary>
    /// Builds a dictionary-encoded decimal array.
    /// </summary>
    public static DictionaryArray BuildDictionaryEncodedDecimalArray(
        IReadOnlyList<decimal> values,
        MemoryAllocator allocator)
    {
        var dictionary = new Dictionary<decimal, int>();
        var dictionaryValues = new List<decimal>();
        var indices = new int[values.Count];

        for (int i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (!dictionary.TryGetValue(value, out var index))
            {
                index = dictionaryValues.Count;
                dictionary[value] = index;
                dictionaryValues.Add(value);
            }
            indices[i] = index;
        }

        var dictBuilder = new Decimal128Array.Builder(new Decimal128Type(29, 6));
        foreach (var value in dictionaryValues)
            dictBuilder.Append(value);
        var dictArray = dictBuilder.Build(allocator);

        IArrowArray indicesArray;
        IArrowType indexType;

        if (dictionaryValues.Count <= byte.MaxValue)
        {
            var indicesBuilder = new UInt8Array.Builder();
            foreach (var idx in indices)
                indicesBuilder.Append((byte)idx);
            indicesArray = indicesBuilder.Build(allocator);
            indexType = UInt8Type.Default;
        }
        else if (dictionaryValues.Count <= ushort.MaxValue)
        {
            var indicesBuilder = new UInt16Array.Builder();
            foreach (var idx in indices)
                indicesBuilder.Append((ushort)idx);
            indicesArray = indicesBuilder.Build(allocator);
            indexType = UInt16Type.Default;
        }
        else
        {
            var indicesBuilder = new Int32Array.Builder();
            foreach (var idx in indices)
                indicesBuilder.Append(idx);
            indicesArray = indicesBuilder.Build(allocator);
            indexType = Int32Type.Default;
        }

        var dictType = new DictionaryType(indexType, new Decimal128Type(29, 6), false);
        return new DictionaryArray(dictType, indicesArray, dictArray);
    }

    private static Decimal128Array BuildPrimitiveDecimalArray(
        IReadOnlyList<decimal> values,
        MemoryAllocator allocator)
    {
        var builder = new Decimal128Array.Builder(new Decimal128Type(29, 6)).Reserve(values.Count);
        foreach (var value in values)
            builder.Append(value);
        return builder.Build(allocator);
    }

    /// <summary>
    /// Reads a value from an array that may be dictionary-encoded or primitive.
    /// </summary>
    public static string? GetStringValue(IArrowArray array, int index)
    {
        if (array is DictionaryArray dictArray)
        {
            if (dictArray.IsNull(index))
                return null;

            var dictIndex = GetDictionaryIndex(dictArray.Indices, index);
            var dictionary = (StringArray)dictArray.Dictionary;
            return dictionary.GetString(dictIndex);
        }

        var stringArray = (StringArray)array;
        return stringArray.IsNull(index) ? null : stringArray.GetString(index);
    }

    /// <summary>
    /// Reads a value from an array that may be dictionary-encoded or primitive.
    /// </summary>
    public static int GetInt32Value(IArrowArray array, int index)
    {
        if (array is DictionaryArray dictArray)
        {
            var dictIndex = GetDictionaryIndex(dictArray.Indices, index);
            var dictionary = (Int32Array)dictArray.Dictionary;
            return dictionary.GetValue(dictIndex)!.Value;
        }

        return ((Int32Array)array).GetValue(index)!.Value;
    }

    /// <summary>
    /// Reads a value from an array that may be dictionary-encoded or primitive.
    /// </summary>
    public static double GetDoubleValue(IArrowArray array, int index)
    {
        if (array is DictionaryArray dictArray)
        {
            var dictIndex = GetDictionaryIndex(dictArray.Indices, index);
            var dictionary = (DoubleArray)dictArray.Dictionary;
            return dictionary.GetValue(dictIndex)!.Value;
        }

        return ((DoubleArray)array).GetValue(index)!.Value;
    }

    /// <summary>
    /// Reads a value from an array that may be dictionary-encoded or primitive.
    /// </summary>
    public static decimal GetDecimalValue(IArrowArray array, int index)
    {
        if (array is DictionaryArray dictArray)
        {
            var dictIndex = GetDictionaryIndex(dictArray.Indices, index);
            var dictionary = (Decimal128Array)dictArray.Dictionary;
            return (decimal)dictionary.GetValue(dictIndex)!.Value;
        }

        return (decimal)((Decimal128Array)array).GetValue(index)!.Value;
    }

    private static int GetDictionaryIndex(IArrowArray indices, int index)
    {
        return indices switch
        {
            UInt8Array u8 => u8.GetValue(index)!.Value,
            UInt16Array u16 => u16.GetValue(index)!.Value,
            Int32Array i32 => i32.GetValue(index)!.Value,
            _ => throw new NotSupportedException($"Unsupported index array type: {indices.GetType()}")
        };
    }
}
