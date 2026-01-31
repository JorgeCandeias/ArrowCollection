using System.Collections;

namespace ArrowCollection;

/// <summary>
/// Collects statistics for a column during data iteration.
/// Thread-safe for single-writer scenarios.
/// </summary>
/// <typeparam name="T">The type of values in the column.</typeparam>
public sealed class ColumnStatisticsCollector<T>
{
    private readonly string _columnName;
    private readonly HashSet<T> _distinctValues;
    private readonly IEqualityComparer<T> _comparer;
    
    private long _totalCount;
    private long _nullCount;
    private long _runCount;
    private T? _previousValue;
    private bool _hasPreviousValue;

    /// <summary>
    /// Creates a new statistics collector for a column.
    /// </summary>
    /// <param name="columnName">Name of the column being tracked.</param>
    /// <param name="comparer">Optional equality comparer for values.</param>
    public ColumnStatisticsCollector(string columnName, IEqualityComparer<T>? comparer = null)
    {
        _columnName = columnName;
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _distinctValues = new HashSet<T>(_comparer);
    }

    /// <summary>
    /// Records a value for statistics collection.
    /// </summary>
    /// <param name="value">The value to record.</param>
    public void Record(T? value)
    {
        _totalCount++;

        if (value is null)
        {
            _nullCount++;
            // Null is treated as a distinct "value" for run detection
            if (_hasPreviousValue && _previousValue is not null)
            {
                _runCount++;
            }
            else if (!_hasPreviousValue)
            {
                _runCount++;
            }
            _previousValue = default;
            _hasPreviousValue = true;
            return;
        }

        // Track distinct values
        _distinctValues.Add(value);

        // Track runs - count transitions between different values
        if (!_hasPreviousValue)
        {
            // First value starts the first run
            _runCount++;
            _hasPreviousValue = true;
        }
        else if (_previousValue is null || !_comparer.Equals(_previousValue, value))
        {
            // Value changed - new run
            _runCount++;
        }

        _previousValue = value;
    }

    /// <summary>
    /// Gets the collected statistics.
    /// </summary>
    public ColumnStatistics GetStatistics()
    {
        return new ColumnStatistics
        {
            ColumnName = _columnName,
            ValueType = typeof(T),
            TotalCount = _totalCount,
            NullCount = _nullCount,
            DistinctCount = _distinctValues.Count + (_nullCount > 0 ? 1 : 0), // Count null as a distinct value if present
            RunCount = _runCount
        };
    }

    /// <summary>
    /// Resets the collector for reuse.
    /// </summary>
    public void Reset()
    {
        _totalCount = 0;
        _nullCount = 0;
        _runCount = 0;
        _distinctValues.Clear();
        _previousValue = default;
        _hasPreviousValue = false;
    }
}

/// <summary>
/// Non-generic interface for column statistics collectors.
/// </summary>
public interface IColumnStatisticsCollector
{
    /// <summary>
    /// Records a value (boxed for non-generic access).
    /// </summary>
    void RecordObject(object? value);

    /// <summary>
    /// Gets the collected statistics.
    /// </summary>
    ColumnStatistics GetStatistics();

    /// <summary>
    /// Resets the collector for reuse.
    /// </summary>
    void Reset();
}

/// <summary>
/// Generic implementation that also implements the non-generic interface.
/// </summary>
public sealed class ColumnStatisticsCollectorBoxed<T> : IColumnStatisticsCollector
{
    private readonly ColumnStatisticsCollector<T> _inner;

    public ColumnStatisticsCollectorBoxed(string columnName, IEqualityComparer<T>? comparer = null)
    {
        _inner = new ColumnStatisticsCollector<T>(columnName, comparer);
    }

    public void Record(T? value) => _inner.Record(value);

    public void RecordObject(object? value) => _inner.Record(value is null ? default : (T)value);

    public ColumnStatistics GetStatistics() => _inner.GetStatistics();

    public void Reset() => _inner.Reset();
}

/// <summary>
/// Collects statistics for all columns in a record type.
/// </summary>
public sealed class RecordStatisticsCollector
{
    private readonly Dictionary<string, IColumnStatisticsCollector> _collectors = new();

    /// <summary>
    /// Registers a collector for a column.
    /// </summary>
    public void RegisterColumn<T>(string columnName, IEqualityComparer<T>? comparer = null)
    {
        _collectors[columnName] = new ColumnStatisticsCollectorBoxed<T>(columnName, comparer);
    }

    /// <summary>
    /// Gets the collector for a specific column.
    /// </summary>
    public ColumnStatisticsCollector<T> GetCollector<T>(string columnName)
    {
        if (_collectors.TryGetValue(columnName, out var collector) && 
            collector is ColumnStatisticsCollectorBoxed<T> typed)
        {
            return typed.GetStatistics() as ColumnStatisticsCollector<T> 
                   ?? throw new InvalidOperationException($"Column {columnName} is not of type {typeof(T)}");
        }
        throw new KeyNotFoundException($"No collector registered for column {columnName}");
    }

    /// <summary>
    /// Records a value for a specific column.
    /// </summary>
    public void Record<T>(string columnName, T? value)
    {
        if (_collectors.TryGetValue(columnName, out var collector) &&
            collector is ColumnStatisticsCollectorBoxed<T> typed)
        {
            typed.Record(value);
        }
    }

    /// <summary>
    /// Gets statistics for all columns.
    /// </summary>
    public IReadOnlyDictionary<string, ColumnStatistics> GetAllStatistics()
    {
        return _collectors.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetStatistics());
    }

    /// <summary>
    /// Resets all collectors.
    /// </summary>
    public void Reset()
    {
        foreach (var collector in _collectors.Values)
        {
            collector.Reset();
        }
    }
}
