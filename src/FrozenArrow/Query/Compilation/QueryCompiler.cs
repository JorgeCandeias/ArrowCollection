using System.Linq.Expressions;
using System.Reflection;
using Apache.Arrow;

namespace FrozenArrow.Query.Compilation;

/// <summary>
/// Compiles logical plan predicates into fast, specialized delegates.
/// Phase 9: Query compilation for 2-5× faster execution.
/// </summary>
public static class QueryCompiler
{
    /// <summary>
    /// Compiles a predicate into a fast delegate that operates on RecordBatch indices.
    /// </summary>
    /// <param name="predicate">The predicate to compile.</param>
    /// <param name="recordBatch">The RecordBatch to access.</param>
    /// <returns>A compiled function: (int index) => bool</returns>
    public static Func<int, bool> CompilePredicate(ColumnPredicate predicate, RecordBatch recordBatch)
    {
        // Parameter: row index
        var indexParam = Expression.Parameter(typeof(int), "index");

        // Get the column array
        var column = recordBatch.Column(predicate.ColumnIndex);

        // Generate specialized code based on predicate type
        Expression body = predicate switch
        {
            Int32ComparisonPredicate int32Pred => CompileInt32Predicate(int32Pred, column, indexParam),
            DoubleComparisonPredicate doublePred => CompileDoublePredicate(doublePred, column, indexParam),
            _ => throw new NotSupportedException($"Predicate type {predicate.GetType().Name} compilation not yet supported")
        };

        // Compile to delegate
        var lambda = Expression.Lambda<Func<int, bool>>(body, indexParam);
        return lambda.Compile();
    }

    private static Expression CompileInt32Predicate(
        Int32ComparisonPredicate predicate,
        IArrowArray column,
        ParameterExpression indexParam)
    {
        // Get the Int32Array
        var int32Array = (Apache.Arrow.Int32Array)column;

        // Create expression: array.GetValue(index)
        var arrayConstant = Expression.Constant(int32Array);
        var getValueMethod = typeof(Apache.Arrow.Int32Array).GetMethod("GetValue", new[] { typeof(int) })!;
        var getValue = Expression.Call(arrayConstant, getValueMethod, indexParam);

        // Create comparison: value op constant
        var constantValue = Expression.Constant(predicate.Value);

        return predicate.Operator switch
        {
            ComparisonOperator.Equal => Expression.Equal(getValue, constantValue),
            ComparisonOperator.NotEqual => Expression.NotEqual(getValue, constantValue),
            ComparisonOperator.GreaterThan => Expression.GreaterThan(getValue, constantValue),
            ComparisonOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(getValue, constantValue),
            ComparisonOperator.LessThan => Expression.LessThan(getValue, constantValue),
            ComparisonOperator.LessThanOrEqual => Expression.LessThanOrEqual(getValue, constantValue),
            _ => throw new NotSupportedException($"Operator {predicate.Operator} not supported")
        };
    }

    private static Expression CompileDoublePredicate(
        DoubleComparisonPredicate predicate,
        IArrowArray column,
        ParameterExpression indexParam)
    {
        // Get the DoubleArray
        var doubleArray = (Apache.Arrow.DoubleArray)column;

        // Create expression: array.GetValue(index)
        var arrayConstant = Expression.Constant(doubleArray);
        var getValueMethod = typeof(Apache.Arrow.DoubleArray).GetMethod("GetValue", new[] { typeof(int) })!;
        var getValue = Expression.Call(arrayConstant, getValueMethod, indexParam);

        // Create comparison: value op constant
        var constantValue = Expression.Constant(predicate.Value);

        return predicate.Operator switch
        {
            ComparisonOperator.Equal => Expression.Equal(getValue, constantValue),
            ComparisonOperator.NotEqual => Expression.NotEqual(getValue, constantValue),
            ComparisonOperator.GreaterThan => Expression.GreaterThan(getValue, constantValue),
            ComparisonOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(getValue, constantValue),
            ComparisonOperator.LessThan => Expression.LessThan(getValue, constantValue),
            ComparisonOperator.LessThanOrEqual => Expression.LessThanOrEqual(getValue, constantValue),
            _ => throw new NotSupportedException($"Operator {predicate.Operator} not supported")
        };
    }

    /// <summary>
    /// Compiles multiple predicates into a single fused predicate function.
    /// This eliminates multiple virtual calls and branches.
    /// </summary>
    public static Func<int, bool> CompilePredicates(
        IReadOnlyList<ColumnPredicate> predicates,
        RecordBatch recordBatch)
    {
        if (predicates.Count == 0)
        {
            return _ => true; // No predicates = always true
        }

        if (predicates.Count == 1)
        {
            return CompilePredicate(predicates[0], recordBatch);
        }

        // Fuse multiple predicates with AND
        var indexParam = Expression.Parameter(typeof(int), "index");
        
        Expression? combined = null;
        foreach (var predicate in predicates)
        {
            var column = recordBatch.Column(predicate.ColumnIndex);
            
            Expression predicateExpr = predicate switch
            {
                Int32ComparisonPredicate int32Pred => CompileInt32Predicate(int32Pred, column, indexParam),
                DoubleComparisonPredicate doublePred => CompileDoublePredicate(doublePred, column, indexParam),
                _ => throw new NotSupportedException($"Predicate type {predicate.GetType().Name} not supported")
            };

            combined = combined == null
                ? predicateExpr
                : Expression.AndAlso(combined, predicateExpr);
        }

        var lambda = Expression.Lambda<Func<int, bool>>(combined!, indexParam);
        return lambda.Compile();
    }
}
