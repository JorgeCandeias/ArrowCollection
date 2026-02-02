namespace FrozenArrow;

/// <summary>
/// Statistics collected during the build of a FrozenArrow collection.
/// Provides insights into data characteristics and encoding decisions.
/// </summary>
public sealed class FrozenArrowBuildStatistics
{
    /// <summary>
    /// Statistics for each column, keyed by column name.
    /// </summary>
    public required IReadOnlyDictionary<string, ColumnStatistics> ColumnStatistics { get; init; }

    /// <summary>
    /// Total number of rows in the collection.
    /// </summary>
    public required long RowCount { get; init; }

    /// <summary>
    /// Total number of columns in the collection.
    /// </summary>
    public int ColumnCount => ColumnStatistics.Count;

    /// <summary>
    /// Time taken to collect statistics (if measured).
    /// </summary>
    public TimeSpan? StatisticsCollectionTime { get; init; }

    /// <summary>
    /// Time taken to build the Arrow arrays (if measured).
    /// </summary>
    public TimeSpan? ArrayBuildTime { get; init; }

    /// <summary>
    /// Gets columns that would benefit from dictionary encoding.
    /// </summary>
    public IEnumerable<ColumnStatistics> GetDictionaryEncodingCandidates(double threshold = 0.5)
    {
        return ColumnStatistics.Values.Where(c => c.ShouldUseDictionaryEncoding(threshold));
    }

    /// <summary>
    /// Gets columns that would benefit from run-length encoding.
    /// </summary>
    public IEnumerable<ColumnStatistics> GetRunLengthEncodingCandidates(double threshold = 0.1)
    {
        return ColumnStatistics.Values.Where(c => c.ShouldUseRunLengthEncoding(threshold));
    }

    /// <summary>
    /// Prints a summary of the build statistics to the console.
    /// </summary>
    public void PrintSummary()
    {
        Console.WriteLine($"FrozenArrow Build Statistics");
        Console.WriteLine($"================================");
        Console.WriteLine($"Rows: {RowCount:N0}");
        Console.WriteLine($"Columns: {ColumnCount}");
        
        if (StatisticsCollectionTime.HasValue)
            Console.WriteLine($"Statistics Collection Time: {StatisticsCollectionTime.Value.TotalMilliseconds:F2}ms");
        
        if (ArrayBuildTime.HasValue)
            Console.WriteLine($"Array Build Time: {ArrayBuildTime.Value.TotalMilliseconds:F2}ms");

        Console.WriteLine();
        Console.WriteLine("Column Details:");
        Console.WriteLine("---------------");

        foreach (var col in ColumnStatistics.Values.OrderBy(c => c.ColumnName))
        {
            var encoding = col.RecommendedEncoding switch
            {
                ColumnEncoding.Dictionary => "DICT",
                ColumnEncoding.RunLengthEncoded => "RLE",
                _ => "PRIM"
            };

            Console.WriteLine($"  {col.ColumnName,-20} | " +
                            $"Distinct: {col.DistinctCount,10:N0} ({col.CardinalityRatio,6:P1}) | " +
                            $"Runs: {col.RunCount,10:N0} ({col.RunRatio,6:P1}) | " +
                            $"Nulls: {col.NullCount,10:N0} ({col.NullRatio,6:P1}) | " +
                            $"Rec: {encoding}");
        }

        var dictCandidates = GetDictionaryEncodingCandidates().ToList();
        var rleCandidates = GetRunLengthEncodingCandidates().ToList();

        Console.WriteLine();
        Console.WriteLine($"Encoding Recommendations:");
        Console.WriteLine($"  Dictionary Encoding Candidates: {dictCandidates.Count} columns");
        Console.WriteLine($"  Run-Length Encoding Candidates: {rleCandidates.Count} columns");
    }

    /// <summary>
    /// Estimates memory savings from using recommended encodings.
    /// </summary>
    /// <returns>Estimated bytes saved compared to primitive encoding.</returns>
    public long EstimateMemorySavings()
    {
        long savings = 0;

        foreach (var col in ColumnStatistics.Values)
        {
            var typeSize = GetTypeSize(col.ValueType);
            var primitiveSize = col.TotalCount * typeSize;

            switch (col.RecommendedEncoding)
            {
                case ColumnEncoding.Dictionary:
                    // Dictionary: distinct_count * type_size + total_count * index_size
                    var indexSize = col.DistinctCount <= 255 ? 1 : col.DistinctCount <= 65535 ? 2 : 4;
                    var dictSize = col.DistinctCount * typeSize + col.TotalCount * indexSize;
                    savings += primitiveSize - dictSize;
                    break;

                case ColumnEncoding.RunLengthEncoded:
                    // RLE: run_count * (type_size + run_end_size)
                    var runEndSize = 4; // int32 for run ends
                    var rleSize = col.RunCount * (typeSize + runEndSize);
                    savings += primitiveSize - rleSize;
                    break;
            }
        }

        return Math.Max(0, savings);
    }

    private static int GetTypeSize(Type type)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType.Name switch
        {
            "Boolean" => 1,
            "Byte" => 1,
            "SByte" => 1,
            "Int16" => 2,
            "UInt16" => 2,
            "Int32" => 4,
            "UInt32" => 4,
            "Int64" => 8,
            "UInt64" => 8,
            "Single" => 4,
            "Double" => 8,
            "Decimal" => 16,
            "DateTime" => 8,
            "String" => 8, // Average estimate for strings
            _ => 8 // Default assumption
        };
    }
}
