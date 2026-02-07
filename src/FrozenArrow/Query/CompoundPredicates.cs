using Apache.Arrow;

namespace FrozenArrow.Query;

/// <summary>
/// Predicate that combines multiple predicates with OR logic.
/// Phase 8 Enhancement (Part 2): Enables OR operator in SQL WHERE clauses.
/// </summary>
public sealed class OrPredicate : ColumnPredicate
{
    private readonly ColumnPredicate _left;
    private readonly ColumnPredicate _right;

    public override string ColumnName => $"({_left.ColumnName} OR {_right.ColumnName})";
    public override int ColumnIndex => _left.ColumnIndex; // Use first predicate's column

    public OrPredicate(ColumnPredicate left, ColumnPredicate right)
    {
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public override void Evaluate(RecordBatch batch, Span<bool> selection)
    {
        // Create temporary selection arrays for each predicate
        var leftSelection = new bool[selection.Length];
        var rightSelection = new bool[selection.Length];
        
        // Copy original selection
        selection.CopyTo(leftSelection);
        selection.CopyTo(rightSelection);

        // Evaluate both predicates
        _left.Evaluate(batch, leftSelection.AsSpan());
        _right.Evaluate(batch, rightSelection.AsSpan());

        // Combine with OR logic
        for (int i = 0; i < selection.Length; i++)
        {
            selection[i] = selection[i] && (leftSelection[i] || rightSelection[i]);
        }
    }

    protected override bool EvaluateSingle(IArrowArray column, int rowIndex)
    {
        // For OR, return true if either predicate is true
        // Note: Both predicates may operate on different columns
        // This is a simplified version - full implementation would handle this properly
        return true; // Placeholder - actual evaluation happens in Evaluate(RecordBatch, Span<bool>)
    }

    public override string ToString()
    {
        return $"({_left} OR {_right})";
    }
}

/// <summary>
/// Predicate that negates another predicate with NOT logic.
/// Phase 8 Enhancement (Part 2): Enables NOT operator in SQL WHERE clauses.
/// </summary>
public sealed class NotPredicate : ColumnPredicate
{
    private readonly ColumnPredicate _inner;

    public override string ColumnName => $"NOT {_inner.ColumnName}";
    public override int ColumnIndex => _inner.ColumnIndex;

    public NotPredicate(ColumnPredicate inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override void Evaluate(RecordBatch batch, Span<bool> selection)
    {
        // Create a copy of the selection to evaluate the inner predicate
        var innerSelection = new bool[selection.Length];
        selection.CopyTo(innerSelection);

        // Evaluate the inner predicate
        _inner.Evaluate(batch, innerSelection.AsSpan());

        // Negate the results: if inner selected it, we don't; if inner didn't, we do
        for (int i = 0; i < selection.Length; i++)
        {
            selection[i] = selection[i] && !innerSelection[i];
        }
    }

    protected override bool EvaluateSingle(IArrowArray column, int rowIndex)
    {
        // Compound predicates use Evaluate(RecordBatch, Span<bool>) instead
        // This method is not used for compound predicates
        throw new NotSupportedException("NotPredicate uses Evaluate(RecordBatch, Span<bool>) for evaluation");
    }

    public override string ToString()
    {
        return $"NOT ({_inner})";
    }
}
