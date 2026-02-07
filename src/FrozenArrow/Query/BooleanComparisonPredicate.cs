using Apache.Arrow;
using Apache.Arrow.Types;

namespace FrozenArrow.Query;

/// <summary>
/// Predicate for boolean column comparisons.
/// Phase B Quick Win: Boolean predicate support.
/// </summary>
public sealed class BooleanComparisonPredicate : ColumnPredicate
{
    public override string ColumnName { get; }
    public override int ColumnIndex { get; }
    public ComparisonOperator Operator { get; }
    public bool Value { get; }

    public BooleanComparisonPredicate(string columnName, int columnIndex, ComparisonOperator op, bool value)
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

            var columnValue = GetBooleanValue(column, i);
            
            selection[i] = Operator switch
            {
                ComparisonOperator.Equal => columnValue == Value,
                ComparisonOperator.NotEqual => columnValue != Value,
                _ => throw new NotSupportedException($"Operator {Operator} not supported for boolean columns")
            };
        }
    }

    protected override bool EvaluateSingle(IArrowArray column, int rowIndex)
    {
        if (column.IsNull(rowIndex))
        {
            return false;
        }

        var columnValue = GetBooleanValue(column, rowIndex);
        
        return Operator switch
        {
            ComparisonOperator.Equal => columnValue == Value,
            ComparisonOperator.NotEqual => columnValue != Value,
            _ => throw new NotSupportedException($"Operator {Operator} not supported for boolean columns")
        };
    }

    private bool GetBooleanValue(IArrowArray column, int rowIndex)
    {
        // Handle BooleanArray
        if (column is BooleanArray boolArray)
        {
            return boolArray.GetValue(rowIndex) ?? false;
        }

        throw new NotSupportedException($"Column type {column.Data.DataType} not supported for boolean predicates");
    }

    public override string ToString()
    {
        var opStr = Operator switch
        {
            ComparisonOperator.Equal => "=",
            ComparisonOperator.NotEqual => "!=",
            _ => Operator.ToString()
        };

        return $"{ColumnName} {opStr} {Value}";
    }
}
