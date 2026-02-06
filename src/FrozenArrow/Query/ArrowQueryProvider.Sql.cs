using Apache.Arrow;
using FrozenArrow.Query.LogicalPlan;
using FrozenArrow.Query.Sql;

namespace FrozenArrow.Query;

// SQL Query Support (Phase 8)
public sealed partial class ArrowQueryProvider
{
    /// <summary>
    /// Executes a SQL query and returns dynamic results.
    /// Phase 8: SQL queries use the full logical plan optimization pipeline.
    /// </summary>
    public IEnumerable<dynamic> ExecuteSqlQueryDynamic(string sql)
    {
        return ExecuteSqlQueryInternal<IEnumerable<object>>(sql).Cast<dynamic>();
    }

    /// <summary>
    /// Executes a SQL query with a specific result type.
    /// Results are projected onto TResult if it's a data type, otherwise executed as-is.
    /// </summary>
    public IEnumerable<TResult> ExecuteSqlQuery<TResult>(string sql)
    {
        // Check if TResult is a simple enumerable or a projection type
        var resultType = typeof(TResult);
        
        // If TResult is a user-defined type (record/class), we'd need mapping logic
        // For now, execute and cast
        var results = ExecuteSqlQueryInternal<IEnumerable<object>>(sql);
        
        // Simple cast for now - in production, you'd add property mapping here
        return results.Cast<TResult>();
    }

    /// <summary>
    /// Executes a SQL query and returns a single scalar result.
    /// </summary>
    public TResult ExecuteSqlQueryScalar<TResult>(string sql)
    {
        return ExecuteSqlQueryInternal<TResult>(sql);
    }

    /// <summary>
    /// Internal method that handles the core SQL execution logic.
    /// </summary>
    private TResult ExecuteSqlQueryInternal<TResult>(string sql)
    {
        if (!UseLogicalPlanExecution)
        {
            throw new InvalidOperationException(
                "SQL queries require logical plan execution. Set UseLogicalPlanExecution = true.");
        }

        // Build schema from record batch
        var schema = new Dictionary<string, Type>();
        foreach (var kvp in _columnIndexMap)
        {
            var columnIndex = kvp.Value;
            if (columnIndex >= 0 && columnIndex < _recordBatch.ColumnCount)
            {
                var column = _recordBatch.Column(columnIndex);
                schema[kvp.Key] = GetClrTypeFromArrowType(column.Data.DataType);
            }
        }

        // Parse SQL to logical plan
        var parser = new SqlParser(schema, _columnIndexMap, _count);
        var logicalPlan = parser.Parse(sql);

        // Optimize the logical plan
        var optimizer = new LogicalPlanOptimizer(_zoneMap);
        var optimizedPlan = optimizer.Optimize(logicalPlan);

        // Check cache if enabled
        if (UseLogicalPlanCache)
        {
            var cacheKey = LogicalPlan.LogicalPlanCache.ComputeKey($"SQL:{sql}");
            
            if (!_logicalPlanCache.TryGet(cacheKey, out _))
            {
                // Cache the optimized plan for future use
                _logicalPlanCache.Add(cacheKey, optimizedPlan);
            }
        }

        // Execute using the same pipeline as LINQ queries
        if (UsePhysicalPlanExecution)
        {
            return ExecuteLogicalPlanViaPhysical<TResult>(optimizedPlan);
        }
        else if (UseDirectLogicalPlanExecution)
        {
            return ExecuteLogicalPlanDirect<TResult>(optimizedPlan);
        }
        else
        {
            // For SQL, we can't use bridge (no Expression), so force direct execution
            return ExecuteLogicalPlanDirect<TResult>(optimizedPlan);
        }
    }
}
