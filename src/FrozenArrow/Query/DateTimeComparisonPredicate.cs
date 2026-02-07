using Apache.Arrow;
using Apache.Arrow.Types;

namespace FrozenArrow.Query;

/// <summary>
/// Predicate for DateTime column comparisons.
/// Phase B Quick Win: DateTime predicate support.
/// </summary>
public sealed class DateTimeComparisonPredicate : ColumnPredicate
{
    public override string ColumnName { get; }
    public override int ColumnIndex { get; }
    public ComparisonOperator Operator { get; }
    public DateTime Value { get; }

    public DateTimeComparisonPredicate(string columnName, int columnIndex, ComparisonOperator op, DateTime value)
    {
        ColumnName = columnName;
        ColumnIndex = columnIndex;
        Operator = op;
        Value = value;
    }

    public override void Evaluate(RecordBatch batch, Span<bool> selection)
    {
        var column = batch.Column(ColumnIndex);
        var length = batch.Length;

        for (int i = 0; i < length; i++)
        {
            if (!selection[i]) continue; // Already filtered out

            if (column.IsNull(i))
            {
                selection[i] = false;
                continue;
            }

            var columnValue = GetDateTimeValue(column, i);
            
            selection[i] = Operator switch
            {
                ComparisonOperator.Equal => columnValue == Value,
                ComparisonOperator.NotEqual => columnValue != Value,
                ComparisonOperator.LessThan => columnValue < Value,
                ComparisonOperator.LessThanOrEqual => columnValue <= Value,
                ComparisonOperator.GreaterThan => columnValue > Value,
                ComparisonOperator.GreaterThanOrEqual => columnValue >= Value,
                _ => false
            };
        }
    }

    protected override bool EvaluateSingle(IArrowArray column, int rowIndex)
    {
        if (column.IsNull(rowIndex))
        {
            return false;
        }

        var columnValue = GetDateTimeValue(column, rowIndex);
        
        return Operator switch
        {
            ComparisonOperator.Equal => columnValue == Value,
            ComparisonOperator.NotEqual => columnValue != Value,
            ComparisonOperator.LessThan => columnValue < Value,
            ComparisonOperator.LessThanOrEqual => columnValue <= Value,
            ComparisonOperator.GreaterThan => columnValue > Value,
            ComparisonOperator.GreaterThanOrEqual => columnValue >= Value,
            _ => false
        };
    }

    private static DateTime GetDateTimeValue(IArrowArray column, int rowIndex)
    {
        // Handle Date32Array (days since epoch)
        if (column is Date32Array date32Array)
        {
            var daysSinceEpoch = date32Array.GetValue(rowIndex);
            if (daysSinceEpoch.HasValue)
            {
                return DateTimeOffset.FromUnixTimeSeconds(daysSinceEpoch.Value * 86400L).DateTime;
            }
        }
        
        // Handle Date64Array (milliseconds since epoch)
        if (column is Date64Array date64Array)
        {
            var msSinceEpoch = date64Array.GetValue(rowIndex);
            if (msSinceEpoch.HasValue)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(msSinceEpoch.Value).DateTime;
            }
        }
        
        // Handle TimestampArray
        if (column.Data.DataType is TimestampType timestampType)
        {
            var timestampArray = new TimestampArray(column.Data);
            var value = timestampArray.GetValue(rowIndex);
            
            if (value.HasValue)
            {
                // Convert based on timestamp unit
                return timestampType.Unit switch
                {
                    TimeUnit.Second => DateTimeOffset.FromUnixTimeSeconds(value.Value).DateTime,
                    TimeUnit.Millisecond => DateTimeOffset.FromUnixTimeMilliseconds(value.Value).DateTime,
                    TimeUnit.Microsecond => DateTimeOffset.FromUnixTimeMilliseconds(value.Value / 1000).DateTime,
                    TimeUnit.Nanosecond => DateTimeOffset.FromUnixTimeMilliseconds(value.Value / 1_000_000).DateTime,
                    _ => throw new NotSupportedException($"Timestamp unit {timestampType.Unit} not supported")
                };
            }
        }

        throw new NotSupportedException($"Column type {column.Data.DataType} not supported for DateTime predicates");
    }

    public override string ToString()
    {
        var opStr = Operator switch
        {
            ComparisonOperator.Equal => "=",
            ComparisonOperator.NotEqual => "!=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanOrEqual => "<=",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            _ => Operator.ToString()
        };

        return $"{ColumnName} {opStr} '{Value:yyyy-MM-dd}'";
    }
}
