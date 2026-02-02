namespace FrozenArrow;

/// <summary>
/// Statistics collected for a single column during ArrowCollection build.
/// Used to determine the optimal Arrow memory layout for the column.
/// </summary>
public sealed class ColumnStatistics
{
    /// <summary>
    /// Name of the column/field.
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// The .NET type of the column values.
    /// </summary>
    public required Type ValueType { get; init; }

    /// <summary>
    /// Total number of values in the column.
    /// </summary>
    public long TotalCount { get; set; }

    /// <summary>
    /// Number of null values in the column.
    /// </summary>
    public long NullCount { get; set; }

    /// <summary>
    /// Number of distinct (unique) values in the column.
    /// </summary>
    public long DistinctCount { get; set; }

    /// <summary>
    /// Number of "runs" - consecutive sequences of identical values.
    /// A column with values [A, A, A, B, B, C] has 3 runs.
    /// Lower run count relative to total count indicates good RLE potential.
    /// </summary>
    public long RunCount { get; set; }

    /// <summary>
    /// Ratio of distinct values to total values (0.0 to 1.0).
    /// Lower values indicate better dictionary encoding potential.
    /// </summary>
    public double CardinalityRatio => TotalCount > 0 ? (double)DistinctCount / TotalCount : 0;

    /// <summary>
    /// Ratio of runs to total values (0.0 to 1.0).
    /// Lower values indicate better RLE potential.
    /// </summary>
    public double RunRatio => TotalCount > 0 ? (double)RunCount / TotalCount : 0;

    /// <summary>
    /// Ratio of null values to total values (0.0 to 1.0).
    /// </summary>
    public double NullRatio => TotalCount > 0 ? (double)NullCount / TotalCount : 0;

    /// <summary>
    /// Determines if dictionary encoding would be beneficial for this column.
    /// </summary>
    /// <param name="threshold">Maximum cardinality ratio to consider dictionary encoding (default 0.5 = 50%).</param>
    /// <returns>True if dictionary encoding is recommended.</returns>
    public bool ShouldUseDictionaryEncoding(double threshold = 0.5)
    {
        // Dictionary encoding is beneficial when:
        // 1. Cardinality is low (many repeated values)
        // 2. There's a minimum number of rows to amortize dictionary overhead
        return CardinalityRatio < threshold && TotalCount >= 100;
    }

    /// <summary>
    /// Determines if run-length encoding would be beneficial for this column.
    /// </summary>
    /// <param name="threshold">Maximum run ratio to consider RLE (default 0.1 = 10%).</param>
    /// <returns>True if RLE is recommended.</returns>
    public bool ShouldUseRunLengthEncoding(double threshold = 0.1)
    {
        // RLE is beneficial when:
        // 1. Run ratio is very low (data is sorted with many consecutive identical values)
        // 2. There's a minimum number of rows
        return RunRatio < threshold && TotalCount >= 100;
    }

    /// <summary>
    /// Gets the recommended encoding for this column based on collected statistics.
    /// </summary>
    public ColumnEncoding RecommendedEncoding
    {
        get
        {
            // RLE is the most space-efficient for sorted data with long runs
            if (ShouldUseRunLengthEncoding())
                return ColumnEncoding.RunLengthEncoded;

            // Dictionary encoding is great for low-cardinality data
            if (ShouldUseDictionaryEncoding())
                return ColumnEncoding.Dictionary;

            // Default to primitive/plain encoding
            return ColumnEncoding.Primitive;
        }
    }

    public override string ToString()
    {
        return $"{ColumnName}: Total={TotalCount}, Distinct={DistinctCount} ({CardinalityRatio:P1}), " +
               $"Runs={RunCount} ({RunRatio:P1}), Nulls={NullCount} ({NullRatio:P1}), " +
               $"Recommended={RecommendedEncoding}";
    }
}

/// <summary>
/// Encoding types supported for Arrow columns.
/// </summary>
public enum ColumnEncoding
{
    /// <summary>
    /// Standard primitive array - values stored directly.
    /// Best for high-cardinality, unsorted data.
    /// </summary>
    Primitive,

    /// <summary>
    /// Dictionary encoding - unique values stored once, indices reference them.
    /// Best for low-cardinality data (many repeated values).
    /// </summary>
    Dictionary,

    /// <summary>
    /// Run-length encoding - consecutive identical values stored as (value, count) pairs.
    /// Best for sorted data with long runs of identical values.
    /// </summary>
    RunLengthEncoded
}
